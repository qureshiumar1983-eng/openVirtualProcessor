using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Crestron.SimplSharp.CrestronSockets;

namespace OpenVP.Tests;

/// <summary>The SIMPL# socket classes (TCPServer, TCPClient, SecureTCPServer, SecureTCPClient, UDPServer) against real loopback sockets.</summary>
public static class SocketTests
{
    private static int Free() => IntegrationServiceTests.FreePort();

    private static TcpClient Dial(int port)
    {
        var c = new TcpClient();
        c.Connect("127.0.0.1", port);
        return c;
    }

    private static string ReadSome(Stream s, int timeoutMs = 3000)
    {
        var buf = new byte[1024];
        var read = s.ReadAsync(buf, 0, buf.Length);
        Assert.True(read.Wait(timeoutMs), "the peer sent something");
        return Encoding.ASCII.GetString(buf, 0, read.Result);
    }

    /// <summary>Re-arms ReceiveDataAsync for a client index, collecting what arrives.</summary>
    private static void Pump(TCPServer server, uint index, ConcurrentQueue<string> got)
    {
        server.ReceiveDataAsync(index, (srv, i, n) =>
        {
            if (n <= 0) return;
            got.Enqueue($"{i}:{Encoding.ASCII.GetString(srv.GetIncomingDataBufferForSpecificClient(i), 0, n)}");
            Pump(srv, i, got);
        });
    }

    [Test]
    public static void TcpServer_takes_several_clients_sends_to_each_by_index_and_reports_who_left()
    {
        var port = Free();
        using var server = new TCPServer("0.0.0.0", port, 1024, Crestron.SimplSharp.EthernetAdapterType.EthernetUnknownAdapter, 3);
        var connected = new ConcurrentQueue<uint>();
        var statuses = new ConcurrentQueue<(uint, SocketStatus)>();
        var got = new ConcurrentQueue<string>();
        server.SocketStatusChange += (_, i, s) => statuses.Enqueue((i, s));
        Assert.Equal(SocketErrorCodes.SOCKET_OPERATION_PENDING, server.WaitForConnectionsAlways((srv, i) => { connected.Enqueue(i); Pump(srv, i, got); }), "listening");
        Assert.Equal(ServerState.SERVER_LISTENING, server.State, "listening, no clients yet");

        using var a = Dial(port); Assert.Eventually(() => connected.Count == 1, "first client");
        using var b = Dial(port); Assert.Eventually(() => connected.Count == 2, "second client");
        using var c = Dial(port); Assert.Eventually(() => connected.Count == 3, "third client");
        var idx = connected.ToArray();
        Assert.True(idx.OrderBy(x => x).SequenceEqual(new uint[] { 1, 2, 3 }), "1-based client indexes: " + string.Join(",", idx));
        Assert.Equal(3, server.NumberOfClientsConnected, "three connected");
        Assert.Equal(ServerState.SERVER_CONNECTED, server.State, "connected state");
        Assert.True(idx.All(i => server.ClientConnected(i) && server.GetServerSocketStatusForSpecificClient(i) == SocketStatus.SOCKET_STATUS_CONNECTED), "each client is connected");
        Assert.Equal("127.0.0.1", server.GetAddressServerAcceptedConnectionFromForSpecificClient(idx[0]), "peer address");

        // The index tells which client is which: match each socket to its index by what it receives.
        var byClient = new Dictionary<TcpClient, uint>();
        foreach (var i in idx)
            Assert.Equal(SocketErrorCodes.SOCKET_OK, server.SendData(i, Encoding.ASCII.GetBytes("hello" + i), 6), "send to " + i);
        foreach (var cl in new[] { a, b, c })
        {
            var text = ReadSome(cl.GetStream());
            Assert.True(text.StartsWith("hello") && uint.TryParse(text[5..], out var n) && idx.Contains(n), "got the greeting meant for it: " + text);
            byClient[cl] = uint.Parse(text[5..]);
        }
        Assert.Equal(3, byClient.Values.Distinct().Count(), "each client got a different index");

        // Receiving: each client's data carries its index.
        foreach (var kv in byClient) kv.Key.GetStream().Write(Encoding.ASCII.GetBytes("from" + kv.Value));
        Assert.Eventually(() => got.Count >= 3, "three messages");
        foreach (var kv in byClient) Assert.True(got.Contains($"{kv.Value}:from{kv.Value}"), "message for index " + kv.Value);

        // A fourth connection when the server is full is turned away.
        using var d = Dial(port);
        var buf = new byte[8];
        Assert.True(d.GetStream().ReadAsync(buf, 0, 8).Wait(3000) && server.NumberOfClientsConnected == 3, "the fourth client is closed, not added");

        // One client leaves: the program is told which one, with no read pending on a connection it never re-armed.
        var leaving = byClient[b];
        b.Dispose();
        Assert.Eventually(() => statuses.Contains((leaving, SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY)), "BROKEN_REMOTELY for the client that left");
        Assert.Eventually(() => server.NumberOfClientsConnected == 2, "two left");
        Assert.True(!server.ClientConnected(leaving), "its index is no longer connected");
        Assert.Equal(SocketErrorCodes.SOCKET_NOT_CONNECTED, server.SendData(leaving, new byte[] { 1 }, 1), "sending to a gone client");

        // A new client takes the freed index.
        var before = connected.Count;
        using var e = Dial(port);
        Assert.Eventually(() => connected.Count == before + 1 && connected.Last() == leaving, "the freed index is reused");

        // Server-side disconnect closes that client's socket.
        var victim = byClient[a];
        Assert.Equal(SocketErrorCodes.SOCKET_OK, server.Disconnect(victim), "disconnect");
        Assert.True(a.GetStream().ReadAsync(buf, 0, 8).Wait(3000), "client saw the close");
        Assert.True(statuses.Any(s => s.Item1 == victim && s.Item2 == SocketStatus.SOCKET_STATUS_NO_CONNECT), "status for the local disconnect");
        server.DisconnectAll();
        Assert.Equal(0, server.NumberOfClientsConnected, "all gone");
        Assert.Equal(ServerState.SERVER_LISTENING, server.State, "still listening");
    }

    [Test]
    public static void TcpServer_blocking_and_async_wait_for_one_connection_and_enforce_the_maximum()
    {
        var port = Free();
        using var server = new TCPServer("0.0.0.0", port, 512, Crestron.SimplSharp.EthernetAdapterType.EthernetUnknownAdapter, 1);
        uint first = 0;
        var waiter = Task.Run(() => { var r = server.WaitForConnection(ref first); return r; });
        Thread.Sleep(100);
        using var a = Dial(port);
        Assert.True(waiter.Wait(3000), "WaitForConnection returned");
        Assert.Equal(SocketErrorCodes.SOCKET_OK, waiter.Result, "ok");
        Assert.Equal(1u, first, "client index 1");

        // At the maximum, waiting for another is refused at once.
        uint second = 0;
        Assert.Equal(SocketErrorCodes.SOCKET_MAX_CONNECTIONS_REACHED, server.WaitForConnection(ref second), "sync wait when full");
        Assert.Equal(SocketErrorCodes.SOCKET_MAX_CONNECTIONS_REACHED, server.WaitForConnectionAsync((_, _) => { }), "async wait when full");

        // Blocking receive and send.
        a.GetStream().Write(Encoding.ASCII.GetBytes("ping"));
        var n = server.ReceiveData(first);
        Assert.Equal(4, n, "blocking ReceiveData");
        Assert.Equal("ping", Encoding.ASCII.GetString(server.GetIncomingDataBufferForSpecificClient(first), 0, n), "bytes");
        Assert.Equal(SocketErrorCodes.SOCKET_OK, server.SendData(Encoding.ASCII.GetBytes("pong"), 4), "send to the default (first) client");
        Assert.Equal("pong", ReadSome(a.GetStream()), "client got it");

        // SendDataAsync reports the byte count on its callback; the sender's close is seen by a read.
        var sent = new ConcurrentQueue<int>();
        Assert.Equal(SocketErrorCodes.SOCKET_OPERATION_PENDING, server.SendDataAsync(first, Encoding.ASCII.GetBytes("async!"), 6, (_, _, count) => sent.Enqueue(count)), "async send pending");
        Assert.Eventually(() => sent.Count == 1 && sent.First() == 6, "send callback with 6 bytes");
        Assert.Equal("async!", ReadSome(a.GetStream()), "client got the async data");
        a.Dispose();
        Assert.Equal(0, server.ReceiveData(first), "a blocking read on a closed client returns 0");
        Assert.Eventually(() => server.NumberOfClientsConnected == 0, "client removed");

        // Waiting again after a client left works (the same server, no restart).
        var again = new ConcurrentQueue<uint>();
        Assert.Equal(SocketErrorCodes.SOCKET_OPERATION_PENDING, server.WaitForConnectionAsync((_, i) => again.Enqueue(i)), "async wait");
        using var b = Dial(port);
        Assert.Eventually(() => again.Count == 1, "second client accepted");
    }

    [Test]
    public static void TcpServer_only_accepts_the_address_it_was_told_to_and_stop_closes_everything()
    {
        var port = Free();
        using var server = new TCPServer("10.9.9.9", port, 512, Crestron.SimplSharp.EthernetAdapterType.EthernetUnknownAdapter, 2);
        var connected = new ConcurrentQueue<uint>();
        server.WaitForConnectionsAlways((_, i) => connected.Enqueue(i));
        using var stranger = Dial(port); // 127.0.0.1 isn't 10.9.9.9
        var buf = new byte[4];
        Assert.True(stranger.GetStream().ReadAsync(buf, 0, 4).Wait(3000), "a client from another address is closed");
        Assert.Equal(0, connected.Count, "and never reported");
        server.Stop();
        Assert.Equal(ServerState.SERVER_NOT_LISTENING, server.State, "stopped");
        Assert.True(Assert.Throws<SocketException>(() => { using var c = Dial(port); }) != null, "nothing listens any more");
    }

    [Test]
    public static void TcpClient_connects_sends_receives_and_sees_the_server_close()
    {
        var port = Free();
        var listener = new TcpListener(System.Net.IPAddress.Loopback, port); listener.Start();
        try
        {
            var statuses = new ConcurrentQueue<SocketStatus>();
            using var client = new TCPClient("127.0.0.1", port, 1024);
            client.SocketStatusChange += (_, s) => statuses.Enqueue(s);
            var accept = listener.AcceptTcpClientAsync();
            var done = new ManualResetEventSlim();
            Assert.Equal(SocketErrorCodes.SOCKET_OPERATION_PENDING, client.ConnectToServerAsync(_ => done.Set()), "async connect pending");
            Assert.True(done.Wait(3000), "connect callback");
            Assert.Equal(SocketStatus.SOCKET_STATUS_CONNECTED, client.ClientStatus, "connected");
            Assert.True(client.LocalPortNumberOfClient > 0, "local port known");
            Assert.Equal(SocketErrorCodes.SOCKET_INVALID_STATE, client.ConnectToServer(), "connecting twice is refused");
            using var peer = accept.Result;
            var ps = peer.GetStream();

            Assert.Equal(SocketErrorCodes.SOCKET_OK, client.SendData(Encoding.ASCII.GetBytes("hello"), 5), "send");
            Assert.Equal("hello", ReadSome(ps), "server got it");
            Assert.Equal(SocketErrorCodes.SOCKET_OK, client.SendData(Encoding.ASCII.GetBytes("xxhelloxx"), 2, 5), "send with an offset");
            Assert.Equal("hello", ReadSome(ps), "offset honoured");

            var received = new ConcurrentQueue<string>();
            client.ReceiveDataAsync((c, n) => received.Enqueue(n + ":" + Encoding.ASCII.GetString(c.IncomingDataBuffer, 0, n)));
            ps.Write(Encoding.ASCII.GetBytes("reply"));
            Assert.Eventually(() => received.Count == 1 && received.First() == "5:reply", "async receive");
            ps.Write(Encoding.ASCII.GetBytes("more"));
            Assert.Equal(4, client.ReceiveData(), "blocking receive");

            var closed = new ConcurrentQueue<int>();
            client.ReceiveDataAsync((_, n) => closed.Enqueue(n));
            peer.Dispose();
            Assert.Eventually(() => closed.Count == 1 && closed.First() == 0, "a closed server reads as 0 bytes");
            Assert.Eventually(() => statuses.Contains(SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY), "BROKEN_REMOTELY status");
            Assert.Equal(SocketErrorCodes.SOCKET_NOT_CONNECTED, client.SendData(new byte[] { 1 }, 1), "send after close");
            Assert.True(statuses.Contains(SocketStatus.SOCKET_STATUS_WAITING) && statuses.Contains(SocketStatus.SOCKET_STATUS_CONNECTED), "waiting then connected were reported");
        }
        finally { listener.Stop(); }
    }

    [Test]
    public static void TcpClient_reports_refused_connections_and_unknown_hosts_and_local_disconnects()
    {
        var port = Free(); // nothing listens
        var statuses = new ConcurrentQueue<SocketStatus>();
        using var refused = new TCPClient("127.0.0.1", port, 256);
        refused.SocketStatusChange += (_, s) => statuses.Enqueue(s);
        Assert.Equal(SocketErrorCodes.SOCKET_CONNECT_FAILED, refused.ConnectToServer(), "refused");
        Assert.Equal(SocketStatus.SOCKET_STATUS_CONNECT_FAILED, refused.ClientStatus, "status");
        Assert.True(statuses.Contains(SocketStatus.SOCKET_STATUS_CONNECT_FAILED), "event");

        using var nohost = new TCPClient("no-such-host.invalid", 80, 256);
        var r = nohost.ConnectToServer();
        Assert.True(r is SocketErrorCodes.SOCKET_NO_HOSTNAME_RESOLVE or SocketErrorCodes.SOCKET_CONNECT_FAILED, "an unknown host fails: " + r);
        Assert.Equal(SocketErrorCodes.SOCKET_NO_HOSTNAME_RESOLVE, new TCPClient("", 80, 16).ConnectToServer(), "no address");
        Assert.Equal(SocketErrorCodes.SOCKET_INVALID_PORT_NUMBER, new TCPClient("127.0.0.1", 0, 16).ConnectToServer(), "no port");

        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        try
        {
            var lp = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            using var c = new TCPClient("127.0.0.1", lp, 64);
            Assert.Equal(SocketErrorCodes.SOCKET_OK, c.ConnectToServer(), "connect");
            using var peer = listener.AcceptTcpClient();
            Assert.Equal(SocketErrorCodes.SOCKET_OK, c.DisconnectFromServer(), "disconnect");
            Assert.True(peer.GetStream().ReadAsync(new byte[4], 0, 4).Wait(3000), "the server saw the close");
            Assert.Equal(SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY, c.ClientStatus, "closed by us");
            Assert.Equal(SocketErrorCodes.SOCKET_OK, c.ConnectToServer(), "reconnect works");
        }
        finally { listener.Stop(); }
    }

    [Test]
    public static void UdpServer_sends_and_receives_between_two_servers_and_reports_the_sender()
    {
        var pa = Free(); var pb = Free();
        using var a = new UDPServer("127.0.0.1", pb, 512, pa);
        using var b = new UDPServer("127.0.0.1", pa, 512, pb);
        Assert.Equal(SocketErrorCodes.SOCKET_OK, a.EnableUDPServer(), "A listens on its local port");
        Assert.Equal(SocketErrorCodes.SOCKET_OK, b.EnableUDPServer("127.0.0.1", pb, pa), "B: local pb, remote pa");
        Assert.Equal(SocketStatus.SOCKET_STATUS_CONNECTED, b.ServerStatus, "enabled");

        var got = new ConcurrentQueue<string>();
        a.ReceiveDataAsync((s, n) => got.Enqueue(n + ":" + Encoding.ASCII.GetString(s.IncomingDataBuffer, 0, n)));
        Assert.Equal(SocketErrorCodes.SOCKET_OK, b.SendData(Encoding.ASCII.GetBytes("datagram"), 8), "B sends to its remote");
        Assert.Eventually(() => got.Count == 1 && got.First() == "8:datagram", "A received it");
        Assert.Equal("127.0.0.1", a.IPAddressLastMessageReceivedFrom, "sender address");
        Assert.Equal(pb, a.IPPortLastMessageReceivedFrom, "sender port");

        // Reply to whoever sent, to an explicit address; and a blocking receive.
        Assert.Equal(SocketErrorCodes.SOCKET_OK, a.SendData(Encoding.ASCII.GetBytes("back"), 4, a.IPAddressLastMessageReceivedFrom, a.IPPortLastMessageReceivedFrom), "A replies");
        Assert.Equal(4, b.ReceiveData(), "B blocking receive");
        Assert.Equal("back", Encoding.ASCII.GetString(b.IncomingDataBuffer, 0, 4), "bytes");

        var sent = new ConcurrentQueue<int>();
        Assert.Equal(SocketErrorCodes.SOCKET_OPERATION_PENDING, a.SendDataAsync(Encoding.ASCII.GetBytes("async"), 5, "127.0.0.1", pb, (_, n, _) => sent.Enqueue(n), null), "async send");
        Assert.Eventually(() => sent.Count == 1 && sent.First() == 5, "send callback");
        Assert.Equal(5, b.ReceiveData(), "B got the async datagram");
        a.DisableUDPServer();
        Assert.Equal(SocketStatus.SOCKET_STATUS_NO_CONNECT, a.ServerStatus, "disabled");
        Assert.Equal(SocketErrorCodes.SOCKET_NOT_CONNECTED, a.SendData(new byte[1], 1), "no sending when disabled");
    }

    [Test]
    public static void Secure_server_and_client_talk_over_tls_with_a_self_signed_certificate()
    {
        var port = Free();
        using var server = new SecureTCPServer("0.0.0.0", port, 1024, Crestron.SimplSharp.EthernetAdapterType.EthernetUnknownAdapter, 2);
        var connected = new ConcurrentQueue<uint>();
        var statuses = new ConcurrentQueue<(uint, SocketStatus)>();
        var got = new ConcurrentQueue<string>();
        server.SocketStatusChange += (_, i, s) => statuses.Enqueue((i, s));
        server.WaitForConnectionsAlways((srv, i) =>
        {
            connected.Enqueue(i);
            void Arm() => srv.ReceiveDataAsync(i, (s2, c, n) => { if (n <= 0) return; got.Enqueue($"{c}:{Encoding.ASCII.GetString(s2.GetIncomingDataBufferForSpecificClient(c), 0, n)}"); Arm(); });
            Arm();
        });

        // A SIMPL# SecureTCPClient (which accepts the self-signed certificate).
        var cstatus = new ConcurrentQueue<SocketStatus>();
        using var client = new SecureTCPClient("127.0.0.1", port, 1024);
        client.SocketStatusChange += (_, s) => cstatus.Enqueue(s);
        Assert.Equal(SocketErrorCodes.SOCKET_OK, client.ConnectToServer(), "TLS connect");
        Assert.Eventually(() => connected.Count == 1, "server accepted");
        client.SendData(Encoding.ASCII.GetBytes("secret"), 6);
        Assert.Eventually(() => got.Contains("1:secret"), "server read the client's data");
        server.SendData(1, Encoding.ASCII.GetBytes("ack!"), 4);
        Assert.Equal(4, client.ReceiveData(), "client read the server's reply");
        Assert.Equal("ack!", Encoding.ASCII.GetString(client.IncomingDataBuffer, 0, 4), "bytes");

        // A second client, a plain SslStream that ignores the certificate: both are served, with different indexes.
        using var raw = Dial(port);
        using var ssl = new SslStream(raw.GetStream(), false, (_, _, _, _) => true);
        ssl.AuthenticateAsClient("localhost");
        Assert.Eventually(() => connected.Count == 2 && connected.ToArray().Distinct().Count() == 2, "second TLS client");
        ssl.Write(Encoding.ASCII.GetBytes("two"));
        Assert.Eventually(() => got.Any(g => g.EndsWith(":two")), "second client's data");

        // The client leaves; the server says which.
        client.DisconnectFromServer();
        Assert.Eventually(() => statuses.Any(s => s.Item2 == SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY), "server saw the TLS client leave");
        Assert.True(cstatus.Contains(SocketStatus.SOCKET_STATUS_CONNECTED) && cstatus.Contains(SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY), "client statuses");
        Assert.Equal(SocketErrorCodes.SOCKET_OK, client.ConnectToServer(), "and it can connect again");
    }

    [Test]
    public static void Secure_client_reports_a_failed_connection()
    {
        using var client = new SecureTCPClient("127.0.0.1", Free(), 256);
        Assert.Equal(SocketErrorCodes.SOCKET_CONNECT_FAILED, client.ConnectToServer(), "nothing is listening");
        Assert.Equal(SocketStatus.SOCKET_STATUS_CONNECT_FAILED, client.ClientStatus, "status");
        Assert.Equal(-1, client.SendData(new byte[1], 1), "no sending while not connected");
        // A plain TCP server is not TLS: the handshake fails and the client says so rather than hanging.
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0); l.Start();
        try
        {
            var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            _ = Task.Run(() => { using var c = l.AcceptTcpClient(); c.GetStream().Write(Encoding.ASCII.GetBytes("not tls at all, sorry\r\n")); Thread.Sleep(300); });
            using var c2 = new SecureTCPClient("127.0.0.1", p, 256);
            Assert.Equal(SocketErrorCodes.SOCKET_CONNECT_FAILED, c2.ConnectToServer(), "handshake failed");
        }
        finally { l.Stop(); }
    }
}
