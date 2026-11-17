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
    void Set(int outputPin, object value);
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
    public abstract void OnInput(IPrimitiveContext c, int pin, object oldValue, object newValue);
    /// <summary>Signal types this primitive implies for its pins, used when no .sig is available.</summary>
    public virtual SigKind? InputKind(int pin) => null;
    public virtual SigKind? OutputKind(int pin) => null;
    protected static bool Rising(object o, object n) => o is false && n is true;
    protected static bool Falling(object o, object n) => o is true && n is false;
}

// ------------------------------------------------------------------ digital

/// <summary>Type 0, SIMPL AND.</summary>
public sealed class AndPrim : Primitive
{
    public override void Start(IPrimitiveContext c) => Eval(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Eval(c);
    private static void Eval(IPrimitiveContext c)
    {
        var all = c.InputCount > 0;
        for (int i = 1; i <= c.InputCount; i++) all &= c.Digital(i);
        c.Set(1, all);
    }
}

/// <summary>Type 2 OR and type 3 NOR.</summary>
public sealed class OrPrim(bool invert) : Primitive
{
    public override void Start(IPrimitiveContext c) => Eval(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Eval(c);
    private void Eval(IPrimitiveContext c)
    {
        var any = false;
        for (int i = 1; i <= c.InputCount; i++) any |= c.Digital(i);
        c.Set(1, any ^ invert);
    }
}

/// <summary>Type 1 NOT and type 206 Multiple NOT: output k = !input k.</summary>
public sealed class NotPrim : Primitive
{
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, !c.Digital(k));
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => c.Set(p, n is not true);
}

/// <summary>Type 18, SIMPL Buffer: input 1 enables; while enabled output k follows input k+1; disabled outputs go low.</summary>
public sealed class BufferPrim : Primitive
{
    public override void Start(IPrimitiveContext c) => All(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1) All(c);
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    private static void All(IPrimitiveContext c)
    {
        var on = c.Digital(1);
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, on ? c.Input(k + 1) : SigValue.Default(c.OutputSignalKind(k)));
    }
}

/// <summary>Types 67 (Analog Buffer) and 88 (Serial Buffer): like Buffer, but outputs hold their last value when disabled.</summary>
public sealed class HoldBufferPrim(SigKind kind) : Primitive
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Digital : kind;
    public override SigKind? OutputKind(int pin) => kind;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) All(c); }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1) { if (Rising(o, n)) All(c); }
        else if (c.Digital(1)) c.Set(p - 1, n);
    }
    private static void All(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, c.Input(k + 1));
    }
}

/// <summary>Type 138, Button Presser: outputs follow the input (used to drive signals that have other drivers).</summary>
public sealed class FollowPrim : Primitive
{
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, n);
    }
}

/// <summary>Type 9, Set/Reset Latch: input 1 set, input 2 reset (reset wins); outputs out, out*.</summary>
public sealed class SrLatchPrim : Primitive
{
    private bool _state;
    public override void Start(IPrimitiveContext c) => Out(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (c.InputCount >= 2 && c.Digital(2)) _state = false;
        else if (p == 1 && Rising(o, n)) _state = true;
        Out(c);
    }
    private void Out(IPrimitiveContext c) { c.Set(1, _state); c.Set(2, !_state); }
}

/// <summary>Type 10, Toggle: rising clock (1) flips; optional reset (2) and set (3); outputs out, out*.</summary>
public sealed class TogglePrim : Primitive
{
    private bool _state;
    public override void Start(IPrimitiveContext c) => Out(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (!Rising(o, n)) return;
        _state = p switch { 1 => !_state, 2 => false, 3 => true, _ => _state };
        Out(c);
    }
    private void Out(IPrimitiveContext c) { c.Set(1, _state); c.Set(2, !_state); }
}

/// <summary>Type 11, Interlock: input 1 clears; rising input k+1 drives output k high and the rest low.</summary>
public sealed class InterlockPrim : Primitive
{
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (!Rising(o, n)) return;
        var selected = p - 1; // 0 = clear
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, k == selected);
    }
}

// ------------------------------------------------------------------ timing

/// <summary>Type 15 One Shot / 17 Retriggerable One Shot: rising input 1 drives output 1 high for P1;
/// input 3 (if driven) cancels. Output 2, if present, is the inverse.</summary>
public sealed class OneShotPrim(bool retrigger) : Primitive
{
    private IDisposable? _timer;
    private bool _on;
    public override void Start(IPrimitiveContext c) => Out(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1 && Rising(o, n) && (!_on || retrigger))
        {
            _timer?.Dispose();
            _on = true; Out(c);
            _timer = c.AfterTicks(c.Number(0, 50), () => { _on = false; Out(c); });
        }
        else if (p == 3 && Rising(o, n) && _on)
        {
            _timer?.Dispose(); _on = false; Out(c);
        }
    }
    private void Out(IPrimitiveContext c) { c.Set(1, _on); c.Set(2, !_on); }
}

/// <summary>Type 29, Multiple One Shots: rising input k drives output k high for P1.</summary>
public sealed class MultiOneShotPrim : Primitive
{
    private readonly Dictionary<int, IDisposable> _timers = new();
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (!Rising(o, n) || p > c.OutputCount) return;
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
public sealed class StepperPrim : Primitive
{
    private readonly List<IDisposable> _timers = new();
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1 || !Rising(o, n)) return;
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
public sealed class WavePulsePrim : Primitive
{
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (c.IsConstant(p) || !Rising(o, n)) return;
        c.Set(1, true);
        c.NextWave(() => c.Set(1, false));
    }
}

// ------------------------------------------------------------------ analog / serial

/// <summary>Type 38, Analog Initialize (numeric params) / serial initialize (string params):
/// on a rising input (or at start if the input is the constant 1), output k = parameter k.</summary>
public sealed class InitPrim : Primitive
{
    public override SigKind? OutputKind(int pin) => null;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) Fire(c); }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) { if (p == 1 && Rising(o, n)) Fire(c); }
    private static void Fire(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++)
        {
            var text = c.Text(k - 1);
            if (text != null) c.Set(k, text);
            else c.Set(k, (ushort)Math.Clamp(c.Number(k - 1), 0, 65535));
        }
    }
}

/// <summary>Type 51, Analog Equate: output k is high while the analog input equals parameter k.</summary>
public sealed class EquatePrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override void Start(IPrimitiveContext c) => Eval(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Eval(c);
    private static void Eval(IPrimitiveContext c)
    {
        var v = c.Analog(1);
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, c.Number(k - 1, -1) == v);
    }
}

/// <summary>Type 58, Serial Send / Multiple Serial Send: rising input k sends string parameter k on output 1
/// (or on output k when there is one output per input).</summary>
public sealed class SerialSendPrim : Primitive
{
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (!Rising(o, n)) return;
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
public sealed class ChangePulsePrim : Primitive
{
    private IDisposable? _timer;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        _timer?.Dispose();
        c.Set(1, true);
        _timer = c.AfterTicks(Math.Max(1, c.Number(0, 10)), () => c.Set(1, false));
    }
}

/// <summary>Type 254, String Length.</summary>
public sealed class StringLengthPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void Start(IPrimitiveContext c) => c.Set(1, (ushort)Math.Min(c.Serial(1).Length, 65535));
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Start(c);
}

/// <summary>Type 100, Analog Sum (clamped to 0–65535).</summary>
public sealed class AnalogSumPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void Start(IPrimitiveContext c) => Eval(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Eval(c);
    private static void Eval(IPrimitiveContext c)
    {
        long sum = 0;
        for (int i = 1; i <= c.InputCount; i++) sum += c.Analog(i);
        c.Set(1, (ushort)Math.Clamp(sum, 0, 65535));
    }
}

/// <summary>Type 103, Serial to Analog: decimal text to a number.</summary>
public sealed class SerialToAnalogPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (ushort.TryParse((n as string ?? "").Trim(), out var v)) c.Set(1, v);
    }
}

/// <summary>Type 220, Analog Comparison: outputs (as far as identified) a=b, a≠b, a&lt;b, a≤b, a&gt;b, a≥b.</summary>
public sealed class ComparePrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override void Start(IPrimitiveContext c) => Eval(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Eval(c);
    private static void Eval(IPrimitiveContext c)
    {
        int a = c.Analog(1), b = c.Analog(2);
        bool[] r = [a == b, a != b, a < b, a <= b, a > b, a >= b];
        for (int k = 1; k <= Math.Min(c.OutputCount, r.Length); k++) c.Set(k, r[k - 1]);
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

/// <summary>Type 205, Make String Permanent: no inputs; its outputs are serial signals whose values are kept
/// across restarts. Restores them at start and saves them whenever they change.</summary>
public sealed class PermanentPrim : Primitive, IWatchesOutputs
{
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.OutputCount; k++)
            if (c.Recall(k) is string v) c.Set(k, v);
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) { }
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
public sealed class RingCounterPrim : Primitive
{
    private int _pos;
    public override void Start(IPrimitiveContext c) => Out(c);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (!Rising(o, n)) return;
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

/// <summary>Primitive types OpenVP can run, keyed by the .bin record type.</summary>
public static class PrimitiveLibrary
{
    private static readonly Dictionary<int, Func<Primitive>> Factories = new()
    {
        [0] = () => new AndPrim(),
        [1] = () => new NotPrim(),
        [2] = () => new OrPrim(false),
        [3] = () => new OrPrim(true),
        [9] = () => new SrLatchPrim(),
        [10] = () => new TogglePrim(),
        [11] = () => new InterlockPrim(),
        [14] = () => new OscillatorPrim(),
        [15] = () => new OneShotPrim(false),
        [16] = () => new StretchPrim(),
        [17] = () => new OneShotPrim(true),
        [18] = () => new BufferPrim(),
        [27] = () => new DelayPrim(),
        [29] = () => new MultiOneShotPrim(),
        [38] = () => new InitPrim(),
        [51] = () => new EquatePrim(),
        [58] = () => new SerialSendPrim(),
        [62] = () => new ClockPrim(),
        [67] = () => new HoldBufferPrim(SigKind.Analog),
        [88] = () => new HoldBufferPrim(SigKind.Serial),
        [97] = () => new SerialIoPrim(),
        [100] = () => new AnalogSumPrim(),
        [103] = () => new SerialToAnalogPrim(),
        [114] = () => new ChangePulsePrim(),
        [138] = () => new FollowPrim(),
        [139] = () => new StepperPrim(),
        [156] = () => new ConstantLowPrim(),
        [204] = () => new WavePulsePrim(),
        [206] = () => new NotPrim(),
        [220] = () => new ComparePrim(),
        [254] = () => new StringLengthPrim(),
        [205] = () => new PermanentPrim(),
        [56] = () => new DebouncePrim(),
        [23] = () => new RingCounterPrim(),
        [61] = () => new WhenPrim(),
        [4] = () => new XorPrim(),
        [6] = () => new TransitionPrim(false),
        [7] = () => new TransitionPrim(true),
        [19] = () => new TruthTablePrim(),
        [20] = () => new AnalogRampPrim(false),
        [223] = () => new AnalogRampPrim(true),
        [30] = () => new AnalogScalerPrim(),
        [36] = () => new AnalogRamPrim(),
        [37] = () => new AnalogStepPrim(),
        [52] = () => new DecadePrim(),
        [80] = () => new AnalogFollowPrim(),
        [82] = () => new AnalogToSerialPrim(),
        [87] = () => new AnalogFlipPrim(),
        [93] = () => new SerialGatherPrim(),
        [99] = () => new AnalogScalerSignedPrim(),
        [106] = () => new AnalogToDigitalPrim(),
        [121] = () => new NoOpPrim(),
        [122] = () => new SerialMergePrim(),
        [136] = () => new DivModPrim(),
        [202] = () => new ConsolePrim(),
        [203] = () => new WaveDelayPrim(),
        [207] = () => new InterlockTogglePrim(),
        [224] = () => new AnalogScalerLimitsPrim(),
        [230] = () => new AnalogIncrementPrim(),
        [236] = () => new AnalogIncrementPrim(),
        [237] = () => new AnalogClampPrim(),
        [241] = () => new StepperProgressPrim(),
        [266] = () => new MultiSerialSendPrim(),
        [85] = () => new XsigPrim(),
        [161] = () => new NoOpPrim(),
    };

    public static bool Supports(int type) => Factories.ContainsKey(type);
    public static Primitive? Create(int type) => Factories.TryGetValue(type, out var f) ? f() : null;
    public static IEnumerable<int> Types => Factories.Keys;
}
