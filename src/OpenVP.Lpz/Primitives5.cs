using System.Globalization;
using System.Text;
using OpenVP.Core;

namespace OpenVP.Lpz;

// More symbols written from the SIMPL Windows symbol descriptions (SymbolEvidence.Documented in
// LogicSymbolCatalog): math, delays, counters, ASCII/hex conversions, string compares, Serial RAM,
// multiplexers and time splitting. Like Primitives3.cs they run from .smw source, matched by name, and from
// compiled programs once primitives.json names their primitive type. Pin order and parameter units are best
// guesses until a real program confirms them; docs/lpz-format.md lists the assumptions.

/// <summary>
/// Analog math on any number of inputs, all 16-bit unsigned with wraparound. Pins wired to a constant
/// (including unconnected ones) are skipped, so an unused pin never zeroes a product or drags a minimum to 0.
/// Subtract is i1 - i2 - i3 ...; Multiply is the low 16 bits of the product; Divide is the integer quotient
/// i1 / i2 / i3 ... and a zero divisor gives 0; Min, Max and Average (rounded down) look at every wired input.
/// </summary>
public sealed class AnalogMathPrim(AnalogMathPrim.Op op) : EvalPrim
{
    public enum Op { Subtract, Multiply, Divide, Min, Max, Average }

    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;

    protected override void Eval(IPrimitiveContext c)
    {
        var values = new List<int>();
        for (int k = 1; k <= c.InputCount; k++)
            if (!c.IsConstant(k)) values.Add(c.Analog(k));
        if (values.Count == 0) { c.Set(1, (ushort)0); return; }
        int r = values[0];
        switch (op)
        {
            case Op.Subtract: for (int i = 1; i < values.Count; i++) r = (r - values[i]) & 0xFFFF; break;
            case Op.Multiply: for (int i = 1; i < values.Count; i++) r = (int)(((long)r * values[i]) & 0xFFFF); break;
            case Op.Divide:
                for (int i = 1; i < values.Count; i++) { if (values[i] == 0) { r = 0; break; } r /= values[i]; }
                break;
            case Op.Min: r = values.Min(); break;
            case Op.Max: r = values.Max(); break;
            case Op.Average: r = (int)(values.Sum(v => (long)v) / values.Count); break;
        }
        c.Set(1, (ushort)r);
    }
}

/// <summary>Analog Delay and Serial Delay: every change of the input appears on the output P1 (1/100 s) later,
/// in the order it happened, so a value that changes twice inside the delay still shows both. A delay of 0 passes
/// the change straight through. The output stays at its default until the first delayed change.</summary>
public sealed class ValueDelayPrim(SigKind kind) : Primitive
{
    public override SigKind? InputKind(int pin) => kind;
    public override SigKind? OutputKind(int pin) => kind;
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { if (p == 1) Pass(c, n); }
    public override void OnSerial(IPrimitiveContext c, int p, string o, string n) { if (p == 1) Pass(c, n); }
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        if (n is ushort a) Pass(c, a);
        else if (n is string s) Pass(c, s);
    }

    private void Pass(IPrimitiveContext c, object value)
    {
        var ticks = c.Number(0, 0);
        if (ticks <= 0) { c.Set(1, value); return; }
        c.AfterTicks(ticks, () => c.Set(1, value));
    }
}

/// <summary>
/// Up/Down Counter (also Analog Counter): a rising edge on input 1 counts up and on input 2 counts down, one step
/// each, stopping at the limits instead of wrapping. Input 3 resets the count: it goes to the initial value on the
/// rising edge and counting is ignored while it is held. Parameters: P1 lowest value (default 0), P2 highest
/// (default 65535), P3 initial value (default the lowest).
/// </summary>
public sealed class UpDownCounterPrim : Primitive
{
    private long _value;
    public override SigKind? InputKind(int pin) => SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    private static long Min(IPrimitiveContext c) => Math.Clamp(c.Number(0, 0), 0, 65535);
    private static long Max(IPrimitiveContext c) => Math.Clamp(c.Number(1, 65535), Min(c), 65535);
    private static long Initial(IPrimitiveContext c) => Math.Clamp(c.Number(2, Min(c)), Min(c), Max(c));

    public override void Start(IPrimitiveContext c)
    {
        _value = Initial(c);
        c.Set(1, (ushort)_value);
    }

    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n)
    {
        if (o == n) return;
        if (p == 3)
        {
            if (n) { _value = Initial(c); c.Set(1, (ushort)_value); }
            return;
        }
        if (!n || (c.InputCount >= 3 && c.Digital(3))) return;
        if (p == 1) _value = Math.Min(Max(c), _value + 1);
        else if (p == 2) _value = Math.Max(Min(c), _value - 1);
        else return;
        c.Set(1, (ushort)_value);
    }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (o is bool ob && n is bool nb) OnDigital(c, p, ob, nb);
    }
}

/// <summary>Serial to Hex: the output spells each character of the input as two upper-case hex digits
/// ("AB" becomes "4142"). Characters above 255 use their low byte.</summary>
public sealed class SerialToHexPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        var sb = new StringBuilder();
        foreach (var ch in n as string ?? string.Empty) sb.Append((ch & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
        c.Set(1, sb.ToString());
    }
}

/// <summary>Hex to Serial: pairs of hex digits (either case) become characters ("4142" gives "AB"). Conversion
/// stops at the first character that isn't a hex digit, and a lone last digit is dropped.</summary>
public sealed class HexToSerialPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        var s = n as string ?? string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < s.Length; i += 2)
        {
            if (!int.TryParse(s.AsSpan(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b)) break;
            sb.Append((char)b);
        }
        c.Set(1, sb.ToString());
    }
}

/// <summary>Analog to Hex: the output is the input as upper-case hex digits, zero padded to P1 digits (1 to 4,
/// default 4). A value too big for the width keeps its low digits.</summary>
public sealed class AnalogToHexPrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    protected override void Eval(IPrimitiveContext c)
    {
        var digits = (int)Math.Clamp(c.Number(0, 4), 1, 4);
        var hex = c.Analog(1).ToString("X4", CultureInfo.InvariantCulture);
        c.Set(1, hex[(4 - digits)..]);
    }
}

/// <summary>Hex to Analog: the input read as hex digits (an optional 0x prefix or trailing h is allowed) gives the
/// output, keeping the low 16 bits. Text that isn't hex, including an empty string, gives 0.</summary>
public sealed class HexToAnalogPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        var s = (n as string ?? string.Empty).Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        else if (s.EndsWith('h') || s.EndsWith('H')) s = s[..^1];
        c.Set(1, s.Length > 0 && s.Length <= 15 && long.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)
            ? (ushort)(v & 0xFFFF) : (ushort)0);
    }
}

/// <summary>Serial Equate: output k is high while input 1 is exactly (case-sensitive) string parameter k. Input 2,
/// when wired, enables it the way Analog Equate's does (unwired = enabled); disabled outputs are low.</summary>
public sealed class SerialEquatePrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => pin == 2 ? SigKind.Digital : SigKind.Serial;
    protected override void Eval(IPrimitiveContext c)
    {
        var enabled = c.InputCount < 2 || c.Digital(2);
        var s = c.Serial(1);
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, enabled && string.Equals(c.Text(k - 1), s, StringComparison.Ordinal));
    }
}

/// <summary>Serial Compare: output 1 is high while the two inputs are the same string, output 2 while they differ.</summary>
public sealed class SerialComparePrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    protected override void Eval(IPrimitiveContext c)
    {
        var same = string.Equals(c.Serial(1), c.Serial(2), StringComparison.Ordinal);
        c.Set(1, same);
        if (c.OutputCount >= 2) c.Set(2, !same);
    }
}

/// <summary>
/// Serial RAM: the string version of Analog RAM. Inputs are the values, then store and recall, then bank selects;
/// the outputs are the recalled values. A rising edge on store saves the values into the selected bank (the first
/// bank-select input that is high, or bank 0 with no bank inputs); recall drives the saved values to the outputs.
/// Memory is non-volatile and addressed by field 5 like Analog RAM, so two symbols with one address share it.
/// </summary>
public sealed class SerialRamPrim : EdgePrim
{
    private static int N(IPrimitiveContext c) => c.OutputCount;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;

    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p == N(c) + 1) Store(c);
        else if (p == N(c) + 2) Recall(c);
    }

    private static int Bank(IPrimitiveContext c)
    {
        for (int i = N(c) + 3; i <= c.InputCount; i++)
            if (c.Digital(i)) return i - (N(c) + 3);
        return c.InputCount <= N(c) + 2 ? 0 : -1;
    }

    private static string Key(IPrimitiveContext c, int bank, int i) =>
        $"sram@{(c.Symbol.FieldInt(5) ?? 0) + ((long)bank * N(c) + i) * 4}";

    private static void Store(IPrimitiveContext c)
    {
        var bank = Bank(c);
        if (bank < 0) return;
        for (int i = 0; i < N(c); i++) c.RememberKey(Key(c, bank, i), c.Serial(i + 1));
    }

    private static void Recall(IPrimitiveContext c)
    {
        var bank = Bank(c);
        if (bank < 0) return;
        for (int i = 0; i < N(c); i++)
            if (c.RecallKey(Key(c, bank, i)) is string v) c.Set(i + 1, v);
    }
}

/// <summary>Multiplexer (Digital, Analog and Serial): input 1 is an analog select, inputs 2..N the data. The output
/// follows data input number "select" (1-based); a select of 0 or past the last input gives the default (low, 0, empty).</summary>
public sealed class MuxPrim(SigKind kind) : EvalPrim
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Analog : kind;
    public override SigKind? OutputKind(int pin) => kind;
    protected override void Eval(IPrimitiveContext c)
    {
        var sel = c.Analog(1);
        var pin = sel + 1;
        if (sel == 0 || pin > c.InputCount) { SetDefault(c, 1); return; }
        switch (kind)
        {
            case SigKind.Digital: c.Set(1, c.Digital(pin)); break;
            case SigKind.Analog: c.Set(1, c.Analog(pin)); break;
            default: c.Set(1, c.Serial(pin)); break;
        }
    }
}

/// <summary>Demultiplexer (Digital, Analog and Serial): input 1 is an analog select, input 2 the data. Output
/// number "select" (1-based) follows the data and every other output sits at its default (low, 0, empty).</summary>
public sealed class DemuxPrim(SigKind kind) : EvalPrim
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Analog : kind;
    public override SigKind? OutputKind(int pin) => kind;
    protected override void Eval(IPrimitiveContext c)
    {
        var sel = c.Analog(1);
        for (int k = 1; k <= c.OutputCount; k++)
        {
            if (k != sel) { SetDefault(c, k); continue; }
            switch (kind)
            {
                case SigKind.Digital: c.Set(k, c.Digital(2)); break;
                case SigKind.Analog: c.Set(k, c.Analog(2)); break;
                default: c.Set(k, c.Serial(2)); break;
            }
        }
    }
}

/// <summary>Time Split: the time-of-day text on input 1 ("HH:MM:SS", "H:MM", or either with AM/PM) becomes the
/// analog outputs hour (0-23), minute and second. Text that isn't a time, or has a field out of range, gives 0 0 0.</summary>
public sealed class TimeSplitPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        var (h, m, s) = Parse(n as string ?? string.Empty);
        c.Set(1, (ushort)h);
        if (c.OutputCount >= 2) c.Set(2, (ushort)m);
        if (c.OutputCount >= 3) c.Set(3, (ushort)s);
    }

    internal static (int H, int M, int S) Parse(string text)
    {
        var t = text.Trim();
        int pm = -1;
        if (t.EndsWith("AM", StringComparison.OrdinalIgnoreCase)) { pm = 0; t = t[..^2].TrimEnd(); }
        else if (t.EndsWith("PM", StringComparison.OrdinalIgnoreCase)) { pm = 1; t = t[..^2].TrimEnd(); }
        var parts = t.Split(':');
        if (parts.Length is < 2 or > 3) return (0, 0, 0);
        var v = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return (0, 0, 0);
        if (v[1] > 59 || v[2] > 59) return (0, 0, 0);
        if (pm >= 0)
        {
            if (v[0] < 1 || v[0] > 12) return (0, 0, 0);
            v[0] = v[0] % 12 + pm * 12;
        }
        return v[0] > 23 ? (0, 0, 0) : (v[0], v[1], v[2]);
    }
}

/// <summary>Time Join: the analog inputs hour, minute and second become the text "HH:MM:SS" (two digits each;
/// a missing second input counts as 0). Values past 23 or 59 are shown as they are, not wrapped.</summary>
public sealed class TimeJoinPrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    protected override void Eval(IPrimitiveContext c) =>
        c.Set(1, string.Create(CultureInfo.InvariantCulture, $"{c.Analog(1):D2}:{(c.InputCount >= 2 ? c.Analog(2) : 0):D2}:{(c.InputCount >= 3 ? c.Analog(3) : 0):D2}"));
}
