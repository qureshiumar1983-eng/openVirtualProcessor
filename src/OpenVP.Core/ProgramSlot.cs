using System.Collections.Concurrent;

namespace OpenVP.Core;

public sealed record LogEntry(DateTimeOffset Time, int Slot, string Level, string Message);

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
        Stepper.Changed += _ => Processor.NotifyStepChanged(this);
        Metrics = new SlotMetrics(() => Volatile.Read(ref _generation));
        _dispatcher = NewDispatcher();
        LoadSettings();
    }

    /// <summary>The Room ID a slot has until someone sets one: <c>SLOT01</c>, <c>SLOT02</c>, ...</summary>
    public string DefaultRoomId => $"SLOT{Number:00}";

    /// <summary>
    /// The slot's Virtual Control Room ID, like the Room ID of a VC-4 program. Panels can be routed to
    /// the slot by it (<c>/ws/panel?room=</c>), and programs read it from <c>InitialParametersClass.RoomId</c>.
    /// Unique across slots, compared without regard to case, and saved in <c>slot.json</c>.
    /// </summary>
    public string RoomId => Volatile.Read(ref _roomId);

    private string _roomId = "";
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
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(new { roomId = id }));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Couldn't save the Room ID: {e.Message}", e);
            }
            Volatile.Write(ref _roomId, id);
        }
        Log("Notice", $"Room ID set to '{id}'");
        StateChanged?.Invoke(this);
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
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("roomId", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String)
                _roomId = ValidateRoomId(r.GetString());
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

    public SlotDispatcher Dispatcher => _dispatcher;

    /// <summary>Step-through debugging: pause the slot's logic on each signal change.</summary>
    public SignalStepper Stepper { get; } = new();

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
                Log("Warning", $"IP ID {ipId:X2} is also registered in slot {other.Number}. CIP panels can't tell slots apart yet and will bind to the lowest slot ({Math.Min(Number, other.Number)}); give each slot its own IP IDs, or use Room IDs with browser panels (?room=).");
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
            Processor.NotifyDeviceUnregistered(d);
    }

    /// <summary>The "ipt" console command: each IP ID, then who is connected to it.</summary>
    private string IpTableReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Slot {Number}  Room ID {RoomId}\n");
        foreach (var d in _ipTable.Values.OrderBy(d => d.IpId))
        {
            sb.Append($"IPID {d.IpId:X2}  {d.Type,-28} {(d.Online ? "ONLINE" : "OFFLINE")}  {d.Description}\n");
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

    public void Trace(string signal, SigKind kind, object value, string source)
    {
        var handlers = _traced;
        if (handlers == null && !Stepper.Enabled) return; // nobody listening: skip the record and the clock read
        var e = new TraceEvent(DateTimeOffset.Now, Number, signal, kind, value, source);
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
                return builtIn + string.Join('\n', _commands.Values.Select(c => $"{c.Name,-20} {c.Help}"));
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
                Runtime.Start();
                SetState(SlotState.Running);
                Log("Notice", "Program started");
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
            _commands.Clear();
            Interlocked.Increment(ref _generation);
            Metrics.ResetTimers();
            foreach (var id in _ipTable.Keys.ToList()) UnregisterDevice(id);
            _dispatcher.Dispose();
            _dispatcher = NewDispatcher();
            SetState(SlotState.Empty);
        }
    }

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

    private SlotDispatcher NewDispatcher() =>
        new($"slot{Number:00}", e => Log("Error", $"Unhandled exception in program: {e}"), () => Stepper.IsPaused, Processor.SlotSpinMicroseconds);

    public void Dispose() => Unload();
}
