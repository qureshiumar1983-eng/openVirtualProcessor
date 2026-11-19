using OpenVP.Core;

namespace OpenVP.Simpl;

// Symbols that aren't logic primitives or IP-ID devices: what hangs off a panel (Smart Graphics objects,
// reserved-join extenders) and the crosspoint routing symbols. Each mirrors the compiled-program node of
// the same name in OpenVP.Lpz, so a program behaves the same from .smw and from .lpz.
public sealed partial class SimplRuntime
{
    /// <summary>SmC codes of the Equipment Crosspoint Routing, Control Crosspoint Routing and Crosspoint Connect symbols.</summary>
    private const int EquipmentCrosspoint = 446, ControlCrosspoint = 447, CrosspointConnect = 448;
    /// <summary>A Smart Graphics object (or a Contract Editor .ced component) on a panel.</summary>
    private const int SmartObjectCode = 2942;

    private readonly List<(PanelChildNode Node, int Parent)> _panelChildren = new();
    private readonly Dictionary<long, XEquipmentNode> _xEquipment = new();
    private readonly Dictionary<long, XControlNode> _xControls = new();

    /// <summary>Creates the node for a symbol handled outside the logic library. False if it isn't one.</summary>
    private bool BuildSpecial(SmwSymbol sym)
    {
        switch (sym.Code)
        {
            case EquipmentCrosspoint:
                var eq = new XEquipmentNode(this, sym);
                _xEquipment[eq.Id] = eq;
                Register(eq);
                return true;
            case ControlCrosspoint:
                var ctl = new XControlNode(this, sym);
                _xControls[ctl.Id] = ctl;
                Register(ctl);
                return true;
            case CrosspointConnect:
                Register(new XConnectNode(this, sym));
                return true;
        }
        if (DeviceAncestor(sym) is not { } device) return false;
        PanelChildNode? child = null;
        if (sym.Code == SmartObjectCode || sym.Raw.Get("CedH") != null) child = new SmartObjectNode(this, sym);
        else if (sym.HasGroups && ExtenderJoins.For(sym.Code, sym.Name) is { } bases) child = new ExtenderNode(this, sym, bases);
        if (child == null) return false;
        _panelChildren.Add((child, device.Handle));
        Register(child);
        return true;
    }

    /// <summary>The device a symbol sits under: its parent, or further up through grouping symbols (Smart Graphics
    /// list items sit under a list symbol, which sits under the panel).</summary>
    private SmwSymbol? DeviceAncestor(SmwSymbol sym)
    {
        var seen = new HashSet<int>();
        for (var h = sym.Parent; h > 0 && seen.Add(h) && _file.Symbols.TryGetValue(h, out var p); h = p.Parent)
            if (IsDevice(p)) return p;
        return null;
    }

    /// <summary>After every device exists: hand each panel child to its device. A child of a device that isn't in
    /// the IP table (it has no IP ID) stays idle.</summary>
    private void LinkPanelChildren()
    {
        var byHandle = _devices.ToDictionary(d => d.Symbol.Handle);
        foreach (var (node, parent) in _panelChildren)
        {
            if (!byHandle.TryGetValue(parent, out var dev)) continue;
            node.Owner = dev;
            switch (node)
            {
                case SmartObjectNode so: dev.SmartObjects[so.Id] = so; break;
                case ExtenderNode ext: dev.Extenders.Add(ext); break;
            }
        }
    }

    /// <summary>Which join a pin is, from the symbol's digital/analog/serial pin counts.</summary>
    private static (SigKind Kind, uint Index) JoinOf(int[] groups, int pin) =>
        pin <= groups[0] ? (SigKind.Digital, (uint)pin)
        : pin <= groups[0] + groups[1] ? (SigKind.Analog, (uint)(pin - groups[0]))
        : (SigKind.Serial, (uint)(pin - groups[0] - groups[1]));

    /// <summary>The pin for a join, or 0 if the symbol doesn't have it.</summary>
    private static int PinOf(int[] groups, int pinCount, SigKind kind, uint index)
    {
        if (index < 1) return 0;
        var (offset, size) = kind switch
        {
            SigKind.Digital => (0, groups[0]),
            SigKind.Analog => (groups[0], groups[1]),
            _ => (groups[0] + groups[1], pinCount - groups[0] - groups[1])
        };
        return index <= size ? offset + (int)index : 0;
    }

    private void DriveOut(Node node, int pin, object value)
    {
        if (pin < 1 || pin > node.Symbol.Outputs.Count) return;
        Drive(node.Symbol.Outputs[pin - 1], value, node.Symbol.Name);
    }

    private object InValue(Node node, int pin) =>
        pin >= 1 && pin <= node.Symbol.Inputs.Count ? Value(node.Symbol.Inputs[pin - 1]) : SigValue.False;

    /// <summary>Whether an input pin has something worth sending to a device at attach time.</summary>
    private static bool Wired(int handle) => handle > 0;

    // ------------------------------------------------------------------ panel children

    /// <summary>A symbol whose joins live on its parent device: inputs are feedback to it, outputs are presses from it.</summary>
    private abstract class PanelChildNode : Node
    {
        private int[]? _in, _out;
        protected PanelChildNode(SimplRuntime rt, SmwSymbol sym) : base(rt, sym) { Source = sym.Name; }
        public DeviceNode? Owner { get; set; }
        protected string Source { get; }

        /// <summary>Digital/analog/serial pin counts. Some Smart Graphics list items carry none in the file: then
        /// they come from the kinds of the wired signals, in the usual digital, analog, serial pin order.</summary>
        protected int[] InGroups => _in ??= Symbol.HasGroups ? Symbol.InputGroups : FromSignals(Symbol.Inputs);
        protected int[] OutGroups => _out ??= Symbol.HasGroups ? Symbol.OutputGroups : FromSignals(Symbol.Outputs);

        private int[] FromSignals(List<int> pins)
        {
            var g = new int[3];
            for (int pin = 1; pin <= pins.Count; pin++)
            {
                var k = pins[pin - 1] > 0 ? Runtime.KindOf(pins[pin - 1]) : SigKind.Digital;
                // Pins run digital, then analog, then serial: a pin counts toward its kind or a later one.
                var at = Math.Max((int)k, g[2] > 0 ? 2 : g[1] > 0 ? 1 : 0);
                g[at]++;
            }
            return g;
        }

        public override SigKind? InputKind(int pin) => Symbol.HasGroups ? JoinOf(Symbol.InputGroups, pin).Kind : null;
        public override SigKind? OutputKind(int pin) => Symbol.HasGroups ? JoinOf(Symbol.OutputGroups, pin).Kind : null;

        /// <summary>The device join for an input pin, or null if this child can't place it.</summary>
        protected abstract JoinKey? FeedbackJoin(int pin);

        public override void OnInput(int pin, object o, object n)
        {
            if (Owner?.Device is { } d && FeedbackJoin(pin) is { } key) d.SetFeedback(key, n);
        }

        public void PushAll()
        {
            if (Owner?.Device is not { } d) return;
            for (int pin = 1; pin <= Symbol.Inputs.Count; pin++)
                if (Wired(Symbol.Inputs[pin - 1]) && FeedbackJoin(pin) is { } key) d.SetFeedback(key, Runtime.Value(Symbol.Inputs[pin - 1]));
        }

        protected void Press(int pin, object value)
        {
            var handle = pin >= 1 && pin <= Symbol.Outputs.Count ? Symbol.Outputs[pin - 1] : 0;
            if (handle > 0) Runtime._slot.Dispatcher.Post(static (n, v, h) => { var c = (PanelChildNode)n!; c.Runtime.Drive(h, v!, c.Source); }, this, value, handle);
        }
    }

    /// <summary>A Smart Graphics object (SmC 2942, including Contract Editor .ced components). Its joins travel with
    /// the object ID, P1; pin k of a kind is join k inside the object. Same as compiled type 268, whose object ID
    /// (field 44) matches P1 in the sample programs.</summary>
    private sealed class SmartObjectNode : PanelChildNode
    {
        public SmartObjectNode(SimplRuntime rt, SmwSymbol sym) : base(rt, sym)
        {
            Id = SmwFile.TryParseNumber(sym.Parameters.FirstOrDefault() ?? "", out var id) && id > 0 ? (uint)id : 0;
        }

        public uint Id { get; }

        protected override JoinKey? FeedbackJoin(int pin)
        {
            var (kind, index) = JoinOf(InGroups, pin);
            return new JoinKey(kind, index, Id);
        }

        public void FromPanel(JoinKey key, object value) =>
            Press(PinOf(OutGroups, Symbol.Outputs.Count, key.Kind, key.Join), value);
    }

    /// <summary>A device extender (reserved joins, button toolbar, app control): pin k of a kind is join base + k
    /// on the device (see <see cref="ExtenderJoins"/>). Pins of a kind with no known base are left unmapped.</summary>
    private sealed class ExtenderNode : PanelChildNode
    {
        private readonly ExtenderJoins.Bases _bases;
        public ExtenderNode(SimplRuntime rt, SmwSymbol sym, ExtenderJoins.Bases bases) : base(rt, sym) { _bases = bases; }

        protected override JoinKey? FeedbackJoin(int pin)
        {
            var (kind, index) = JoinOf(InGroups, pin);
            return _bases.Of(kind) is { } b ? new JoinKey(kind, b + index) : null;
        }

        /// <summary>A press from the device: true if the join is one of this extender's.</summary>
        public bool FromPanel(JoinKey key, object value)
        {
            if (_bases.Of(key.Kind) is not { } b || key.Join <= b) return false;
            var pin = PinOf(OutGroups, Symbol.Outputs.Count, key.Kind, key.Join - b);
            if (pin < 1) return false;
            Press(pin, value);
            return true;
        }
    }

    // ------------------------------------------------------------------ crosspoints

    private static long CrosspointId(SmwSymbol sym) =>
        SmwFile.TryParseNumber(sym.Parameters.FirstOrDefault() ?? "", out var id) ? id : 0;

    /// <summary>Equipment Crosspoint Routing (SmC 446), ID = P1. Inputs are feedback from the equipment logic,
    /// outputs are presses toward it. Every connected control crosspoint mirrors it join for join.</summary>
    private sealed class XEquipmentNode : Node
    {
        public XEquipmentNode(SimplRuntime rt, SmwSymbol sym) : base(rt, sym) { Id = CrosspointId(sym); }
        public long Id { get; }
        public override SigKind? InputKind(int pin) => JoinOf(Symbol.InputGroups, pin).Kind;
        public override SigKind? OutputKind(int pin) => JoinOf(Symbol.OutputGroups, pin).Kind;

        public IEnumerable<XControlNode> Controls => Runtime._xControls.Values.Where(c => c.Connected == this);

        public override void OnInput(int pin, object o, object n)
        {
            var (kind, index) = JoinOf(Symbol.InputGroups, pin);
            foreach (var c in Controls) c.Feedback(kind, index, n);
        }

        public void Press(SigKind kind, uint index, object value) =>
            Runtime.DriveOut(this, PinOf(Symbol.OutputGroups, Symbol.Outputs.Count, kind, index), value);
    }

    /// <summary>Control Crosspoint Routing (SmC 447), ID = P1. Inputs are presses from a panel, outputs are feedback to it.</summary>
    private sealed class XControlNode : Node
    {
        public XControlNode(SimplRuntime rt, SmwSymbol sym) : base(rt, sym) { Id = CrosspointId(sym); }
        public long Id { get; }
        public XEquipmentNode? Connected { get; private set; }
        public override SigKind? InputKind(int pin) => JoinOf(Symbol.InputGroups, pin).Kind;
        public override SigKind? OutputKind(int pin) => JoinOf(Symbol.OutputGroups, pin).Kind;

        public override void OnInput(int pin, object o, object n)
        {
            var (kind, index) = JoinOf(Symbol.InputGroups, pin);
            Connected?.Press(kind, index, n);
        }

        public void Feedback(SigKind kind, uint index, object value) =>
            Runtime.DriveOut(this, PinOf(Symbol.OutputGroups, Symbol.Outputs.Count, kind, index), value);

        public void Connect(XEquipmentNode? equipment)
        {
            if (Connected == equipment) return;
            if (Connected != null)
            {
                // Release held presses and clear feedback.
                for (int pin = 1; pin <= Symbol.Inputs.Count; pin++)
                    if (Runtime.InValue(this, pin) is true)
                    {
                        var (kind, index) = JoinOf(Symbol.InputGroups, pin);
                        Connected.Press(kind, index, SigValue.False);
                    }
                for (int pin = 1; pin <= Symbol.Outputs.Count; pin++)
                    if (Symbol.Outputs[pin - 1] > 0)
                        Runtime.DriveOut(this, pin, SigValue.Default(Runtime.KindOf(Symbol.Outputs[pin - 1])));
            }
            Connected = equipment;
            if (equipment == null) return;
            for (int pin = 1; pin <= equipment.Symbol.Inputs.Count; pin++)
                if (Wired(equipment.Symbol.Inputs[pin - 1]))
                {
                    var (kind, index) = JoinOf(equipment.Symbol.InputGroups, pin);
                    Feedback(kind, index, Runtime.InValue(equipment, pin));
                }
        }
    }

    /// <summary>Equipment/Control Crosspoint Connect (SmC 448). Inputs: control ID, equipment ID (analog), then
    /// rising edges for connect, disconnect, disconnect the control, disconnect the equipment, disconnect all.</summary>
    private sealed class XConnectNode : Node
    {
        public XConnectNode(SimplRuntime rt, SmwSymbol sym) : base(rt, sym) { }
        public override SigKind? InputKind(int pin) => pin <= 2 ? SigKind.Analog : SigKind.Digital;
        public override SigKind? OutputKind(int pin) => null;

        public override void OnInput(int pin, object o, object n)
        {
            if (pin <= 2 || !(o is false && n is true)) return;
            long cid = Runtime.InValue(this, 1) is ushort a ? a : 0;
            long eid = Runtime.InValue(this, 2) is ushort b ? b : 0;
            Runtime._xControls.TryGetValue(cid, out var control);
            Runtime._xEquipment.TryGetValue(eid, out var equipment);
            switch (pin)
            {
                case 3: control?.Connect(equipment); break;
                case 4: if (control?.Connected == equipment) control?.Connect(null); break;
                case 5: control?.Connect(null); break;
                case 6: if (equipment != null) foreach (var c in equipment.Controls.ToList()) c.Connect(null); break;
                default: foreach (var c in Runtime._xControls.Values) c.Connect(null); break;
            }
        }
    }
}
