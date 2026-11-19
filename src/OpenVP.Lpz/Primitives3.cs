using OpenVP.Core;

namespace OpenVP.Lpz;

// Symbols written from the SIMPL Windows symbol descriptions, not yet seen in a compiled program
// (SymbolEvidence.Documented in LogicSymbolCatalog). They run from .smw source, matched by name, and from
// compiled programs once primitives.json names their primitive type. Pin order and parameter units are best
// guesses until a real program confirms them.

/// <summary>NAND: low only when every input is high.</summary>
public sealed class NandPrim : GatePrim
{
    internal override bool Compute(int high, int inputCount) => !(inputCount > 0 && high == inputCount);
}

/// <summary>XNOR: high when an even number of inputs (including none) are high.</summary>
public sealed class XnorPrim : EvalPrim
{
    protected override void Eval(IPrimitiveContext c)
    {
        var odd = false;
        for (int i = 1; i <= c.InputCount; i++) odd ^= c.Digital(i);
        c.Set(1, !odd);
    }
}

/// <summary>Digital to Analog: the inputs are the bits of the output, input 1 the most significant
/// (the reverse of Analog to Digital, type 106).</summary>
public sealed class DigitalToAnalogPrim : EvalPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Digital;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    protected override void Eval(IPrimitiveContext c)
    {
        int v = 0, count = Math.Min(c.InputCount, 16);
        for (int k = 1; k <= count; k++)
            if (c.Digital(k)) v |= 1 << (count - k);
        c.Set(1, (ushort)v);
    }
}

/// <summary>Analog 2's Offset Converter: output k is input k with bit 15 flipped, converting between
/// signed (two's complement) and offset-binary values.</summary>
public sealed class TwosOffsetPrim : ReactPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= Math.Min(c.InputCount, c.OutputCount); k++) React(c, k);
    }
    protected override void React(IPrimitiveContext c, int pin)
    {
        if (pin <= c.OutputCount) c.Set(pin, (ushort)(c.Analog(pin) ^ 0x8000));
    }
}

/// <summary>Analog Negation: output k is the two's complement negative of input k.</summary>
public sealed class AnalogNegatePrim : ReactPrim
{
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void Start(IPrimitiveContext c)
    {
        for (int k = 1; k <= Math.Min(c.InputCount, c.OutputCount); k++) React(c, k);
    }
    protected override void React(IPrimitiveContext c, int pin)
    {
        if (pin <= c.OutputCount) c.Set(pin, (ushort)(-c.Analog(pin)));
    }
}

/// <summary>Analog Debounce: the output takes the input's value once it has stayed unchanged for P1
/// (1/100 s; default 0.1 s).</summary>
public sealed class AnalogDebouncePrim : Primitive
{
    private IDisposable? _timer;
    public override SigKind? InputKind(int pin) => SigKind.Analog;
    public override SigKind? OutputKind(int pin) => SigKind.Analog;
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) => Arm(c, p);
    public override void OnInput(IPrimitiveContext c, int p, object o, object n) => Arm(c, p);
    private void Arm(IPrimitiveContext c, int p)
    {
        if (p != 1) return;
        _timer?.Dispose();
        _timer = c.AfterTicks(Math.Max(1, c.Number(0, 10)), () => c.Set(1, c.Analog(1)));
    }
}

/// <summary>Serial Substring: whenever the input changes, the output is the part of it starting at character
/// P1 (1-based) and P2 characters long (0 or missing = to the end). Out of range gives an empty string.</summary>
public sealed class SerialSubstringPrim : Primitive
{
    public override SigKind? InputKind(int pin) => SigKind.Serial;
    public override SigKind? OutputKind(int pin) => SigKind.Serial;
    public override void OnDigital(IPrimitiveContext c, int p, bool o, bool n) { }
    public override void OnAnalog(IPrimitiveContext c, int p, ushort o, ushort n) { }
    public override void OnInput(IPrimitiveContext c, int p, object o, object n)
    {
        if (p != 1) return;
        var s = n as string ?? string.Empty;
        var start = (int)Math.Max(1, c.Number(0, 1)) - 1;
        var len = c.Number(1, 0);
        if (start >= s.Length) { c.Set(1, string.Empty); return; }
        var take = len <= 0 ? s.Length - start : (int)Math.Min(len, s.Length - start);
        c.Set(1, s.Substring(start, take));
    }
}
