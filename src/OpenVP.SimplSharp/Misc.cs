using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharp
{
    /// <summary>Blocking queue with the SIMPL# API shape.</summary>
    public class CrestronQueue<T> : IDisposable
    {
        private readonly BlockingCollection<T> _q;
        public CrestronQueue() : this(int.MaxValue) { }
        public CrestronQueue(int capacity) { _q = new BlockingCollection<T>(new ConcurrentQueue<T>(), Math.Max(1, capacity)); }
        public int Count => _q.Count;
        public bool IsEmpty => _q.Count == 0;
        public bool IsFull => _q.BoundedCapacity > 0 && _q.Count >= _q.BoundedCapacity;
        public void Enqueue(T item) => _q.Add(item);
        public bool TryToEnqueue(T item) => _q.TryAdd(item);
        public T Dequeue() => _q.Take();
        public T Dequeue(int timeoutMs) => _q.TryTake(out var v, timeoutMs) ? v : default;
        public T Dequeue(uint timeoutMs) => _q.TryTake(out var v, timeoutMs == uint.MaxValue ? -1 : (int)Math.Min(timeoutMs, int.MaxValue)) ? v : default;
        public bool Dequeue(ref T item) { if (_q.TryTake(out var v)) { item = v; return true; } return false; }
        public bool Enqueue(T item, uint timeoutMs) => _q.TryAdd(item, (int)Math.Min(timeoutMs, int.MaxValue));
        public T TryToDequeue() => _q.TryTake(out var v) ? v : default;
        public T Peek() { foreach (var v in _q) return v; return default; }
        public bool Disposed { get; private set; }
        public bool TryToDequeue(out T item) => _q.TryTake(out item);
        public void Clear() { while (_q.TryTake(out _)) { } }
        public void Dispose() { Disposed = true; _q.Dispose(); }
    }

    /// <summary>IP address type used by the SIMPL# socket classes.</summary>
    public class IPAddress
    {
        internal readonly System.Net.IPAddress Inner;
        internal IPAddress(System.Net.IPAddress inner) { Inner = inner; }
        public IPAddress(byte[] address) { Inner = new System.Net.IPAddress(address); }
        public IPAddress(long address) { Inner = new System.Net.IPAddress(address); }
        public static IPAddress Parse(string s) => new IPAddress(System.Net.IPAddress.Parse(s));
        public static IPAddress Any => new IPAddress(System.Net.IPAddress.Any);
        public static IPAddress Loopback => new IPAddress(System.Net.IPAddress.Loopback);
        public static IPAddress None => new IPAddress(System.Net.IPAddress.None);
        public static IPAddress Broadcast => new IPAddress(System.Net.IPAddress.Broadcast);
        public byte[] GetAddressBytes() => Inner.GetAddressBytes();
        public override string ToString() => Inner.ToString();
    }

    public class IPEndPoint
    {
        public IPEndPoint(IPAddress address, int port) { Address = address; Port = port; }
        public IPAddress Address { get; set; }
        public int Port { get; set; }
        public override string ToString() => $"{Address}:{Port}";
    }

    public class IPHostEntry
    {
        public IPAddress[] AddressList { get; set; } = Array.Empty<IPAddress>();
        public string HostName { get; set; } = string.Empty;
    }

    public static class Dns
    {
        public static IPHostEntry GetHostEntry(string host)
        {
            var e = System.Net.Dns.GetHostEntry(host);
            return new IPHostEntry { HostName = e.HostName, AddressList = e.AddressList.Select(a => new IPAddress(a)).ToArray() };
        }
    }

    public enum eEthernetAdapterParameter { }

    public static class CrestronEthernetHelper
    {
        public enum ETHERNET_PARAMETER_TO_GET
        {
            GET_CURRENT_IP_ADDRESS = 0, GET_CURRENT_IP_MASK = 1, GET_CURRENT_ROUTER = 2, GET_HOSTNAME = 3,
            GET_LINK_STATUS = 4, GET_DOMAIN_NAME = 5, GET_MAC_ADDRESS = 6, GET_DHCP_STATUS = 7, GET_STATIC_IPADDRESS = 8
        }

        public static short GetAdapterdIdForSpecifiedAdapterType(EthernetAdapterType type) => 0;

        public enum PortForwardingUserPatRetCodes { NoErr = 0, Error = 1 }
        public class PortForward
        {
            public ushort ExternalPort { get; set; }
            public ushort InternalPort { get; set; }
            public string IPAddress { get; set; }
            public string IPProtocol { get; set; }
        }
        // A virtual processor has no control-subnet router; port forwarding requests succeed without effect.
        public static PortForwardingUserPatRetCodes AddPortForwarding(ushort ext, ushort internalPort, string address, string protocol) => PortForwardingUserPatRetCodes.NoErr;
        public static PortForwardingUserPatRetCodes RemovePortForwarding(ushort ext, ushort internalPort, string address, string protocol) => PortForwardingUserPatRetCodes.NoErr;
        public static PortForwardingUserPatRetCodes ListPortForward(ref List<PortForward> list) { list ??= new List<PortForward>(); return PortForwardingUserPatRetCodes.NoErr; }

        public static string GetEthernetParameter(ETHERNET_PARAMETER_TO_GET what, short adapter)
        {
            try
            {
                var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                         n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
                var ip = nic?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                return what switch
                {
                    ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_ADDRESS or ETHERNET_PARAMETER_TO_GET.GET_STATIC_IPADDRESS => ip?.Address.ToString() ?? "127.0.0.1",
                    ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_MASK => ip?.IPv4Mask.ToString() ?? "255.0.0.0",
                    ETHERNET_PARAMETER_TO_GET.GET_HOSTNAME => System.Net.Dns.GetHostName(),
                    ETHERNET_PARAMETER_TO_GET.GET_MAC_ADDRESS => string.Join(":", (nic?.GetPhysicalAddress().GetAddressBytes() ?? new byte[6]).Select(b => b.ToString("x2"))),
                    ETHERNET_PARAMETER_TO_GET.GET_LINK_STATUS => "1",
                    ETHERNET_PARAMETER_TO_GET.GET_DHCP_STATUS => "OFF",
                    _ => string.Empty
                };
            }
            catch { return string.Empty; }
        }
    }
}

namespace Crestron.SimplSharp.CrestronDataStore
{
    public class CrestronDataStore
    {
        public enum CDS_ERROR
        {
            CDS_SUCCESS = 0, CDS_ERROR_INVALID_PARAMETER = 1, CDS_RECORD_NOT_FOUND = 2, CDS_TAG_NOT_FOUND = 3,
            CDS_INVALID_TAG_NAME = 4, CDS_INVALID_VALUE = 5, CDS_ERROR_MEMORY = 6, CDS_ERROR_NOT_INITIALIZED = 7
        }
    }

    /// <summary>Key/value store kept as JSON: "local" values per slot, "global" values shared by the processor.</summary>
    public static class CrestronDataStoreStatic
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, JsonElement> _local, _global;

        private static string LocalPath => System.IO.Path.Combine(OpenVPBridge.Slot?.Directory ?? System.IO.Path.GetTempPath(), "datastore.json");
        private static string GlobalPath => System.IO.Path.Combine(OpenVPBridge.Slot?.Processor.DataDirectory ?? System.IO.Path.GetTempPath(), "datastore-global.json");

        public static CrestronDataStore.CDS_ERROR InitCrestronDataStore()
        {
            lock (Gate) { _local = Load(LocalPath); _global = Load(GlobalPath); }
            return CrestronDataStore.CDS_ERROR.CDS_SUCCESS;
        }

        private static Dictionary<string, JsonElement> Load(string path)
        {
            try { return System.IO.File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(System.IO.File.ReadAllText(path)) : new Dictionary<string, JsonElement>(); }
            catch { return new Dictionary<string, JsonElement>(); }
        }

        private static CrestronDataStore.CDS_ERROR Set(bool global, string tag, object value)
        {
            if (string.IsNullOrEmpty(tag)) return CrestronDataStore.CDS_ERROR.CDS_INVALID_TAG_NAME;
            lock (Gate)
            {
                if (_local == null) InitCrestronDataStore();
                var d = global ? _global : _local;
                d[tag] = JsonSerializer.SerializeToElement(value);
                var path = global ? GlobalPath : LocalPath;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, JsonSerializer.Serialize(d));
            }
            return CrestronDataStore.CDS_ERROR.CDS_SUCCESS;
        }

        private static CrestronDataStore.CDS_ERROR Get<T>(bool global, string tag, ref T value)
        {
            lock (Gate)
            {
                if (_local == null) InitCrestronDataStore();
                var d = global ? _global : _local;
                if (!d.TryGetValue(tag ?? "", out var e)) return CrestronDataStore.CDS_ERROR.CDS_TAG_NOT_FOUND;
                try { value = e.Deserialize<T>(); return CrestronDataStore.CDS_ERROR.CDS_SUCCESS; }
                catch { return CrestronDataStore.CDS_ERROR.CDS_INVALID_VALUE; }
            }
        }

        public static CrestronDataStore.CDS_ERROR SetLocalBoolValue(string t, bool v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalIntValue(string t, int v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalUintValue(string t, uint v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalLongValue(string t, long v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalULongValue(string t, ulong v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalDoubleValue(string t, double v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalStringValue(string t, string v) => Set(false, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalBoolValue(string t, bool v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalIntValue(string t, int v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalUintValue(string t, uint v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalLongValue(string t, long v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalULongValue(string t, ulong v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalDoubleValue(string t, double v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetGlobalStringValue(string t, string v) => Set(true, t, v);

        public static CrestronDataStore.CDS_ERROR SetGlobalLongValue(string t, ulong v) => Set(true, t, v);
        public static CrestronDataStore.CDS_ERROR SetLocalLongValue(string t, ulong v) => Set(false, t, v);
        public static bool Flush() => true;
        public static CrestronDataStore.CDS_ERROR clearLocal(string tag) => Clear(false, tag);
        public static CrestronDataStore.CDS_ERROR clearGlobal(string tag) => Clear(true, tag);
        private static CrestronDataStore.CDS_ERROR Clear(bool global, string tag)
        {
            lock (Gate)
            {
                if (_local == null) InitCrestronDataStore();
                var d = global ? _global : _local;
                if (!d.Remove(tag ?? "")) return CrestronDataStore.CDS_ERROR.CDS_TAG_NOT_FOUND;
                var path = global ? GlobalPath : LocalPath;
                System.IO.File.WriteAllText(path, JsonSerializer.Serialize(d));
            }
            return CrestronDataStore.CDS_ERROR.CDS_SUCCESS;
        }

        public static CrestronDataStore.CDS_ERROR GetLocalBoolValue(string t, ref bool v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetLocalIntValue(string t, ref int v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetLocalUintValue(string t, ref uint v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetLocalLongValue(string t, ref long v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetLocalULongValue(string t, ref ulong v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetLocalDoubleValue(string t, ref double v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetLocalStringValue(string t, ref string v) => Get(false, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalBoolValue(string t, ref bool v) => Get(true, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalIntValue(string t, ref int v) => Get(true, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalUintValue(string t, ref uint v) => Get(true, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalLongValue(string t, ref long v) => Get(true, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalULongValue(string t, ref ulong v) => Get(true, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalDoubleValue(string t, ref double v) => Get(true, t, ref v);
        public static CrestronDataStore.CDS_ERROR GetGlobalStringValue(string t, ref string v) => Get(true, t, ref v);
    }
}

namespace Crestron.SimplSharp.CrestronSockets
{
    public delegate void UDPServerReceiveCallback(UDPServer server, int numberOfBytesReceived);
    public delegate void UDPServerSendCallbackEx(UDPServer server, int numberOfBytesSent, object userObject);

    /// <summary>UDP endpoint with the SIMPL# API shape. Callbacks run on the slot thread.</summary>
    public class UDPServer : IDisposable
    {
        private UdpClient _udp;
        private System.Net.IPEndPoint _remote;

        public UDPServer() : this("0.0.0.0", 0, 1024) { }
        public UDPServer(string address, int port, int bufferSize) : this(address, port, bufferSize, port) { }
        public UDPServer(string address, int port, int bufferSize, EthernetAdapterType adapter, int localPort) : this(address, port, bufferSize, localPort) { }
        public UDPServer(string address, int port, int bufferSize, EthernetAdapterType adapter) : this(address, port, bufferSize, port) { }
        public EthernetAdapterType EthernetAdapterToBindTo { get; set; }
        public bool ClearIncomingDataBuffer { get; set; }
        public UDPServer(string address, int port, int bufferSize, int localPort)
        {
            AddressToAcceptConnectionFrom = address;
            PortNumber = port;
            LocalPort = localPort;
            IncomingDataBuffer = new byte[Math.Max(1, bufferSize)];
            OpenVPBridge.Track(this);
        }

        public string AddressToAcceptConnectionFrom { get; set; }
        public int PortNumber { get; set; }
        public int LocalPort { get; set; }
        public byte[] IncomingDataBuffer { get; private set; }
        public string IPAddressLastMessageReceivedFrom { get; private set; } = "";
        public int IPPortLastMessageReceivedFrom { get; private set; }

        public SocketErrorCodes EnableUDPServer()
        {
            try
            {
                _udp?.Dispose();
                _udp = new UdpClient(LocalPort) { EnableBroadcast = true };
                _remote = AddressToAcceptConnectionFrom is null or "0.0.0.0" ? null
                    : new System.Net.IPEndPoint(System.Net.Dns.GetHostAddresses(AddressToAcceptConnectionFrom).First(), PortNumber);
                return SocketErrorCodes.SOCKET_OK;
            }
            catch { return SocketErrorCodes.SOCKET_INVALID_PORT_NUMBER; }
        }

        public SocketErrorCodes EnableUDPServer(string address, int port) { AddressToAcceptConnectionFrom = address; PortNumber = port; LocalPort = port; return EnableUDPServer(); }
        public SocketErrorCodes EnableUDPServer(Crestron.SimplSharp.IPAddress address, int port) => EnableUDPServer(address.ToString(), port);
        public SocketErrorCodes EnableUDPServer(Crestron.SimplSharp.IPAddress address, int port, int localPort) { LocalPort = localPort; AddressToAcceptConnectionFrom = address.ToString(); PortNumber = port; return EnableUDPServer(); }

        public SocketErrorCodes DisableUDPServer() { _udp?.Dispose(); _udp = null; return SocketErrorCodes.SOCKET_OK; }

        public SocketErrorCodes SendData(byte[] data, int length)
        {
            if (_udp == null || _remote == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            _udp.Send(data, length, _remote);
            return SocketErrorCodes.SOCKET_OK;
        }

        public SocketErrorCodes SendData(byte[] data, int length, Crestron.SimplSharp.IPEndPoint to)
        {
            if (_udp == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            _udp.Send(data, length, new System.Net.IPEndPoint(to.Address.Inner, to.Port));
            return SocketErrorCodes.SOCKET_OK;
        }

        public SocketErrorCodes SendData(byte[] data, int length, string address, int port)
        {
            if (_udp == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            _udp.Send(data, length, address, port);
            return SocketErrorCodes.SOCKET_OK;
        }

        public SocketErrorCodes SendDataAsync(byte[] data, int length, string address, int port, UDPServerSendCallbackEx callback, object userObject)
        {
            if (_udp == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            _udp.SendAsync(data, length, address, port).ContinueWith(t => OpenVPBridge.Post(() => callback?.Invoke(this, t.IsFaulted ? -1 : t.Result, userObject)));
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public SocketErrorCodes ReceiveDataAsync(UDPServerReceiveCallback callback)
        {
            if (_udp == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            _udp.ReceiveAsync().ContinueWith(t =>
            {
                var n = 0;
                if (!t.IsFaulted && !t.IsCanceled)
                {
                    var r = t.Result;
                    IncomingDataBuffer = r.Buffer;
                    n = r.Buffer.Length;
                    IPAddressLastMessageReceivedFrom = r.RemoteEndPoint.Address.ToString();
                    IPPortLastMessageReceivedFrom = r.RemoteEndPoint.Port;
                }
                OpenVPBridge.Post(() => callback?.Invoke(this, n));
            });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public void Dispose()
        {
            _udp?.Dispose();
            OpenVPBridge.Untrack(this);
        }
    }
}
