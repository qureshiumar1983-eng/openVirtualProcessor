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

    /// <summary>Held while the slot loads, starts, stops or unloads, so two requests can't interleave.</summary>
    internal readonly object Lifecycle = new();

    internal ProgramSlot(Processor processor, int number)
    {
        Processor = processor;
        Number = number;
        Stepper.Changed += _ => Processor.NotifyStepChanged(this);
        _dispatcher = NewDispatcher();
    }

    public Processor Processor { get; }
    public int Number { get; }
    public SlotState State { get; private set; } = SlotState.Empty;
    public IProgramRuntime? Runtime { get; private set; }
    public string? ProgramPath { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Working directory for this slot's program files (like /user/programN).</summary>
    public string Directory => Path.Combine(Processor.DataDirectory, $"program{Number:00}");

    public SlotDispatcher Dispatcher => _dispatcher;

    /// <summary>Step-through debugging: pause the slot's logic on each signal change.</summary>
    public SignalStepper Stepper { get; } = new();

    public IReadOnlyCollection<JoinDevice> Devices => _ipTable.Values.ToList();

    public event Action<ProgramSlot>? StateChanged;

    // ---------- IP table ----------

    /// <summary>Get or create the device for an IP ID. Called when a program registers a panel.</summary>
    public JoinDevice RegisterDevice(uint ipId, string type, string description)
    {
        var dev = _ipTable.GetOrAdd(ipId, id => new JoinDevice(Number, id, type, description));
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
                var builtIn = "Built-in: help, progcomments, ipt, appstat, signals\n";
                return builtIn + string.Join('\n', _commands.Values.Select(c => $"{c.Name,-20} {c.Help}"));
            case "progcomments" or "appstat":
                return $"Slot {Number}: {State}\nProgram: {Runtime?.ProgramName ?? "(none)"}\nType: {Runtime?.Kind ?? "-"}\nFile: {ProgramPath ?? "-"}";
            case "ipt":
                return string.Join('\n', _ipTable.Values.OrderBy(d => d.IpId)
                    .Select(d => $"IPID {d.IpId:X2}  {d.Type,-28} {(d.Online ? "ONLINE" : "OFFLINE")}  {d.Description}"));
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
