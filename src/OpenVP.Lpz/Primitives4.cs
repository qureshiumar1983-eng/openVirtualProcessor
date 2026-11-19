using System.Text;
using OpenVP.Core;

namespace OpenVP.Lpz;

// Compiled types found in the sample programs (Sample Programs/) whose source symbol couldn't be checked pin by
// pin, because they sit inside macros whose source isn't in the program's .smw, or because the source names alone
// identify them. Behavior is inferred from how real programs wire them (SymbolEvidence.Inferred); each says what
// that rests on.

/// <summary>
/// Type 45, Analog Compare (SmC 35): output 1 = a &gt; b, output 2 = a = b, output 3 (if present) = a &lt; b.
/// Inferred: 216 records, two analog inputs, at most two outputs used. Up/down limit macros disable their buttons
/// from output 2 at the limits (equality); Request/Actual pairs use output 1 twice with the inputs swapped, which
/// only makes sense for a strict inequality.
/// </summary>
public sealed class AnalogComparePrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    protected override void Eval(IPrimitiveContext c)
    {
        int a = c.Analog(1), b = c.Analog(2);
        c.Set(1, a > b);
        if (c.OutputCount >= 2) c.Set(2, a == b);
        if (c.OutputCount >= 3) c.Set(3, a < b);
    }
}

/// <summary>
/// Type 65, ASCII Serial Decoder (SmC 54): a rising input 2 plays the text on input 1 as button presses, one
/// character every P1 (1/100 s): the output for character code n is pin n + 2, pulsed for half the interval.
/// Output 1 is high while it plays. Inferred from a dialer macro: triggers named "trigger_0".."trigger_9",
/// "trigger_-", "trigger_." sit exactly at code + 2, and the source's parameter is the per-character time.
/// </summary>
public sealed class AsciiSerialDecoderPrim : Primitive
{
    private readonly List<IDisposable> _timers = new();
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Serial : SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (p != 2 || o || !n) return;
        foreach (var t in _timers) t.Dispose();
        _timers.Clear();
        var text = c.Serial(1);
        var step = Math.Max(2, c.Number(0, 20));
        c.Set(1, text.Length > 0);
        for (int i = 0; i < text.Length; i++)
        {
            var pin = text[i] + 2;
            _timers.Add(c.AfterTicks(i * step, () => c.Set(pin, true)));
            _timers.Add(c.AfterTicks(i * step + step / 2, () => c.Set(pin, false)));
        }
        if (text.Length > 0) _timers.Add(c.AfterTicks(text.Length * step, () => c.Set(1, false)));
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (o is bool ob && n is bool nb) OnDigital(c, p, ob, nb);
    }
}

/// <summary>
/// Type 109, ASCII Keypad (SmC 123): input 1 clears, input 2 enables (presses are ignored while it is low),
/// input 3 deletes the last character, and input n + 4 types character code n. The output is the text so far,
/// at most P1 characters. Inferred from on-screen keyboards: space at pin 36, digits at 52-61, "A" at 69.
/// </summary>
public sealed class AsciiKeypadPrim : EdgePrim
{
    private readonly StringBuilder _text = new();
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p == 1) _text.Clear();
        else if (p == 2) return;
        else if (!c.Digital(2)) return;
        else if (p == 3) { if (_text.Length > 0) _text.Length--; }
        else if (_text.Length < Math.Max(1, c.Number(0, 255))) _text.Append((char)(p - 4));
        c.Set(1, _text.ToString());
    }
}

/// <summary>
/// Type 41, numeric keypad: inputs 1-10 are digits 0-9, 11 clears, 12 and 13 step the value up and down,
/// 14 enters. Output 1 is the value (at most P1; P2 is the lowest value the steps reach), output 2 pulses on
/// enter. Inferred from Crestron's password macro (digits, clear and enter named) and a preset selector that
/// wires only pins 12 and 13 with P = 51, 1.
/// </summary>
public sealed class NumericKeypadPrim : EdgePrim
{
    private long _value;
    public override SigKind? OutputKind(int pin) => pin == 1 ? SigKind.Analog : SigKind.Digital;
    public override void Start(IPrimitiveContext c)
    {
        if (c.ParamCount >= 2) _value = c.Number(1);
        c.Set(1, (ushort)_value);
    }
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        long max = c.Number(0, 65535), min = c.ParamCount >= 2 ? c.Number(1) : 0;
        switch (p)
        {
            case >= 1 and <= 10:
                var next = _value * 10 + (p - 1);
                if (next <= max) _value = next;
                break;
            case 11: _value = min > 0 ? min : 0; break;
            case 12: _value = Math.Min(max, _value + 1); break;
            case 13: _value = Math.Max(min, _value - 1); break;
            case 14:
                c.Set(2, true);
                c.NextWave(() => c.Set(2, false));
                return;
        }
        c.Set(1, (ushort)Math.Clamp(_value, 0, 65535));
    }
}

/// <summary>
/// Type 63, digit string keypad: input 1 clears, input 2 selects (presses count only while it is high or
/// unconnected), inputs 3-12 type "0".."9", input 13 types "*" and 14 "#". Output: the text, at most P1
/// characters. Inferred from Crestron's password macro, where one copy builds the code and another, wired
/// only to pin 13 by an "any key" OR, builds the row of stars shown on the panel.
/// </summary>
public sealed class DigitKeypadPrim : EdgePrim
{
    private readonly StringBuilder _text = new();
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p == 1) _text.Clear();
        else if (p == 2) return;
        else if (!c.IsConstant(2) && !c.Digital(2)) return;
        else if (_text.Length < Math.Max(1, c.Number(0, 255)))
            _text.Append(p switch { >= 3 and <= 12 => (char)('0' + p - 3), 13 => '*', _ => '#' });
        c.Set(1, _text.ToString());
    }
}

/// <summary>
/// Type 66, password memory: a rising input 2 stores the text on input 1 (kept across restarts); a rising
/// input 3 or 4 outputs the stored text. Until something is stored the text is P1. Inferred from Crestron's
/// password macro (store, recall and enter named; P1 is the macro's default password).
/// </summary>
public sealed class PasswordMemoryPrim : EdgePrim
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Serial : SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    private static string Key(IPrimitiveContext c) => $"{c.Symbol.Path}#password";
    private static string Stored(IPrimitiveContext c) => c.RecallKey(Key(c)) as string ?? c.Text(0) ?? c.Number(0).ToString();
    public override bool WantsStartEdge => false;
    public override void Start(IPrimitiveContext c) => c.Set(1, Stored(c));
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p == 2) c.RememberKey(Key(c), c.Serial(1));
        if (p is 2 or 3 or 4) c.Set(1, Stored(c));
    }
}

/// <summary>
/// Type 144, password match: on a rising input 2, while input 3 is high (or unconnected), output 1 goes high if
/// the text on input 1 equals the text on input 4 (or P1, when input 4 is unconnected), and low otherwise.
/// Inferred from Crestron's password macro (string, enter, enable named; output named MATCH).
/// </summary>
public sealed class PasswordMatchPrim : Primitive
{
    public override SigKind? InputKind(int pin) => pin is 1 or 4 ? SigKind.Serial : SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (p != 2 || o || !n) return;
        if (!c.IsConstant(3) && !c.Digital(3)) return;
        var want = c.IsConstant(4) ? c.Text(0) ?? c.Number(0).ToString() : c.Serial(4);
        c.Set(1, c.Serial(1) == want);
    }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (o is bool ob && n is bool nb) OnDigital(c, p, ob, nb);
    }
}

/// <summary>
/// Type 200, delay with an analog time: input 1 going high starts a delay of input 3 (in 1/100 s); if input 1
/// is still high at the end, the output goes high. The output drops as soon as input 1 does, and a rising
/// input 2 cancels a delay in progress. Inferred from microphone "talking" logic: start and cancel are the
/// one-shots of a signal and its inverse, the time is an analog set elsewhere, and the output is named ".delay".
/// </summary>
public sealed class AnalogTimeDelayPrim : Primitive
{
    private IDisposable? _timer;
    public override SigKind? InputKind(int pin) => pin == 3 ? SigKind.Analog : SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) Arm(c); }
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (p == 1)
        {
            _timer?.Dispose();
            _timer = null;
            if (n) Arm(c); else c.Set(1, false);
        }
        else if (p == 2 && !o && n) { _timer?.Dispose(); _timer = null; }
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (o is bool ob && n is bool nb) OnDigital(c, p, ob, nb);
    }
    private void Arm(IPrimitiveContext c) => _timer = c.AfterTicks(Math.Max(0, (int)c.Analog(3)), () => { _timer = null; c.Set(1, true); });
}

/// <summary>
/// Type 21, preset: a rising input sets output k to parameter k + 1 (the first parameter is not a value).
/// Inferred from a meter macro: one digital input, N outputs and N + 1 parameters (all 0 in the programs seen).
/// </summary>
public sealed class PresetPrim : EdgePrim
{
    public override bool WantsStartEdge => false;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) OnEdge(c, 1); }
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p != 1) return;
        for (int k = 1; k <= c.OutputCount; k++)
        {
            if (c.Text(k) is string s) c.Set(k, s);
            else c.Set(k, (ushort)Math.Clamp(c.Number(k), 0, 65535));
        }
    }
}
