using System.Text;
using OpenVP.Core;

namespace OpenVP.Lpz;

// Primitives identified from 93 real programs (see docs/lpz-format.md). Pins are 1-based.

/// <summary>Type 4, XOR: output high when an odd number of inputs are high.</summary>
public sealed class XorPrim : EvalPrim
{
    protected override void Eval(IPrimitiveContext c)
    {
        var odd = false;
        for (int i = 1; i <= c.InputCount; i++) odd ^= c.Digital(i);
        c.Set(1, odd);
    }
}

/// <summary>Types 6 (Transition Gate: rising edge of any input) and 7 (falling edge of any input):
/// a one-wave pulse on output 1.</summary>
public sealed class TransitionPrim(bool falling) : EdgePrim(falling)
{
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (c.IsConstant(p)) return;
        c.Set(1, true);
        c.NextWave(() => c.Set(1, false));
    }
}

/// <summary>
/// Type 19, Truth Table. Parameters are rows: the first half are input patterns, the second half the
/// matching output patterns, one character per pin (1 = low, 2 = high, 4 = don't care / unchanged).
/// The first row matching the inputs sets the outputs; with no match the outputs hold.
/// </summary>
public sealed class TruthTablePrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Digital;

    private List<string?>? _texts; // the parameters never change, so list them once

    protected override void Eval(IPrimitiveContext c)
    {
        var texts = _texts ??= Enumerable.Range(0, c.ParamCount).Select(c.Text).Where(t => t != null).ToList();
        var rows = texts.Count / 2;
        for (int r = 0; r < rows; r++)
        {
            var pattern = texts[r]!;
            var match = true;
            for (int i = 0; i < pattern.Length && i < c.InputCount && match; i++)
                match = pattern[i] switch { (char)1 => !c.Digital(i + 1), (char)2 => c.Digital(i + 1), _ => true };
            if (!match) continue;
            var outs = texts[rows + r]!;
            for (int k = 0; k < outs.Length && k < c.OutputCount; k++)
                if (outs[k] is (char)1 or (char)2) c.Set(k + 1, outs[k] == (char)2);
            return;
        }
    }
}

/// <summary>
/// Type 36, Analog RAM: inputs are the values, then store, recall, then bank selects. Store saves the
/// values into the selected bank, recall drives them to the outputs. Memory is non-volatile and addressed
/// (field 5), so two symbols with the same address share it.
/// </summary>
public sealed class AnalogRamPrim : EdgePrim
{
    private int N(IPrimitiveContext c) => c.OutputCount;
    public override SigKind? InputKind(int pin) => null;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;

    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        var n0 = N(c);
        if (p == n0 + 1) Store(c);
        else if (p == n0 + 2) Recall(c);
    }

    private int Bank(IPrimitiveContext c)
    {
        for (int i = N(c) + 3; i <= c.InputCount; i++)
            if (c.Digital(i)) return i - (N(c) + 3);
        return c.InputCount <= N(c) + 2 ? 0 : -1;
    }

    private string Key(IPrimitiveContext c, int bank, int i) =>
        $"aram@{(c.Symbol.FieldInt(5) ?? 0) + ((long)bank * N(c) + i) * 4}";

    private void Store(IPrimitiveContext c)
    {
        var bank = Bank(c);
        if (bank < 0) return;
        for (int i = 0; i < N(c); i++) c.RememberKey(Key(c, bank, i), c.Analog(i + 1));
    }

    private void Recall(IPrimitiveContext c)
    {
        var bank = Bank(c);
        if (bank < 0) return;
        for (int i = 0; i < N(c); i++)
            if (c.RecallKey(Key(c, bank, i)) is { } v) c.Set(i + 1, v);
    }
}

/// <summary>Base for symbols that change a value repeatedly while a button is held.</summary>
public abstract class HeldRepeatPrim : Primitive
{
    private IDisposable? _timer;
    private int _dir;

    protected void Begin(IPrimitiveContext c, int dir, long firstDelay, long repeat)
    {
        _dir = dir;
        _timer?.Dispose();
        Step(c, dir);
        Schedule(c, firstDelay, repeat);
    }

    private void Schedule(IPrimitiveContext c, long delay, long repeat)
    {
        if (delay <= 0) delay = 1;
        _timer = c.AfterTicks(delay, () =>
        {
            if (_dir == 0) return;
            Step(c, _dir);
            Schedule(c, repeat, repeat);
        });
    }

    protected void End()
    {
        _dir = 0;
        _timer?.Dispose();
        _timer = null;
    }

    protected abstract void Step(IPrimitiveContext c, int dir);
}

/// <summary>
/// Types 20 (Analog Ramp: P1 = time for the full 0-65535 range) and 223 (ramp with limits: P1 time,
/// P2 lower, P3 upper). Input 1 ramps up, input 2 down, while held.
/// </summary>
public sealed class AnalogRampPrim(bool limited) : HeldRepeatPrim
{
    private const long TickStep = 5; // update every 50 ms
    private int _value = -1;

    public override SigKind? OutputKind(int pin) => SigKind.Analog;

    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p > 2) return;
        if (n is true) Begin(c, p == 1 ? 1 : -1, TickStep, TickStep);
        else if (!c.Digital(1) && !c.Digital(2)) End();
        else Begin(c, c.Digital(1) ? 1 : -1, TickStep, TickStep);
    }

    protected override void Step(IPrimitiveContext c, int dir)
    {
        long lo = limited ? c.Number(1) : 0, hi = limited ? c.Number(2, 65535) : 65535;
        if (hi < lo) (lo, hi) = (hi, lo);
        var time = Math.Max(1, c.Number(0, 500));
        var step = Math.Max(1, (hi - lo) * TickStep / time);
        if (_value < 0) _value = c.Output(1) is ushort u ? u : (int)lo;
        _value = (int)Math.Clamp(_value + dir * step, lo, hi);
        c.Set(1, (ushort)(short)_value);
    }
}

/// <summary>
/// Types 230 (Analog Increment) and 236 (with optional feedback on input 4). P1 step, P2 time held
/// before repeating, P3 repeat rate, P4 lower limit, P5 upper limit, P6 initial value.
/// </summary>
public sealed class AnalogIncrementPrim : HeldRepeatPrim
{
    private long? _value;

    public override SigKind? OutputKind(int pin) => SigKind.Analog;

    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 4) { _value = c.Number(3) < 0 ? (short)c.Analog(4) : c.Analog(4); return; }
        if (p > 2) return;
        if (n is true) Begin(c, p == 1 ? 1 : -1, c.Number(1, 50), Math.Max(1, c.Number(2, 10)));
        else if (!c.Digital(1) && !c.Digital(2)) End();
    }

    protected override void Step(IPrimitiveContext c, int dir)
    {
        long lo = c.Number(3), hi = c.Number(4, 65535);
        if (hi < lo) (lo, hi) = (hi, lo);
        var v = _value ?? (c.InputCount >= 4 && !c.IsConstant(4) ? c.Analog(4) : c.Number(5, lo));
        v = Math.Clamp(v + dir * Math.Max(1, c.Number(0, 1)), lo, hi);
        _value = v;
        c.Set(1, (ushort)(short)v);
    }
}

/// <summary>Type 30, Analog Scaler: output = input × P2 / P1 + P3.</summary>
public sealed class AnalogScalerPrim : ReactPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void React(IPrimitiveContext c, int p)
    {
        var range = c.Number(0, 65535);
        if (range == 0) range = 65535;
        var v = c.Analog(1) * c.Number(1, 65535) / range + c.Number(2);
        c.Set(1, (ushort)Math.Clamp(v, 0, 65535));
    }
}

/// <summary>Type 99, Analog Scaler w/o Zero Pass: output = input × P1 / 65535 + P2 (signed result).</summary>
public sealed class AnalogScalerSignedPrim : ReactPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void React(IPrimitiveContext c, int p)
    {
        var v = (long)Math.Round(c.Analog(1) * (double)c.Number(0, 65535) / 65535.0) + c.Number(1);
        c.Set(1, (ushort)(short)Math.Clamp(v, short.MinValue, ushort.MaxValue));
    }
}

/// <summary>Type 224, Analog Scaler with I/O Limits: P1..P2 input range maps to P3..P4 output range.</summary>
public sealed class AnalogScalerLimitsPrim : ReactPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void React(IPrimitiveContext c, int p)
    {
        long inLo = c.Number(0) & 0xFFFF, inHi = c.Number(1, 65535) & 0xFFFF, outLo = c.Number(2), outHi = c.Number(3, 65535);
        if (inHi == inLo) { c.Set(1, (ushort)(short)outLo); return; }
        var x = Math.Clamp((long)c.Analog(1), Math.Min(inLo, inHi), Math.Max(inLo, inHi));
        var v = outLo + (long)Math.Round((x - inLo) * (double)(outHi - outLo) / (inHi - inLo));
        c.Set(1, (ushort)(short)Math.Clamp(v, short.MinValue, ushort.MaxValue));
    }
}

/// <summary>Type 87, Analog Flip: output = 65535 − input.</summary>
public sealed class AnalogFlipPrim : ReactPrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void React(IPrimitiveContext c, int p) => c.Set(1, (ushort)(65535 - c.Analog(1)));
}

/// <summary>Type 136, Analog DivMod: output 1 = input / P1, output 2 = input mod P1.</summary>
public sealed class DivModPrim : ReactPrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void React(IPrimitiveContext c, int p)
    {
        var d = Math.Max(1, c.Number(0, 1));
        c.Set(1, (ushort)(c.Analog(1) / d));
        c.Set(2, (ushort)(c.Analog(1) % d));
    }
}

/// <summary>Type 237: output = input 3 clamped to [input 1, input 2].</summary>
public sealed class AnalogClampPrim : ReactPrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void React(IPrimitiveContext c, int p)
    {
        int lo = c.Analog(1), hi = c.Analog(2);
        if (hi < lo) (lo, hi) = (hi, lo);
        c.Set(1, (ushort)Math.Clamp((int)c.Analog(3), lo, hi));
    }
}

/// <summary>Type 80 (inside Q-SYS modules): analog follows its input.</summary>
public sealed class AnalogFollowPrim : ReactPrim
{
    protected override void React(IPrimitiveContext c, int p) => c.Set(1, c.Analog(1));
}

/// <summary>Type 106, Analog to Digital: output k is bit (outputs − k) of the input.</summary>
public sealed class AnalogToDigitalPrim : ReactPrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    protected override void React(IPrimitiveContext c, int p)
    {
        int v = c.Analog(1), count = c.OutputCount;
        for (int k = 1; k <= count; k++) c.Set(k, ((v >> (count - k)) & 1) == 1);
    }
}

/// <summary>Type 52, Decade: one-hot units (outputs 1-10), tens (11-20) and hundreds (21-30) digits.</summary>
public sealed class DecadePrim : ReactPrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    protected override void React(IPrimitiveContext c, int p)
    {
        if (p != 1) return;
        int v = c.Analog(1);
        for (int group = 0; group < 3; group++)
        {
            var digit = group == 0 ? v % 10 : group == 1 ? v / 10 % 10 : v / 100 % 10;
            for (int d = 0; d < 10; d++)
                c.Set(group * 10 + d + 1, digit == d);
        }
    }
}

/// <summary>Type 37, Analog Step: each rising clock (input 1) outputs the next parameter value, cycling;
/// input 2 resets to the first.</summary>
public sealed class AnalogStepPrim : EdgePrim
{
    private int _index = -1;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (c.ParamCount == 0) return;
        _index = p == 2 ? 0 : (_index + 1) % c.ParamCount;
        c.Set(1, (ushort)(short)c.Number(_index));
    }
}

/// <summary>Type 82, Analog to Serial: on a rising input 1, output = S1 + byte(A1) + S2 + byte(A2) … + Sn.
/// P1 is the modulus (256 = one byte per value; larger = two bytes, high first).</summary>
public sealed class AnalogToSerialPrim : EdgePrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p != 1) return;
        var mod = Math.Max(2, c.Number(0, 256));
        var sb = new StringBuilder();
        var texts = Enumerable.Range(0, c.ParamCount).Select(c.Text).Where(t => t != null).ToList();
        for (int i = 0; i < texts.Count; i++)
        {
            sb.Append(texts[i]);
            if (i + 2 > c.InputCount || i == texts.Count - 1) continue;
            var v = c.Analog(i + 2) % mod;
            if (mod > 256) sb.Append((char)(v >> 8));
            sb.Append((char)(v & 0xFF));
        }
        c.Set(1, sb.ToString());
    }
}

/// <summary>Type 93, Serial Gather: collects input text and outputs it up to and including the delimiter
/// (P1, a character code); P2 is the maximum length.</summary>
public sealed class SerialGatherPrim : Primitive
{
    private readonly StringBuilder _buf = new();
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        _buf.Append(n as string);
        var delim = (char)c.Number(0, 13);
        var max = (int)Math.Max(1, c.Number(1, 255));
        while (true)
        {
            var s = _buf.ToString();
            var i = s.IndexOf(delim);
            if (i >= 0 && i < max) { _buf.Remove(0, i + 1); c.Set(1, s[..(i + 1)]); continue; }
            if (s.Length >= max) { _buf.Remove(0, max); c.Set(1, s[..max]); continue; }
            break;
        }
    }
}

/// <summary>Type 122, Serial Concatenation: whatever arrives on any input goes to the output.</summary>
public sealed class SerialMergePrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (n is string s && s.Length > 0) c.Set(1, s);
    }
}

/// <summary>Type 266, Multiple Discrete Serial Send: a rising input k sends parameter k on output k.</summary>
public sealed class MultiSerialSendPrim : EdgePrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= c.InputCount; k++) if (c.Digital(k)) c.Set(k, c.Text(k - 1) ?? "");
    }
    protected override void OnEdge(IPrimitiveContext c, int p) => c.Set(p, c.Text(p - 1) ?? "");
}

/// <summary>Type 207, Interlock-Toggle: input k toggles output k and clears the others; the input after
/// the toggles clears all.</summary>
public sealed class InterlockTogglePrim : EdgePrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        var count = c.OutputCount;
        var turnOn = p <= count && c.Output(p) is not true;
        for (int k = 1; k <= count; k++) c.Set(k, turnOn && k == p);
    }
}

/// <summary>Type 203, Logic Wave Delay: input k reaches output k after P_k logic waves.</summary>
public sealed class WaveDelayPrim : Primitive
{
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p > c.OutputCount) return;
        var waves = (int)Math.Max(1, c.Number(p - 1, 1));
        void Hop(int left) { if (left <= 1) c.Set(p, n); else c.NextWave(() => Hop(left - 1)); }
        c.NextWave(() => Hop(waves));
    }
}

/// <summary>
/// Type 241, Stepper with Progress &amp; Reset. Input 1 starts, input 2 resets. Outputs: 1 running,
/// 2 progress (0-65535), 3-4 reserved, 5 progress text, then the steps. Timing as Stepper:
/// P1 initial delay, then (pulse time, gap) per step.
/// </summary>
public sealed class StepperProgressPrim : Primitive
{
    private const int FirstStep = 6;
    private readonly List<IDisposable> _timers = new();

    public override SigKind? OutputKind(int pin) => pin switch { 2 or 3 or 4 => SigKind.Analog, 5 => SigKind.Serial, _ => SigKind.Digital };

    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (Rising(o, n)) OnEdge(c, p);
    }
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (!o && n) OnEdge(c, p);
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }

    private void OnEdge(IPrimitiveContext c, int p)
    {
        Cancel(c);
        if (p == 2) { c.Set(1, false); c.Set(2, (ushort)0); c.Set(5, ""); return; }

        var steps = Math.Max(0, c.OutputCount - FirstStep + 1);
        var starts = new long[steps];
        long at = c.Number(0);
        for (int s = 0, param = 1; s < steps; s++, param += 2)
        {
            starts[s] = at;
            var output = FirstStep + s;
            var duration = Math.Max(1, c.Number(param, 1));
            _timers.Add(c.AfterTicks(at, () => c.Set(output, true)));
            _timers.Add(c.AfterTicks(at + duration, () => c.Set(output, false)));
            at += duration + c.Number(param + 1);
        }
        var total = Math.Max(1, at);
        c.Set(1, true);
        Progress(c, 0);
        for (int s = 0; s < steps; s++)
        {
            var pct = starts[s] * 100 / total;
            _timers.Add(c.AfterTicks(starts[s], () => Progress(c, pct)));
        }
        _timers.Add(c.AfterTicks(total, () => { Progress(c, 100); c.Set(1, false); }));
    }

    private static void Progress(IPrimitiveContext c, long pct)
    {
        c.Set(2, (ushort)(pct * 65535 / 100));
        c.Set(5, $"{pct}%");
    }

    private void Cancel(IPrimitiveContext c)
    {
        foreach (var t in _timers) t.Dispose();
        _timers.Clear();
        for (int k = FirstStep; k <= c.OutputCount; k++) c.Set(k, false);
    }
}

/// <summary>Type 202, Console: sends the input text to the processor console and outputs the reply.</summary>
public sealed class ConsolePrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        var cmd = (n as string ?? "").Trim('\r', '\n', ' ');
        if (cmd.Length == 0) return;
        string reply;
        try { reply = c.Slot.RunConsoleCommand(cmd); }
        catch (Exception e) { reply = "Error: " + e.Message; }
        c.Set(1, reply.Replace("\n", "\r\n"));
    }
}

/// <summary>Type 121, clock adjust (hour/minute up/down). The virtual processor follows the host clock,
/// so this does nothing.</summary>
public sealed class NoOpPrim : ReactPrim
{
    protected override void React(IPrimitiveContext c, int p) { }
}

/// <summary>
/// Type 85, Intersystem Communications (XSIG): decodes the serial input 1 into outputs 2.. and encodes
/// changes on inputs 2.. onto serial output 1. Join n (0-based, plus the P1 offset) maps to pin n + 2.
/// Encoding: digital 1 0 L J J J J J | 0 J J J J J J J (L = 1 for low); analog 1 1 V V 0 J J J | 0 J7 | 0 V7 | 0 V7;
/// serial 1 1 0 0 1 J J J | 0 J7 | text | 0xFF.
/// </summary>
public sealed class XsigPrim : Primitive
{
    private readonly StringBuilder _rx = new();
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Serial : null;
    public override SigKind? OutputKind(int pin) => pin == 1 ? SigKind.Serial : null;

    private int Offset(IPrimitiveContext c) => (int)c.Number(0);

    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p == 1) { Decode(c, n as string ?? ""); return; }
        if (c.IsConstant(p)) return;
        var join = p - 2 + Offset(c);
        c.Set(1, Encode(join, n));
    }

    internal static string Encode(int join, object value)
    {
        var sb = new StringBuilder();
        switch (value)
        {
            case bool b:
                sb.Append((char)(0x80 | (b ? 0 : 0x20) | ((join >> 7) & 0x1F)));
                sb.Append((char)(join & 0x7F));
                break;
            case ushort v:
                sb.Append((char)(0xC0 | (((v >> 14) & 3) << 4) | ((join >> 7) & 7)));
                sb.Append((char)(join & 0x7F));
                sb.Append((char)((v >> 7) & 0x7F));
                sb.Append((char)(v & 0x7F));
                break;
            default:
                sb.Append((char)(0xC8 | ((join >> 7) & 7)));
                sb.Append((char)(join & 0x7F));
                sb.Append(value?.ToString() ?? "");
                sb.Append((char)0xFF);
                break;
        }
        return sb.ToString();
    }

    private void Decode(IPrimitiveContext c, string text)
    {
        _rx.Append(text);
        while (_rx.Length >= 2)
        {
            int b0 = _rx[0] & 0xFF;
            if ((b0 & 0x80) == 0) { _rx.Remove(0, 1); continue; } // resync
            int consumed; int join; object value;
            if ((b0 & 0xC0) == 0x80)
            {
                join = ((b0 & 0x1F) << 7) | (_rx[1] & 0x7F);
                value = (b0 & 0x20) == 0;
                consumed = 2;
            }
            else if ((b0 & 0xF8) == 0xC8)
            {
                var end = _rx.ToString().IndexOf((char)0xFF, 2);
                if (end < 0) return;
                join = ((b0 & 7) << 7) | (_rx[1] & 0x7F);
                value = _rx.ToString(2, end - 2);
                consumed = end + 1;
            }
            else if ((b0 & 0xC8) == 0xC0)
            {
                if (_rx.Length < 4) return;
                join = ((b0 & 7) << 7) | (_rx[1] & 0x7F);
                value = (ushort)((((b0 >> 4) & 3) << 14) | ((_rx[2] & 0x7F) << 7) | (_rx[3] & 0x7F));
                consumed = 4;
            }
            else { _rx.Remove(0, 1); continue; }
            _rx.Remove(0, consumed);
            var pin = join - Offset(c) + 2;
            if (pin >= 2 && pin <= c.OutputCount) c.Set(pin, value);
        }
    }
}
