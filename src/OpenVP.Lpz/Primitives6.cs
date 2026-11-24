using OpenVP.Core;

namespace OpenVP.Lpz;

// Symbols found in real .smw programs that OpenVP had no behavior for. Their pins are read off how real programs wire
// and name them (tests/OpenVP.Tests/logic-test checks each one); the compiled type is not known yet, so they run from
// .smw source only until a compiled program shows it.

/// <summary>Digital To Scaled Analog (SmC 36): a high input drives the output to 65535 (100%), a low one to 0.</summary>
public sealed class DigitalToScaledAnalogPrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void Eval(IPrimitiveContext c)
    {
        for (int k = 1; k <= Math.Min(c.InputCount, c.OutputCount); k++) c.Set(k, c.Digital(k) ? (ushort)65535 : (ushort)0);
    }
}

/// <summary>
/// Analog Value Sample (SmC 122). .smw pins: 1 (real programs tie it to 0), 2 = sample, 3.. = values; one output per value.
/// A rising sample copies each value to its output; the outputs hold until the next sample. Real programs use it to
/// capture lighting levels on a "save preset" press (LTG_Preset_n_save_ok sampling …Level into …Preset_n_Level).
/// </summary>
public sealed class AnalogValueSamplePrim : EdgePrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        if (p != 2) return;
        for (int k = 1; k <= c.OutputCount && k + 2 <= c.InputCount; k++) c.SetFromInput(k, k + 2);
    }
}

/// <summary>
/// Variable Oscillator (SmC 410). Pins: enable, high time, low time (analog, in 1/100 s like every compiled time; at least
/// 1). While enabled the output is high for the high time, then low for the low time, and so on; disabled, it is low.
/// Real programs wire Enable_Blink, Blink_Hi_Time, Blink_Lo_Time in that order.
/// </summary>
public sealed class VariableOscillatorPrim : Primitive
{
    private IDisposable? _timer;
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Digital : SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Digital;
    public override void Start(IPrimitiveContext c) { if (c.Digital(1)) High(c); }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;   // new times take effect on the next half-cycle
        _timer?.Dispose();
        if (n is true) High(c); else c.Set(1, false);
    }
    private void High(IPrimitiveContext c)
    {
        c.Set(1, true);
        _timer = c.AfterTicks(Math.Max(1, (long)c.Analog(2)), () => Low(c));
    }
    private void Low(IPrimitiveContext c)
    {
        c.Set(1, false);
        _timer = c.AfterTicks(Math.Max(1, (long)c.Analog(3)), () => { if (c.Digital(1)) High(c); });
    }
}

/// <summary>
/// Double-Precision Analog Initialize (SmC 635): a rising input k sends parameter k as a 32-bit value, output 1 the high
/// word and output 2 the low word. Real programs send fade times this way (press6_enabled → Fade_Time_HiWord, Fade_Time
/// with 200t).
/// </summary>
public sealed class DoubleAnalogInitPrim : EdgePrim
{
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void OnEdge(IPrimitiveContext c, int p)
    {
        var v = (uint)c.Number(p - 1);
        c.Set(1, (ushort)(v >> 16));
        if (c.OutputCount >= 2) c.Set(2, (ushort)(v & 0xFFFF));
    }
}

/// <summary>
/// Serial Demultiplexor (SmC 86). Pins: string, analog selector; parameter 1 is the base. When the string changes it goes
/// to output (selector − base), if there is one. Real programs route a lighting response by its ID this way (Response$,
/// ResponseID → out1$, base 0d).
/// </summary>
public sealed class SerialDemuxPrim : Primitive
{
    public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Serial : SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        var k = c.Analog(2) - c.Number(0);
        if (k >= 1 && k <= c.OutputCount) c.Set((int)k, c.Serial(1));
    }
}

/// <summary>
/// Serial Substring (Expandable) (SmC 1317): output k is a piece of the input string, from parameter 2k − 1 to parameter
/// 2k. A start below 100h is a 1-based position; CCXXh starts after occurrence CC of character XX. An end below
/// 100h is a length; CCXXh stops before occurrence CC of character XX. Real programs split a console command at
/// its first space: (1d, 0120h) gives the command, (0120h, 50d) the arguments.
/// </summary>
public sealed class SerialSubstringExpandablePrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        var s = c.Serial(1);
        for (int k = 1; k <= c.OutputCount; k++) c.Set(k, Piece(c, s, k));
    }

    // A delimiter is written CCXXh (count, character), so it is at least 100h; a position or a length is below that
    // (serials are at most 255 characters in these symbols).
    private static (bool Delimiter, long Value) Param(IPrimitiveContext c, int index)
    {
        var v = c.Number(index);
        return (v >= 0x100, v);
    }

    private static int Find(string s, char ch, int count)
    {
        int at = -1;
        for (int i = 0; i < Math.Max(1, count); i++) { at = s.IndexOf(ch, at + 1); if (at < 0) return -1; }
        return at;
    }

    private static string Piece(IPrimitiveContext c, string s, int k)
    {
        var (startHex, start) = Param(c, 2 * k - 2);
        var (endHex, end) = Param(c, 2 * k - 1);
        int from;
        if (startHex) { var at = Find(s, (char)(start & 0xFF), (int)(start >> 8)); if (at < 0) return ""; from = at + 1; }
        else from = (int)Math.Max(0, start - 1);
        if (from > s.Length) return "";
        int to;
        if (endHex) { var at = Find(s[from..], (char)(end & 0xFF), (int)(end >> 8)); to = at < 0 ? s.Length : from + at; }
        else to = (int)Math.Min(s.Length, from + Math.Max(0, end));
        return s[from..to];
    }
}

/// <summary>Message to Computer Port (SmC 94): a rising trigger writes parameter 1 to the processor's console (the log).</summary>
public sealed class ConsoleMessagePrim : EdgePrim
{
    protected override void OnEdge(IPrimitiveContext c, int p) => c.Slot.Log("Info", c.Text(0) ?? c.Number(0).ToString());
}
