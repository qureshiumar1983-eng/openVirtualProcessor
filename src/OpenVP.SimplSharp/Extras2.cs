using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OpenVP.SimplSharp;
using SR = System.Reflection;

namespace Crestron.SimplSharp
{
    public enum CrestronStringEncoding { eEncodingUnknown = 0, eEncodingASCII = 1, eEncodingUTF16 = 2 }
    public enum eCrestronSeries { Series2 = 2, Series3 = 3, Series4 = 4 }

    public static class CMonitor
    {
        public static void Enter(object o) => Monitor.Enter(o);
        public static bool TryEnter(object o) => Monitor.TryEnter(o);
        public static bool TryEnter(object o, int timeoutMs) => Monitor.TryEnter(o, timeoutMs);
        public static void Exit(object o) => Monitor.Exit(o);
        public static void Pulse(object o) => Monitor.Pulse(o);
        public static void PulseAll(object o) => Monitor.PulseAll(o);
        public static bool Wait(object o) => Monitor.Wait(o);
        public static bool Wait(object o, int timeoutMs) => Monitor.Wait(o, timeoutMs);
    }

    /// <summary>Base of CEvent-style handles.</summary>
    public abstract class CEventHandle : IDisposable
    {
        protected readonly EventWaitHandle Handle;
        protected CEventHandle(bool autoReset, bool initialState) { Handle = new EventWaitHandle(initialState, autoReset ? EventResetMode.AutoReset : EventResetMode.ManualReset); }
        public bool Wait() => Handle.WaitOne();
        public bool Wait(int timeoutMs) => timeoutMs < 0 ? Handle.WaitOne() : Handle.WaitOne(timeoutMs);
        public bool Set() => Handle.Set();
        public bool Reset() => Handle.Reset();
        public bool Disposed { get; private set; }
        public void Close() => Dispose();
        public void Dispose() { Disposed = true; Handle.Dispose(); }
    }

    public class CrestronQueue : CrestronQueue<object>
    {
        public CrestronQueue() { }
        public CrestronQueue(int capacity) : base(capacity) { }
    }

    public class ReadOnlyDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly Dictionary<TKey, TValue> _d;
        public ReadOnlyDictionary(Dictionary<TKey, TValue> source) { _d = new Dictionary<TKey, TValue>(source ?? new Dictionary<TKey, TValue>()); }
        public TValue this[TKey key] => _d[key];
        public int Count => _d.Count;
        public bool ContainsKey(TKey key) => _d.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _d.TryGetValue(key, out value);
        public Dictionary<TKey, TValue>.KeyCollection Keys => _d.Keys;
        public Dictionary<TKey, TValue>.ValueCollection Values => _d.Values;
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _d.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Not available on a virtual processor.</summary>
    public static class JsonDb
    {
        public static bool Supported => false;
    }

    public static class CrestronZIP
    {
        public enum ResultCode { ZR_OK = 0, ZR_FAILED = 1 }

        public static ResultCode Unzip(string zipFile, string destination)
        {
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(CrestronIO.Paths.Map(zipFile), CrestronIO.Paths.Map(destination), true);
                return ResultCode.ZR_OK;
            }
            catch (Exception e) { OpenVPBridge.Log("Warning", "Unzip: " + e.Message); return ResultCode.ZR_FAILED; }
        }

        public static ResultCode Zip(string folder, string zipFile)
        {
            try { System.IO.Compression.ZipFile.CreateFromDirectory(CrestronIO.Paths.Map(folder), CrestronIO.Paths.Map(zipFile)); return ResultCode.ZR_OK; }
            catch (Exception e) { OpenVPBridge.Log("Warning", "Zip: " + e.Message); return ResultCode.ZR_FAILED; }
        }
    }
}

namespace Crestron.SimplSharp.Reporting
{
    public delegate void DeviceReportingCallback<T>(object userObject, T value);

    /// <summary>Crestron cloud (XiO) device reporting. A virtual processor has no cloud link; updates are dropped.</summary>
    public class SimplSharpDeviceReporting<T> : IDisposable
    {
        public SimplSharpDeviceReporting(string name, object userObject, DeviceReportingCallback<T> callback) { Name = name; }
        public string Name { get; }
        public string DeviceUuid { get; } = Guid.NewGuid().ToString();
        public string ProgramLocation => InitialParametersClass.ProgramDirectory;
        public void SendUpdate(T value) { }
        public void Dispose() { }
    }
}

namespace Crestron.SimplSharp.Cryptography.X509Certificates
{
    public class X509Certificate
    {
        internal System.Security.Cryptography.X509Certificates.X509Certificate2 Inner;
        public X509Certificate() { }
        public X509Certificate(byte[] data) { Inner = new System.Security.Cryptography.X509Certificates.X509Certificate2(data); }
        public byte[] GetRawCertData() => Inner?.RawData ?? Array.Empty<byte>();
        public string Subject => Inner?.Subject ?? "";
        public string Issuer => Inner?.Issuer ?? "";
    }
}

namespace Crestron.SimplSharp.CrestronIO.Compression
{
    public enum CompressionMode { Decompress = 0, Compress = 1 }

    public class GZipStream : Stream
    {
        public GZipStream(Stream stream, CompressionMode mode) : this(stream, mode, false) { }
        public GZipStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : base(new System.IO.Compression.GZipStream(stream.Inner, mode == CompressionMode.Compress ? System.IO.Compression.CompressionMode.Compress : System.IO.Compression.CompressionMode.Decompress, leaveOpen)) { }
    }

    public class DeflateStream : Stream
    {
        public DeflateStream(Stream stream, CompressionMode mode) : this(stream, mode, false) { }
        public DeflateStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : base(new System.IO.Compression.DeflateStream(stream.Inner, mode == CompressionMode.Compress ? System.IO.Compression.CompressionMode.Compress : System.IO.Compression.CompressionMode.Decompress, leaveOpen)) { }
    }
}

namespace Crestron.SimplSharp.CrestronIO
{
    public class DirectoryInfo : FileSystemInfo
    {
        private readonly System.IO.DirectoryInfo _d;
        private readonly string _path;
        public DirectoryInfo(string path) { _path = path; _d = new System.IO.DirectoryInfo(Paths.Map(path)); }
        public override string Name => _d.Name;
        public override string FullName => _d.FullName;
        public override bool Exists { get { _d.Refresh(); if (!_d.Exists) Paths.Missing(_path, folder: true); return _d.Exists; } }
        public DateTime LastWriteTime => _d.LastWriteTime;
        public void Create() => _d.Create();
        public FileInfo[] GetFiles() => _d.GetFiles().Select(f => new FileInfo(f.FullName)).ToArray();
        public DirectoryInfo[] GetDirectories() => _d.GetDirectories().Select(d => new DirectoryInfo(d.FullName)).ToArray();
    }

    public class EndOfStreamException : System.IO.IOException
    {
        public EndOfStreamException() { }
        public EndOfStreamException(string message) : base(message) { }
    }

    public class StringWriter : TextWriter
    {
        public StringWriter() { Inner = new System.IO.StringWriter(); }
        public override string ToString() => Inner.ToString();
    }

    public class StringReader : TextReader
    {
        public StringReader(string s) { Inner = new System.IO.StringReader(s ?? ""); }
    }
}

namespace Crestron.SimplSharp.Reflection
{
    [Flags]
    public enum BindingFlags
    {
        Default = 0, IgnoreCase = 1, DeclaredOnly = 2, Instance = 4, Static = 8, Public = 16, NonPublic = 32,
        FlattenHierarchy = 64, InvokeMethod = 256, CreateInstance = 512, GetField = 1024, SetField = 2048,
        GetProperty = 4096, SetProperty = 8192
    }

    /// <summary>SIMPL#'s wrapper around System.Type.</summary>
    public class CType : MemberInfo
    {
        internal readonly Type T;
        internal CType(Type t) : base(t) { T = t; }
        public static implicit operator CType(Type t) => t == null ? null : new CType(t);
        public static implicit operator Type(CType t) => t?.T;
        public static bool operator ==(CType a, CType b) => a?.T == b?.T;
        public static bool operator !=(CType a, CType b) => a?.T != b?.T;
        public override bool Equals(object o) => o is CType c ? c.T == T : o is Type t && t == T;
        public override int GetHashCode() => T.GetHashCode();
        public override string ToString() => T.ToString();
        public static CType GetType(string name) => Type.GetType(name);

        public string FullName => T.FullName;
        public string Namespace => T.Namespace;
        public string AssemblyQualifiedName => T.AssemblyQualifiedName;
        public Assembly Assembly => new Assembly(T.Assembly);
        public CType BaseType => T.BaseType;
        public bool IsEnum => T.IsEnum;
        public bool IsClass => T.IsClass;
        public bool IsInterface => T.IsInterface;
        public bool IsValueType => T.IsValueType;
        public bool IsAbstract => T.IsAbstract;
        public bool IsArray => T.IsArray;
        public bool IsGenericType => T.IsGenericType;
        public bool IsPublic => T.IsPublic;
        public bool IsAnsiClass => T.IsAnsiClass;
        public bool IsPrimitive => T.IsPrimitive;
        public CType GetElementType() => T.GetElementType();
        public CType[] GetGenericArguments() => T.GetGenericArguments().Select(x => (CType)x).ToArray();
        public CType GetGenericTypeDefinition() => T.GetGenericTypeDefinition();
        public CType MakeGenericType(params CType[] args) => T.MakeGenericType(args.Select(a => a.T).ToArray());
        public CType[] GetInterfaces() => T.GetInterfaces().Select(x => (CType)x).ToArray();
        public bool IsAssignableFrom(CType other) => other != null && T.IsAssignableFrom(other.T);
        public bool IsSubclassOf(CType other) => other != null && T.IsSubclassOf(other.T);
        public bool IsInstanceOfType(object o) => T.IsInstanceOfType(o);

        private static SR.BindingFlags F(BindingFlags f) => (SR.BindingFlags)(int)f;
        public MethodInfo GetMethod(string name) => Wrap(T.GetMethod(name));
        public MethodInfo GetMethod(string name, BindingFlags f) => Wrap(T.GetMethod(name, F(f)));
        public MethodInfo GetMethod(string name, CType[] types) => Wrap(T.GetMethod(name, types.Select(x => x.T).ToArray()));
        public MethodInfo[] GetMethods() => T.GetMethods().Select(m => new MethodInfo(m)).ToArray();
        public MethodInfo[] GetMethods(BindingFlags f) => T.GetMethods(F(f)).Select(m => new MethodInfo(m)).ToArray();
        public PropertyInfo GetProperty(string name) => T.GetProperty(name) is { } p ? new PropertyInfo(p) : null;
        public PropertyInfo GetProperty(string name, BindingFlags f) => T.GetProperty(name, F(f)) is { } p ? new PropertyInfo(p) : null;
        public PropertyInfo[] GetProperties() => T.GetProperties().Select(p => new PropertyInfo(p)).ToArray();
        public PropertyInfo[] GetProperties(BindingFlags f) => T.GetProperties(F(f)).Select(p => new PropertyInfo(p)).ToArray();
        public FieldInfo GetField(string name) => T.GetField(name) is { } x ? new FieldInfo(x) : null;
        public FieldInfo GetField(string name, BindingFlags f) => T.GetField(name, F(f)) is { } x ? new FieldInfo(x) : null;
        public FieldInfo[] GetFields() => T.GetFields().Select(x => new FieldInfo(x)).ToArray();
        public FieldInfo[] GetFields(BindingFlags f) => T.GetFields(F(f)).Select(x => new FieldInfo(x)).ToArray();
        public EventInfo GetEvent(string name) => T.GetEvent(name) is { } e ? new EventInfo(e) : null;
        public EventInfo[] GetEvents() => T.GetEvents().Select(e => new EventInfo(e)).ToArray();
        public EventInfo[] GetEvents(BindingFlags f) => T.GetEvents(F(f)).Select(e => new EventInfo(e)).ToArray();
        public MemberInfo[] GetMember(string name) => T.GetMember(name).Select(m => new MemberInfo(m)).ToArray();
        public CType[] GetNestedTypes(BindingFlags f) => T.GetNestedTypes(F(f)).Select(x => (CType)x).ToArray();
        public static TypeCode GetTypeCode(CType t) => Type.GetTypeCode(t?.T);
        public ConstructorInfo GetConstructor(CType[] types) => T.GetConstructor(types.Select(x => x.T).ToArray()) is { } c ? new ConstructorInfo(c) : null;
        public ConstructorInfo[] GetConstructors() => T.GetConstructors().Select(c => new ConstructorInfo(c)).ToArray();
        public object InvokeMember(string name, BindingFlags f, object binder, object target, object[] args) => T.InvokeMember(name, F(f), null, target, args);
        private static MethodInfo Wrap(SR.MethodInfo m) => m == null ? null : new MethodInfo(m);
    }

    public static class TypeExtensions
    {
        public static CType GetCType(this object o) => o?.GetType();
        public static CType GetCType(this Type t) => t;
    }

    public class MemberInfo
    {
        internal readonly SR.MemberInfo M;
        internal MemberInfo(SR.MemberInfo m) { M = m; }
        public string Name => M.Name;
        public CType DeclaringType => M.DeclaringType;
        public object[] GetCustomAttributes(bool inherit) => M.GetCustomAttributes(inherit);
        public object[] GetCustomAttributes(CType type, bool inherit) => M.GetCustomAttributes(type.T, inherit);
        public bool IsDefined(CType type, bool inherit) => M.IsDefined(type.T, inherit);
    }

    public class MethodBase : MemberInfo
    {
        internal readonly SR.MethodBase B;
        internal MethodBase(SR.MethodBase b) : base(b) { B = b; }
        public ParameterInfo[] GetParameters() => B.GetParameters().Select(p => new ParameterInfo(p)).ToArray();
        public object Invoke(object target, object[] args) => B.Invoke(target, args);
        public bool IsStatic => B.IsStatic;
        public bool IsPublic => B.IsPublic;
        public bool IsConstructor => B.IsConstructor;
        public bool ContainsGenericParameters => B.ContainsGenericParameters;
        public CType[] GetGenericArguments() => B.GetGenericArguments().Select(x => (CType)x).ToArray();
    }

    public class MethodInfo : MethodBase
    {
        internal readonly SR.MethodInfo Mi;
        internal MethodInfo(SR.MethodInfo m) : base(m) { Mi = m; }
        public CType ReturnType => Mi.ReturnType;
        public MethodInfo MakeGenericMethod(params CType[] args) => new MethodInfo(Mi.MakeGenericMethod(args.Select(a => a.T).ToArray()));
    }

    public class ConstructorInfo : MethodBase
    {
        internal readonly SR.ConstructorInfo C;
        internal ConstructorInfo(SR.ConstructorInfo c) : base(c) { C = c; }
        public object Invoke(object[] args) => C.Invoke(args);
    }

    public class PropertyInfo : MemberInfo
    {
        internal readonly SR.PropertyInfo P;
        internal PropertyInfo(SR.PropertyInfo p) : base(p) { P = p; }
        public CType PropertyType => P.PropertyType;
        public bool CanRead => P.CanRead;
        public bool CanWrite => P.CanWrite;
        public object GetValue(object target, object[] index) => P.GetValue(target, index);
        public void SetValue(object target, object value, object[] index) => P.SetValue(target, value, index);
        public MethodInfo GetGetMethod() => P.GetGetMethod() is { } m ? new MethodInfo(m) : null;
        public MethodInfo GetGetMethod(bool nonPublic) => P.GetGetMethod(nonPublic) is { } m ? new MethodInfo(m) : null;
        public MethodInfo GetSetMethod() => P.GetSetMethod() is { } m ? new MethodInfo(m) : null;
        public MethodInfo GetSetMethod(bool nonPublic) => P.GetSetMethod(nonPublic) is { } m ? new MethodInfo(m) : null;
    }

    public class FieldInfo : MemberInfo
    {
        internal readonly SR.FieldInfo F;
        internal FieldInfo(SR.FieldInfo f) : base(f) { F = f; }
        public CType FieldType => F.FieldType;
        public object GetValue(object target) => F.GetValue(target);
        public void SetValue(object target, object value) => F.SetValue(target, value);
        public bool IsStatic => F.IsStatic;
        public bool IsLiteral => F.IsLiteral;
        public bool IsSpecialName => F.IsSpecialName;
    }

    public class EventInfo : MemberInfo
    {
        internal readonly SR.EventInfo E;
        internal EventInfo(SR.EventInfo e) : base(e) { E = e; }
        public CType EventHandlerType => E.EventHandlerType;
        public void AddEventHandler(object target, Delegate handler) => E.AddEventHandler(target, handler);
        public void RemoveEventHandler(object target, Delegate handler) => E.RemoveEventHandler(target, handler);
    }

    public class ParameterInfo
    {
        internal readonly SR.ParameterInfo P;
        internal ParameterInfo(SR.ParameterInfo p) { P = p; }
        public CType ParameterType => P.ParameterType;
        public string Name => P.Name;
        public int Position => P.Position;
        public object[] GetCustomAttributes(CType type, bool inherit) => P.GetCustomAttributes(type.T, inherit);
        public object[] GetCustomAttributes(bool inherit) => P.GetCustomAttributes(inherit);
    }

    public class AssemblyName
    {
        internal readonly SR.AssemblyName N;
        internal AssemblyName(SR.AssemblyName n) { N = n; }
        public string Name => N.Name;
        public Version Version => N.Version;
        public string FullName => N.FullName;
    }

    public class Assembly
    {
        internal readonly SR.Assembly A;
        internal Assembly(SR.Assembly a) { A = a; }
        public static Assembly GetExecutingAssembly() => new Assembly(SR.Assembly.GetCallingAssembly());
        public static Assembly LoadFrom(string path)
        {
            var ctx = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(SR.Assembly.GetCallingAssembly()) ?? System.Runtime.Loader.AssemblyLoadContext.Default;
            return new Assembly(ctx.LoadFromAssemblyPath(CrestronIO.Paths.Map(path)));
        }
        public static Assembly Load(AssemblyName name) => new Assembly(SR.Assembly.Load(name.N));
        public static Assembly Load(string name) => new Assembly(SR.Assembly.Load(name));
        public string FullName => A.FullName;
        public AssemblyName GetName() => new AssemblyName(A.GetName());
        public CType[] GetTypes() => A.GetTypes().Select(t => (CType)t).ToArray();
        public CType GetType(string name) => A.GetType(name);
        public object CreateInstance(string typeName) => A.CreateInstance(typeName);
        public string[] GetManifestResourceNames() => A.GetManifestResourceNames();
        public CrestronIO.Stream GetManifestResourceStream(string name) => A.GetManifestResourceStream(name) is { } s ? new CrestronIO.WrappedStream(s) : null;
    }

    public static class Activator
    {
        public static object CreateInstance(CType type) => System.Activator.CreateInstance(type.T);
        public static object CreateInstance(CType type, params object[] args) => System.Activator.CreateInstance(type.T, args);
        public static T CreateInstance<T>() => System.Activator.CreateInstance<T>();
    }

    public static class CDelegate
    {
        public static Delegate CreateDelegate(CType type, object target, MethodInfo method) => Delegate.CreateDelegate(type.T, target, method.Mi);
        public static Delegate CreateDelegate(CType type, MethodInfo method) => Delegate.CreateDelegate(type.T, method.Mi);
    }
}

namespace Crestron.SimplSharp.CrestronIO
{
    /// <summary>Wraps an arbitrary .NET stream (e.g. an embedded resource).</summary>
    public class WrappedStream : Stream
    {
        public WrappedStream(System.IO.Stream s) : base(s) { }
    }
}

namespace Crestron.SimplSharp.CrestronSockets
{
    public delegate void SecureTCPClientSocketStatusChangeEventHandler(SecureTCPClient client, SocketStatus status);
    public delegate void SecureTCPClientReceiveCallback(SecureTCPClient client, int bytesReceived);
    public delegate void SecureTCPClientSendCallback(SecureTCPClient client, int bytesSent);
    public delegate void SecureTCPClientConnectCallback(SecureTCPClient client);

    /// <summary>TLS client with the SIMPL# shape. Server certificates are not validated.</summary>
    public class SecureTCPClient : IDisposable
    {
        private TcpClient _tcp;
        private SslStream _ssl;
        private System.Security.Cryptography.X509Certificates.X509Certificate2 _clientCert;
        private OpenVP.Core.SocketRecord _rec;

        public SecureTCPClient() : this("", 443, 4096) { }
        public SecureTCPClient(string address, int port, int bufferSize)
        {
            AddressClientConnectedTo = address;
            PortNumber = port;
            IncomingDataBuffer = new byte[Math.Max(1, bufferSize)];
            OpenVPBridge.Track(this);
        }
        public SecureTCPClient(string address, int port, int bufferSize, EthernetAdapterType adapter) : this(address, port, bufferSize) { }
        public SecureTCPClient(Crestron.SimplSharp.IPAddress address, int port, int bufferSize) : this(address?.ToString(), port, bufferSize) { }
        public SecureTCPClient(Crestron.SimplSharp.IPAddress address, int port, int bufferSize, EthernetAdapterType adapter) : this(address?.ToString(), port, bufferSize) { }

        public string AddressClientConnectedTo { get; set; }
        public int PortNumber { get; set; }
        public byte[] IncomingDataBuffer { get; private set; }
        public SocketStatus ClientStatus { get; private set; } = SocketStatus.SOCKET_STATUS_NO_CONNECT;
        public bool IsConnected => ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED;
        public bool DataAvailable => _tcp?.Available > 0;
        public int SocketSendOrReceiveTimeOutInMs { get; set; }
        public int LocalPortNumberOfClient => (_tcp?.Client?.LocalEndPoint as System.Net.IPEndPoint)?.Port ?? 0;
        /// <summary>The server's certificate may be self-signed or for another name: a device on a control network usually is. Nothing is verified.</summary>
        public event SecureTCPClientSocketStatusChangeEventHandler SocketStatusChange;

        public void SetClientCertificate(Cryptography.X509Certificates.X509Certificate cert) => _clientCert = cert?.Inner;
        public void SetClientPrivateKey(byte[] key) { }

        private void Status(SocketStatus s)
        {
            if (ClientStatus == s) return;
            ClientStatus = s;
            OpenVPBridge.Post(() => SocketStatusChange?.Invoke(this, s));
        }

        public SocketErrorCodes ConnectToServer()
        {
            if (ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED) return SocketErrorCodes.SOCKET_INVALID_STATE;
            if (string.IsNullOrWhiteSpace(AddressClientConnectedTo)) return SocketErrorCodes.SOCKET_NO_HOSTNAME_RESOLVE;
            if (PortNumber < 1 || PortNumber > 65535) return SocketErrorCodes.SOCKET_INVALID_PORT_NUMBER;
            try
            {
                _rec?.Close("closed");
                _rec = OpenVPBridge.Sock("SecureTCPClient", "client", "tcp", "", $"{AddressClientConnectedTo}:{PortNumber}");
                _tcp = new TcpClient();
                var connect = _tcp.ConnectAsync(AddressClientConnectedTo, PortNumber);
                if (SocketSendOrReceiveTimeOutInMs > 0 && !connect.Wait(SocketSendOrReceiveTimeOutInMs)) throw new TimeoutException();
                connect.GetAwaiter().GetResult();
                _ssl = new SslStream(_tcp.GetStream(), false, (_, _, _, _) => true);
                var certs = _clientCert != null ? new System.Security.Cryptography.X509Certificates.X509CertificateCollection { _clientCert } : null;
                _ssl.AuthenticateAsClient(AddressClientConnectedTo, certs, System.Security.Authentication.SslProtocols.None, false);
                _rec?.Connected(OpenVPBridge.Ep(_tcp.Client.LocalEndPoint));
                Status(SocketStatus.SOCKET_STATUS_CONNECTED);
                return SocketErrorCodes.SOCKET_OK;
            }
            catch (Exception e)
            {
                try { _ssl?.Dispose(); _tcp?.Dispose(); } catch { }
                _ssl = null; _tcp = null;
                _rec?.Close("connect failed");
                OpenVPBridge.Log("Warning", $"SecureTCPClient {AddressClientConnectedTo}:{PortNumber}: {e.Message}");
                var inner = e is AggregateException { InnerException: { } ie } ? ie : e;
                var dns = inner is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound or System.Net.Sockets.SocketError.NoData or System.Net.Sockets.SocketError.TryAgain };
                Status(dns ? SocketStatus.SOCKET_STATUS_DNS_FAILED : SocketStatus.SOCKET_STATUS_CONNECT_FAILED);
                return dns ? SocketErrorCodes.SOCKET_NO_HOSTNAME_RESOLVE : SocketErrorCodes.SOCKET_CONNECT_FAILED;
            }
        }

        public SocketErrorCodes ConnectToServerAsync(SecureTCPClientConnectCallback callback)
        {
            if (ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED) return SocketErrorCodes.SOCKET_INVALID_STATE;
            Task.Run(() => { ConnectToServer(); OpenVPBridge.Post(() => callback?.Invoke(this)); });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public SocketErrorCodes DisconnectFromServer()
        {
            var was = _tcp != null;
            try { _ssl?.Dispose(); _tcp?.Dispose(); } catch { }
            _ssl = null; _tcp = null;
            _rec?.Close("closed");
            if (was) Status(SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY);
            return SocketErrorCodes.SOCKET_OK;
        }

        private void Broken()
        {
            try { _ssl?.Dispose(); _tcp?.Dispose(); } catch { }
            _ssl = null; _tcp = null;
            _rec?.Close("closed by remote");
            Status(SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY);
        }

        public int SendData(byte[] data, int length)
        {
            var ssl = _ssl;
            if (ssl == null) return -1;
            try { ssl.Write(data, 0, length); _rec?.Sent(data, 0, length); return length; }
            catch (Exception) { Broken(); return -1; }
        }

        public SocketErrorCodes SendDataAsync(byte[] data, int length, SecureTCPClientSendCallback callback)
        {
            if (_ssl == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            Task.Run(() => { var n = SendData(data, length); OpenVPBridge.Post(() => callback?.Invoke(this, n)); });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        /// <summary>Blocking read; 0 means the connection closed (the status event follows).</summary>
        public int ReceiveData()
        {
            var ssl = _ssl;
            if (ssl == null) return 0;
            try
            {
                var n = ssl.Read(IncomingDataBuffer, 0, IncomingDataBuffer.Length);
                if (n <= 0) { Broken(); return 0; }
                _rec?.Received(IncomingDataBuffer, 0, n);
                return n;
            }
            catch (Exception) { if (_ssl != null) Broken(); return 0; }
        }

        public SocketErrorCodes ReceiveDataAsync(SecureTCPClientReceiveCallback callback)
        {
            if (_ssl == null) return SocketErrorCodes.SOCKET_NOT_CONNECTED;
            Task.Run(() => { var n = ReceiveData(); OpenVPBridge.Post(() => callback?.Invoke(this, n)); });
            return SocketErrorCodes.SOCKET_OPERATION_PENDING;
        }

        public void Dispose() { DisconnectFromServer(); OpenVPBridge.Untrack(this); }
    }
}

namespace Crestron.SimplSharp.CrestronWebSocketClient
{
    /// <summary>WebSocket client with the SIMPL# shape.</summary>
    public class WebSocketClient : IDisposable
    {
        public enum WEBSOCKET_RESULT_CODES { WEBSOCKET_CLIENT_SUCCESS = 0, WEBSOCKET_CLIENT_PENDING = 1, WEBSOCKET_CLIENT_ERROR = 2, WEBSOCKET_CLIENT_SOCKET_CLOSED = 3 }
        public enum WEBSOCKET_PACKET_TYPES { LWS_WS_OPCODE_07__CONTINUATION = 0, LWS_WS_OPCODE_07__TEXT_FRAME = 1, LWS_WS_OPCODE_07__BINARY_FRAME = 2, LWS_WS_OPCODE_07__CLOSE = 8, LWS_WS_OPCODE_07__PING = 9, LWS_WS_OPCODE_07__PONG = 10 }
        public delegate int WebSocketClientAsyncConnectCallback(WEBSOCKET_RESULT_CODES error);
        public delegate int WebSocketClientAsyncReceiveCallback(byte[] data, uint length, WEBSOCKET_PACKET_TYPES opcode, WEBSOCKET_RESULT_CODES error);
        public delegate int WebSocketClientAsyncSendCallback(WEBSOCKET_RESULT_CODES error);
        public delegate int WebSocketClientAsyncDisconnectCallback(WEBSOCKET_RESULT_CODES error, object state);

        private System.Net.WebSockets.ClientWebSocket _ws;

        public WebSocketClient() { OpenVPBridge.Track(this); }
        public string URL { get; set; }
        public uint? Port { get; set; }
        public bool SSL { get; set; }
        public bool KeepAlive { get; set; }
        public bool VerifyServerCertificate { get; set; }
        private string _host;
        public string Host { get => _host ?? (Uri.TryCreate(URL, UriKind.Absolute, out var u) ? u.Host : URL); set => _host = value; }
        public bool Connected => _ws?.State == System.Net.WebSockets.WebSocketState.Open;
        public string AddOnHeader { get; set; }
        public WebSocketClientAsyncConnectCallback ConnectionCallBack { get; set; }
        public WebSocketClientAsyncReceiveCallback ReceiveCallBack { get; set; }
        public WebSocketClientAsyncSendCallback SendCallBack { get; set; }
        public WebSocketClientAsyncDisconnectCallback DisconnectCallBack { get; set; }

        private Uri Target()
        {
            var url = URL ?? "";
            if (!url.Contains("://")) url = (SSL ? "wss://" : "ws://") + url;
            var b = new UriBuilder(url);
            if (Port is uint p && p != 0) b.Port = (int)p;
            return b.Uri;
        }

        public WEBSOCKET_RESULT_CODES Connect()
        {
            try
            {
                _ws = new System.Net.WebSockets.ClientWebSocket();
                if (!VerifyServerCertificate) _ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                _ws.ConnectAsync(Target(), CancellationToken.None).GetAwaiter().GetResult();
                return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SUCCESS;
            }
            catch (Exception e) { OpenVPBridge.Log("Warning", $"WebSocket {URL}: {e.Message}"); return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_ERROR; }
        }

        public WEBSOCKET_RESULT_CODES ConnectAsync() => ConnectAsyncEx();
        public WEBSOCKET_RESULT_CODES ConnectAsyncEx()
        {
            Task.Run(() => { var r = Connect(); ConnectionCallBack?.Invoke(r); });
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_PENDING;
        }

        public WEBSOCKET_RESULT_CODES Send(byte[] data, uint length, WEBSOCKET_PACKET_TYPES type)
        {
            if (!Connected) return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SOCKET_CLOSED;
            var t = type == WEBSOCKET_PACKET_TYPES.LWS_WS_OPCODE_07__BINARY_FRAME ? System.Net.WebSockets.WebSocketMessageType.Binary : System.Net.WebSockets.WebSocketMessageType.Text;
            _ws.SendAsync(new ArraySegment<byte>(data, 0, (int)length), t, true, CancellationToken.None).GetAwaiter().GetResult();
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SUCCESS;
        }

        public enum WEBSOCKET_PACKET_SEGMENT_CONTROL { WEBSOCKET_CLIENT_PACKET_END = 0, WEBSOCKET_CLIENT_PACKET_START = 1, WEBSOCKET_CLIENT_PACKET_MIDDLE = 2 }
        public string ProxyIpAddr { get; set; }
        public string ProxyUsername { get; set; }
        public string ProxyPassword { get; set; }
        public WEBSOCKET_RESULT_CODES Send(byte[] data, uint length, WEBSOCKET_PACKET_TYPES type, WEBSOCKET_PACKET_SEGMENT_CONTROL segment)
        {
            if (!Connected) return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SOCKET_CLOSED;
            var t = type == WEBSOCKET_PACKET_TYPES.LWS_WS_OPCODE_07__BINARY_FRAME ? System.Net.WebSockets.WebSocketMessageType.Binary : System.Net.WebSockets.WebSocketMessageType.Text;
            _ws.SendAsync(new ArraySegment<byte>(data, 0, (int)length), t, segment == WEBSOCKET_PACKET_SEGMENT_CONTROL.WEBSOCKET_CLIENT_PACKET_END, CancellationToken.None).GetAwaiter().GetResult();
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SUCCESS;
        }

        public WEBSOCKET_RESULT_CODES SendAsync(byte[] data, uint length, WEBSOCKET_PACKET_TYPES type)
        {
            Task.Run(() => { var r = Send(data, length, type); SendCallBack?.Invoke(r); });
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_PENDING;
        }

        public WEBSOCKET_RESULT_CODES Receive(out byte[] data, out WEBSOCKET_PACKET_TYPES type)
        {
            data = Array.Empty<byte>(); type = WEBSOCKET_PACKET_TYPES.LWS_WS_OPCODE_07__TEXT_FRAME;
            if (!Connected) return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SOCKET_CLOSED;
            var buf = new byte[64 * 1024];
            using var ms = new System.IO.MemoryStream();
            System.Net.WebSockets.WebSocketReceiveResult r;
            do
            {
                r = _ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).GetAwaiter().GetResult();
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SOCKET_CLOSED;
                ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            data = ms.ToArray();
            type = r.MessageType == System.Net.WebSockets.WebSocketMessageType.Binary ? WEBSOCKET_PACKET_TYPES.LWS_WS_OPCODE_07__BINARY_FRAME : WEBSOCKET_PACKET_TYPES.LWS_WS_OPCODE_07__TEXT_FRAME;
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SUCCESS;
        }

        /// <summary>Receives one message and calls ReceiveCallBack.</summary>
        public WEBSOCKET_RESULT_CODES ReceiveAsync()
        {
            Task.Run(() =>
            {
                WEBSOCKET_RESULT_CODES r;
                byte[] data; WEBSOCKET_PACKET_TYPES type;
                try { r = Receive(out data, out type); }
                catch { r = WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SOCKET_CLOSED; data = Array.Empty<byte>(); type = WEBSOCKET_PACKET_TYPES.LWS_WS_OPCODE_07__CLOSE; }
                ReceiveCallBack?.Invoke(data, (uint)data.Length, type, r);
            });
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_PENDING;
        }

        public WEBSOCKET_RESULT_CODES Disconnect()
        {
            try { _ws?.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(2000); } catch { }
            _ws?.Dispose(); _ws = null;
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SUCCESS;
        }

        public WEBSOCKET_RESULT_CODES DisconnectAsync(object state)
        {
            Task.Run(() => { var r = Disconnect(); DisconnectCallBack?.Invoke(r, state); });
            return WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_PENDING;
        }

        public void Dispose() { Disconnect(); OpenVPBridge.Untrack(this); }
    }
}
