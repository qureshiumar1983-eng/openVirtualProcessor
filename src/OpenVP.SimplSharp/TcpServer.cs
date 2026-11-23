using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using OpenVP.Core;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharp.CrestronSockets
{
    public enum ServerState { SERVER_NOT_LISTENING = 0, SERVER_LISTENING = 1, SERVER_CONNECTED = 2 }

    public delegate void TCPServerClientConnectCallback(TCPServer server, uint clientIndex);
    public delegate void TCPServerReceiveCallback(TCPServer server, uint clientIndex, int numberOfBytesReceived);
    public delegate void TCPServerSendCallback(TCPServer server, uint clientIndex, int numberOfBytesSent);
    public delegate void TCPServerSocketStatusChangeEventHandler(TCPServer server, uint clientIndex, SocketStatus status);

    public delegate void SecureTCPServerClientConnectCallback(SecureTCPServer server, uint clientIndex);
    public delegate void SecureTCPServerReceiveCallback(SecureTCPServer server, uint clientIndex, int numberOfBytesReceived);
    public delegate void SecureTCPServerSendCallback(SecureTCPServer server, uint clientIndex, int numberOfBytesSent);
    public delegate void SecureTCPServerSocketStatusChangeEventHandler(SecureTCPServer server, uint clientIndex, SocketStatus status);

    /// <summary>
    /// Shared engine for TCPServer and SecureTCPServer: a listener with up to N numbered clients (1-based
    /// client indexes, as in SIMPL#). Callbacks run on the slot thread.
    /// </summary>
    public abstract class TcpServerBase : IDisposable
    {
        internal sealed class Conn
        {
            public TcpClient Tcp;
            public System.IO.Stream Stream;
            public byte[] Buffer;
            public int Received;
            public SocketStatus Status = SocketStatus.SOCKET_STATUS_CONNECTED;
            public SocketRecord Rec;
        }

        private TcpListener _listener;
        private SocketRecord _listenRec;
        internal readonly ConcurrentDictionary<uint, Conn> Clients = new ConcurrentDictionary<uint, Conn>();
        private readonly int _bufferSize;
        private Action<uint> _alwaysCallback;

        protected TcpServerBase(string address, int port, int bufferSize, int maxClients)
        {
            AddressToAcceptConnectionFrom = address ?? "0.0.0.0";
            PortNumber = port;
            _bufferSize = Math.Max(1, bufferSize);
            MaxNumberOfClientSupported = Math.Max(1, maxClients);
            OpenVPBridge.Track(this);
        }

        public string AddressToAcceptConnectionFrom { get; set; }
        public int PortNumber { get; set; }
        public int MaxNumberOfClientSupported { get; }
        public int NumberOfClientsConnected => Clients.Count;
        public bool Nagle { get; set; }
        public bool ClearIncomingDataBuffer { get; set; }
        public int SocketSendOrReceiveTimeOutInMs { get; set; } = 30000;
        public EthernetAdapterType EthernetAdapterToBindTo { get; set; }
        public ServerState State => _listener == null ? ServerState.SERVER_NOT_LISTENING : Clients.IsEmpty ? ServerState.SERVER_LISTENING : ServerState.SERVER_CONNECTED;
        public SocketStatus ServerSocketStatus => Clients.IsEmpty ? SocketStatus.SOCKET_STATUS_NO_CONNECT : SocketStatus.SOCKET_STATUS_CONNECTED;

        private Conn First => Clients.OrderBy(k => k.Key).Select(k => k.Value).FirstOrDefault();
        private uint FirstIndex => Clients.Keys.OrderBy(k => k).FirstOrDefault();
        private Conn Get(uint i) => Clients.TryGetValue(i, out var c) ? c : null;

        public byte[] IncomingDataBuffer => First?.Buffer ?? Array.Empty<byte>();
        public bool DataAvailable => First?.Tcp.Available > 0;
        public string AddressServerAcceptedConnectionFrom => GetAddressServerAcceptedConnectionFromForSpecificClient(FirstIndex);
        public string LocalAddressServerAcceptedConnectionFrom => GetLocalAddressServerAcceptedConnectionFromForSpecificClient(FirstIndex);
        public int PortNumberServerAcceptedConnectionFrom => GetPortNumberServerAcceptedConnectionFromForSpecificClient(FirstIndex);

        public byte[] GetIncomingDataBufferForSpecificClient(uint i) => Get(i)?.Buffer ?? Array.Empty<byte>();
        public bool GetIfDataAvailableForSpecificClient(uint i) => Get(i)?.Tcp.Available > 0;
        public string GetAddressServerAcceptedConnectionFromForSpecificClient(uint i) => (Get(i)?.Tcp.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "";
        public string GetLocalAddressServerAcceptedConnectionFromForSpecificClient(uint i) => (Get(i)?.Tcp.Client.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "";
        public int GetPortNumberServerAcceptedConnectionFromForSpecificClient(uint i) => (Get(i)?.Tcp.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Port ?? 0;
        public SocketStatus GetServerSocketStatusForSpecificClient(uint i) => Get(i)?.Status ?? SocketStatus.SOCKET_STATUS_NO_CONNECT;
        public bool ClientConnected(uint i) => Get(i)?.Tcp.Connected == true;

        protected abstract System.IO.Stream Wrap(NetworkStream s);
        protected abstract void RaiseStatus(uint i, SocketStatus s);
        protected void Post(Action a) => OpenVPBridge.Post(a);

        private bool EnsureListening()
        {
            if (_listener != null) return true;
            try
            {
                var ip = AddressToAcceptConnectionFrom is null or "" or "0.0.0.0" ? System.Net.IPAddress.Any : System.Net.IPAddress.Parse(AddressToAcceptConnectionFrom);
                _listener = new TcpListener(System.Net.IPAddress.Any, PortNumber);
                _listener.Start();
                _listenRec = OpenVPBridge.Sock(this is SecureTCPServer ? "SecureTCPServer" : "TCPServer", "server", "tcp", $"0.0.0.0:{PortNumber}", "");
                _listenRec?.Connected();
                return true;
            }
            catch (Exception e)
            {
                OpenVPBridge.Log("Warning", $"TCP server on port {PortNumber}: {e.Message}");
                _listener = null;
                return false;
            }
        }

        private uint? Accept(string fromAddress)
        {
            if (!EnsureListening()) return null;
            while (true)
            {
                TcpClient tcp;
                try { tcp = _listener.AcceptTcpClient(); } catch { return null; }
                var remote = (tcp.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString();
                if (!string.IsNullOrEmpty(fromAddress) && fromAddress != "0.0.0.0" && remote != fromAddress) { tcp.Dispose(); continue; }
                if (Clients.Count >= MaxNumberOfClientSupported) { tcp.Dispose(); continue; }
                tcp.NoDelay = !Nagle;
                System.IO.Stream stream;
                try { stream = Wrap(tcp.GetStream()); }
                catch (Exception e) { OpenVPBridge.Log("Warning", "TLS handshake failed: " + e.Message); tcp.Dispose(); continue; }
                uint index = 1;
                while (Clients.ContainsKey(index)) index++;
                var rec = OpenVPBridge.Sock(this is SecureTCPServer ? "SecureTCPServer connection" : "TCPServer connection", "server", "tcp", OpenVPBridge.Ep(tcp.Client.LocalEndPoint), OpenVPBridge.Ep(tcp.Client.RemoteEndPoint));
                rec?.Connected();
                Clients[index] = new Conn { Tcp = tcp, Stream = stream, Buffer = new byte[_bufferSize], Rec = rec };
                RaiseStatus(index, SocketStatus.SOCKET_STATUS_CONNECTED);
                return index;
            }
        }

        protected SocketErrorCodes WaitSync(string from, ref uint index)
        {
            var i = Accept(from);
            if (i == null) return SocketErrorCodes.SOCKET_CONNECTION_IN_PROGRESS;
            index = i.Value;
            return SocketErrorCodes.SOCKET_OK;
        }

        protected SocketErrorCodes WaitAsync(string from, Action<uint> callback)
        {
            if (!EnsureListening()) return SocketErrorCodes.SOCKET_INVALID_PORT_NUMBER;
            Task.Run(() => { var i = Accept(from); if (i != null) Post(() => callback(i.Value)); });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        protected SocketErrorCodes WaitAlways(Action<uint> callback)
        {
            if (!EnsureListening()) return SocketErrorCodes.SOCKET_INVALID_PORT_NUMBER;
            if (_alwaysCallback != null) { _alwaysCallback = callback; return SocketErrorCodes.SOCKET_OPERATION_PENDING; }
            _alwaysCallback = callback;
            Task.Run(() =>
            {
                while (_listener != null)
                {
                    var i = Accept(null);
                    if (i == null) break;
                    var cb = _alwaysCallback;
                    Post(() => cb?.Invoke(i.Value));
                }
            });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public bool Pending() => _listener?.Pending() == true;

        protected int Receive(uint i)
        {
            var c = Get(i);
            if (c == null) return 0;
            try
            {
                var n = c.Stream.Read(c.Buffer, 0, c.Buffer.Length);
                if (n <= 0) { Drop(i, SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY); return 0; }
                c.Received = n;
                c.Rec?.Received(c.Buffer, 0, n);
                return n;
            }
            catch { Drop(i, SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY); return 0; }
        }

        protected SocketErrorCodes ReceiveAsync(uint i, Action<uint, int> callback)
        {
            if (Get(i) == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            Task.Run(() => { var n = Receive(i); Post(() => callback(i, n)); });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        protected SocketErrorCodes Send(uint i, byte[] data, int offset, int count)
        {
            var c = Get(i);
            if (c == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            try { c.Stream.Write(data, offset, count); c.Rec?.Sent(data, offset, count); return SocketErrorCodes.SOCKET_OK; }
            catch { Drop(i, SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY); return SocketErrorCodes.SOCKET_NOT_CONNECTED; }
        }

        protected SocketErrorCodes SendAsync(uint i, byte[] data, int offset, int count, Action<uint, int> callback)
        {
            if (Get(i) == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            Task.Run(() => { var r = Send(i, data, offset, count); Post(() => callback?.Invoke(i, r == SocketErrorCodes.SOCKET_OK ? count : -1)); });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        internal void Drop(uint i, SocketStatus status)
        {
            if (!Clients.TryRemove(i, out var c)) return;
            c.Rec?.Close(status == SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY ? "closed by remote" : "closed");
            try { c.Stream.Dispose(); c.Tcp.Dispose(); } catch { }
            RaiseStatus(i, status);
        }

        protected uint Default => FirstIndex;

        public SocketErrorCodes Disconnect() => Disconnect(FirstIndex);
        public SocketErrorCodes Disconnect(uint i) { Drop(i, SocketStatus.SOCKET_STATUS_NO_CONNECT); return SocketErrorCodes.SOCKET_OK; }
        public void DisconnectAll() { foreach (var k in Clients.Keys.ToList()) Disconnect(k); }
        public void Stop() { DisconnectAll(); try { _listener?.Stop(); } catch { } _listener = null; _alwaysCallback = null; _listenRec?.Close("stopped"); _listenRec = null; }
        public void HandleLinkLoss() { }
        public void HandleLinkUp() { }
        public void Dispose() { Stop(); OpenVPBridge.Untrack(this); }
    }

    public class TCPServer : TcpServerBase
    {
        public TCPServer(int port) : this("0.0.0.0", port, 4096, EthernetAdapterType.EthernetUnknownAdapter, 1) { }
        public TCPServer(int port, int maxClients) : this("0.0.0.0", port, 4096, EthernetAdapterType.EthernetUnknownAdapter, maxClients) { }
        public TCPServer(string address, int port, int bufferSize) : this(address, port, bufferSize, EthernetAdapterType.EthernetUnknownAdapter, 1) { }
        public TCPServer(string address, int port, int bufferSize, EthernetAdapterType adapter) : this(address, port, bufferSize, adapter, 1) { }
        public TCPServer(string address, int port, int bufferSize, EthernetAdapterType adapter, int maxClients) : base(address, port, bufferSize, maxClients) { EthernetAdapterToBindTo = adapter; }
        public TCPServer(Crestron.SimplSharp.IPEndPoint ep, int bufferSize) : this(ep.Address.ToString(), ep.Port, bufferSize) { }
        public TCPServer(Crestron.SimplSharp.IPEndPoint ep, int bufferSize, EthernetAdapterType adapter) : this(ep.Address.ToString(), ep.Port, bufferSize, adapter) { }
        public TCPServer(Crestron.SimplSharp.IPEndPoint ep, int bufferSize, EthernetAdapterType adapter, int maxClients) : this(ep.Address.ToString(), ep.Port, bufferSize, adapter, maxClients) { }

        public event TCPServerSocketStatusChangeEventHandler SocketStatusChange;
        protected override System.IO.Stream Wrap(NetworkStream s) => s;
        protected override void RaiseStatus(uint i, SocketStatus s) => Post(() => SocketStatusChange?.Invoke(this, i, s));

        public SocketErrorCodes WaitForConnection() { uint i = 0; return WaitSync(null, ref i); }
        public SocketErrorCodes WaitForConnection(ref uint index) => WaitSync(null, ref index);
        public SocketErrorCodes WaitForConnection(string from) { uint i = 0; return WaitSync(from, ref i); }
        public SocketErrorCodes WaitForConnection(string from, ref uint index) => WaitSync(from, ref index);
        public SocketErrorCodes WaitForConnection(Crestron.SimplSharp.IPAddress from) { uint i = 0; return WaitSync(from?.ToString(), ref i); }
        public SocketErrorCodes WaitForConnection(Crestron.SimplSharp.IPAddress from, ref uint index) => WaitSync(from?.ToString(), ref index);
        public SocketErrorCodes WaitForConnection(string from, TCPServerClientConnectCallback cb) => WaitAsync(from, i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnection(Crestron.SimplSharp.IPAddress from, TCPServerClientConnectCallback cb) => WaitAsync(from?.ToString(), i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionAsync(TCPServerClientConnectCallback cb) => WaitAsync(null, i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionAsync(string from, TCPServerClientConnectCallback cb) => WaitAsync(from, i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionAsync(Crestron.SimplSharp.IPAddress from, TCPServerClientConnectCallback cb) => WaitAsync(from?.ToString(), i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionsAlways(TCPServerClientConnectCallback cb) => WaitAlways(i => cb?.Invoke(this, i));

        public int ReceiveData() => Receive(Default);
        public int ReceiveData(uint i) => Receive(i);
        public int ReceiveData(uint i, int timeoutMs) => Receive(i);
        public SocketErrorCodes ReceiveDataAsync(TCPServerReceiveCallback cb) => ReceiveAsync(Default, (i, n) => cb?.Invoke(this, i, n));
        public SocketErrorCodes ReceiveDataAsync(uint i, TCPServerReceiveCallback cb) => ReceiveAsync(i, (c, n) => cb?.Invoke(this, c, n));
        public SocketErrorCodes ReceiveDataAsync(uint i, TCPServerReceiveCallback cb, int timeoutMs) => ReceiveAsync(i, (c, n) => cb?.Invoke(this, c, n));

        public SocketErrorCodes SendData(byte[] d, int len) => Send(Default, d, 0, len);
        public SocketErrorCodes SendData(uint i, byte[] d, int len) => Send(i, d, 0, len);
        public SocketErrorCodes SendData(uint i, byte[] d, int offset, int len) => Send(i, d, offset, len);
        public SocketErrorCodes SendData(uint i, byte[] d, int offset, int len, int timeoutMs) => Send(i, d, offset, len);
        public SocketErrorCodes SendDataAsync(byte[] d, int len, TCPServerSendCallback cb) => SendAsync(Default, d, 0, len, (i, n) => cb?.Invoke(this, i, n));
        public SocketErrorCodes SendDataAsync(byte[] d, int offset, int len, TCPServerSendCallback cb) => SendAsync(Default, d, offset, len, (i, n) => cb?.Invoke(this, i, n));
        public SocketErrorCodes SendDataAsync(uint i, byte[] d, int len, TCPServerSendCallback cb) => SendAsync(i, d, 0, len, (c, n) => cb?.Invoke(this, c, n));
        public SocketErrorCodes SendDataAsync(uint i, byte[] d, int offset, int len, TCPServerSendCallback cb) => SendAsync(i, d, offset, len, (c, n) => cb?.Invoke(this, c, n));
    }

    /// <summary>TLS server. Uses a self-signed certificate generated at start-up.</summary>
    public class SecureTCPServer : TcpServerBase
    {
        private static readonly Lazy<X509Certificate2> Cert = new Lazy<X509Certificate2>(() =>
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=OpenVP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
            return new X509Certificate2(c.Export(X509ContentType.Pfx));
        });

        public SecureTCPServer(int port) : this("0.0.0.0", port, 4096, EthernetAdapterType.EthernetUnknownAdapter, 1) { }
        public SecureTCPServer(int port, int maxClients) : this("0.0.0.0", port, 4096, EthernetAdapterType.EthernetUnknownAdapter, maxClients) { }
        public SecureTCPServer(string address, int port, int bufferSize) : this(address, port, bufferSize, EthernetAdapterType.EthernetUnknownAdapter, 1) { }
        public SecureTCPServer(string address, int port, int bufferSize, EthernetAdapterType adapter) : this(address, port, bufferSize, adapter, 1) { }
        public SecureTCPServer(string address, int port, int bufferSize, EthernetAdapterType adapter, int maxClients) : base(address, port, bufferSize, maxClients) { EthernetAdapterToBindTo = adapter; }
        public SecureTCPServer(Crestron.SimplSharp.IPEndPoint ep, int bufferSize) : this(ep.Address.ToString(), ep.Port, bufferSize) { }
        public SecureTCPServer(Crestron.SimplSharp.IPEndPoint ep, int bufferSize, EthernetAdapterType adapter) : this(ep.Address.ToString(), ep.Port, bufferSize, adapter) { }
        public SecureTCPServer(Crestron.SimplSharp.IPEndPoint ep, int bufferSize, EthernetAdapterType adapter, int maxClients) : this(ep.Address.ToString(), ep.Port, bufferSize, adapter, maxClients) { }

        public event SecureTCPServerSocketStatusChangeEventHandler SocketStatusChange;
        protected override System.IO.Stream Wrap(NetworkStream s)
        {
            var ssl = new SslStream(s, false);
            ssl.AuthenticateAsServer(Cert.Value);
            return ssl;
        }
        protected override void RaiseStatus(uint i, SocketStatus s) => Post(() => SocketStatusChange?.Invoke(this, i, s));

        public SocketErrorCodes WaitForConnection() { uint i = 0; return WaitSync(null, ref i); }
        public SocketErrorCodes WaitForConnection(ref uint index) => WaitSync(null, ref index);
        public SocketErrorCodes WaitForConnection(string from) { uint i = 0; return WaitSync(from, ref i); }
        public SocketErrorCodes WaitForConnection(string from, ref uint index) => WaitSync(from, ref index);
        public SocketErrorCodes WaitForConnection(Crestron.SimplSharp.IPAddress from) { uint i = 0; return WaitSync(from?.ToString(), ref i); }
        public SocketErrorCodes WaitForConnection(Crestron.SimplSharp.IPAddress from, ref uint index) => WaitSync(from?.ToString(), ref index);
        public SocketErrorCodes WaitForConnection(string from, SecureTCPServerClientConnectCallback cb) => WaitAsync(from, i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnection(Crestron.SimplSharp.IPAddress from, SecureTCPServerClientConnectCallback cb) => WaitAsync(from?.ToString(), i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionAsync(SecureTCPServerClientConnectCallback cb) => WaitAsync(null, i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionAsync(string from, SecureTCPServerClientConnectCallback cb) => WaitAsync(from, i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionAsync(Crestron.SimplSharp.IPAddress from, SecureTCPServerClientConnectCallback cb) => WaitAsync(from?.ToString(), i => cb?.Invoke(this, i));
        public SocketErrorCodes WaitForConnectionsAlways(SecureTCPServerClientConnectCallback cb) => WaitAlways(i => cb?.Invoke(this, i));

        public int ReceiveData() => Receive(Default);
        public int ReceiveData(uint i) => Receive(i);
        public int ReceiveData(uint i, int timeoutMs) => Receive(i);
        public SocketErrorCodes ReceiveDataAsync(SecureTCPServerReceiveCallback cb) => ReceiveAsync(Default, (i, n) => cb?.Invoke(this, i, n));
        public SocketErrorCodes ReceiveDataAsync(uint i, SecureTCPServerReceiveCallback cb) => ReceiveAsync(i, (c, n) => cb?.Invoke(this, c, n));
        public SocketErrorCodes ReceiveDataAsync(uint i, SecureTCPServerReceiveCallback cb, int timeoutMs) => ReceiveAsync(i, (c, n) => cb?.Invoke(this, c, n));

        public SocketErrorCodes SendData(byte[] d, int len) => Send(Default, d, 0, len);
        public SocketErrorCodes SendData(uint i, byte[] d, int len) => Send(i, d, 0, len);
        public SocketErrorCodes SendData(uint i, byte[] d, int offset, int len) => Send(i, d, offset, len);
        public SocketErrorCodes SendData(uint i, byte[] d, int offset, int len, int timeoutMs) => Send(i, d, offset, len);
        public SocketErrorCodes SendDataAsync(byte[] d, int len, SecureTCPServerSendCallback cb) => SendAsync(Default, d, 0, len, (i, n) => cb?.Invoke(this, i, n));
        public SocketErrorCodes SendDataAsync(byte[] d, int offset, int len, SecureTCPServerSendCallback cb) => SendAsync(Default, d, offset, len, (i, n) => cb?.Invoke(this, i, n));
        public SocketErrorCodes SendDataAsync(uint i, byte[] d, int len, SecureTCPServerSendCallback cb) => SendAsync(i, d, 0, len, (c, n) => cb?.Invoke(this, c, n));
        public SocketErrorCodes SendDataAsync(uint i, byte[] d, int offset, int len, SecureTCPServerSendCallback cb) => SendAsync(i, d, offset, len, (c, n) => cb?.Invoke(this, c, n));
    }
}
