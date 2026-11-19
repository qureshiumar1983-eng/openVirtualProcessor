using OpenVP.Core;

namespace OpenVP.Simpl;

/// <summary>What a symbol behavior can see and do while the program runs.</summary>
public interface ISymbolContext
{
    SmwSymbol Symbol { get; }
    int InputCount { get; }
    int OutputCount { get; }
    bool IsConnected(int inputPin);
    bool Digital(int inputPin);
    ushort Analog(int inputPin);
    string Serial(int inputPin);
    /// <summary>Drive an output. The typed overloads never box; each converts to the output signal's kind
    /// the way SigValue.Coerce does.</summary>
    void SetOutput(int outputPin, object value);
    void SetOutput(int outputPin, bool value);
    void SetOutput(int outputPin, ushort value);
    void SetOutput(int outputPin, string value);
    object GetOutput(int outputPin);
    string Param(int index);
    long ParamNumber(int index, long fallback);

    /// <summary>Run an action after a delay on the slot thread. Returns a handle to cancel.</summary>
    IDisposable After(long milliseconds, Action action);
    void Warn(string message);
}

/// <summary>Runtime behavior of one symbol type. Pins are 1-based.</summary>
public abstract class SymbolBehavior
{
    /// <summary>Kind of each input pin, for symbols whose file entry has no n#I group counts.</summary>
    public virtual SigKind InputKind(int pin) => SigKind.Digital;
    public virtual SigKind OutputKind(int pin) => SigKind.Digital;

    /// <summary>Called once at program start, after every signal has its initial value.</summary>
    public virtual void Start(ISymbolContext ctx) { }

    /// <summary>Called when the signal on an input pin changes, with the old and new value boxed.</summary>
    public abstract void OnInput(ISymbolContext ctx, int pin, object oldValue, object newValue);

    // The typed entry points the engine calls. The defaults box (digital uses the shared boxes) and call
    // OnInput, so a behavior may implement only that; the common ones override these to skip the boxing.
    public virtual void OnDigital(ISymbolContext ctx, int pin, bool oldValue, bool newValue) => OnInput(ctx, pin, SigValue.Box(oldValue), SigValue.Box(newValue));
    public virtual void OnAnalog(ISymbolContext ctx, int pin, ushort oldValue, ushort newValue) => OnInput(ctx, pin, oldValue, newValue);
    public virtual void OnSerial(ISymbolContext ctx, int pin, string oldValue, string newValue) => OnInput(ctx, pin, oldValue, newValue);

    protected static bool Rising(object oldV, object newV) => oldV is false && newV is true;
    protected static bool Falling(object oldV, object newV) => oldV is true && newV is false;
}

// ------------------------------------------------------------------ digital logic

public sealed class AndSymbol : SymbolBehavior
{
    public override void Start(ISymbolContext c) => Eval(c);
    public override void OnInput(ISymbolContext c, int pin, object o, object n) => Eval(c);
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n) => Eval(c);
    public override void OnAnalog(ISymbolContext c, int pin, ushort o, ushort n) => Eval(c);
    public override void OnSerial(ISymbolContext c, int pin, string o, string n) => Eval(c);
    private static void Eval(ISymbolContext c)
    {
        bool any = false, all = true;
        for (int i = 1; i <= c.InputCount; i++)
        {
            if (!c.IsConnected(i)) continue;
            any = true;
            all &= c.Digital(i);
        }
        c.SetOutput(1, any && all);
    }
}

public sealed class OrSymbol : SymbolBehavior
{
    public override void Start(ISymbolContext c) => Eval(c);
    public override void OnInput(ISymbolContext c, int pin, object o, object n) => Eval(c);
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n) => Eval(c);
    public override void OnAnalog(ISymbolContext c, int pin, ushort o, ushort n) => Eval(c);
    public override void OnSerial(ISymbolContext c, int pin, string o, string n) => Eval(c);
    private static void Eval(ISymbolContext c)
    {
        var v = false;
        for (int i = 1; i <= c.InputCount; i++) v |= c.IsConnected(i) && c.Digital(i);
        c.SetOutput(1, v);
    }
}

public sealed class XorSymbol : SymbolBehavior
{
    public override void Start(ISymbolContext c) => Eval(c);
    public override void OnInput(ISymbolContext c, int pin, object o, object n) => Eval(c);
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n) => Eval(c);
    public override void OnAnalog(ISymbolContext c, int pin, ushort o, ushort n) => Eval(c);
    public override void OnSerial(ISymbolContext c, int pin, string o, string n) => Eval(c);
    private static void Eval(ISymbolContext c)
    {
        var count = 0;
        for (int i = 1; i <= c.InputCount; i++) if (c.IsConnected(i) && c.Digital(i)) count++;
        c.SetOutput(1, count % 2 == 1);
    }
}

/// <summary>NOT: each output is the inverse of the matching input.</summary>
public sealed class NotSymbol : SymbolBehavior
{
    public override void Start(ISymbolContext c)
    {
        for (int i = 1; i <= c.OutputCount; i++) c.SetOutput(i, !c.Digital(i));
    }
    public override void OnInput(ISymbolContext c, int pin, object o, object n) => c.SetOutput(pin, !(bool)n);
    // Only a digital change is a bool; any other kind reaches OnInput and fails the cast, as it always did.
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n) => c.SetOutput(pin, !n);
}

/// <summary>Buffer: input 1 enables; while enabled, output k follows input k+1. Disabled outputs go low.</summary>
public sealed class BufferSymbol : SymbolBehavior
{
    private readonly SigKind _kind;
    public BufferSymbol(SigKind kind) { _kind = kind; }
    public override SigKind InputKind(int pin) => pin == 1 ? SigKind.Digital : _kind;
    public override SigKind OutputKind(int pin) => _kind;

    public override void Start(ISymbolContext c) => All(c);

    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (pin == 1) All(c);
        else if (c.Digital(1)) c.SetOutput(pin - 1, n);
    }

    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n)
    {
        if (pin == 1) All(c);
        else if (c.Digital(1)) c.SetOutput(pin - 1, n);
    }

    public override void OnAnalog(ISymbolContext c, int pin, ushort o, ushort n)
    {
        if (pin == 1) All(c);
        else if (c.Digital(1)) c.SetOutput(pin - 1, n);
    }

    public override void OnSerial(ISymbolContext c, int pin, string o, string n)
    {
        if (pin == 1) All(c);
        else if (c.Digital(1)) c.SetOutput(pin - 1, n);
    }

    private void All(ISymbolContext c)
    {
        var on = c.Digital(1);
        for (int k = 1; k <= c.OutputCount; k++)
        {
            switch (_kind)
            {
                case SigKind.Digital: c.SetOutput(k, on && c.Digital(k + 1)); break;
                case SigKind.Analog: c.SetOutput(k, on ? c.Analog(k + 1) : (ushort)0); break;
                default: c.SetOutput(k, on ? c.Serial(k + 1) : string.Empty); break;
            }
        }
    }
}

/// <summary>Interlock: a rising edge on input k drives output k high and every other output low.</summary>
public sealed class InterlockSymbol : SymbolBehavior
{
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (!Rising(o, n)) return;
        for (int k = 1; k <= c.OutputCount; k++) c.SetOutput(k, k == pin);
    }
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n)
    {
        if (o || !n) return;
        for (int k = 1; k <= c.OutputCount; k++) c.SetOutput(k, k == pin);
    }
}

/// <summary>Toggle: each rising edge on input 1 flips the output. Optional inputs 2 (set) and 3 (reset).</summary>
public sealed class ToggleSymbol : SymbolBehavior
{
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (!Rising(o, n)) return;
        var current = (bool)c.GetOutput(1);
        c.SetOutput(1, pin switch { 1 => !current, 2 => true, 3 => false, _ => current });
    }
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n)
    {
        if (o || !n) return;
        var current = (bool)c.GetOutput(1);
        c.SetOutput(1, pin switch { 1 => !current, 2 => true, 3 => false, _ => current });
    }
}

/// <summary>Set/reset latch: input 1 sets, input 2 resets (reset wins).</summary>
public sealed class SrLatchSymbol : SymbolBehavior
{
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (c.Digital(2)) c.SetOutput(1, false);
        else if (pin == 1 && Rising(o, n)) c.SetOutput(1, true);
    }
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n)
    {
        if (c.Digital(2)) c.SetOutput(1, false);
        else if (pin == 1 && !o && n) c.SetOutput(1, true);
    }
}

/// <summary>One shot: a rising edge on input 1 drives the output high for P1 (default 0.5 s).</summary>
public sealed class OneShotSymbol : SymbolBehavior
{
    private IDisposable? _timer;
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (pin != 1 || !Rising(o, n)) return;
        Trigger(c);
    }
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n)
    {
        if (pin != 1 || o || !n) return;
        Trigger(c);
    }
    private void Trigger(ISymbolContext c)
    {
        _timer?.Dispose();
        c.SetOutput(1, true);
        _timer = c.After(c.ParamNumber(1, 500), () => c.SetOutput(1, false));
    }
}

/// <summary>Delay: output goes high P1 after the input goes high (if still high); low as soon as the input drops.</summary>
public sealed class DelaySymbol : SymbolBehavior
{
    private IDisposable? _timer;
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (pin != 1) return;
        Change(c, n is true);
    }
    public override void OnDigital(ISymbolContext c, int pin, bool o, bool n)
    {
        if (pin != 1) return;
        Change(c, n);
    }
    private void Change(ISymbolContext c, bool high)
    {
        _timer?.Dispose();
        if (high) _timer = c.After(c.ParamNumber(1, 1000), () => c.SetOutput(1, true));
        else c.SetOutput(1, false);
    }
}

// ------------------------------------------------------------------ analog / serial

/// <summary>Analog Initialize, in its two forms. One trigger and N outputs: output k is set to parameter k at
/// program start and on each rising edge of input 1. N triggers and one output: a rising edge on input k sets
/// the output to parameter k (the constant 1 rises at program start, so a trigger wired to it loads then).</summary>
public sealed class AnalogInitSymbol : SymbolBehavior
{
    public override SigKind OutputKind(int pin) => SigKind.Analog;
    private static bool Multi(ISymbolContext c) => c.InputCount > 1 && c.OutputCount == 1;
    public override void Start(ISymbolContext c)
    {
        if (!Multi(c)) Fire(c);
    }
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (!Rising(o, n)) return;
        if (Multi(c)) Load(c, pin, 1);
        else Fire(c);
    }
    private static void Fire(ISymbolContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++) Load(c, k, k);
    }
    private static void Load(ISymbolContext c, int param, int outputPin) =>
        c.SetOutput(outputPin, (ushort)Math.Clamp(c.ParamNumber(param, 0), 0, 65535));
}

/// <summary>Equate: digital output k is high while the analog input (input 1) equals parameter k and the
/// digital enable (input 2) is high. With the enable low every output is low; an unconnected enable is
/// treated as enabled.</summary>
public sealed class EquateSymbol : SymbolBehavior
{
    public override SigKind InputKind(int pin) => pin == 2 ? SigKind.Digital : SigKind.Analog;
    public override void Start(ISymbolContext c) => Eval(c);
    public override void OnInput(ISymbolContext c, int pin, object o, object n) => Eval(c);
    private static void Eval(ISymbolContext c)
    {
        var enabled = !c.IsConnected(2) || c.Digital(2);
        var v = c.Analog(1);
        for (int k = 1; k <= c.OutputCount; k++) c.SetOutput(k, enabled && c.ParamNumber(k, -1) == v);
    }
}

/// <summary>Serial I/O style send: on a rising edge of input k, output 1 gets the string in parameter k.</summary>
public sealed class SerialSendSymbol : SymbolBehavior
{
    public override SigKind OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (Rising(o, n)) c.SetOutput(1, SmwFile.ParseString(c.Param(pin)));
    }
}

/// <summary>
/// Analog Ramp (Bounds Limited), SmC 540: input 1 ramps the output up while held, input 2 down; both or
/// neither hold. Parameters as in the compiled form (LPZ type 223): P1 time for the full lower-to-upper span
/// in 1/100 s, P2 lower bound, P3 upper bound, P4 initial value. Elapsed-time based, updated every 20 ms.
/// </summary>
public sealed class BoundedRampSymbol : SymbolBehavior
{
    private double _value;
    private int _dir, _shown = int.MinValue;
    private long _last;
    private IDisposable? _timer;

    public override SigKind OutputKind(int pin) => SigKind.Analog;

    private static (long lo, long hi) Bounds(ISymbolContext c)
    {
        long lo = c.ParamNumber(2, 0), hi = c.ParamNumber(3, 65535);
        return hi < lo ? (hi, lo) : (lo, hi);
    }

    public override void Start(ISymbolContext c)
    {
        var (lo, hi) = Bounds(c);
        _value = Math.Clamp(c.ParamNumber(4, lo), lo, hi);
        Publish(c);
    }

    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (pin > 2) return;
        Advance(c);
        _dir = c.Digital(1) == c.Digital(2) ? 0 : c.Digital(1) ? 1 : -1;
        _timer?.Dispose();
        _timer = null;
        if (_dir != 0) Schedule(c);
    }

    private void Schedule(ISymbolContext c) => _timer = c.After(20, () =>
    {
        Advance(c);
        if (_dir != 0) Schedule(c);
    });

    private void Advance(ISymbolContext c)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_dir != 0)
        {
            var (lo, hi) = Bounds(c);
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
            var time = Math.Max(1, c.ParamNumber(1, 500)) * 10.0;
            _value = Math.Clamp(_value + _dir * (hi - lo) * ms / time, lo, hi);
            if (_value <= lo && _dir < 0 || _value >= hi && _dir > 0) _dir = 0;
            Publish(c);
        }
        _last = now;
    }

    private void Publish(ISymbolContext c)
    {
        var v = (int)Math.Round(_value);
        if (v == _shown) return;
        _shown = v;
        c.SetOutput(1, (ushort)(short)v);
    }
}

/// <summary>
/// Analog Rate Limiter, SmC 66: the output follows input 1 but moves no faster than P1 = time to traverse
/// the full 0-65535 range, in 1/100 s (0 = no limit). Same behavior as LPZ type 80.
/// </summary>
public sealed class RateLimiterSymbol : SymbolBehavior
{
    private double _value;
    private ushort _target;
    private long _last;
    private int _shown = -1;
    private IDisposable? _timer;

    public override SigKind InputKind(int pin) => SigKind.Analog;
    public override SigKind OutputKind(int pin) => SigKind.Analog;

    public override void OnInput(ISymbolContext c, int pin, object o, object n)
    {
        if (pin != 1) return;
        if (_timer != null) Advance(c);
        _target = c.Analog(1);
        _last = System.Diagnostics.Stopwatch.GetTimestamp();
        if (c.ParamNumber(1, 500) <= 0) { _timer?.Dispose(); _timer = null; _value = _target; Publish(c); return; }
        if (_timer == null && Math.Round(_value) != _target) Schedule(c);
    }

    private void Schedule(ISymbolContext c) => _timer = c.After(20, () =>
    {
        _timer = null;
        if (Advance(c)) Schedule(c);
    });

    private bool Advance(ISymbolContext c)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
        var reach = 65535.0 * ms / (c.ParamNumber(1, 500) * 10.0);
        _value = _value < _target ? Math.Min(_value + reach, _target) : Math.Max(_value - reach, _target);
        Publish(c);
        if (_value != _target) return true;
        _timer?.Dispose();
        _timer = null;
        return false;
    }

    private void Publish(ISymbolContext c)
    {
        var v = (int)Math.Round(_value);
        if (v == _shown) return;
        _shown = v;
        c.SetOutput(1, (ushort)v);
    }
}

/// <summary>
/// Picks a behavior for each symbol. The .smw format identifies symbols by a numeric code (SmC) whose
/// values are not published, so matching is by code when a mapping is known (symbols.json) and otherwise
/// by the symbol's default name.
/// </summary>
public sealed class SymbolLibrary
{
    /// <summary>SmC codes confirmed from real SIMPL Windows 4.x programs.</summary>
    private readonly Dictionary<int, string> _byCode = new()
    {
        [1] = "AND", [3] = "OR", [9] = "One Shot", [13] = "Toggle", [16] = "Delay", [19] = "Interlock",
        [20] = "Buffer", [32] = "Analog Initialize", [38] = "Analog Equate", [46] = "Analog Buffer",
        [56] = "Serial Buffer", [66] = "Analog Rate Limiter", [205] = "NOT", [540] = "Analog Ramp (Bounds Limited)",
    };
    private readonly Dictionary<string, Func<SymbolBehavior>> _factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AND"] = () => new AndSymbol(),
        ["OR"] = () => new OrSymbol(),
        ["XOR"] = () => new XorSymbol(),
        ["NOT"] = () => new NotSymbol(),
        ["Buffer"] = () => new BufferSymbol(SigKind.Digital),
        ["Digital Buffer"] = () => new BufferSymbol(SigKind.Digital),
        ["Analog Buffer"] = () => new BufferSymbol(SigKind.Analog),
        ["Serial Buffer"] = () => new BufferSymbol(SigKind.Serial),
        ["Interlock"] = () => new InterlockSymbol(),
        ["Toggle"] = () => new ToggleSymbol(),
        ["SR Latch"] = () => new SrLatchSymbol(),
        ["Set/Reset Latch"] = () => new SrLatchSymbol(),
        ["One Shot"] = () => new OneShotSymbol(),
        ["Delay"] = () => new DelaySymbol(),
        ["Analog Initialize"] = () => new AnalogInitSymbol(),
        ["Analog Init"] = () => new AnalogInitSymbol(),
        ["Equate"] = () => new EquateSymbol(),
        ["Analog Equate"] = () => new EquateSymbol(),
        ["Serial Send"] = () => new SerialSendSymbol(),
        ["Analog Ramp (Bounds Limited)"] = () => new BoundedRampSymbol(),
        ["Analog Rate Limiter"] = () => new RateLimiterSymbol(),
    };

    public IEnumerable<string> KnownNames => _factories.Keys;

    /// <summary>Teach the library a symbol code, e.g. from symbols.json: 1234 → "AND".</summary>
    public void MapCode(int code, string behaviorName) => _byCode[code] = behaviorName;

    public void LoadMap(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return;
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(jsonPath)) ?? new();
        foreach (var (k, v) in map)
            if (int.TryParse(k, out var code)) MapCode(code, v);
    }

    public SymbolBehavior? Create(SmwSymbol s)
    {
        if (_byCode.TryGetValue(s.Code, out var name) && _factories.TryGetValue(name, out var f1)) return f1();
        if (_factories.TryGetValue(s.Name.Trim(), out var f2)) return f2();
        return null;
    }
}
