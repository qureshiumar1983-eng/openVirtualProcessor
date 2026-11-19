using OpenVP.Core;

namespace OpenVP.Lpz;

/// <summary>
/// Loads the compiled SIMPL+ modules shipped in a .lpz into a slot: one load context per program (see
/// <see cref="LpzLoadContext"/>), with the SIMPL# runtime pointed at the slot and library callbacks on a thread
/// of their own. Used by compiled programs and by SIMPL Windows source that borrows modules from its .lpz.
/// </summary>
public sealed class SplusModuleFactory : IDisposable
{
    private readonly ProgramSlot _slot;
    private readonly LpzPackage _pkg;
    private LpzLoadContext? _alc;
    private SlotDispatcher? _callbacks;

    public SplusModuleFactory(ProgramSlot slot, LpzPackage pkg)
    {
        _slot = slot;
        _pkg = pkg;
    }

    /// <summary>Instantiate the module in <paramref name="dll"/> as the instance at <paramref name="path"/>.
    /// Null if it can't run, with the reason (and any assemblies that couldn't be found) in <paramref name="error"/>.</summary>
    public ISplusModule? Create(string dll, string path, out string? error)
    {
        error = null;
        object? instance = null;
        try
        {
            if (_alc == null)
            {
                var dir = Path.Combine(_slot.Directory, "lpz");
                _pkg.ExtractAssemblies(dir);
                _alc = new LpzLoadContext(_slot.Number, dir, AppContext.BaseDirectory);
                // Point the SIMPL# runtime used by modules and their libraries at this slot.
                var shim = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("OpenVP.SimplSharp"));
                var bridge = shim.GetType("OpenVP.SimplSharp.OpenVPBridge");
                bridge?.GetProperty("Slot")?.SetValue(null, _slot);
                // Library callbacks get a thread of their own, so the step debugger holding the logic
                // thread doesn't freeze the SIMPL+ modules that depend on them.
                _callbacks = new SlotDispatcher($"slot{_slot.Number:00}-simplsharp",
                    e => _slot.Log("Error", $"Unhandled exception in SIMPL# library callback: {e}"));
                bridge?.GetProperty("Callbacks")?.SetValue(null, _callbacks);
                _slot.Metrics.Callbacks = _callbacks;
            }
            var asm = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName(Path.GetFileNameWithoutExtension(dll)));
            var splusObject = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("SplusObjects"))
                .GetType("Crestron.Logos.SplusObjects.SplusObject", throwOnError: true)!;
            var type = asm.GetTypes().FirstOrDefault(t => !t.IsAbstract && splusObject.IsAssignableFrom(t))
                ?? throw new InvalidDataException("no SIMPL+ module class");
            var ctor = type.GetConstructors().First(c => c.GetParameters().Length == 3);
            var encType = ctor.GetParameters()[2].ParameterType;
            instance = ctor.Invoke(new object[] { path, path, Enum.ToObject(encType, 1) });
            return (ISplusModule)instance;
        }
        catch (Exception e)
        {
            // A module that was created but can't run must release the program-wide start-up barrier it joined
            // in its constructor; otherwise every other module's WaitForInitializationComplete waits for it.
            try { (instance as ISplusModule)?.Stop(); } catch (Exception) { }
            var inner = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
            var unresolved = _alc?.Unresolved.Count > 0 ? $" (missing: {string.Join(", ", _alc.Unresolved.Distinct())})" : "";
            error = $"{inner.GetType().Name}: {inner.Message}{unresolved}";
            return null;
        }
    }

    public void Dispose()
    {
        if (_slot.Metrics.Callbacks == _callbacks) _slot.Metrics.Callbacks = null;
        // A SIMPL# library can park a worker loop on this dispatcher (the Crestron driver SDK's DriverClock does) that
        // never returns, and nothing can stop it from here. Waiting the full 5 s for it made every unload of such a
        // program stall; a short wait still lets ordinary callbacks finish.
        _callbacks?.Dispose(TimeSpan.FromMilliseconds(250));
        _callbacks = null;
        _alc?.Unload();
        _alc = null;
    }
}

/// <summary>The logic engine a SIMPL+ module instance runs in: how it drives and reads signals.</summary>
public interface ISplusEngine
{
    void Drive(int sig, object value, string source);
    object Value(int sig);
    ProgramSlot Slot { get; }
}

/// <summary>
/// A compiled SIMPL+ module instance (record type −48) in a logic engine. The record's inputs are the module's
/// digital inputs (ids 0…field10−1) followed by its analog/serial inputs (ids 0…); outputs likewise split at
/// field 11. Parameter id n is the record's n-th parameter (the first two are the module and .dll names).
/// Signal numbers in the record are the engine's; 0 and 1 are the constants.
/// </summary>
public sealed class SplusModuleHost : ISplusHost
{
    private readonly BinSymbol _sym;
    private readonly ISplusEngine _engine;
    private readonly int _digitalIns, _digitalOuts;
    private readonly ISplusModule _module;
    private readonly Dictionary<(bool, uint), SigKind> _pins;
    private readonly int _one;

    /// <param name="oneSignal">The engine's signal for the constant 1 (1 in compiled programs).</param>
    public SplusModuleHost(ISplusEngine engine, BinSymbol sym, ISplusModule module, int oneSignal = 1)
    {
        _engine = engine;
        _one = oneSignal;
        _sym = sym;
        _module = module;
        _digitalIns = Math.Min(sym.FieldInt(10) ?? 0, sym.Inputs.Length);
        _digitalOuts = Math.Min(sym.FieldInt(11) ?? 0, sym.Outputs.Length);
        _module.AttachHost(this);
        _module.Initialize();
        _pins = new Dictionary<(bool, uint), SigKind>(_module.Pins);
        for (int i = 0; i < sym.Params.Count; i++)
        {
            var p = sym.Params[i];
            _module.SetParameter((uint)i, p.Text ?? (object)(p.Number ?? 0));
        }
    }

    public string DllName => _sym.Params.Count > 1 ? _sym.Params[1].Text ?? "" : "";

    private (SigKind kind, uint id)? InputOf(int pin)
    {
        if (pin <= _digitalIns) return (SigKind.Digital, (uint)(pin - 1));
        var id = (uint)(pin - 1 - _digitalIns);
        return _pins.TryGetValue((true, id), out var k) ? (k, id) : null;
    }

    private int OutputPin(SigKind kind, uint id) =>
        kind == SigKind.Digital ? (int)id + 1 : _digitalOuts + (int)id + 1;

    public SigKind? InputKind(int pin) => InputOf(pin)?.kind;

    public SigKind? OutputKind(int pin)
    {
        if (pin <= _digitalOuts) return SigKind.Digital;
        return _pins.TryGetValue((false, (uint)(pin - 1 - _digitalOuts)), out var k) ? k : null;
    }

    public bool WantsStartEdge(int pin) => pin <= _digitalIns;

    public void Start()
    {
        // A digital input tied to the constant 1 starts low here: the runtime's start-up edge on "1" then raises
        // it, so the module gets the PUSH/CHANGE a processor gives an input held high by a typed 1.
        for (int pin = 1; pin <= _sym.Inputs.Length; pin++)
            if (InputOf(pin) is var (kind, id))
                _module.Preset(kind, id, _sym.Inputs[pin - 1] == _one && kind == SigKind.Digital ? SigValue.False : _engine.Value(_sym.Inputs[pin - 1]));
        _module.StartMain();
    }

    public void OnInput(int pin, object n)
    {
        if (InputOf(pin) is var (kind, id)) _module.Input(kind, id, n);
    }

    public void Stop() => _module.Stop();

    // ---------------- ISplusHost

    // A module's outputs queue here and one drain on the slot thread runs them, so a burst costs one post, not
    // one per output. The drain calls Drive once per output, in the order they were set, so each output
    // settles before the next, as when each had its own post. It posts itself again between batches, so
    // other events (a panel press, a timer) still get their turn.
    private readonly object _outGate = new();
    private List<(int sig, object value)> _outbox = new(), _spare = new();
    private bool _drainPosted;

    public void Output(SigKind kind, uint id, object value)
    {
        var pin = OutputPin(kind, id);
        if (pin < 1 || pin > _sym.Outputs.Length) return;
        var sig = _sym.Outputs[pin - 1];
        if (sig <= 1) return;
        bool post;
        lock (_outGate)
        {
            _outbox.Add((sig, value));
            post = !_drainPosted;
            _drainPosted = true;
        }
        if (post) _engine.Slot.Dispatcher.Post(static (n, _, _) => ((SplusModuleHost)n!).Drain(), this, null, 0);
    }

    private void Drain()
    {
        List<(int sig, object value)> batch;
        lock (_outGate) { batch = _outbox; _outbox = _spare; _spare = batch; }
        try
        {
            foreach (var (sig, value) in batch)
            {
                try { _engine.Drive(sig, value, _sym.Path); }
                catch (Exception e) { _engine.Slot.Log("Error", $"{_sym.Path}: {e.Message}"); }
            }
        }
        finally
        {
            batch.Clear();
            bool again;
            lock (_outGate) { again = _outbox.Count > 0; if (!again) _drainPosted = false; }
            if (again) _engine.Slot.Dispatcher.Post(static (n, _, _) => ((SplusModuleHost)n!).Drain(), this, null, 0);
        }
    }

    public bool IsConnected(bool isInput, SigKind kind, uint id)
    {
        int pin = isInput ? (kind == SigKind.Digital ? (int)id + 1 : _digitalIns + (int)id + 1) : OutputPin(kind, id);
        var arr = isInput ? _sym.Inputs : _sym.Outputs;
        // The compiler gives a pin left blank (or on a commented-out signal) its own unused signal, so 0 and 1
        // here are a literal 0 or 1 typed on the pin, which SIMPL+ counts as connected.
        return pin >= 1 && pin <= arr.Length;
    }

    public ProgramSlot Slot => _engine.Slot;

    public string FileRoot => _engine.Slot.Directory;

    public void Log(string level, string message) => _engine.Slot.Log(level, message);
}
