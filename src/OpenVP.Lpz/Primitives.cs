using OpenVP.Core;

namespace OpenVP.Lpz;

/// <summary>What a primitive can see and do. Pins are 1-based, in the order the compiler wrote them,
/// which matches the SIMPL symbol's pins (unconnected pins point at the constant signals 0 and 1).</summary>
public interface IPrimitiveContext
{
    BinSymbol Symbol { get; }
    int InputCount { get; }
    int OutputCount { get; }
    bool Digital(int pin);
    ushort Analog(int pin);
    string Serial(int pin);
    object Input(int pin);
    bool IsConstant(int inputPin);
    /// <summary>Drive an output. The typed overloads never box; they convert to the output signal's kind
    /// the way SigValue.Coerce does (a bool into a serial signal becomes an empty string).</summary>
    void Set(int outputPin, bool value);
    void Set(int outputPin, ushort value);
    void Set(int outputPin, string? value);
    void Set(int outputPin, object value);
    /// <summary>Drive an output with the current value of an input pin, whatever its kind.</summary>
    void SetFromInput(int outputPin, int inputPin);
    /// <summary>Drive the signal wired to an input pin, for the rare primitive that owns what is wired to it
    /// (Make String Permanent restoring its saved strings). Does nothing for the constant signals.</summary>
    void SetInput(int inputPin, string value);
    object Output(int outputPin);
    SigKind OutputSignalKind(int outputPin);
    int ParamCount { get; }
    long Number(int index, long fallback = 0);
    string? Text(int index);
    /// <summary>Run after a delay given in 1/100 s (the unit times are compiled in).</summary>
    IDisposable AfterTicks(long ticks, Action action);
    /// <summary>Run after the current logic solve settles ("next logic wave").</summary>
    void NextWave(Action action);
    /// <summary>Non-volatile storage for this symbol, kept in the slot's folder across restarts.</summary>
    object? Recall(int outputPin);
    void Remember(int outputPin, object value);
    /// <summary>Non-volatile storage shared by key (for addressed memory like Analog RAM).</summary>
    object? RecallKey(string key);
    void RememberKey(string key, object value);
    ProgramSlot Slot { get; }
}

public abstract class Primitive
{
    public virtual void Start(IPrimitiveContext c) { }
    /// <summary>True to get a rising edge at start-up on inputs wired to the constant 1, as on a processor.
    /// Edge-triggered primitives want it, unless their Start already acts on a high input.</summary>
    public virtual bool WantsStartEdge => false;
    /// <summary>A change on an input pin, boxed. The typed handlers below call this unless a primitive
    /// overrides them, so a primitive may implement only this one (serial changes always arrive here).</summary>
    public virtual void OnInput(IPrimitiveContext c, int pin, object oldValue, object newValue) { }
    // Typed handlers: the engine calls these, so digital and analog changes reach a primitive unboxed.
    public virtual void OnDigital(IPrimitiveContext c, int pin, bool oldValue, bool newValue) => OnInput(c, pin, SigValue.Box(oldValue), SigValue.Box(newValue));
    public virtual void OnAnalog(IPrimitiveContext c, int pin, ushort oldValue, ushort newValue) => OnInput(c, pin, oldValue, newValue);
    public virtual void OnSerial(IPrimitiveContext c, int pin, string oldValue, string newValue) => OnInput(c, pin, oldValue, newValue);
    /// <summary>Signal types this primitive implies for its pins, used when no .sig is available.</summary>
    public virtual SigKind? InputKind(int pin) => null;
    public virtual SigKind? OutputKind(int pin) => null;
    protected static bool Rising(object o, object n) => o is false && n is true;
    protected static bool Falling(object o, object n) => o is true && n is false;

    /// <summary>Drive an output to the default of its own kind (false, 0 or empty).</summary>
    protected static void SetDefault(IPrimitiveContext c, int pin)
    {
        switch (c.OutputSignalKind(pin))
        {
            case SigKind.Digital: c.Set(pin, false); break;
            case SigKind.Analog: c.Set(pin, (ushort)0); break;
            default: c.Set(pin, string.Empty); break;
        }
    }
}

/// <summary>A primitive that reacts to a change on any pin, of any kind, without looking at the values.</summary>
public abstract class ReactPrim : Primitive
{
    protected abstract void React(IPrimitiveContext c, int pin);
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) => React(c, p);
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) => React(c, p);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => React(c, p);
}

/// <summary>A primitive that re-evaluates all its outputs from its inputs on start and on every change.</summary>
public abstract class EvalPrim : ReactPrim
{
    protected abstract void Eval(IPrimitiveContext c);
    public override void Start(IPrimitiveContext c) => Eval(c);
    protected override void React(IPrimitiveContext c, int pin) => Eval(c);
}

/// <summary>A primitive that acts only on a rising (or, if falling, a falling) edge of a digital input.
/// Analog and serial changes never are an edge, so they do nothing.</summary>
public abstract class EdgePrim(bool falling = false) : Primitive
{
    public override bool WantsStartEdge => !falling;
    protected abstract void OnEdge(IPrimitiveContext c, int pin);
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (falling ? o && !n : !o && n) OnEdge(c, p);
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (o is bool ob && n is bool nb) OnDigital(c, p, ob, nb);
    }
}

// ------------------------------------------------------------------ digital

/// <summary>
/// Base for AND, OR and NOR. They keep a running count of high inputs, updated from the old and new value
/// of the pin that changed, instead of reading every input on every change. Each change still ends in one
/// Set, exactly as before, so the same outputs are queued in the same order (including repeats of an
/// unchanged value).
/// </summary>
public abstract class GatePrim : Primitive
{
    internal int _high;
    // A signal wired to two pins is notified once per pin, and the first notification would see the
    // second pin's value already changed. The count can't follow that, so such a gate scans as it used to.
    internal bool _scan;

    /// <summary>The output for this many high inputs out of inputCount.</summary>
    internal abstract bool Compute(int high, int inputCount);

    /// <summary>Drive the output from the number of high inputs out of c.InputCount.</summary>
    protected void Result(IPrimitiveContext c, int high) => c.Set(1, Compute(high, c.InputCount));

    public override void Start(IPrimitiveContext c)
    {
        // Count what is high now. Inputs wired to the constants 0 and 1 are included and never change.
        _scan = HasRepeatedSignal(c.Symbol.Inputs);
        _high = Scan(c);
        Result(c, _high);
    }

    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (_scan) _high = Scan(c);
        else _high += (n ? 1 : 0) - (o ? 1 : 0);
        Result(c, _high);
    }

    // Analog and serial inputs are never high, so a change there leaves the count alone (but still re-runs the gate).
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) => Other(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (o is bool ob && n is bool nb) OnDigital(c, p, ob, nb);
        else Other(c);
    }

    private void Other(IPrimitiveContext c)
    {
        if (_scan) _high = Scan(c);
        Result(c, _high);
    }

    internal static int Scan(IPrimitiveContext c)
    {
        var high = 0;
        for (int i = 1; i <= c.InputCount; i++)
            if (c.Digital(i)) high++;
        return high;
    }

    private static bool HasRepeatedSignal(int[] inputs)
    {
        HashSet<int>? seen = null;
        foreach (var sig in inputs)
        {
            if (sig <= 1) continue; // the constants can repeat; they never change
            seen ??= new HashSet<int>();
            if (!seen.Add(sig)) return true;
        }
        return false;
    }
}

/// <summary>Type 0, SIMPL AND.</summary>
public sealed class AndPrim : GatePrim
{
    internal override bool Compute(int high, int inputCount) => inputCount > 0 && high == inputCount;
}

/// <summary>Type 2 OR and type 3 NOR.</summary>
public sealed class OrPrim(bool invert) : GatePrim
{
    internal override bool Compute(int high, int inputCount) => (high > 0) ^ invert;
}

/// <summary>Type 1 NOT and type 206 Multiple NOT: output k = !input k.</summary>
public sealed class NotPrim : Primitive
{
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, !c.Digital(k));
    }
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) => c.Set(p, !n);
    // Anything that isn't a high digital reads as "not true".
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) => c.Set(p, true);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => c.Set(p, n is not true);
}

/// <summary>Type 18, SIMPL Buffer: input 1 enables; while enabled output k follows input k+1; disabled outputs go low.</summary>
public sealed class BufferPrim : Primitive
{
    public override void Start(IPrimitiveContext c) => All(c);
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (p == 1) All(c);
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n)
    {
        if (p == 1) All(c);
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1) All(c);
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    private static void All(IPrimitiveContext c)
    {
        var on = c.Digital(1);
        for (int k = 1; k <= c.OutputCount; k++)
        {
            if (on) c.SetFromInput(k, k + 1);
            else SetDefault(c, k);
        }
    }
}

/// <summary>Types 67 (Analog Buffer) and 88 (Serial Buffer): like Buffer, but outputs hold their last value when disabled.</summary>
public sealed class HoldBufferPrim(SigKind kind) : Primitive
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Digital : kind;
    public override SigKind? OutputKind(int pin) => kind;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) All(c); }
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (p == 1) { if (!o && n) All(c); }
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n)
    {
        if (p != 1 && c.Digital(1)) c.Set(p - 1, n);
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1) { if (Rising(o, n)) All(c); }
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    private static void All(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.SetFromInput(k, k + 1);
    }
}

/// <summary>Type 138, Button Presser: outputs follow the input (used to drive signals that have other drivers).</summary>
public sealed class FollowPrim : Primitive
{
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, n);
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, n);
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, n);
    }
}

/// <summary>Type 9, Set/Reset Latch: input 1 set, input 2 reset (reset wins); outputs out, out*.</summary>
public sealed class SrLatchPrim : Primitive
{
    internal bool _state;
    public override void Start(IPrimitiveContext c) => Out(c);
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) => Step(c, p == 1 && !o && n);
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) => Step(c, false);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Step(c, p == 1 && Rising(o, n));
    private void Step(IPrimitiveContext c, bool set)
    {
        if (c.InputCount >= 2 && c.Digital(2)) _state = false;
        else if (set) _state = true;
        Out(c);
    }
    private void Out(IPrimitiveContext c) { c.Set(1, _state); c.Set(2, !_state); }
}

/// <summary>Type 10, Toggle: rising clock (1) flips; optional reset (2) and set (3); outputs out, out*.</summary>
public sealed class TogglePrim : EdgePrim
{
    internal bool _state;
    public override void Start(IPrimitiveContext c) => Out(c);
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        _state = p switch { 1 => !_state, 2 => false, 3 => true, _ => _state };
        Out(c);
    }
    private void Out(IPrimitiveContext c) { c.Set(1, _state); c.Set(2, !_state); }
}

/// <summary>Type 11, Interlock: input 1 clears; rising input k+1 drives output k high and the rest low.</summary>
public sealed class InterlockPrim : EdgePrim
{
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        var selected = p - 1; // 0 = clear
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, k == selected);
    }
}

// ------------------------------------------------------------------ timing

/// <summary>Type 15 One Shot / 17 Retriggerable One Shot: rising input 1 drives output 1 high for P1;
/// input 3 (if driven) cancels. Output 2, if present, is the inverse.</summary>
public sealed class OneShotPrim(bool retrigger) : EdgePrim
{
    private IDisposable? _timer;
    private bool _on;
    public override void Start(IPrimitiveContext c) => Out(c);
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p == 1 && (!_on || retrigger))
        {
            _timer?.Dispose();
            _on = true; Out(c);
            _timer = c.AfterTicks(c.Number(0, 50), () => { _on = false; Out(c); });
        }
        else if (p == 3 && _on)
        {
            _timer?.Dispose(); _on = false; Out(c);
        }
    }
    private void Out(IPrimitiveContext c) { c.Set(1, _on); c.Set(2, !_on); }
}

/// <summary>Type 29, Multiple One Shots: rising input k drives output k high for P1.</summary>
public sealed class MultiOneShotPrim : EdgePrim
{
    private readonly Dictionary<int, IDisposable> _timers = new();
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p > c.OutputCount) return;
        if (_timers.Remove(p, out var t)) t.Dispose();
        c.Set(p, true);
        _timers[p] = c.AfterTicks(c.Number(0, 50), () => c.Set(p, false));
    }
}

/// <summary>Type 27, Delay: output goes high P1 after the input goes high (if still high); low as soon as it drops.</summary>
public sealed class DelayPrim : Primitive
{
    private IDisposable? _timer;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) Arm(c); }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        _timer?.Dispose();
        if (n is true) Arm(c); else c.Set(1, false);
    }
    private void Arm(IPrimitiveContext c) => _timer = c.AfterTicks(c.Number(0, 100), () => c.Set(1, true));
}

/// <summary>Type 16, Pulse Stretcher: output high while input high and for P1 after it drops.</summary>
public sealed class StretchPrim : Primitive
{
    private IDisposable? _timer;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        _timer?.Dispose();
        if (n is true) c.Set(1, true);
        else _timer = c.AfterTicks(c.Number(0, 50), () => c.Set(1, false));
    }
}

/// <summary>Type 14, Oscillator: while input 1 is high, output alternates high for P1 and low for P2.</summary>
public sealed class OscillatorPrim : Primitive
{
    private IDisposable? _timer;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) High(c); }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        _timer?.Dispose();
        if (n is true) High(c); else c.Set(1, false);
    }
    private void High(IPrimitiveContext c)
    {
        c.Set(1, true);
        _timer = c.AfterTicks(Math.Max(1, c.Number(0, 50)), () =>
        {
            c.Set(1, false);
            _timer = c.AfterTicks(Math.Max(1, c.Number(1, 50)), () => { if (c.Digital(1)) High(c); });
        });
    }
}

/// <summary>Type 139, Stepper: on a rising input, after P1 output 2 pulses for P2, then after P3 output 3
/// pulses for P4, and so on. Output 1 is reserved (busy).</summary>
public sealed class StepperPrim : EdgePrim
{
    private readonly List<IDisposable> _timers = new();
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p != 1) return;
        foreach (var t in _timers) t.Dispose();
        _timers.Clear();
        long at = c.Number(0);
        for (int k = 2, param = 1; k <= c.OutputCount; k++, param += 2)
        {
            var output = k;
            var duration = Math.Max(1, c.Number(param, 1));
            var start = at;
            _timers.Add(c.AfterTicks(start, () => c.Set(output, true)));
            _timers.Add(c.AfterTicks(start + duration, () => c.Set(output, false)));
            at = start + duration + c.Number(param + 1);
        }
    }
}

/// <summary>Type 204, Logic Wave Pulse: a rising edge on a driven input gives a one-wave pulse on output 1.</summary>
public sealed class WavePulsePrim : EdgePrim
{
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (c.IsConstant(p)) return;
        c.Set(1, true);
        c.NextWave(() => c.Set(1, false));
    }
}

// ------------------------------------------------------------------ analog / serial

/// <summary>Type 38, Analog Initialize (numeric params) / serial initialize (string params). Two forms:
/// one trigger and N outputs (on a rising input, or at start if the input is the constant 1, output k =
/// parameter k), or N triggers and one output (a rising input k, or input k high at start, sets the output
/// to parameter k).</summary>
public sealed class InitPrim : EdgePrim
{
    public override bool WantsStartEdge => false; // Start loads inputs that are high already
    public override SigKind? OutputKind(int pin) => null;
    private static bool Multi(IPrimitiveContext c) => c.InputCount > 1 && c.OutputCount == 1;
    public override void Start(IPrimitiveContext c)
    {
        if (Multi(c)) { for (int k = 1; k <= c.InputCount; k++) if (c.Digital(k)) Load(c, k, 1); }
        else if (c.Digital(1)) Fire(c);
    }
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (Multi(c)) Load(c, p, 1);
        else if (p == 1) Fire(c);
    }
    private static void Fire(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++) Load(c, k, k);
    }
    /// <summary>Drive an output with parameter k.</summary>
    private static void Load(IPrimitiveContext c, int k, int outputPin)
    {
        var text = c.Text(k - 1);
        if (text != null) c.Set(outputPin, text);
        else c.Set(outputPin, (ushort)Math.Clamp(c.Number(k - 1), 0, 65535));
    }
}

/// <summary>Type 51, Analog Equate: output k is high while the analog input (input 1) equals parameter k
/// and the digital enable (input 2) is high. With the enable low every output is low. An equate compiled
/// without an enable (one input) is always enabled.</summary>
public sealed class EquatePrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => pin == 2 ? SigKind.Digital : SigKind.Analog;
    protected override void Eval(IPrimitiveContext c)
    {
        var enabled = c.InputCount < 2 || c.Digital(2);
        var v = c.Analog(1);
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, enabled && c.Number(k - 1, -1) == v);
    }
}

/// <summary>Type 58, Serial Send / Multiple Serial Send: rising input k sends string parameter k on output 1
/// (or on output k when there is one output per input). Multiple Serial Send with one trigger and several
/// outputs (programs use it to set every room, display and source name at start-up) sends parameter k on
/// output k, all of them, on that one trigger.</summary>
public sealed class SerialSendPrim : EdgePrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (c.InputCount == 1 && c.OutputCount > 1)
        {
            for (int k = 1; k <= c.OutputCount; k++) c.Set(k, c.Text(k - 1) ?? string.Empty);
            return;
        }
        var outPin = c.OutputCount >= p ? p : 1;
        c.Set(outPin, c.Text(p - 1) ?? string.Empty);
    }
}

/// <summary>Type 97, Serial I/O: input 1 is received text; a rising input k+1 sends parameter k on output 1;
/// received text equal to parameter k pulses output k+1.</summary>
public sealed class SerialIoPrim : Primitive
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Serial : SigKind.Digital;
    public override SigKind? OutputKind(int pin) => pin == 1 ? SigKind.Serial : SigKind.Digital;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1)
        {
            var rx = n as string ?? string.Empty;
            for (int k = 0; k < c.ParamCount; k++)
                if (c.Text(k) is string s && s.Length > 0 && rx == s && k + 2 <= c.OutputCount)
                {
                    var pin = k + 2;
                    c.Set(pin, true);
                    c.NextWave(() => c.Set(pin, false));
                }
        }
        else if (Rising(o, n) && c.Text(p - 2) is string tx)
            c.Set(1, tx);
    }
}

/// <summary>Type 114, Serial/Analog One-Shot: any change on the input pulses output 1 for P1.</summary>
public sealed class ChangePulsePrim : ReactPrim
{
    private IDisposable? _timer;
    protected override void React(IPrimitiveContext c, int p)
    {
        _timer?.Dispose();
        c.Set(1, true);
        _timer = c.AfterTicks(Math.Max(1, c.Number(0, 10)), () => c.Set(1, false));
    }
}

/// <summary>Type 254, String Length.</summary>
public sealed class StringLengthPrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void Eval(IPrimitiveContext c) => c.Set(1, (ushort)Math.Min(c.Serial(1).Length, 65535));
}

/// <summary>Type 100, Analog Sum and Digital Sum (both compile to it): the sum of the analog inputs plus the
/// number of digital inputs that are high, clamped to 0–65535. Digital Sum (SmC 61) counts high inputs; real
/// programs use it to count configured sources or routed microphones.</summary>
public sealed class AnalogSumPrim : EvalPrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void Eval(IPrimitiveContext c)
    {
        long sum = 0;
        // A signal is one kind: the other reads as 0/false, so each input adds its value or its 1.
        for (int i = 1; i <= c.InputCount; i++) sum += c.Analog(i) + (c.Digital(i) ? 1 : 0);
        c.Set(1, (ushort)Math.Clamp(sum, 0, 65535));
    }
}

/// <summary>Type 103, Serial to Analog: decimal text to a number.</summary>
public sealed class SerialToAnalogPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    // Only text can parse: any other kind reads as "" and sets nothing.
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (ushort.TryParse((n as string ?? "").Trim(), out var v)) c.Set(1, v);
    }
}

/// <summary>Type 220, Analog Comparison: outputs (as far as identified) a=b, a≠b, a&lt;b, a≤b, a&gt;b, a≥b.</summary>
public sealed class ComparePrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    protected override void Eval(IPrimitiveContext c)
    {
        int a = c.Analog(1), b = c.Analog(2);
        for (int k = 1; k <= Math.Min(c.OutputCount, 6); k++)
            c.Set(k, k switch { 1 => a == b, 2 => a != b, 3 => a < b, 4 => a <= b, 5 => a > b, _ => a >= b });
    }
}

/// <summary>Type 156, Ethernet Offline Manager: reports offline = low (OpenVP treats devices as present).</summary>
public sealed class ConstantLowPrim : Primitive
{
    public override void Start(IPrimitiveContext c) { for (int k = 1; k <= c.OutputCount; k++) c.Set(k, false); }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) { }
}

/// <summary>Type 62, Clock Driver: drives the time-of-day string once a second.</summary>
public sealed class ClockPrim : Primitive
{
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void Start(IPrimitiveContext c) => Tick(c);
    private void Tick(IPrimitiveContext c)
    {
        c.Set(1, DateTime.Now.ToString("HH:mm:ss"));
        c.AfterTicks(100, () => Tick(c));
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) { }
}

/// <summary>Type 205, Make String Permanent: the serial signals wired to its inputs keep their last value
/// across restarts (parameter 1 is the longest string kept). At start each one is restored by driving its
/// signal, and whenever one changes the new value is saved. Programs the compiler wrote with no inputs and
/// the signals on outputs instead are handled the same way, watching those outputs.</summary>
public sealed class PermanentPrim : Primitive, IWatchesOutputs
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    private static string InKey(IPrimitiveContext c, int pin) => $"{c.Symbol.Path}#in{pin}";
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++)
            if (c.Recall(k) is string v) c.Set(k, v);
        for (int k = 1; k <= c.InputCount; k++)
            if (!c.IsConstant(k) && c.RecallKey(InKey(c, k)) is string v) c.SetInput(k, v);
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        var text = n as string ?? string.Empty;
        var max = c.Number(0);
        if (max > 0 && text.Length > max) text = text[..(int)max];
        c.RememberKey(InKey(c, p), text);
    }
    public void OnOutputChanged(IPrimitiveContext c, int pin, object value) => c.Remember(pin, value as string ?? "");
}

/// <summary>Type 56, Debounce: the output follows the input once it has been stable for P1.</summary>
public sealed class DebouncePrim : Primitive
{
    private IDisposable? _timer;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        _timer?.Dispose();
        _timer = c.AfterTicks(Math.Max(1, c.Number(0, 10)), () => c.Set(1, c.Digital(1)));
    }
}

/// <summary>Type 23, Ring Counter: each rising clock (input 1) moves the single high output to the next one.</summary>
public sealed class RingCounterPrim : EdgePrim
{
    private int _pos;
    public override void Start(IPrimitiveContext c) => Out(c);
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p == 1) _pos = c.OutputCount == 0 ? 0 : (_pos + 1) % c.OutputCount;
        else if (p == 2) _pos = 0; // reset, when present
        Out(c);
    }
    private void Out(IPrimitiveContext c) { for (int k = 1; k <= c.OutputCount; k++) c.Set(k, k - 1 == _pos); }
}

/// <summary>Type 61, When: pulses the output for P1 when the time-of-day input (HH:MM:SS) reaches P2
/// (P2 in 1/100 s since midnight).</summary>
public sealed class WhenPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (!TimeSpan.TryParse(n as string ?? "", out var t)) return;
        if ((long)t.TotalSeconds * 100 != c.Number(1, -1)) return;
        c.Set(1, true);
        c.AfterTicks(Math.Max(1, c.Number(0, 10)), () => c.Set(1, false));
    }
}

/// <summary>Primitives that also need to hear about changes on their own output signals.</summary>
public interface IWatchesOutputs
{
    void OnOutputChanged(IPrimitiveContext c, int pin, object value);
}

/// <summary>Primitive types OpenVP can run, keyed by the .bin record type. The table is
/// <see cref="LogicSymbolCatalog"/>; a program can name more types in primitives.json.</summary>
public static class PrimitiveLibrary
{
    public static bool Supports(int type) => LogicSymbolCatalog.ByLpzType(type) != null;
    public static Primitive? Create(int type) => LogicSymbolCatalog.ByLpzType(type)?.Create();
    public static IEnumerable<int> Types => LogicSymbolCatalog.All.SelectMany(s => s.LpzTypes);

    /// <summary>Create a primitive for a type, consulting <paramref name="overrides"/> (from primitives.json)
    /// for types the catalog doesn't know.</summary>
    public static Primitive? Create(int type, IReadOnlyDictionary<int, LogicSymbol>? overrides) =>
        Create(type) ?? (overrides != null && overrides.TryGetValue(type, out var s) ? s.Create() : null);
}
