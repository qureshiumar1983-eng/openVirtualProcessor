using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Net.Sockets;
using Crestron.Logos.SplusObjects;

namespace OpenVP.Splus;

/// <summary>
/// One OS socket per local UDP port, shared by every SIMPL+ UDP socket enabled on that port. On a processor
/// several modules can each enable UDP on the same port (programs run four or five instances of a module that
/// broadcasts on one port); a plain bind lets only the first one have it. A datagram goes to the sockets whose
/// remote address is its sender, or to every socket on the port if none is (so a socket set to broadcast, or
/// a device replying from another address, still hears it). The OS socket closes when its last socket leaves:
/// on SocketUDP_Disable, a new Enable, or when the module stops.
/// </summary>
internal static class UdpPorts
{
    private sealed class Port(UdpClient client)
    {
        public readonly UdpClient Client = client;
        public readonly List<SplusUdpSocket> Members = new();
        public SplusUdpSocket[] Snapshot = Array.Empty<SplusUdpSocket>();
    }

    private static readonly Dictionary<int, Port> Ports = new();
    private static readonly object Gate = new();

    /// <summary>Number of OS sockets open (for tests).</summary>
    internal static int OpenPorts { get { lock (Gate) return Ports.Count; } }

    public static void Join(SplusUdpSocket socket, int localPort)
    {
        Port? started = null;
        lock (Gate)
        {
            if (!Ports.TryGetValue(localPort, out var port))
            {
                var client = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
                Ports[localPort] = port = started = new Port(client);
            }
            port.Members.Add(socket);
            port.Snapshot = port.Members.ToArray();
            socket.Udp = port.Client;
        }
        if (started != null) _ = Receive(started);
    }

    public static void Leave(SplusUdpSocket socket)
    {
        UdpClient? close = null;
        lock (Gate)
        {
            if (socket.Udp == null) return;
            foreach (var (key, port) in Ports)
            {
                if (port.Client != socket.Udp) continue;
                port.Members.Remove(socket);
                port.Snapshot = port.Members.ToArray();
                if (port.Members.Count == 0) { Ports.Remove(key); close = port.Client; }
                break;
            }
            socket.Udp = null;
        }
        close?.Dispose();
    }

    private static async Task Receive(Port port)
    {
        while (true)
        {
            UdpReceiveResult r;
            try { r = await port.Client.ReceiveAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException)
            {
                lock (Gate) if (!Ports.ContainsValue(port)) return;
                continue; // e.g. ICMP port unreachable from an earlier send: keep listening
            }
            var members = port.Snapshot;
            var any = false;
            foreach (var m in members)
                if (m.Remote != null && m.Remote.Address.Equals(r.RemoteEndPoint.Address)) { Deliver(m, r); any = true; }
            if (!any) foreach (var m in members) Deliver(m, r);
        }
    }

    private static void Deliver(SplusUdpSocket socket, UdpReceiveResult r)
    {
        try { Crestron.Logos.SplusLibrary.Functions.DeliverUdp(socket, r.RemoteEndPoint, r.Buffer); }
        catch (Exception) { /* one module's failure must not stop the port */ }
    }
}
