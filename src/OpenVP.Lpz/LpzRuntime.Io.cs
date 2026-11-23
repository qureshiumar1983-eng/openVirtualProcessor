using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenVP.Core;

namespace OpenVP.Lpz;

public sealed partial class LpzRuntime
{
    private readonly Dictionary<string, DeviceNode> _devicesByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<long, XEquipmentNode> _xEquipment = new();
    private readonly Dictionary<long, XControlNode> _xControls = new();

    /// <summary>Fields 10-13 split a record's pins into digital, analog and serial joins (devices, smart
    /// objects, crosspoints, TCP/IP clients). Position n within a group is join n.</summary>
    internal readonly struct JoinSplit
    {
        private readonly int _inD, _inA, _inTotal, _outD, _outA, _outTotal;

        public JoinSplit(BinSymbol sym)
        {
            _inTotal = sym.Inputs.Length;
            _outTotal = sym.Outputs.Length;
            _inD = Math.Min(sym.FieldInt(10) ?? _inTotal, _inTotal);
            _inA = Math.Clamp(sym.FieldInt(12) ?? _inD, _inD, _inTotal);
            _outD = Math.Min(sym.FieldInt(11) ?? _outTotal, _outTotal);
            _outA = Math.Clamp(sym.FieldInt(13) ?? _outD, _outD, _outTotal);
        }

        public JoinKey InJoin(int pin, uint so = 0) =>
            pin <= _inD ? new(SigKind.Digital, (uint)pin, so)
            : pin <= _inA ? new(SigKind.Analog, (uint)(pin - _inD), so)
            : new(SigKind.Serial, (uint)(pin - _inA), so);

        public JoinKey OutJoin(int pin) =>
            pin <= _outD ? new(SigKind.Digital, (uint)pin)
            : pin <= _outA ? new(SigKind.Analog, (uint)(pin - _outD))
            : new(SigKind.Serial, (uint)(pin - _outA));

        public int InPin(JoinKey k) => Pin(k, _inD, _inA, _inTotal);
        public int OutPin(JoinKey k) => Pin(k, _outD, _outA, _outTotal);

        private static int Pin(JoinKey k, int d, int a, int total) => k.Kind switch
        {
            SigKind.Digital => k.Join >= 1 && k.Join <= d ? (int)k.Join : 0,
            SigKind.Analog => k.Join >= 1 && d + k.Join <= a ? d + (int)k.Join : 0,
            _ => k.Join >= 1 && a + k.Join <= total ? a + (int)k.Join : 0
        };
    }

    private void DriveOut(Node node, int pin, object value)
    {
        if (pin < 1 || pin > node.Symbol.Outputs.Length) return;
        Drive(node.Symbol.Outputs[pin - 1], value, node.Symbol.Path);
    }

    private object InValue(Node node, int pin) =>
        pin >= 1 && pin <= node.Symbol.Inputs.Length ? Value(node.Symbol.Inputs[pin - 1]) : false;

    /// <summary>Creates nodes for the record types handled outside the primitive library. Returns false if the
    /// record isn't one of them.</summary>
    private bool BuildSpecial(BinSymbol sym)
    {
        switch (sym.Type)
        {
            case 268:
                var dot = sym.Path.LastIndexOf('.');
                if (dot > 0 && _devicesByPath.TryGetValue(sym.Path[..dot], out var parent))
                {
                    var so = new SmartObjectNode(this, sym, parent);
                    parent.SmartObjects[so.Id] = so;
                    Register(so);
                }
                return true; // smart objects of panels OpenVP doesn't host are simply idle
            case -12 when sym.FieldInt(27) != null:
                // A device extender (reserved joins, button toolbar, app control): its joins sit on the panel at fixed bases.
                for (var path = sym.Path; path.LastIndexOf('.') is var d and > 0;)
                {
                    path = path[..d];
                    if (!_devicesByPath.TryGetValue(path, out var owner)) continue;
                    var ext = new ExtenderNode(this, sym, owner);
                    owner.Extenders.Add(ext);
                    Register(ext);
                    break;
                }
                return true;
            case 213:
                var eq = new XEquipmentNode(this, sym);
                _xEquipment[eq.Id] = eq;
                Register(eq);
                return true;
            case 214:
                var ctl = new XControlNode(this, sym);
                _xControls[ctl.Id] = ctl;
                Register(ctl);
                return true;
            case 215:
                Register(new XConnectNode(this, sym));
                return true;
            case -310:
                Register(new TcpClientNode(this, sym));
                return true;
            case -242:
                Register(new UdpNode(this, sym));
                return true;
            case -200:
                Register(new ComPortNode(this, sym));
                return true;
            case -202:
                Register(new IrPortNode(this, sym));
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Smart Graphics

    /// <summary>Type 268: a Smart Graphics object on a panel. Its joins travel with the object ID (field 44).</summary>
    private sealed class SmartObjectNode(LpzRuntime rt, BinSymbol sym, DeviceNode parent) : Node(rt, sym)
    {
        private readonly JoinSplit _split = new(sym);
        public uint Id { get; } = (uint)(sym.FieldInt(44) ?? 0);
        private readonly string _source = $"{sym.Path} so{(uint)(sym.FieldInt(44) ?? 0)}"; // built once, not per press

        public override SigKind? InputKind(int pin) => _split.InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) => _split.OutJoin(pin).Kind;
        public override void OnInput(int pin, object o, object n) => parent.Device?.SetFeedback(_split.InJoin(pin, Id), n);

        public void PushAll()
        {
            for (int pin = 1; pin <= Symbol.Inputs.Length; pin++)
                if (HasFeedback(Symbol.Inputs[pin - 1], _split.InJoin(pin).Kind)) parent.Device?.SetFeedback(_split.InJoin(pin, Id), Runtime.Value(Symbol.Inputs[pin - 1]));
        }

        public void FromPanel(JoinKey key, object value)
        {
            var pin = _split.OutPin(key with { SmartObject = 0 });
            if (pin < 1) return;
            var sig = Symbol.Outputs[pin - 1];
            if (sig > 1) Runtime._slot.Dispatcher.Post(static (n, v, s) => { var d = (SmartObjectNode)n!; d.Runtime.Drive(s, v!, d._source); }, this, value, sig);
        }
    }

    /// <summary>
    /// A type −12 record under an IP-ID device that carries join bases (fields 27-29 for its inputs, 30-32 for its
    /// outputs, digital/analog/serial): an extender such as a panel's reserved joins, button toolbar or app control.
    /// Pin k of a kind is join base + k on the owning device; a base of 10000 means the record has none of that kind.
    /// Identified by pairing the compiled records with their source extenders in the sample programs: the bases are
    /// Crestron's reserved join ranges (base 17299 = joins from 17300, 18899 = from 18900...).
    /// </summary>
    private sealed class ExtenderNode(LpzRuntime rt, BinSymbol sym, DeviceNode owner) : Node(rt, sym)
    {
        private readonly JoinSplit _split = new(sym);
        private readonly string _source = $"{sym.Path} (IP-ID extender)";

        private uint InBase(SigKind k) => (uint)(Symbol.FieldInt(27 + (int)k) ?? 0);
        private uint OutBase(SigKind k) => (uint)(Symbol.FieldInt(30 + (int)k) ?? 0);

        private JoinKey PanelJoin(int pin)
        {
            var k = _split.InJoin(pin);
            return new JoinKey(k.Kind, InBase(k.Kind) + k.Join);
        }

        public override SigKind? InputKind(int pin) => _split.InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) => _split.OutJoin(pin).Kind;
        public override void OnInput(int pin, object o, object n) => owner.Device?.SetFeedback(PanelJoin(pin), n);

        public void PushAll()
        {
            for (int pin = 1; pin <= Symbol.Inputs.Length; pin++)
                if (HasFeedback(Symbol.Inputs[pin - 1], _split.InJoin(pin).Kind)) owner.Device?.SetFeedback(PanelJoin(pin), Runtime.Value(Symbol.Inputs[pin - 1]));
        }

        /// <summary>A press from the panel: true if the join is one of this extender's.</summary>
        public bool FromPanel(JoinKey key, object value)
        {
            var b = OutBase(key.Kind);
            if (key.Join <= b) return false;
            var pin = _split.OutPin(new JoinKey(key.Kind, key.Join - b));
            if (pin < 1) return false;
            var sig = Symbol.Outputs[pin - 1];
            if (sig > 1) Runtime._slot.Dispatcher.Post(static (n, v, s) => { var d = (ExtenderNode)n!; d.Runtime.Drive(s, v!, d._source); }, this, value, sig);
            return true;
        }
    }

    // ------------------------------------------------------------------ crosspoints

    /// <summary>Type 213, Equipment Crosspoint (ID = P1). Inputs are feedback from the equipment logic,
    /// outputs are presses toward it. Every connected control crosspoint mirrors it join for join.</summary>
    private sealed class XEquipmentNode(LpzRuntime rt, BinSymbol sym) : Node(rt, sym)
    {
        public readonly JoinSplit Split = new(sym);
        public long Id { get; } = sym.Params.Where(p => p.Number != null).Select(p => (long)p.Number!.Value).FirstOrDefault();
        public override SigKind? InputKind(int pin) => Split.InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) => Split.OutJoin(pin).Kind;

        public IEnumerable<XControlNode> Controls => Runtime._xControls.Values.Where(c => c.Connected == this);

        public override void OnInput(int pin, object o, object n)
        {
            var key = Split.InJoin(pin);
            foreach (var c in Controls) c.Feedback(key, n);
        }

        public void Press(JoinKey key, object value) => Runtime.DriveOut(this, Split.OutPin(key), value);
    }

    /// <summary>Type 214, Control Crosspoint (ID = P1). Inputs are presses from a panel, outputs are feedback to it.</summary>
    private sealed class XControlNode(LpzRuntime rt, BinSymbol sym) : Node(rt, sym)
    {
        private readonly JoinSplit _split = new(sym);
        public long Id { get; } = sym.Params.Where(p => p.Number != null).Select(p => (long)p.Number!.Value).FirstOrDefault();
        public XEquipmentNode? Connected { get; private set; }
        public override SigKind? InputKind(int pin) => _split.InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) => _split.OutJoin(pin).Kind;

        public override void OnInput(int pin, object o, object n) => Connected?.Press(_split.InJoin(pin), n);

        public void Feedback(JoinKey key, object value) => Runtime.DriveOut(this, _split.OutPin(key), value);

        public void Connect(XEquipmentNode? equipment)
        {
            if (Connected == equipment) return;
            if (Connected != null)
            {
                // Release held presses and clear feedback.
                for (int pin = 1; pin <= Symbol.Inputs.Length; pin++)
                    if (Runtime.InValue(this, pin) is true) Connected.Press(_split.InJoin(pin), false);
                for (int pin = 1; pin <= Symbol.Outputs.Length; pin++)
                    Runtime.DriveOut(this, pin, SigValue.Default(Runtime.KindOf(Symbol.Outputs[pin - 1])));
            }
            Connected = equipment;
            if (equipment == null) return;
            for (int pin = 1; pin <= equipment.Symbol.Inputs.Length; pin++)
                if (HasFeedback(equipment.Symbol.Inputs[pin - 1], equipment.Split.InJoin(pin).Kind))
                    Feedback(equipment.Split.InJoin(pin), Runtime.InValue(equipment, pin));
        }
    }

    /// <summary>Type 215, Equipment/Control Crosspoint Connect. Inputs: control ID, equipment ID, connect,
    /// disconnect, disconnect the control, disconnect the equipment, disconnect all.</summary>
    private sealed class XConnectNode(LpzRuntime rt, BinSymbol sym) : Node(rt, sym)
    {
        public override SigKind? InputKind(int pin) => pin <= 2 ? SigKind.Analog : SigKind.Digital;
        public override SigKind? OutputKind(int pin) => null;

        public override void OnInput(int pin, object o, object n)
        {
            if (pin <= 2 || !(o is false && n is true)) return;
            var cid = Runtime.InValue(this, 1) is ushort a ? a : 0;
            var eid = Runtime.InValue(this, 2) is ushort b ? b : 0;
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

    // ------------------------------------------------------------------ network devices

    /// <summary>Shared plumbing for nodes that talk over a socket from background threads.</summary>
    private abstract class SocketNode(LpzRuntime rt, BinSymbol sym) : Node(rt, sym)
    {
        protected CancellationTokenSource? Cts;

        protected void Post(Action a) => Runtime._slot.Dispatcher.Post(() => { if (Runtime._running) a(); });
        protected void Log(string level, string msg) => Runtime._slot.Log(level, $"{Symbol.Path}: {msg}");

        protected string Address(int ipId) => Runtime._pkg.IpTable.FirstOrDefault(e => e.IpId == ipId)?.Address ?? "";

        protected int Port => Symbol.Params.Where(p => p.Number != null).Select(p => p.Number!.Value).FirstOrDefault();

        protected static string Latin1(byte[] b, int n) => Encoding.Latin1.GetString(b, 0, n);

        public override void Stop()
        {
            Cts?.Cancel();
            Cts = null;
        }
    }

    /// <summary>
    /// Type −310, TCP/IP Client. Digital inputs connect; the serial input is sent. Outputs: connected,
    /// status (0 not connected, 1 waiting, 2 connected, 3 failed, 4 closed by remote, 5 closed locally),
    /// received text. Address from the IP table, port from P1. Retries every 5 s while connect is high.
    /// </summary>
    private sealed class TcpClientNode(LpzRuntime rt, BinSymbol sym) : SocketNode(rt, sym)
    {
        private readonly JoinSplit _split = new(sym);
        private NetworkStream? _stream;

        public override SigKind? InputKind(int pin) => _split.InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) => _split.OutJoin(pin).Kind;

        private bool Wanted()
        {
            for (int pin = 1; pin <= Symbol.Inputs.Length; pin++)
                if (_split.InJoin(pin).Kind == SigKind.Digital && Runtime.InValue(this, pin) is true) return true;
            return false;
        }

        public override void Start() => Update();

        public override void OnInput(int pin, object o, object n)
        {
            var key = _split.InJoin(pin);
            if (key.Kind == SigKind.Digital) Update();
            else if (key.Kind == SigKind.Serial && n is string s && s.Length > 0)
            {
                var bytes = Encoding.Latin1.GetBytes(s);
                try { _stream?.WriteAsync(bytes); } catch (Exception) { }
            }
        }

        private void Update()
        {
            var want = Wanted();
            if (want && Cts == null) { Cts = new CancellationTokenSource(); _ = Run(Cts.Token); }
            else if (!want && Cts != null) { Stop(); Status(5, false); }
        }

        private void Status(ushort code, bool connected)
        {
            Post(() =>
            {
                Runtime.DriveOut(this, _split.OutPin(new JoinKey(SigKind.Digital, 1)), connected);
                Runtime.DriveOut(this, _split.OutPin(new JoinKey(SigKind.Analog, 1)), code);
            });
        }

        private async Task Run(CancellationToken ct)
        {
            var host = Address(Symbol.IpId ?? Symbol.FieldInt(6) ?? 0);
            var port = Port;
            while (!ct.IsCancellationRequested)
            {
                using var client = new TcpClient();
                try
                {
                    Status(1, false);
                    await client.ConnectAsync(host, port, ct);
                    _stream = client.GetStream();
                    Status(2, true);
                    Log("Info", $"connected to {host}:{port}");
                    var buf = new byte[4096];
                    while (!ct.IsCancellationRequested)
                    {
                        var n = await _stream.ReadAsync(buf, ct);
                        if (n == 0) break;
                        var text = Latin1(buf, n);
                        Post(() => Runtime.DriveOut(this, _split.OutPin(new JoinKey(SigKind.Serial, 1)), text));
                    }
                    if (!ct.IsCancellationRequested) { Status(4, false); Log("Info", $"{host}:{port} closed the connection"); }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    Status(3, false);
                    Log("Warning", $"can't connect to {host}:{port}: {e.Message}");
                }
                finally { _stream = null; }
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Type −242, UDP/IP Communications: input 1 enables, the serial input is sent to the IP table
    /// address on port P1, and datagrams received on that port come out as text.</summary>
    private sealed class UdpNode(LpzRuntime rt, BinSymbol sym) : SocketNode(rt, sym)
    {
        private readonly JoinSplit _split = new(sym);
        private UdpClient? _udp;

        public override SigKind? InputKind(int pin) => _split.InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) => _split.OutJoin(pin).Kind;

        public override void Start() => Update();

        public override void OnInput(int pin, object o, object n)
        {
            var key = _split.InJoin(pin);
            if (key.Kind == SigKind.Digital) Update();
            else if (key.Kind == SigKind.Serial && n is string s && s.Length > 0 && _udp != null)
            {
                var host = Address(Symbol.IpId ?? 0);
                var bytes = Encoding.Latin1.GetBytes(s);
                try { _udp.SendAsync(bytes, bytes.Length, host, Port); } catch (Exception e) { Log("Warning", e.Message); }
            }
        }

        private void Update()
        {
            var enabled = Symbol.Inputs.Length == 0 || Runtime.InValue(this, 1) is true || Symbol.Inputs[0] == 1;
            if (enabled && _udp == null)
            {
                try { _udp = new UdpClient(Port) { EnableBroadcast = true }; }
                catch (SocketException) { _udp = new UdpClient(0) { EnableBroadcast = true }; }
                Cts = new CancellationTokenSource();
                _ = Receive(_udp, Cts.Token);
            }
            else if (!enabled) Stop();
        }

        private async Task Receive(UdpClient udp, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var r = await udp.ReceiveAsync(ct);
                    var text = Latin1(r.Buffer, r.Buffer.Length);
                    Post(() => Runtime.DriveOut(this, _split.OutPin(new JoinKey(SigKind.Serial, 1)), text));
                }
                catch (Exception) { break; }
            }
        }

        public override void Stop()
        {
            base.Stop();
            _udp?.Dispose();
            _udp = null;
        }
    }

    /// <summary>Installs the bridge each new runtime uses for its IR and COM ports. Null (the default) means none:
    /// sends are only logged.</summary>
    public static Func<ProgramSlot, IPortBridge>? PortBridgeFactory { get; set; }

    private IPortBridge? _portBridge;

    /// <summary>What carries this program's IR and COM ports. Replace it before <see cref="Start"/>.</summary>
    public IPortBridge PortBridge
    {
        get => _portBridge ??= new SlotPortBridge(_slot, PortBridgeFactory?.Invoke(_slot));
        set => _portBridge = value;
    }

    private readonly List<PortInfo> _ports = new();

    /// <summary>The IR and COM ports the program uses, in program order.</summary>
    public IReadOnlyList<PortInfo> Ports => _ports;

    /// <summary>
    /// Type −202, an IR port driven by "Crestron DB IR-controlled device" symbols. Each record is one such
    /// symbol on port field 6 + 1. Inputs 1 and 2 are fixed (constants 1 and 0 in every record seen); input
    /// 2 + k is the trigger for params[k], an index into the driver commands the program uses (only wired pins are
    /// compiled, so they are not simply 0..n). A virtual processor has no IR emitter: a rising trigger is
    /// logged and handed to the port bridge.
    /// </summary>
    private sealed class IrPortNode : Node
    {
        private readonly PortInfo _port;
        private readonly int _first; // first command pin; params line up with the pins from here

        public IrPortNode(LpzRuntime rt, BinSymbol sym) : base(rt, sym)
        {
            _port = PortInfo.From(PortKind.Ir, sym, rt._pkg.Logic.Symbols);
            _first = Math.Max(0, sym.Inputs.Length - sym.Params.Count) + 1;
            rt._ports.Add(_port);
        }

        public override SigKind? InputKind(int pin) => SigKind.Digital;
        public override SigKind? OutputKind(int pin) => null;

        public override void OnInput(int pin, object o, object n)
        {
            if (pin < _first || !(o is false && n is true)) return;
            var k = pin - _first;
            if (k >= Symbol.Params.Count) return;
            var cmd = (int)(Symbol.Params[k].Number ?? k);
            var name = Runtime.NameOf(Symbol.Inputs[pin - 1]);
            Runtime._slot.Log("Info", $"{_port.Label}: {name} (cmd {cmd})");
            try { Runtime.PortBridge.SendIr(_port, cmd, name); }
            catch (Exception e) { Runtime._slot.Log("Warning", $"{_port.Label}: port bridge: {e.Message}"); }
        }
    }

    /// <summary>
    /// Type −200, COM port: input 1 is the serial text to send (input 2 is a constant 1 in every record seen),
    /// output 1 is the text received. The port number is field 6 + 1, and the record sits under its device's
    /// card slot (IP-ID-11 → Slot-08 → COM-01). Port settings (baud, parity) aren't in the record. A virtual
    /// processor has no serial ports: every send is logged and handed to the port bridge, and text can be
    /// injected by forcing the receive signal in the debugger. A port can also be bridged to a serial device
    /// server: comports.json in the program folder (or the data folder) maps the port's path to "host:port",
    /// e.g. { "IP-ID-11/Slot-08/COM-01": "192.168.1.50:4999" }. The bare name "COM-01" works too, but every
    /// port of that number shares it.
    /// </summary>
    private sealed class ComPortNode : SocketNode
    {
        private NetworkStream? _stream;
        private readonly PortInfo _port;

        public ComPortNode(LpzRuntime rt, BinSymbol sym) : base(rt, sym)
        {
            _port = PortInfo.From(PortKind.Com, sym, rt._pkg.Logic.Symbols);
            rt._ports.Add(_port);
        }

        public override SigKind? InputKind(int pin) => pin == 1 ? SigKind.Serial : SigKind.Digital;
        public override SigKind? OutputKind(int pin) => pin == 1 ? SigKind.Serial : null;
        public override bool WatchesOutputs => true;

        public override void Stop()
        {
            base.Stop();
            try { Runtime.PortBridge.Close(_port); } catch (Exception) { }
        }

        public override void Start()
        {
            try { Runtime.PortBridge.Open(_port, text => Post(() => Runtime.DriveOut(this, 1, text))); }
            catch (Exception e) { Log("Warning", $"port bridge: {e.Message}"); }
            var target = Lookup();
            if (target == null) return;
            var colon = target.LastIndexOf(':');
            if (colon < 1 || !int.TryParse(target[(colon + 1)..], out var port)) { Log("Warning", $"bad comports.json entry '{target}'"); return; }
            Cts = new CancellationTokenSource();
            _ = Run(target[..colon], port, Cts.Token);
        }

        private string? Lookup()
        {
            foreach (var dir in new[] { Runtime._slot.Directory, Runtime._slot.Processor.DataDirectory })
            {
                var f = Path.Combine(dir, "comports.json");
                if (!File.Exists(f)) continue;
                try
                {
                    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f));
                    if (map != null && (map.TryGetValue(_port.Path, out var t) || map.TryGetValue(Symbol.Path, out t))) return t;
                }
                catch (Exception e) { Log("Warning", $"comports.json: {e.Message}"); }
            }
            return null;
        }

        public override void OnInput(int pin, object o, object n)
        {
            if (n is not string s || s.Length == 0) return;
            if (pin < 0) // our own receive signal changed: from the bridge, a socket or a value forced in the debugger
            {
                Runtime._slot.Log("Info", $"{_port.Label} RX ({s.Length}): \"{PortInfo.Printable(s)}\"");
                return;
            }
            if (pin != 1) return;
            Runtime._slot.Log("Info", $"{_port.Label} TX ({s.Length}): \"{PortInfo.Printable(s)}\"");
            try { Runtime.PortBridge.SendCom(_port, s); }
            catch (Exception e) { Log("Warning", $"port bridge: {e.Message}"); }
            try { _stream?.WriteAsync(Encoding.Latin1.GetBytes(s)); } catch (Exception) { }
        }

        private async Task Run(string host, int port, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(host, port, ct);
                    _stream = client.GetStream();
                    Log("Info", $"bridged to {host}:{port}");
                    var buf = new byte[4096];
                    while (true)
                    {
                        var n = await _stream.ReadAsync(buf, ct);
                        if (n == 0) break;
                        var text = Latin1(buf, n);
                        Runtime._slot.Io.Ports.Find(_port.Path)?.RecordRx(text, "serial device server");
                        Post(() => Runtime.DriveOut(this, 1, text));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Log("Warning", $"bridge {host}:{port}: {e.Message}"); }
                finally { _stream = null; }
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
