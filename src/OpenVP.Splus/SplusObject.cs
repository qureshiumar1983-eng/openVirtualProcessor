using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OpenVP.Core;

namespace Crestron.Logos.SplusObjects
{
    /// <summary>
    /// Base class of every compiled SIMPL+ module.
    ///
    /// Threading follows SIMPL+: each event runs on its own thread, but only one thread of a module runs
    /// at a time. A thread takes the module's baton in SplusThreadStartCode / SplusFunctionMainStartCode
    /// (emitted by the SIMPL+ compiler at the top of every event) and gives it back in
    /// ObjectFinallyHandler. Delay, ProcessLogic, Gather and WaitForInitializationComplete hand the baton
    /// over while they wait, so other events can run.
    /// </summary>
    public partial class SplusObject : ISplusModule
    {
        public delegate void WaitFunction(object waitObject);

        private readonly SemaphoreSlim _baton = new SemaphoreSlim(1, 1);
        private readonly List<Wait> _waits = new List<Wait>();
        private readonly Dictionary<(bool, uint), SigKind> _pins = new Dictionary<(bool, uint), SigKind>();
        private readonly ManualResetEventSlim _initialized = new ManualResetEventSlim(false);

        // Program-wide start-up barrier (this assembly is loaded once per program): events and
        // WaitForInitializationComplete wait until every module's Function Main has got going, i.e. has
        // called WaitForInitializationComplete, returned, or run for two seconds.
        private static int _modulesStarting;
        private static readonly ManualResetEventSlim AllStarted = new ManualResetEventSlim(false);
        private int _readyFlag;

        static SplusObject()
        {
            // SIMPL+ runs every event on its own thread; make sure the pool doesn't throttle them.
            ThreadPool.GetMinThreads(out var w, out var io);
            if (w < 256) ThreadPool.SetMinThreads(256, Math.Max(io, 64));
        }

        private void MarkReady()
        {
            if (Interlocked.Exchange(ref _readyFlag, 1) == 1) return;
            if (Interlocked.Decrement(ref _modulesStarting) <= 0) AllStarted.Set();
        }
        private readonly object _bufferGate = new object();
        private volatile bool _stopped;
        private ISplusHost _host;

        [ThreadStatic] private static SplusObject _current;
        [ThreadStatic] private static int _batonDepth;

        public SplusObject(string instanceName, string referenceId, CrestronStringEncoding encoding)
        {
            InstanceName = instanceName;
            ReferenceID = referenceId;
            InheritedStringEncoding = encoding == CrestronStringEncoding.eEncodingUnknown ? CrestronStringEncoding.eEncodingASCII : encoding;
            Interlocked.Increment(ref _modulesStarting);
            AllStarted.Reset();
        }

        public string InstanceName { get; }
        public string ReferenceID { get; }

        public CrestronStringEncoding InheritedStringEncoding;
        public SplusStructureBase NVRAM;

        public Dictionary<uint, DigitalInput> m_DigitalInputList = new Dictionary<uint, DigitalInput>();
        public Dictionary<uint, AnalogInput> m_AnalogInputList = new Dictionary<uint, AnalogInput>();
        public Dictionary<uint, StringInput> m_StringInputList = new Dictionary<uint, StringInput>();
        public Dictionary<uint, DigitalOutput> m_DigitalOutputList = new Dictionary<uint, DigitalOutput>();
        public Dictionary<uint, AnalogOutput> m_AnalogOutputList = new Dictionary<uint, AnalogOutput>();
        public Dictionary<uint, StringOutput> m_StringOutputList = new Dictionary<uint, StringOutput>();
        public Dictionary<uint, ISplusParameter> m_ParameterList = new Dictionary<uint, ISplusParameter>();

        /// <summary>The module currently running on this thread (for library functions like Delay).</summary>
        internal static SplusObject Current => _current;

        internal ISplusHost Host => _host;

        // ------------------------------------------------------------------ overridden by modules

        public virtual object FunctionMain(object obj) => null;
        public virtual void LogosSplusInitialize() { }
        public virtual void LogosSimplSharpInitialize() { }

        // ------------------------------------------------------------------ baton (called from compiled code)

        public SplusExecutionContext SplusFunctionMainStartCode() { Enter(); return new SplusExecutionContext(); }
        public SplusExecutionContext SplusThreadStartCode(SignalEventArgs args) { Enter(); return new SplusExecutionContext(); }
        public SplusExecutionContext SplusThreadStartCode(SocketEventInfo info) { Enter(); return new SplusExecutionContext(); }
        public SplusExecutionContext SplusThreadStartCode(Wait wait) { Enter(); return new SplusExecutionContext(); }
        public SplusExecutionContext SplusSimplSharpDelegateThreadStartCode() { Enter(); return new SplusExecutionContext(); }

        public void ObjectFinallyHandler() => Leave();
        public void ObjectFinallyHandler(SignalEventArgs args) => Leave();
        public void ObjectFinallyHandler(SocketEventInfo info) => Leave();

        public void ObjectCatchHandler(Exception e)
        {
            if (e is ThreadInterruptedException || e is OperationCanceledException) return;
            Log("Error", $"SIMPL+ {InstanceName}: {e.GetType().Name}: {e.Message}" +
                (Environment.GetEnvironmentVariable("OPENVP_SPLUS_DEBUG") == "1" ? "\n" + e.StackTrace : ""));
        }

        private void Enter()
        {
            if (_current == this && _batonDepth > 0) { _batonDepth++; return; }
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] enter wait (thread {Environment.CurrentManagedThreadId})");
            _baton.Wait();
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] entered (thread {Environment.CurrentManagedThreadId})");
            _current = this;
            _batonDepth = 1;
        }

        private void Leave()
        {
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] leave depth={_batonDepth} current={(_current == this)} (thread {Environment.CurrentManagedThreadId})");
            if (_current != this || _batonDepth == 0) return;
            if (--_batonDepth > 0) return;
            _current = null;
            _baton.Release();
        }

        /// <summary>Give the baton away while waiting, then take it back.</summary>
        internal void Yield(Action wait)
        {
            var held = _current == this ? _batonDepth : 0;
            if (held > 0) { _batonDepth = 0; _current = null; _baton.Release(); }
            try { wait(); }
            finally
            {
                if (held > 0) { _baton.Wait(); _current = this; _batonDepth = held; }
            }
            if (_stopped) throw new OperationCanceledException();
        }

        public int WaitForInitializationComplete()
        {
            MarkReady();
            Yield(() => AllStarted.Wait(TimeSpan.FromSeconds(10)));
            return 0;
        }

        // ------------------------------------------------------------------ waits

        /// <summary>
        /// WAIT: run the callback after time (1/100 s) without blocking. A WAIT whose previous instance (same
        /// label) is still pending doesn't start a second one; the thread hands over the baton until the pending
        /// one has run. That is what makes the common "WHILE(1) { WAIT(100) {...} }" loop tick once per period.
        /// </summary>
        public void CreateWait(string name, int time, WaitFunction callback)
        {
            Wait pending;
            lock (_waits) pending = _waits.FirstOrDefault(x => x.Name == name);
            if (pending != null)
            {
                Yield(() => pending.Done.Wait());
                return;
            }
            var w = new Wait(this, name) { Callback = callback };
            lock (_waits) _waits.Add(w);
            w.Start(time);
        }

        internal void RunWait(Wait w)
        {
            try
            {
                if (_stopped) return;
                lock (_waits) _waits.Remove(w);
                w.Callback?.Invoke(w);
            }
            catch (Exception e) { ObjectCatchHandler(e); }
            finally { w.Done.Set(); }
        }

        internal Wait[] FindWaits(string name)
        {
            lock (_waits) return _waits.Where(x => name == null || x.Name == name).ToArray();
        }

        internal void RemoveWait(Wait w)
        {
            lock (_waits) _waits.Remove(w);
            w.Timer?.Dispose();
            w.Done.Set();
        }

        // ------------------------------------------------------------------ helpers used by compiled code

        public int Mod(int a, int b) => b == 0 ? 0 : a % b;

        public ushort Byte(CrestronString s, int position)
        {
            var v = s?.Str ?? "";
            return position >= 1 && position <= v.Length ? (ushort)(v[position - 1] & 0xFF) : (ushort)0;
        }

        public ushort GetProgramNumber() => (ushort)(_host?.Slot.Number ?? 1);

        public void MakeString(CrestronString dest, string format, params object[] args)
        {
            var text = OpenVP.Splus.Printf.Format(format, args);
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] MakeString {dest?.GetType().Name} '{format}' -> '{text}'");
            if (dest is not null) dest.UpdateValue(text);
        }

        public void Print(string format, params object[] args) => Log("Console", OpenVP.Splus.Printf.Format(format, args));
        public void Trace(string format, params object[] args) => Log("Info", OpenVP.Splus.Printf.Format(format, args));

        public int IsSignalDefined(DigitalInput s) => Connected(true, SigKind.Digital, s?.Id);
        public int IsSignalDefined(AnalogInput s) => Connected(true, SigKind.Analog, s?.Id);
        public int IsSignalDefined(StringInput s) => Connected(true, SigKind.Serial, s?.Id);
        public int IsSignalDefined(DigitalOutput s) => Connected(false, SigKind.Digital, s?.Id);
        public int IsSignalDefined(AnalogOutput s) => Connected(false, SigKind.Analog, s?.Id);
        public int IsSignalDefined(StringOutput s) => Connected(false, SigKind.Serial, s?.Id);
        private int Connected(bool input, SigKind k, uint? id) => id is uint i && (_host?.IsConnected(input, k, i) ?? false) ? 1 : 0;

        // ------------------------------------------------------------------ files (sandboxed to the slot folder)

        private readonly Dictionary<short, FileStream> _files = new Dictionary<short, FileStream>();
        private short _nextHandle = 1;
        private string[] _findResults;
        private int _findIndex;

        public short StartFileOperations() => 0;
        public short EndFileOperations() => 0;
        public ushort CheckForNVRAMDisk() => 1;
        public ushort CheckForDisk() => 1;

        private string MapPath(string crestronPath)
        {
            var root = _host?.FileRoot ?? Path.GetTempPath();
            var rel = (crestronPath ?? "").Replace('\\', '/').TrimStart('/');
            foreach (var prefix in new[] { "nvram/", "user/", "html/", "romdisk/" })
                if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { rel = prefix.TrimEnd('/') + "/" + rel.Substring(prefix.Length); break; }
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal)) throw new UnauthorizedAccessException("Path outside program folder");
            return full;
        }

        public short FileOpen(CrestronString name, ushort flags)
        {
            try
            {
                var path = MapPath(name?.Str);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // SIMPL+ flags: _O_RDONLY 0, _O_WRONLY 1, _O_RDWR 2, _O_APPEND 8, _O_CREAT 256, _O_TRUNC 512, _O_EXCL 1024
                var write = (flags & 3) != 0;
                FileMode mode = (flags & 256) != 0 ? ((flags & 512) != 0 ? FileMode.Create : FileMode.OpenOrCreate) : FileMode.Open;
                var fs = new FileStream(path, mode, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite);
                if ((flags & 8) != 0) fs.Seek(0, SeekOrigin.End);
                lock (_files) { var h = _nextHandle++; _files[h] = fs; return h; }
            }
            catch (Exception e) { Log("Warning", $"SIMPL+ FileOpen {name}: {e.Message}"); return -1; }
        }

        public short FileClose(short handle)
        {
            lock (_files) { if (!_files.Remove(handle, out var fs)) return -1; fs.Dispose(); return 0; }
        }

        public int FileRead(short handle, CrestronString buffer, ushort count)
        {
            FileStream fs; lock (_files) _files.TryGetValue(handle, out fs);
            if (fs == null) return -1;
            var bytes = new byte[count];
            var n = fs.Read(bytes, 0, count);
            buffer?.UpdateValue(Encoding.Latin1.GetString(bytes, 0, n));
            return n;
        }

        public int FileWrite(short handle, CrestronString data, ushort count)
        {
            FileStream fs; lock (_files) _files.TryGetValue(handle, out fs);
            if (fs == null) return -1;
            var bytes = Encoding.Latin1.GetBytes(data?.Str ?? "");
            var n = Math.Min(count == 0 ? bytes.Length : count, bytes.Length);
            fs.Write(bytes, 0, n);
            fs.Flush();
            return n;
        }

        public short FindFirst(CrestronString pattern, ref FILE_INFO info)
        {
            try
            {
                var full = MapPath(pattern?.Str);
                var dir = Path.GetDirectoryName(full);
                _findResults = Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir, Path.GetFileName(full)) : Array.Empty<string>();
                _findIndex = 0;
                return FindNext(ref info);
            }
            catch { return -1; }
        }

        public short FindNext(ref FILE_INFO info)
        {
            if (_findResults == null || _findIndex >= _findResults.Length) return -1;
            var f = new FileInfo(_findResults[_findIndex++]);
            info.Name = new CrestronString(f.Name);
            info.lSize = (uint)f.Length;
            info.Modified = f.LastWriteTime;
            info.iAttributes = (ushort)((int)f.Attributes & 0x3F);
            return 0;
        }

        public short FindClose() { _findResults = null; return 0; }

        // ------------------------------------------------------------------ buffers (Gather)

        internal void BufferChanged(BufferInput b)
        {
            lock (_bufferGate) Monitor.PulseAll(_bufferGate);
        }

        /// <summary>Block (handing over the baton) until the condition holds or the module stops.</summary>
        internal void WaitForBuffer(Func<bool> ready, int timeoutMs)
        {
            var deadline = timeoutMs < 0 ? long.MaxValue : Environment.TickCount64 + timeoutMs;
            while (!_stopped && !ready())
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0) return;
                Yield(() => { lock (_bufferGate) Monitor.Wait(_bufferGate, (int)Math.Min(remaining, 200)); });
            }
        }

        // ------------------------------------------------------------------ outputs

        internal void EmitOutput(SigKind kind, uint id, object value)
        {
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] output {kind} {id} = {value}");
            if (!_stopped) _host?.Output(kind, id, value);
        }

        internal void Log(string level, string message) => _host?.Log(level, message);

        /// <summary>Run an event handler on the thread pool (SIMPL+ gives every event its own thread), recorded for the metrics page.</summary>
        internal void QueueHandler(string kind, Delegate handler, Action run)
        {
            var metrics = _host?.Slot.Metrics;
            if (metrics == null) { ThreadPool.QueueUserWorkItem(_ => run()); return; }
            metrics.QueueWork(kind, InstanceName + " " + (handler?.Method.Name ?? "?"), run);
        }

        internal bool Stopped => _stopped;

        // ------------------------------------------------------------------ ISplusModule (host side)

        void ISplusModule.AttachHost(ISplusHost host) => _host = host;

        IReadOnlyDictionary<(bool isInput, uint id), SigKind> ISplusModule.Pins
        {
            get
            {
                _pins.Clear();
                foreach (var k in m_DigitalInputList.Keys) _pins[(true, k)] = SigKind.Digital;
                foreach (var k in m_AnalogInputList.Keys) _pins[(true, k)] = SigKind.Analog;
                foreach (var k in m_StringInputList.Keys) _pins[(true, k)] = SigKind.Serial;
                foreach (var k in m_DigitalOutputList.Keys) _pins[(false, k)] = SigKind.Digital;
                foreach (var k in m_AnalogOutputList.Keys) _pins[(false, k)] = SigKind.Analog;
                foreach (var k in m_StringOutputList.Keys) _pins[(false, k)] = SigKind.Serial;
                return _pins;
            }
        }

        void ISplusModule.Initialize()
        {
            LogosSplusInitialize();
            LogosSimplSharpInitialize();
        }

        void ISplusModule.SetParameter(uint id, object value)
        {
            if (m_ParameterList.TryGetValue(id, out var p)) p.Set(value);
        }

        void ISplusModule.Preset(SigKind kind, uint id, object value) => Apply(kind, id, value, fire: false);

        void ISplusModule.Input(SigKind kind, uint id, object value) => Apply(kind, id, value, fire: true);

        private static readonly bool TraceOn = Environment.GetEnvironmentVariable("OPENVP_SPLUS_TRACE") == "1";

        private void Apply(SigKind kind, uint id, object value, bool fire)
        {
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] input {kind} {id} = {value} fire={fire} known={m_DigitalInputList.ContainsKey(id)}");
            switch (kind)
            {
                case SigKind.Digital when m_DigitalInputList.TryGetValue(id, out var d):
                    var nv = value is true ? 1 : 0;
                    if (d.RawValue == nv && fire) return;
                    d.RawValue = nv;
                    if (!fire) return;
                    var args = new SignalEventArgs(d, d.Id, d.ArrayStart);
                    Fire(nv == 1 ? d.OnDigitalPush : d.OnDigitalRelease, args);
                    Fire(d.OnDigitalChange, args);
                    break;
                case SigKind.Analog when m_AnalogInputList.TryGetValue(id, out var a):
                    a.RawValue = value is ushort u ? u : (ushort)0;
                    if (fire) Fire(a.OnAnalogChange, new SignalEventArgs(a, a.Id, a.ArrayStart));
                    break;
                case SigKind.Serial when m_StringInputList.TryGetValue(id, out var s):
                    s.Receive(value as string ?? "");
                    if (fire) Fire(s.OnSerialChange, new SignalEventArgs(s, s.Id, s.ArrayStart));
                    break;
            }
        }

        private void Fire(List<InputChangeHandlerWrapper> handlers, SignalEventArgs args)
        {
            if (TraceOn) Log("Info", $"[S+ {InstanceName}] fire {handlers.Count} handler(s)");
            if (handlers.Count == 0 || _stopped) return;
            foreach (var h in handlers.ToArray())
                QueueHandler("S+ event", h.Handler, () =>
                {
                    if (!AllStarted.IsSet) AllStarted.Wait(TimeSpan.FromSeconds(10));
                    if (_stopped) return;
                    try { h.Handler(args); }
                    catch (Exception e) { ObjectCatchHandler(e); }
                });
        }

        void ISplusModule.StartMain()
        {
            var t = new Thread(() =>
            {
                using var tracked = _host?.Slot.Metrics.TrackThread("S+ Function Main");
                try { FunctionMain(null); }
                catch (Exception e) { ObjectCatchHandler(e); }
                finally { _initialized.Set(); MarkReady(); }
            }) { IsBackground = true, Name = "S+ " + InstanceName };
            t.Start();
            // Initialization is complete once Main has had a moment to set up (Main may never return).
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(50); _initialized.Set(); Thread.Sleep(1950); MarkReady(); });
        }

        void ISplusModule.Stop()
        {
            _stopped = true;
            lock (_waits) { foreach (var w in _waits) { w.Timer?.Dispose(); w.Done.Set(); } _waits.Clear(); }
            lock (_files) { foreach (var f in _files.Values) f.Dispose(); _files.Clear(); }
            lock (_bufferGate) Monitor.PulseAll(_bufferGate);
            _initialized.Set();
            MarkReady();
        }
    }

    // ------------------------------------------------------------------ sockets (minimal for now)

    public partial class SocketEventInfo
    {
        public SocketEventInfo(SocketInfo socket) { Socket = socket; }
        public SocketInfo Socket { get; }
    }

    public partial class SocketInfo
    {
        public SocketInfo(int bufferSize, SplusObject owner)
        {
            Owner = owner;
            SocketRxBuf = new CrestronString(CrestronStringEncoding.eEncodingASCII, Math.Max(bufferSize, 1), owner);
        }

        public SplusObject Owner { get; }
        public CrestronString SocketRxBuf;
        public short SocketStatus;

        public List<SocketHandlerWrapper> OnSocketConnect = new List<SocketHandlerWrapper>();
        public List<SocketHandlerWrapper> OnSocketDisconnect = new List<SocketHandlerWrapper>();
        public List<SocketHandlerWrapper> OnSocketReceive = new List<SocketHandlerWrapper>();
        public List<SocketHandlerWrapper> OnSocketStatus = new List<SocketHandlerWrapper>();

        public bool ResolveHostName(string host, ref StringBuilder result)
        {
            try
            {
                var addr = System.Net.Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (addr == null) return false;
                result = new StringBuilder(addr.ToString());
                return true;
            }
            catch { return false; }
        }

        internal void Raise(List<SocketHandlerWrapper> handlers)
        {
            var info = new SocketEventInfo(this);
            foreach (var h in handlers.ToArray())
            {
                void Run()
                {
                    if (Owner?.Stopped == true) return;
                    try { h.Handler(info); } catch (Exception e) { Owner?.ObjectCatchHandler(e); }
                }
                if (Owner == null) ThreadPool.QueueUserWorkItem(_ => Run());
                else Owner.QueueHandler("S+ socket event", h.Handler, Run);
            }
        }
    }

    public partial class SplusTcpClient : SocketInfo
    {
        public SplusTcpClient(int bufferSize, SplusObject owner) : base(bufferSize, owner) { }
        internal System.Net.Sockets.TcpClient Client;
    }

    public partial class SplusTcpServer : SocketInfo
    {
        public SplusTcpServer(int bufferSize, SplusObject owner) : base(bufferSize, owner) { }
        internal System.Net.Sockets.TcpListener Listener;
        internal System.Net.Sockets.TcpClient Client;
    }

    public partial class SplusUdpSocket : SocketInfo
    {
        public SplusUdpSocket(int bufferSize, SplusObject owner) : base(bufferSize, owner) { }
        internal System.Net.Sockets.UdpClient Udp;
        internal System.Net.IPEndPoint Remote;
    }

    public partial class SocketString : StringInput
    {
        public SocketString(uint id, int maxLength, SplusObject owner) : base(id, maxLength, owner) { }
    }
}
