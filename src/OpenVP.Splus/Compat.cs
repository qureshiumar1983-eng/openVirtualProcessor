using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Amib.Threading.Internal;
using OpenVP.Core;

// The rest of the SIMPL+ runtime API surface that compiled modules call: member names and signatures
// follow the SIMPL+ runtime so modules bind; behavior is OpenVP's own, written from the SIMPL+
// language reference.

namespace Crestron.Logos.SplusObjects
{
    public enum IsSignalDefined_e { eSignalUnknown = 0, eSignalDefined = 1, eSignalUndefined = 2 }
    public enum WaitStatusEnum { eWaitNotStarted = 0, eWaitRunning = 1, eWaitPaused = 2, eWaitDone = 3 }

    public partial class SplusExecutionContext
    {
        public SplusExecutionContext() { }
        public SplusExecutionContext(SplusObject owner, Thread thread) { }
        public SplusExecutionContext(SplusObject owner, Thread thread, object eventArgs, ulong startTime) { EventArgs = eventArgs; StartTime = startTime; }
        public SplusExecutionContext(SplusObject owner, Thread thread, bool simplSharpCallback) { IsSimplSharpDelegateOrEventHandlerCallback = simplSharpCallback; }
        public bool IsSimplSharpDelegateOrEventHandlerCallback { get; }
        public bool DoNotDeleteThreadSinceThisIsASimplSharpDelegateCallback { get; set; }
        public ulong StartTime { get; }
        public object EventArgs { get; set; }
    }

    public partial class SignalEventArgs
    {
        public SignalEventArgs() : this(null, 0, uint.MaxValue) { }
        public SignalEventArgs(uint id, bool value, uint arrayStart, SplusObject owner) : this(null, id, arrayStart) { Value = value; }
        public SignalEventArgs(uint id, bool value, SplusObject owner) : this(null, id, uint.MaxValue) { Value = value; }
        public SignalEventArgs(uint id, ushort value, uint arrayStart, SplusObject owner) : this(null, id, arrayStart) { Value = value; }
        public SignalEventArgs(uint id, ushort value, SplusObject owner) : this(null, id, uint.MaxValue) { Value = value; }
        public SignalEventArgs(uint id, CrestronString value, uint arrayStart, SplusObject owner) : this(null, id, arrayStart) { Value = value?.Str; }
        public SignalEventArgs(uint id, CrestronString value, SplusObject owner) : this(null, id, uint.MaxValue) { Value = value?.Str; }
        public SignalEventArgs(uint id, string value, uint arrayStart, SplusObject owner) : this(null, id, arrayStart) { Value = value; }
        public SignalEventArgs(uint id, uint value, SplusObject owner) : this(null, id, uint.MaxValue) { Value = value; }
        public SignalEventArgs(SignalEventArgs other, InputChangeHandlerWrapper handler) : this(other?.Source, other?.Id ?? 0, other?.ArrayStart ?? uint.MaxValue) { Value = other?.Value; }
        public object Value { get; }
        private ushort? _lastModified;
        public ushort LastModified { get => _lastModified ?? ArrayIndex; set => _lastModified = value; }
        public override string ToString() => $"Signal {Id}";
    }

    public partial class InputChangeHandlerWrapper
    {
        public InputChangeHandlerWrapper(SmartThreadSPLUSGLobalEventHandler handler) : this(handler, false) { }
        public bool CheckForReentry() => false;
        public void ClearThreadBusy() { }
    }

    public partial class SocketHandlerWrapper
    {
        public SocketHandlerWrapper(SmartThreadSPLUSGLobalEventHandler handler) : base(handler, false) { }
    }

    public partial class DigitalInput
    {
        public uint JoinNumber => Id;
        public IsSignalDefined_e IsSignalDefined { get; set; } = IsSignalDefined_e.eSignalDefined;
        public void UpdateInputValue(SignalEventArgs args) { if (args?.Value is bool b) RawValue = b ? 1 : 0; }
        public static bool operator ==(DigitalInput a, DigitalInput b) => (a?.Value ?? 0) == (b?.Value ?? 0);
        public static bool operator !=(DigitalInput a, DigitalInput b) => !(a == b);
        public static bool operator ==(DigitalInput a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(DigitalInput a, int b) => !(a == b);
        public static bool operator ==(int a, DigitalInput b) => b == a;
        public static bool operator !=(int a, DigitalInput b) => !(b == a);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
        public int CompareTo(DigitalInput other) => Value.CompareTo(other?.Value ?? 0);
    }

    public partial class AnalogInput
    {
        public uint JoinNumber => Id;
        public uint UintValue => RawValue;
        public IsSignalDefined_e IsSignalDefined { get; set; } = IsSignalDefined_e.eSignalDefined;
        public void UpdateInputValue(SignalEventArgs args) { if (args?.Value is ushort u) RawValue = u; }
        public static bool operator ==(AnalogInput a, AnalogInput b) => (a?.Value ?? 0) == (b?.Value ?? 0);
        public static bool operator !=(AnalogInput a, AnalogInput b) => !(a == b);
        public static bool operator ==(AnalogInput a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(AnalogInput a, int b) => !(a == b);
        public static bool operator ==(int a, AnalogInput b) => b == a;
        public static bool operator !=(int a, AnalogInput b) => !(b == a);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
        public int CompareTo(AnalogInput other) => Value.CompareTo(other?.Value ?? 0);
    }

    public partial class DigitalOutput
    {
        public uint JoinNumber => Id;
        public IsSignalDefined_e IsSignalDefined { get; set; } = IsSignalDefined_e.eSignalDefined;
        public void Pulse(int ticks) => Crestron.Logos.SplusLibrary.Functions.Pulse(ticks, this);
        public void PulseCallback(object state) => Value = 0;
        public static bool operator ==(DigitalOutput a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(DigitalOutput a, int b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
        public int CompareTo(DigitalOutput other) => Value.CompareTo(other?.Value ?? 0);
    }

    public partial class AnalogOutput
    {
        public uint JoinNumber => Id;
        public IsSignalDefined_e IsSignalDefined { get; set; } = IsSignalDefined_e.eSignalDefined;
        public void UpdateOutputValue(SignalEventArgs args) { if (args?.Value is ushort u) Value = u; }
        public static bool operator ==(AnalogOutput a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(AnalogOutput a, int b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
        public int CompareTo(AnalogOutput other) => Value.CompareTo(other?.Value ?? 0);
    }

    public partial class InOutArray<T> : IEnumerable<T>
    {
        public uint Length => (uint)Count;
        public IEnumerator<T> GetEnumerator() { for (int i = 1; i <= Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public partial class UShortParameter
    {
        public uint ParameterNumber => Id;
        public void GetParameterValue() { }
        public void HandleQueryError(int code) { }
        public static bool operator ==(UShortParameter a, UShortParameter b) => (a?.Value ?? 0) == (b?.Value ?? 0);
        public static bool operator !=(UShortParameter a, UShortParameter b) => !(a == b);
        public static bool operator ==(UShortParameter a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(UShortParameter a, int b) => !(a == b);
        public static bool operator ==(int a, UShortParameter b) => b == a;
        public static bool operator !=(int a, UShortParameter b) => !(b == a);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
    }

    public partial class ShortParameter
    {
        public uint ParameterNumber => Id;
        public void GetParameterValue() { }
        public void HandleQueryError(int code) { }
        public static bool operator ==(ShortParameter a, ShortParameter b) => (a?.Value ?? 0) == (b?.Value ?? 0);
        public static bool operator !=(ShortParameter a, ShortParameter b) => !(a == b);
        public static bool operator ==(ShortParameter a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(ShortParameter a, int b) => !(a == b);
        public static bool operator ==(int a, ShortParameter b) => b == a;
        public static bool operator !=(int a, ShortParameter b) => !(b == a);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
    }

    public partial class UIntParameter
    {
        public uint ParameterNumber => Id;
        public void GetParameterValue() { }
        public void HandleQueryError(int code) { }
        public static bool operator ==(UIntParameter a, UIntParameter b) => (a?.Value ?? 0) == (b?.Value ?? 0);
        public static bool operator !=(UIntParameter a, UIntParameter b) => !(a == b);
        public static bool operator ==(UIntParameter a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(UIntParameter a, int b) => !(a == b);
        public static bool operator ==(int a, UIntParameter b) => b == a;
        public static bool operator !=(int a, UIntParameter b) => !(b == a);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
    }

    public partial class IntParameter
    {
        public uint ParameterNumber => Id;
        public void GetParameterValue() { }
        public void HandleQueryError(int code) { }
        public static bool operator ==(IntParameter a, IntParameter b) => (a?.Value ?? 0) == (b?.Value ?? 0);
        public static bool operator !=(IntParameter a, IntParameter b) => !(a == b);
        public static bool operator ==(IntParameter a, int b) => (a?.Value ?? 0) == b;
        public static bool operator !=(IntParameter a, int b) => !(a == b);
        public static bool operator ==(int a, IntParameter b) => b == a;
        public static bool operator !=(int a, IntParameter b) => !(b == a);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => (int)Id;
    }

    public partial class SimplPlusException
    {
        public int ErrorSourceLine { get; set; }
        public int ErrorCode => HResult;
        public string ErrorMessage => Message;
    }

    // ------------------------------------------------------------------ BUFFER_INPUT async gather

    public partial class BufferInput
    {
        private string _gatherDelimiter;
        private int _gatherLength;
        private SmartThreadSPLUSGLobalEventHandler _gatherHandler;
        private bool _gatherArmed;

        /// <summary>GATHERASYNC: call the handler (on its own event thread) each time the delimiter arrives.</summary>
        public int GatherAsync(string delimiter, SmartThreadSPLUSGLobalEventHandler handler, int timeoutTicks)
        {
            _gatherDelimiter = delimiter; _gatherLength = 0; _gatherHandler = handler; _gatherArmed = true;
            CheckAsyncGather();
            return 0;
        }

        public int GatherAsync(int length, SmartThreadSPLUSGLobalEventHandler handler, int timeoutTicks)
        {
            _gatherDelimiter = null; _gatherLength = Math.Max(1, length); _gatherHandler = handler; _gatherArmed = true;
            CheckAsyncGather();
            return 0;
        }

        public int RemoveGather() { _gatherHandler = null; _gatherArmed = false; return 0; }
        public int RearmGather() { if (_gatherHandler == null) return -1; _gatherArmed = true; CheckAsyncGather(); return 0; }
        public void SetCanSpawnEvent() { }

        internal void CheckAsyncGather()
        {
            while (_gatherArmed && _gatherHandler != null)
            {
                string piece;
                if (_gatherDelimiter != null)
                {
                    var i = string.IsNullOrEmpty(_gatherDelimiter) ? -1 : Str.IndexOf(_gatherDelimiter, StringComparison.Ordinal);
                    if (i < 0) return;
                    piece = Str.Substring(0, i + _gatherDelimiter.Length);
                }
                else
                {
                    if (Str.Length < _gatherLength) return;
                    piece = Str.Substring(0, _gatherLength);
                }
                SetRaw(Str.Substring(piece.Length));
                var handler = _gatherHandler;
                var args = new GatherEventArgs(this, piece);
                void Run()
                {
                    if (Owner?.Stopped == true) return;
                    try { handler(args); } catch (Exception e) { Owner?.ObjectCatchHandler(e); }
                }
                if (Owner == null) ThreadPool.QueueUserWorkItem(_ => Run());
                else Owner.QueueHandler("S+ gather", handler, Run);
            }
        }
    }

    public partial class GatherEventArgs : EventArgs
    {
        public GatherEventArgs(BufferInput input, string text) { Input = input; RxString = new CrestronString(text); }
        public BufferInput Input { get; }
        /// <summary>Gathered text.</summary>
        public CrestronString RxString { get; }
        /// <summary>Result code: 0 = gathered, negative = timeout/error.</summary>
        public short Results { get; } = 0;
    }

    public static class CrestronStringExtentions
    {
        public static CrestronString PadLeft(this CrestronString s, int width, char pad) => new CrestronString((s?.Str ?? "").PadLeft(width, pad));
        public static CrestronString PadRight(this CrestronString s, int width, char pad) => new CrestronString((s?.Str ?? "").PadRight(width, pad));
    }

    // ------------------------------------------------------------------ sockets

    public partial class SocketString
    {
        public SocketString(int maxLength, SplusObject owner) : base(0, maxLength, owner) { }
        public short BUFFER_FULL_PERCENT_LIMIT => 90;
        public void HandleSocketChange(List<SocketHandlerWrapper> handlers, SocketInfo socket) { }
        public override void AppendValue(string value) => base.AppendValue(value);
        public override void UpdateValue(string value, CrestronStringEncoding e, SplusStructureBase s) => base.UpdateValue(value, e, s);
        public override void ClearBuffer() => base.ClearBuffer();
        public ushort GetC() => Crestron.Logos.SplusLibrary.Functions.GetC(this);
        public override CrestronString Remove(int count, bool unused) => base.Remove(count, unused);
        public CrestronString Remove(string match, int start) => Crestron.Logos.SplusLibrary.Functions.Remove(match, this, start);
    }

    public partial class SocketEventInfo
    {
        public SocketEventInfo(short status, SocketString buffer, SocketHandlerWrapper handler) { Status = status; }
        public short Status { get; }
    }

    public partial class SocketInfo
    {
        public void ReceiveData(byte[] data, int count, bool unused)
        {
            SocketRxBuf.UpdateValue(SocketRxBuf.Str + Encoding.Latin1.GetString(data, 0, count));
            Raise(OnSocketReceive);
        }
        public void SocketConnect() { SocketStatus = 2; Raise(OnSocketConnect); Raise(OnSocketStatus); }
        public void SocketDisconnect() { SocketStatus = 0; Raise(OnSocketDisconnect); Raise(OnSocketStatus); }
        public bool IsHostname(string address) => !System.Net.IPAddress.TryParse(address ?? "", out _);
        internal string RequestedAddress = "";
        internal int PortNumber;
        internal System.Net.IPEndPoint RemoteEndPoint;
    }

    public partial class SplusTcpClient
    {
        public short Connect(CrestronString address, int port, bool reconnect) => Crestron.Logos.SplusLibrary.Functions.SocketConnectClient(this, address?.Str, (ushort)port, (ushort)(reconnect ? 1 : 0));
        public short Disconnect() => Crestron.Logos.SplusLibrary.Functions.SocketDisconnectClient(this);
        public short Send(CrestronString data) => Crestron.Logos.SplusLibrary.Functions.SocketSend(this, data);
        public short GetRemoteAddress(ref CrestronString address) => Crestron.Logos.SplusLibrary.Functions.SocketGetRemoteIPAddress(this, ref address);
        public short GetAddressAsRequested(ref CrestronString address) => Crestron.Logos.SplusLibrary.Functions.SocketGetAddressAsRequested(this, ref address);
    }

    public partial class SplusTcpServer
    {
        public short StartListening(CrestronString address, int port) => Crestron.Logos.SplusLibrary.Functions.SocketServerStartListen(this, address, (ushort)port);
        public short StopListening() => Crestron.Logos.SplusLibrary.Functions.SocketServerStopListen(this);
        public short Send(CrestronString data) => Crestron.Logos.SplusLibrary.Functions.SocketSend(this, data);
        public short GetRemoteAddress(ref CrestronString address) => Crestron.Logos.SplusLibrary.Functions.SocketGetRemoteIPAddress(this, ref address);
    }

    public partial class SplusUdpSocket
    {
        public short Enable(CrestronString address, int port) => Crestron.Logos.SplusLibrary.Functions.SocketUDP_Enable(this, address, (ushort)port);
        public short Disable() => Crestron.Logos.SplusLibrary.Functions.SocketUDP_Disable(this);
        public short IsBroadcast() => Crestron.Logos.SplusLibrary.Functions.SocketIsBroadcast(this);
        public short IsMulticast() => Crestron.Logos.SplusLibrary.Functions.SocketIsMulticast(this);
        public short GetSenderIPAddress(ref CrestronString address) => Crestron.Logos.SplusLibrary.Functions.SocketGetSenderIPAddress(this, ref address);
        public short Send(CrestronString data) => Crestron.Logos.SplusLibrary.Functions.SocketSend(this, data);
        internal System.Net.IPEndPoint LastSender;
    }

    // ------------------------------------------------------------------ SplusObject

    public partial class SplusObject
    {
        public string ModuleName => GetType().Name.Replace("UserModuleClass_", "");
        public int SourceCodeLine { get; set; }
        public int LastModifiedArrayIndex { get; set; }
        public void InitializeSplusParameters() { }

        public CrestronString GetSymbolInstanceName() => new CrestronString(InstanceName);
        public CrestronString GetSymbolReferenceName() => new CrestronString(ReferenceID);
        public CrestronString GetModelNumber() => new CrestronString("VC-4 (OpenVP)");
        /// <summary>
        /// Puts the program's own folder into a path, as a processor does: "\NVRAM\config.txt" becomes
        /// "\NVRAM\{program name}\config.txt", where the name is the Program Name from the program header.
        /// A name with no top folder goes under \USER\{program name}\.
        /// </summary>
        public CrestronString MakeProgramFilename(string name) =>
            new CrestronString(OpenVP.Core.SlotFiles.ProgramFilename(name, _host?.Slot.Runtime?.ProgramName));
        public CrestronString MakeProgramFilename(CrestronString name) => MakeProgramFilename(name?.Str);

        public int IsParameterDefined(UShortParameter p) => p == null ? 0 : 1;
        public int IsParameterDefined(ShortParameter p) => p == null ? 0 : 1;
        public int IsParameterDefined(UIntParameter p) => p == null ? 0 : 1;
        public int IsParameterDefined(IntParameter p) => p == null ? 0 : 1;
        public int IsParameterDefined(StringParameter p) => p is null ? 0 : 1;

        public void Trace(CrestronString format, params object[] args) => Trace(format?.Str, args);
        public void Print(CrestronString format, params object[] args) => Print(format?.Str, args);
        public void MakeString(int encoding, CrestronString dest, params object[] args) => MakeString(dest, args?.Length > 0 ? args[0]?.ToString() : "", args?.Skip(1).ToArray());
        public void MakeString(int encoding, string format, params object[] args) { }
        public void MakeString(CrestronString dest, CrestronString format, params object[] args) => MakeString(dest, format?.Str, args);

        public void SendPacketToCPU(CrestronString packet) { }
        public void SendPacketToCPU(string packet) { }
        public void SendCresnetPacket(CrestronString packet) { }
        public void SendCresnetPacket(string packet) { }

        private int Mail(string to, string subject)
        {
            Log("Warning", $"SIMPL+ {InstanceName}: SendMail to {to} '{subject}' isn't supported by OpenVP; not sent.");
            return -1;
        }
        public int SendMail(CrestronString server, CrestronString user, CrestronString password, CrestronString to, CrestronString from, CrestronString cc, CrestronString subject, CrestronString message) => Mail(to?.Str, subject?.Str);
        public int SendMailAdvance(CrestronString server, ushort port, CrestronString user, CrestronString password, CrestronString to, CrestronString from, CrestronString cc, CrestronString subject, CrestronString message, ushort attachments, CrestronString files) => Mail(to?.Str, subject?.Str);
        public int SendMailWithAttachments(CrestronString server, CrestronString user, CrestronString password, CrestronString to, CrestronString from, CrestronString cc, CrestronString subject, CrestronString message, ushort attachments, CrestronString files) => Mail(to?.Str, subject?.Str);

        // ---- waits
        public void CreateWait(string name, ushort time, WaitFunction callback) => CreateWait(name, (int)time, callback);
        public void CreateWait(string name, short time, WaitFunction callback) => CreateWait(name, (int)time, callback);
        public void CreateWait(string name, uint time, WaitFunction callback) => CreateWait(name, (int)Math.Min(time, int.MaxValue), callback);
        public void CancelWait(string name) { foreach (var w in FindWaits(name)) RemoveWait(w); }
        public void CancelAllWait() { foreach (var w in FindWaits(null)) RemoveWait(w); }
        public void RemoveWaitFromList(string name) => CancelWait(name);
        public void PauseWait(string name) { foreach (var w in FindWaits(name)) w.Pause(); }
        public void PauseAllWait() { foreach (var w in FindWaits(null)) w.Pause(); }
        public void ResumeWait(string name) { foreach (var w in FindWaits(name)) w.Resume(); }
        public void ResumeAllWait() { foreach (var w in FindWaits(null)) w.Resume(); }
        public void RetimeWait(int time, string name) { foreach (var w in FindWaits(name)) w.Retime(time); }

        public int WaitForInitializationComplete(int timeoutMs)
        {
            WaitForInitializationComplete();
            return 0;
        }

        // ---- errors
        public int Error(string format, params object[] args) { Log("Error", $"SIMPL+ {InstanceName}: {OpenVP.Splus.Printf.Format(format, args)}"); return 0; }
        public int GenerateUserError(string format, params object[] args) { Log("Error", $"{InstanceName}: {OpenVP.Splus.Printf.Format(format, args)}"); return 0; }
        public int GenerateUserWarning(string format, params object[] args) { Log("Warning", $"{InstanceName}: {OpenVP.Splus.Printf.Format(format, args)}"); return 0; }
        public int GenerateUserNotice(string format, params object[] args) { Log("Notice", $"{InstanceName}: {OpenVP.Splus.Printf.Format(format, args)}"); return 0; }

        // ---- arithmetic and bytes
        public uint Divide(uint a, uint b) => b == 0 ? 0 : a / b;
        public int Divide(int a, int b) => b == 0 ? 0 : a / b;
        public uint Mod(uint a, uint b) => b == 0 ? 0 : a % b;

        /// <summary>BIT(source, byte, bit): bit (0-7) of the 1-based byte.</summary>
        public ushort Bit(CrestronString s, int bytePos, int bit)
        {
            var v = s?.Str ?? "";
            if (bytePos < 1 || bytePos > v.Length || bit < 0 || bit > 7) return 0;
            return (ushort)((v[bytePos - 1] >> bit) & 1);
        }

        public ushort SetByte(CrestronString s, ushort pos, ushort value)
        {
            if (s is null || pos < 1 || pos > s.Str.Length) return 0;
            var chars = s.Str.ToCharArray();
            chars[pos - 1] = (char)(value & 0xFF);
            s.UpdateValue(new string(chars));
            return 1;
        }

        /// <summary>SETSTRING(source, position, destination): writes source into destination at position.</summary>
        public int SetString(CrestronString source, int position, CrestronString dest)
        {
            if (dest is null || position < 1) return -1;
            var d = dest.Str;
            var src = source?.Str ?? "";
            if (d.Length < position - 1) d = d.PadRight(position - 1, ' ');
            var head = d.Substring(0, position - 1);
            var tail = d.Length > position - 1 + src.Length ? d.Substring(position - 1 + src.Length) : "";
            dest.UpdateValue(head + src + tail);
            return 0;
        }

        // ---- files
        private FileStream Handle(short h) { lock (_files) return _files.TryGetValue(h, out var fs) ? fs : null; }

        public short FileOpen(string name, ushort flags) => FileOpen(new CrestronString(name), flags);
        public short FileOpenShared(CrestronString name, ushort flags) => FileOpen(name, flags);
        public short FileOpenShared(string name, ushort flags) => FileOpen(new CrestronString(name), flags);
        public short FileBOF(short h) => (short)(Handle(h)?.Position == 0 ? 1 : 0);
        public short FileEOF(short h) { var fs = Handle(h); return (short)(fs == null || fs.Position >= fs.Length ? 1 : 0); }
        public long FileLength(short h) => Handle(h)?.Length ?? -1;
        public long FileSeek(short h, uint offset, ushort origin)
        {
            var fs = Handle(h);
            if (fs == null) return -1;
            try { return fs.Seek(offset, origin switch { 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin }); }
            catch { return -1; }
        }
        public short FileDelete(CrestronString name) => FileDelete(name?.Str);
        public short FileDelete(string name) { try { File.Delete(MapPath(name)); return 0; } catch { return -1; } }
        public short FileDeleteShared(CrestronString name) => FileDelete(name?.Str);
        public short FileDeleteShared(string name) => FileDelete(name);
        public short FindFirst(string pattern, ref FILE_INFO info) => FindFirst(new CrestronString(pattern), ref info);
        public short FindFirstShared(CrestronString pattern, ref FILE_INFO info) => FindFirst(pattern, ref info);
        public short FindFirstShared(string pattern, ref FILE_INFO info) => FindFirst(new CrestronString(pattern), ref info);
        private string _currentDir = "\\";
        public string GetCurrentDirectory() => _currentDir;
        public short SetCurrentDirectory(CrestronString dir) { _currentDir = dir?.Str ?? "\\"; return 0; }
        public short MakeDirectory(CrestronString d) => MakeDirectory(d?.Str);
        public short MakeDirectory(string d) { try { Directory.CreateDirectory(MapPath(d)); return 0; } catch { return -1; } }
        public short MakeDirectoryShared(CrestronString d) => MakeDirectory(d?.Str);
        public short MakeDirectoryShared(string d) => MakeDirectory(d);
        public short RemoveDirectory(CrestronString d) => RemoveDirectory(d?.Str);
        public short RemoveDirectory(string d) { try { Directory.Delete(MapPath(d)); return 0; } catch { return -1; } }
        public short RemoveDirectoryShared(CrestronString d) => RemoveDirectory(d?.Str);
        public short RemoveDirectoryShared(string d) => RemoveDirectory(d);

        // Binary values are written high byte first, as SIMPL+ does on Crestron processors.
        private bool Read(short h, byte[] buf) { var fs = Handle(h); return fs != null && fs.Read(buf, 0, buf.Length) == buf.Length; }
        private short Write(short h, byte[] buf) { var fs = Handle(h); if (fs == null) return -1; fs.Write(buf, 0, buf.Length); fs.Flush(); return (short)buf.Length; }
        private static byte[] BE16(int v) => new[] { (byte)(v >> 8), (byte)v };
        private static byte[] BE32(long v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        public short ReadInteger(short h, ref ushort v) { var b = new byte[2]; if (!Read(h, b)) return -1; v = (ushort)((b[0] << 8) | b[1]); return 2; }
        public short ReadSignedInteger(short h, ref short v) { var b = new byte[2]; if (!Read(h, b)) return -1; v = (short)((b[0] << 8) | b[1]); return 2; }
        public short ReadLongInteger(short h, ref uint v) { var b = new byte[4]; if (!Read(h, b)) return -1; v = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]); return 4; }
        public short ReadSignedLongInteger(short h, ref int v) { var b = new byte[4]; if (!Read(h, b)) return -1; v = (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]; return 4; }
        public short WriteInteger(short h, ushort v) => Write(h, BE16(v));
        public short WriteSignedInteger(short h, short v) => Write(h, BE16(v));
        public short WriteLongInteger(short h, uint v) => Write(h, BE32(v));
        public short WriteSignedLongInteger(short h, int v) => Write(h, BE32(v));

        public short ReadString(short h, ref CrestronString s)
        {
            ushort len = 0;
            if (ReadInteger(h, ref len) < 0) return -1;
            var b = new byte[len];
            if (!Read(h, b)) return -1;
            if (s is null) s = new CrestronString();
            s.UpdateValue(Encoding.Latin1.GetString(b));
            return (short)(len + 2);
        }
        public short WriteString(short h, CrestronString s)
        {
            var b = Encoding.Latin1.GetBytes(s?.Str ?? "");
            if (WriteInteger(h, (ushort)b.Length) < 0) return -1;
            return (short)(Write(h, b) + 2);
        }
        public short ReadStringArray(short h, ref CrestronString[] arr)
        {
            short total = 0;
            for (int i = 0; arr != null && i < arr.Length; i++) { var n = ReadString(h, ref arr[i]); if (n < 0) return -1; total += n; }
            return total;
        }
        public int WriteStringArray(short h, CrestronString[] arr)
        {
            int total = 0;
            foreach (var s in arr ?? Array.Empty<CrestronString>()) { var n = WriteString(h, s); if (n < 0) return -1; total += n; }
            return total;
        }

        public int ReadIntegerArray(short h, ref ushort[] a) { int n = 0; for (int i = 0; i < a.Length; i++) { if (ReadInteger(h, ref a[i]) < 0) return -1; n += 2; } return n; }
        public int ReadSignedIntegerArray(short h, ref short[] a) { int n = 0; for (int i = 0; i < a.Length; i++) { if (ReadSignedInteger(h, ref a[i]) < 0) return -1; n += 2; } return n; }
        public int ReadLongIntegerArray(short h, ref uint[] a) { int n = 0; for (int i = 0; i < a.Length; i++) { if (ReadLongInteger(h, ref a[i]) < 0) return -1; n += 4; } return n; }
        public int ReadSignedLongIntegerArray(short h, ref int[] a) { int n = 0; for (int i = 0; i < a.Length; i++) { if (ReadSignedLongInteger(h, ref a[i]) < 0) return -1; n += 4; } return n; }
        public int ReadIntegerArray(short h, ref ushort[,] a) { int n = 0; for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) { if (ReadInteger(h, ref a[i, j]) < 0) return -1; n += 2; } return n; }
        public int ReadSignedIntegerArray(short h, ref short[,] a) { int n = 0; for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) { if (ReadSignedInteger(h, ref a[i, j]) < 0) return -1; n += 2; } return n; }
        public int ReadLongIntegerArray(short h, ref uint[,] a) { int n = 0; for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) { if (ReadLongInteger(h, ref a[i, j]) < 0) return -1; n += 4; } return n; }
        public int ReadSignedLongIntegerArray(short h, ref int[,] a) { int n = 0; for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) { if (ReadSignedLongInteger(h, ref a[i, j]) < 0) return -1; n += 4; } return n; }
        public int WriteIntegerArray(short h, ushort[] a) { int n = 0; foreach (var v in a) { if (WriteInteger(h, v) < 0) return -1; n += 2; } return n; }
        public int WriteSignedIntegerArray(short h, short[] a) { int n = 0; foreach (var v in a) { if (WriteSignedInteger(h, v) < 0) return -1; n += 2; } return n; }
        public int WriteLongIntegerArray(short h, uint[] a) { int n = 0; foreach (var v in a) { if (WriteLongInteger(h, v) < 0) return -1; n += 4; } return n; }
        public int WriteSignedLongIntegerArray(short h, int[] a) { int n = 0; foreach (var v in a) { if (WriteSignedLongInteger(h, v) < 0) return -1; n += 4; } return n; }
        public int WriteIntegerArray(short h, ushort[,] a) { int n = 0; foreach (var v in a) { if (WriteInteger(h, v) < 0) return -1; n += 2; } return n; }
        public int WriteSignedIntegerArray(short h, short[,] a) { int n = 0; foreach (var v in a) { if (WriteSignedInteger(h, v) < 0) return -1; n += 2; } return n; }
        public int WriteLongIntegerArray(short h, uint[,] a) { int n = 0; foreach (var v in a) { if (WriteLongInteger(h, v) < 0) return -1; n += 4; } return n; }
        public int WriteSignedLongIntegerArray(short h, int[,] a) { int n = 0; foreach (var v in a) { if (WriteSignedLongInteger(h, v) < 0) return -1; n += 4; } return n; }
        public int WriteArray(short h, byte[] data, int offset, int count, int unused)
        {
            var fs = Handle(h);
            if (fs == null || data == null) return -1;
            count = Math.Clamp(count, 0, Math.Max(0, data.Length - offset));
            fs.Write(data, offset, count);
            fs.Flush();
            return count;
        }
    }
}
