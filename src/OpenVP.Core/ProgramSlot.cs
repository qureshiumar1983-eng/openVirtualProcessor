using System.Collections.Concurrent;
using OpenVP.Core.Integrations;

namespace OpenVP.Core;

/// <summary>One log line. <see cref="Detail"/> is extra text shown when the entry is expanded (an exception's stack trace).</summary>
public sealed record LogEntry(DateTimeOffset Time, int Slot, string Level, string Message, string? Detail = null);

/// <summary>
/// A console command registered by a program (CrestronConsole.AddNewConsoleCommand or similar).
/// The handler gets the argument string and a callback to write the response.
/// </summary>
public sealed record ConsoleCommand(string Name, string Help, Action<string, Action<string>> Handler);

/// <summary>One program slot, like slots 1–10 on a 4-Series processor or a room on VC-4.</summary>
public sealed class ProgramSlot : IDisposable
{
    private readonly ConcurrentDictionary<uint, JoinDevice> _ipTable = new();
    private readonly ConcurrentDictionary<string, ConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private SlotDispatcher _dispatcher;
    private int _generation;

    /// <summary>Held while the slot loads, starts, stops or unloads, so two requests can't interleave.</summary>
    internal readonly object Lifecycle = new();

    internal ProgramSlot(Processor processor, int number)
    {
        Processor = processor;
        Number = number;
        Monitor = new SlotMonitor(this);
        Stepper.ValueLookup = Monitor.Values.Get;
        Stepper.Changed += _ => { Processor.NotifyStepChanged(this); Monitor.Refresh(); };
        Metrics = new SlotMetrics(() => Volatile.Read(ref _generation));
        MissingFiles = new MissingFileLog(() => Directory, Log);
        Hub = new SignalHub(this);
        _dispatcher = NewDispatcher();
        LoadSettings();
        Io = new SlotIo(this);
    }

    /// <summary>Lets integrations (MQTT, webhooks, OSC, Art-Net, the REST stream) hear about chosen signals; costs nothing until one subscribes.</summary>
    public SignalHub Hub { get; }

    /// <summary>The slot's COM ports and sockets, device simulators, network redirects and IP ID remap (see <see cref="SlotIo"/>).</summary>
    public SlotIo Io { get; }

    /// <summary>The ID a panel uses to reach a program IP ID (the same ID unless it was remapped).</summary>
    public uint PublicIpId(uint programId) => Io.Remap.PublicOf(programId);

    /// <summary>Remap a program IP ID so panels reach it at <paramref name="publicId"/>. Throws ArgumentException if that clashes.</summary>
    public void SetIpRemap(uint programId, uint publicId)
    {
        Io.Remap.Set(programId, publicId, id => _ipTable.ContainsKey(id) && !Io.Remap.IsRemapped(id));
        Log("Notice", programId == publicId ? $"IP ID {programId:X2} answers at its own number again" : $"IP ID {programId:X2} now answers at {publicId:X2} (panels connect to {publicId:X2}; the program still sees {programId:X2})");
        StateChanged?.Invoke(this);
    }

    public void ClearIpRemap(uint programId)
    {
        if (!Io.Remap.Remove(programId)) return;
        Log("Notice", $"IP ID {programId:X2} answers at its own number again");
        StateChanged?.Invoke(this);
    }

    /// <summary>The device a panel reaches by connecting to <paramref name="publicId"/>: the remap is applied first.</summary>
    public JoinDevice? FindPanelDevice(uint publicId) =>
        Io.Remap.ToProgram(publicId) is { } id ? FindDevice(id) : null;

    /// <summary>The Room ID a slot has until someone sets one: <c>SLOT01</c>, <c>SLOT02</c>, ...</summary>
    public string DefaultRoomId => $"SLOT{Number:00}";

    /// <summary>
    /// The slot's Virtual Control Room ID, like the Room ID of a VC-4 program. Panels can be routed to
    /// the slot by it (<c>/ws/panel?room=</c>), and programs read it from <c>InitialParametersClass.RoomId</c>.
    /// Unique across slots, compared without regard to case, and saved in <c>slot.json</c>.
    /// </summary>
    public string RoomId => Volatile.Read(ref _roomId);

    private string _roomId = "";
    private volatile SlotSettings _settings = new();
    private readonly object _settingsGate = new();
    private string SettingsPath => Path.Combine(Directory, "slot.json");

    /// <summary>Check and normalize a Room ID: trimmed, 1-64 characters of letters, digits, <c>_ - .</c>. Throws ArgumentException.</summary>
    public static string ValidateRoomId(string? roomId)
    {
        var id = roomId?.Trim() ?? "";
        if (id.Length == 0) throw new ArgumentException("Room ID can't be empty.");
        if (id.Length > 64) throw new ArgumentException("Room ID can be at most 64 characters.");
        if (!id.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.'))
            throw new ArgumentException("Room ID can only contain letters, digits, '_', '-' and '.'.");
        return id;
    }

    /// <summary>Change the Room ID and save it. Throws ArgumentException if it is invalid or another slot has it.</summary>
    public void SetRoomId(string? roomId)
    {
        var id = ValidateRoomId(roomId);
        lock (Processor.RoomIdGate)
        {
            var other = Processor.FindSlotByRoomId(id);
            if (other != null && other != this)
                throw new ArgumentException($"Room ID '{id}' is already used by slot {other.Number}.");
            if (string.Equals(RoomId, id, StringComparison.Ordinal)) return;
            Save(s => s with { RoomId = id });
            Volatile.Write(ref _roomId, id);
        }
        Log("Notice", $"Room ID set to '{id}'");
        StateChanged?.Invoke(this);
    }

    /// <summary>The slot's saved settings (<c>slot.json</c>): name, colour, autostart, crash policy, remembered program.</summary>
    public SlotSettings Settings => _settings;

    /// <summary>Friendly name, or null.</summary>
    public string? Name => _settings.Name;

    /// <summary>
    /// Change settings and save them to <c>slot.json</c>. <paramref name="change"/> gets the current settings and returns
    /// the new ones; the result is checked. Throws ArgumentException for a bad value, InvalidOperationException if the
    /// file can't be written. The Room ID is changed with <see cref="SetRoomId"/>, not here.
    /// </summary>
    public SlotSettings UpdateSettings(Func<SlotSettings, SlotSettings> change)
    {
        SlotSettings now;
        lock (_settingsGate)
        {
            var next = change(_settings) with { RoomId = _settings.RoomId };
            now = next.Validated();
            Save(_ => now);
        }
        StateChanged?.Invoke(this);
        return now;
    }

    // Saves without checking; the caller holds the right lock or is the only writer.
    private void Save(Func<SlotSettings, SlotSettings> change)
    {
        lock (_settingsGate)
        {
            var next = change(_settings);
            try { SlotSettings.Save(SettingsPath, next); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Couldn't save the slot's settings: {e.Message}", e);
            }
            _settings = next;
        }
    }

    /// <summary>Remember the program this slot has, so it comes back after OpenVP restarts. Null forgets it.</summary>
    public void RememberProgram(ProgramMemory? program)
    {
        try { lock (_settingsGate) { if (_settings.Program == program) return; Save(s => s with { Program = program }); } }
        catch (InvalidOperationException e) { Log("Warning", e.Message); }
    }

    /// <summary>Remember whether the program should be running after a restart (set by the user's Start and Stop, not by a crash).</summary>
    public void RememberRunning(bool running)
    {
        try
        {
            lock (_settingsGate)
            {
                if (_settings.Program is not { } p || p.Running == running) return;
                Save(s => s with { Program = p with { Running = running } });
            }
        }
        catch (InvalidOperationException e) { Log("Warning", e.Message); }
    }

    /// <summary>Remember whether the slot reloads when its file changes.</summary>
    public void RememberWatch(bool watch)
    {
        try
        {
            lock (_settingsGate)
            {
                if (_settings.Program is not { } p || p.Watch == watch) return;
                Save(s => s with { Program = p with { Watch = watch } });
            }
        }
        catch (InvalidOperationException e) { Log("Warning", e.Message); }
    }

    /// <summary>After all slots exist: if an earlier slot holds this slot's saved Room ID (a hand-edited file), use the default.</summary>
    internal void DropDuplicateRoomId()
    {
        var holder = Processor.Slots.FirstOrDefault(s => s.Number < Number && string.Equals(s.RoomId, RoomId, StringComparison.OrdinalIgnoreCase));
        if (holder == null) return;
        Log("Warning", $"Room ID '{RoomId}' in {SettingsPath} is already used by slot {holder.Number}; using {DefaultRoomId} instead.");
        _roomId = DefaultRoomId;
    }

    private void LoadSettings()
    {
        _roomId = DefaultRoomId;
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = SlotSettings.FromJson(File.ReadAllText(SettingsPath));
            _roomId = s.RoomId is null ? DefaultRoomId : ValidateRoomId(s.RoomId);
            try { s = (s with { RoomId = _roomId }).Validated(); }
            catch (ArgumentException e)
            {
                Log("Warning", $"Some settings in {SettingsPath} were ignored: {e.Message}");
                s = new SlotSettings { RoomId = _roomId, Program = s.Program };
            }
            _settings = s;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _roomId = DefaultRoomId;
            Log("Warning", $"Ignoring unreadable {SettingsPath}: {e.Message}");
        }
    }

    public Processor Processor { get; }
    public int Number { get; }
    public SlotState State { get; private set; } = SlotState.Empty;
    public IProgramRuntime? Runtime { get; private set; }
    public string? ProgramPath { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Working directory for this slot's program files. Holds the slot's <c>nvram</c> and
    /// <c>user</c> folders, which programs see as <c>\NVRAM</c> and <c>\USER</c> (see <see cref="SlotFiles"/>).</summary>
    public string Directory => Path.Combine(Processor.DataDirectory, $"program{Number:00}");

    /// <summary>Maps a processor path (<c>\NVRAM\x</c>, <c>/user/x</c>) to a file inside this slot's folder.</summary>
    public string MapPath(string? processorPath) => SlotFiles.Map(Directory, processorPath);

    /// <summary>Files and folders the program looked for in <c>\NVRAM</c> or <c>\USER</c> and didn't find
    /// (config files nobody has put in place yet), so the console can say which folders to create.</summary>
    public MissingFileLog MissingFiles { get; }

    /// <summary>The program looked for a file (or folder) at this processor path and it may not be there.
    /// Logged once with the folder to create; see <see cref="MissingFiles"/>.</summary>
    public void ReportMissing(string? processorPath, bool isFolder = false) => MissingFiles.Report(processorPath, isFolder);

    /// <summary>The program uses something OpenVP doesn't support yet: add it to the shared list
    /// (<see cref="Processor.Unsupported"/>), which is what tells us what to build next.</summary>
    /// <param name="program">The program's name, so loading it twice doesn't count twice. Never written to the file.</param>
    public void ReportUnsupported(string kind, string name, string? detail = null, string? program = null) =>
        Processor.Unsupported.Report(kind, name, detail?.Replace(Directory, "<slot folder>"), program ?? Runtime?.ProgramName ?? ProgramPath);

    public SlotDispatcher Dispatcher => _dispatcher;

    /// <summary>The recorder capturing this slot right now, if any (see <see cref="SlotRecordings"/>).</summary>
    internal volatile SlotRecorder? ActiveRecorder;

    /// <summary>
    /// Force a signal from the debugger (REST <c>POST /api/slots/{n}/signals</c> uses this). Does what
    /// <see cref="IProgramRuntime.TrySetSignal"/> does, and writes the force into the slot's recording if one is running.
    /// Returns false if there is no program or the signal is unknown.
    /// </summary>
    public bool ForceSignal(string name, object value)
    {
        var rt = Runtime;
        if (rt == null) return false;
        var rec = ActiveRecorder;
        var t = rec?.Now() ?? 0;
        bool ok;
        using (ForceScope.Enter()) ok = rt.TrySetSignal(name, value);
        if (ok) rec?.RecordForce(t, name, false, value);
        return ok;
    }

    /// <summary>Pulse a digital signal from the debugger; see <see cref="ForceSignal"/> and <see cref="IProgramRuntime.TryPulseSignal"/>.</summary>
    public bool ForcePulse(string name)
    {
        var rt = Runtime;
        if (rt == null) return false;
        var rec = ActiveRecorder;
        var t = rec?.Now() ?? 0;
        bool ok;
        using (ForceScope.Enter()) ok = rt.TryPulseSignal(name);
        if (ok) rec?.RecordForce(t, name, true, null);
        return ok;
    }

    /// <summary>Step-through debugging: pause the slot's logic on each signal change.</summary>
    public SignalStepper Stepper { get; } = new();

    /// <summary>Watches, oscillation alerts and the never-changed tracker for this slot (see <see cref="SlotMonitor"/>).</summary>
    public SlotMonitor Monitor { get; }

    /// <summary>Exceptions the program threw (event handlers, timers, threads, background work).</summary>
    public ExceptionLog Exceptions { get; } = new();

    /// <summary>
    /// A program threw an exception that nothing caught. Logged as an Error with the full stack trace (expand the entry),
    /// and added to <see cref="Exceptions"/>. File and line numbers appear when the program's .pdb was loaded with it.
    /// </summary>
    public void ReportException(string source, Exception e)
    {
        var rec = ExceptionReport.Build(e, source, Number);
        Exceptions.Add(rec);
        Processor.WriteLog(new LogEntry(rec.Time, Number, "Error", $"Unhandled {rec.Type} in {source}: {rec.Message}", rec.Stack));
    }

    /// <summary>Threads, background work and timers the program started, for the metrics page.</summary>
    public SlotMetrics Metrics { get; }

    /// <summary>Goes up each time a program is loaded or unloaded, so threads left over from an earlier program can be told apart.</summary>
    public int Generation => Volatile.Read(ref _generation);

    public IReadOnlyCollection<JoinDevice> Devices => _ipTable.Values.ToList();

    public event Action<ProgramSlot>? StateChanged;

    // ---------- IP table ----------

    /// <summary>Get or create the device for an IP ID. Called when a program registers a panel.</summary>
    public JoinDevice RegisterDevice(uint ipId, string type, string description)
    {
        var created = false;
        var dev = _ipTable.GetOrAdd(ipId, id => { created = true; return new JoinDevice(Number, id, type, description); });
        if (created)
        {
            var other = Processor.Slots.FirstOrDefault(s => s != this && s.FindDevice(ipId) != null);
            if (other != null)
                Log("Warning", $"IP ID {ipId:X2} is also registered in slot {other.Number}. A panel with no Room ID binds to the lowest slot ({Math.Min(Number, other.Number)}); set this slot's Room ID ({RoomId}) on its panels, or give each slot its own IP IDs.");
        }
        dev.Type = type;
        dev.Description = description;
        Processor.NotifyDeviceRegistered(dev);
        return dev;
    }

    public JoinDevice? FindDevice(uint ipId) => _ipTable.TryGetValue(ipId, out var d) ? d : null;

    public void UnregisterDevice(uint ipId)
    {
        if (_ipTable.TryRemove(ipId, out var d))
        {
            Processor.Bus.Detach(d); // a runtime that forgot to leave the EISC bus mustn't leave a stale link behind
            Processor.NotifyDeviceUnregistered(d);
        }
    }

    /// <summary>The "ipt" console command: each IP ID, then who is connected to it.</summary>
    private string IpTableReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Slot {Number}  Room ID {RoomId}\n");
        foreach (var d in _ipTable.Values.OrderBy(d => d.IpId))
        {
            sb.Append($"IPID {d.IpId:X2}{(Io.Remap.IsRemapped(d.IpId) ? $" (at {Io.Remap.PublicOf(d.IpId):X2})" : "")}  {d.Type,-28} {(d.Online ? "ONLINE" : "OFFLINE")}  {d.Description}\n");
            foreach (var c in d.Connections)
                sb.Append($"         {c.Transport,-9} {c.Endpoint,-22} {c.Hostname ?? c.Peer ?? "-",-24} {c.ClientKind ?? ""}\n");
        }
        return _ipTable.IsEmpty ? sb.Append("IP table is empty.").ToString() : sb.ToString().TrimEnd('\n');
    }

    // ---------- logging, tracing, console ----------

    public void Log(string level, string message) => Processor.WriteLog(new LogEntry(DateTimeOffset.Now, Number, level, message));

    private volatile Action<TraceEvent>? _traced;
    private readonly object _traceGate = new();

    /// <summary>
    /// Every signal change in this slot's program. Subscribe per slot, so a debugger open on one slot
    /// doesn't slow the others. Handlers run on the slot thread: keep them cheap.
    /// </summary>
    public event Action<TraceEvent>? Traced
    {
        add { lock (_traceGate) _traced += value; }
        remove { lock (_traceGate) _traced -= value; }
    }

    /// <summary>
    /// True while someone is listening to this slot (a trace subscriber, or the step debugger). Runtimes
    /// check it before building a trace name or calling <see cref="Trace"/>. Read without a lock.
    /// </summary>
    public bool TraceWanted => _traced != null || Stepper.Enabled;

    public void Trace(string signal, SigKind kind, object value, string source, string? symbol = null, string? cause = null, int depth = 0, long chain = 0)
    {
        var handlers = _traced;
        if (handlers == null && !Stepper.Enabled) return; // nobody listening: skip the record and the clock read
        var e = new TraceEvent(DateTimeOffset.Now, Number, signal, kind, value, source, symbol, cause, depth, chain);
        handlers?.Invoke(e);
        Stepper.OnChange(e); // blocks here while the debugger is stepping
    }

    public bool AddConsoleCommand(ConsoleCommand cmd) => _commands.TryAdd(cmd.Name, cmd);

    public IReadOnlyCollection<ConsoleCommand> ConsoleCommands => _commands.Values.ToList();

    /// <summary>Run a console command for this slot. Returns the text response.</summary>
    public string RunConsoleCommand(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return string.Empty;
        var space = line.IndexOf(' ');
        var name = space < 0 ? line : line[..space];
        var args = space < 0 ? string.Empty : line[(space + 1)..];

        switch (name.ToLowerInvariant())
        {
            case "help" or "?":
                var builtIn = "Built-in: help, progcomments, ipt, appstat, signals, cpuload, ramfree, taskstat, metrics\n";
                return builtIn + string.Join('\n', _commands.Values.Concat(Processor.ExtraConsoleCommands.Values).Select(c => $"{c.Name,-20} {c.Help}"));
            case "progcomments" or "appstat":
                return $"Slot {Number}: {State}\nProgram: {Runtime?.ProgramName ?? "(none)"}\nType: {Runtime?.Kind ?? "-"}\nFile: {ProgramPath ?? "-"}";
            case "ipt":
                return IpTableReport();
            case "cpuload" or "ramfree" or "taskstat" or "metrics":
                return MetricsReport.Console(Processor, this, name.ToLowerInvariant());
            case "signals":
                return string.Join('\n', (Runtime?.GetSignals() ?? Array.Empty<SignalInfo>())
                    .Select(s => $"{s.Kind,-8} {s.Name,-40} {s.Value}"));
        }

        if (Processor.ExtraConsoleCommands.TryGetValue(name, out var extra) && !_commands.ContainsKey(name))
        {
            var text = new System.Text.StringBuilder();
            extra.Handler(args, s => text.Append(s)); // a plugin's command: runs on the plugin's thread, not this slot's
            return text.ToString();
        }
        if (!_commands.TryGetValue(name, out var cmd)) return $"Bad or incomplete command: {name}";
        var sb = new System.Text.StringBuilder();
        Dispatcher.Invoke(() => cmd.Handler(args, s => sb.Append(s)));
        return sb.ToString();
    }

    // ---------- lifecycle ----------

    public void Load(string path, IProgramRuntime runtime)
    {
        lock (Lifecycle)
        {
            Unload();
            MissingFiles.Clear();
            ProgramPath = path;
            Runtime = runtime;
            Interlocked.Increment(ref _generation);
            LastError = null;
            SetState(SlotState.Loaded);
            Log("Notice", $"Loaded {runtime.Kind} program '{runtime.ProgramName}'");
        }
    }

    public void Start()
    {
        lock (Lifecycle)
        {
            if (Runtime == null) throw new InvalidOperationException($"Slot {Number} has no program loaded.");
            if (State == SlotState.Running) return;
            try
            {
                Monitor.ProgramStarting();
                Runtime.Start();
                SetState(SlotState.Running);
                Log("Notice", "Program started");
                Monitor.ProgramStarted();
            }
            catch (Exception e)
            {
                Fail(e);
                throw;
            }
        }
    }

    public void Stop()
    {
        Stepper.Run(); // release a paused change first, or a step-held Start/Invoke would block the lock
        lock (Lifecycle)
        {
            if (Runtime == null || State != SlotState.Running) return;
            try { Runtime.Stop(); }
            catch (Exception e) { Log("Error", $"Error while stopping: {e.Message}"); }
            SetState(SlotState.Stopped);
            Log("Notice", "Program stopped");
            Monitor.ProgramStopped();
        }
    }

    public void Unload()
    {
        Stepper.Run(); // release a paused change first, or a step-held Start/Invoke would block the lock
        lock (Lifecycle)
        {
            Stop();
            // From here on, queued events (timers, presses, socket data) are dropped instead of running
            // against a program that is being torn down. The runtime's own cleanup can still Invoke.
            _dispatcher.Quiesce();
            try { Runtime?.Dispose(); }
            catch (Exception e) { Log("Error", $"Error while unloading: {e.Message}"); }
            Runtime = null;
            ProgramPath = null;
            Exceptions.Clear();
            Monitor.Refresh();
            _commands.Clear();
            Interlocked.Increment(ref _generation);
            Metrics.ResetTimers();
            foreach (var id in _ipTable.Keys.ToList()) UnregisterDevice(id);
            Io.ProgramUnloaded();
            _dispatcher.Dispose();
            _dispatcher = NewDispatcher();
            SetState(SlotState.Empty);
        }
    }

    /// <summary>A running program faulted (an unhandled exception, or the slot is stuck). The slot goes to Error and
    /// <see cref="Faulted"/> fires, which is what the <see cref="CrashSupervisor"/> acts on. Does nothing unless the slot is running.
    /// Doesn't take the lifecycle lock, so it is safe to call from the slot's own thread.</summary>
    public void Fault(string reason)
    {
        lock (_faultGate)
        {
            if (State != SlotState.Running) return;
            LastError = reason;
            Log("Error", $"The program faulted: {reason}");
            SetState(SlotState.Error);
        }
        Faulted?.Invoke(this, reason);
    }

    private readonly object _faultGate = new();

    /// <summary>A running program faulted; see <see cref="Fault"/>. Handlers must not block.</summary>
    public event Action<ProgramSlot, string>? Faulted;

    public void Fail(Exception e)
    {
        LastError = e.ToString();
        Log("Error", e.Message);
        SetState(SlotState.Error);
    }

    private void SetState(SlotState s)
    {
        State = s;
        StateChanged?.Invoke(this);
    }

    // What was running, in words: the dispatcher knows a panel's input event as the method that raises it.
    private static string FriendlySource(string? label) =>
        string.IsNullOrEmpty(label) ? "an event handler" : label.StartsWith("BasicTriList.OnInputFromDevice", StringComparison.Ordinal) ? "a panel's SigChange handler" : label;

    private SlotDispatcher NewDispatcher() =>
        new($"slot{Number:00}", e =>
        {
            ReportException(FriendlySource(Dispatcher.GetSnapshot().CurrentLabel), e);
            if (_settings.Crash.FaultOnUnhandled) Fault($"unhandled {e.GetType().Name}: {e.Message}");
        }, () => Stepper.IsPaused, Processor.SlotSpinMicroseconds);

    public void Dispose() { Unload(); Hub.Dispose(); Monitor.Dispose(); Io.Dispose(); }
}
