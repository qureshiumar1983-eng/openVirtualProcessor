using OpenVP.Core;

namespace OpenVP.Lpz;

// Fast paths for the primitives that make up most real logic. Each is a sealed node that does exactly what
// its primitive does (same Set calls, in the same order) but is called straight from the solve loop, so a
// change costs one virtual call instead of node -> primitive -> context interface -> Drive. State stays in the
// primitive, which still handles Start. Anything not handled here falls back to the primitive.
public sealed partial class LpzRuntime
{
    private partial class LogicNode
    {
        public static LogicNode Create(LpzRuntime rt, BinSymbol sym, Primitive prim) => prim switch
        {
            NotPrim => new NotNode(rt, sym, prim),
            GatePrim g => new GateNode(rt, sym, g),
            BufferPrim => new BufferNode(rt, sym, prim),
            HoldBufferPrim => new HoldBufferNode(rt, sym, prim),
            InterlockPrim => new InterlockNode(rt, sym, prim),
            TogglePrim t => new ToggleNode(rt, sym, t),
            SrLatchPrim l => new LatchNode(rt, sym, l),
            _ => new LogicNode(rt, sym, prim)
        };
    }

    /// <summary>NOT and Multiple NOT: output p = !input p.</summary>
    private sealed class NotNode(LpzRuntime rt, BinSymbol sym, Primitive prim) : LogicNode(rt, sym, prim)
    {
        public override void OnDigital(int pin, bool o, bool n) => Set(pin, !n);
        public override void OnAnalog(int pin, ushort o, ushort n) => Set(pin, true); // not a high digital
    }

    /// <summary>AND, OR, NOR: a running count of high inputs (see GatePrim).</summary>
    private sealed class GateNode(LpzRuntime rt, BinSymbol sym, GatePrim gate) : LogicNode(rt, sym, gate)
    {
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (gate._scan) gate._high = GatePrim.Scan(this);
            else gate._high += (n ? 1 : 0) - (o ? 1 : 0);
            Set(1, gate.Compute(gate._high, Symbol.Inputs.Length));
        }
        public override void OnAnalog(int pin, ushort o, ushort n) => Other();
        public override void OnSerial(int pin, string o, string n) => Other();
        private void Other()
        {
            if (gate._scan) gate._high = GatePrim.Scan(this);
            Set(1, gate.Compute(gate._high, Symbol.Inputs.Length));
        }
    }

    /// <summary>Buffer: input 1 enables; while enabled, output p-1 follows input p.</summary>
    private sealed class BufferNode(LpzRuntime rt, BinSymbol sym, Primitive prim) : LogicNode(rt, sym, prim)
    {
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (pin == 1) base.OnDigital(pin, o, n); // enable changed: re-drive every output
            else if (Digital(1)) Set(pin - 1, n);
        }
        public override void OnAnalog(int pin, ushort o, ushort n)
        {
            if (pin == 1) base.OnAnalog(pin, o, n);
            else if (Digital(1)) Set(pin - 1, n);
        }
    }

    /// <summary>Analog and Serial Buffer: like Buffer, but outputs hold their value while disabled.</summary>
    private sealed class HoldBufferNode(LpzRuntime rt, BinSymbol sym, Primitive prim) : LogicNode(rt, sym, prim)
    {
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (pin == 1) { if (!o && n) base.OnDigital(pin, o, n); }
            else if (Digital(1)) Set(pin - 1, n);
        }
        public override void OnAnalog(int pin, ushort o, ushort n)
        {
            if (pin != 1 && Digital(1)) Set(pin - 1, n);
        }
        public override void OnSerial(int pin, string o, string n)
        {
            if (pin != 1 && Digital(1)) Set(pin - 1, n);
        }
    }

    /// <summary>Interlock: a rising input p selects output p-1 (input 1 clears).</summary>
    private sealed class InterlockNode(LpzRuntime rt, BinSymbol sym, Primitive prim) : LogicNode(rt, sym, prim)
    {
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (o || !n) return;
            var selected = pin == Symbol.Inputs.Length ? 0 : pin; // the last input clears
            for (int k = 1; k <= Symbol.Outputs.Length; k++) Set(k, k == selected);
        }
        public override void OnAnalog(int pin, ushort o, ushort n) { }
        public override void OnSerial(int pin, string o, string n) { }
    }

    /// <summary>Toggle: rising clock (1) flips, reset (2) and set (3) force; outputs out, out*.</summary>
    private sealed class ToggleNode(LpzRuntime rt, BinSymbol sym, TogglePrim toggle) : LogicNode(rt, sym, toggle)
    {
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (o || !n) return;
            toggle._state = pin switch { 1 => !toggle._state, 2 => false, 3 => true, _ => toggle._state };
            Set(1, toggle._state);
            Set(2, !toggle._state);
        }
        public override void OnAnalog(int pin, ushort o, ushort n) { }
        public override void OnSerial(int pin, string o, string n) { }
    }

    /// <summary>Set/Reset latch: input 1 resets, input 2 sets (reset wins); every change re-drives out, out*.</summary>
    private sealed class LatchNode(LpzRuntime rt, BinSymbol sym, SrLatchPrim latch) : LogicNode(rt, sym, latch)
    {
        public override void OnDigital(int pin, bool o, bool n) => Step(pin == SrLatchPrim.SetPin && !o && n);
        public override void OnAnalog(int pin, ushort o, ushort n) => Step(false);
        public override void OnSerial(int pin, string o, string n) => Step(false);
        private void Step(bool set)
        {
            if (Symbol.Inputs.Length >= 2 && Digital(SrLatchPrim.ResetPin)) latch._state = false;
            else if (set) latch._state = true;
            Set(1, latch._state);
            Set(2, !latch._state);
        }
    }
}
