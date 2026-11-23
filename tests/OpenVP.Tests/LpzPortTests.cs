using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>IR (type -202) and COM (type -200) port records, built the way compiled programs lay them out.</summary>
public static class LpzPortTests
{
    // Signals
    const int KeyA = 10, KeyB = 11, KeyC = 12, Tx = 13, Rx = 14, Fb = 15, Tx2 = 16, Rx2 = 17;

    private sealed class RecordingBridge : IPortBridge
    {
        public readonly List<string> Calls = new();
        public readonly Dictionary<string, Action<string>> Receive = new();
        public void Open(PortInfo port, Action<string> received) { lock (Calls) Calls.Add($"open {port.Path}"); Receive[port.Path] = received; }
        public void Close(PortInfo port) { lock (Calls) Calls.Add($"close {port.Path}"); }
        public void SendCom(PortInfo port, string data) { lock (Calls) Calls.Add($"com {port.Path} {data}"); }
        public void SendIr(PortInfo port, int command, string name) { lock (Calls) Calls.Add($"ir {port.Path} {command} {name}"); }
        public List<string> Snapshot() { lock (Calls) return Calls.ToList(); }
    }

    // Record index = position in the file: 0 header, 1-2 processor slots, 3 IP-ID-11, 4 its card slot 8, 5 COM-01,
    // 6 IP-ID-31, 7 its IR card slot, 8 Port-01, 9 processor Slot-06, 10-11 Port-01 / Port-02 under it, 12 panel.
    internal static string Build()
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: ports.smw")));
        bin.AddRange(Record(-204, "Slot-01", [], [], Int(6, 1)));
        bin.AddRange(Record(-205, "Slot-02", [], [], Int(6, 2)));
        bin.AddRange(Record(-12, "IP-ID-11", [], [], Int(6, 17), Int(7, 2), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0)));
        bin.AddRange(Record(-201, "Slot-08", [], [], Int(6, 8), Int(7, 3)));
        bin.AddRange(Record(-200, "COM-01", [Tx, 1], [Rx], Int(15, 2), Int(6, 0), Int(7, 4)));
        bin.AddRange(Record(-12, "IP-ID-31", [], [], Int(6, 49), Int(7, 2), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0)));
        bin.AddRange(Record(-203, "Slot-03", [], [], Int(6, 3), Int(7, 6)));
        // An IR symbol that wired only two of its pins: params are the driver's command numbers.
        bin.AddRange(Record(-202, "Port-01", [1, 0, KeyA, KeyB], [], Int(15, 4), Int(6, 0), Int(7, 7), Int(2, 3), Int(2, 22)));
        bin.AddRange(Record(-203, "Slot-06", [], [], Int(6, 6), Int(7, 0)));
        bin.AddRange(Record(-202, "Port-01", [1, 0, KeyC], [], Int(15, 3), Int(6, 0), Int(7, 9), Int(2, 0)));
        bin.AddRange(Record(-202, "Port-02", [1, 0, KeyC], [], Int(15, 3), Int(6, 1), Int(7, 9), Int(2, 7)));
        // Second COM port on the same device, to show port paths stay apart.
        bin.AddRange(Record(-200, "COM-02", [Tx2, 1], [Rx2], Int(15, 2), Int(6, 1), Int(7, 4)));
        // Panel IPID 03 shows RX as serial feedback s1 and s2.
        bin.AddRange(Record(-12, "IP-ID-03", [Rx, Rx2], [], Int(6, 3), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0)));

        var path = Path.Combine(Paths.TempData(), "ports.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=ports.bin\r\nSMWFileName=ports.smw\r\nRackType=CP4\r\n"));
        Put("ports.bin", bin.ToArray());
        Put("ports.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\n"));
        return path;
    }

    private static (Processor p, LpzRuntime rt, RecordingBridge bridge) Start()
    {
        var bridge = new RecordingBridge();
        var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        LpzRuntime.PortBridgeFactory = _ => bridge;
        try { p.LoadProgram(1, Build()); }
        finally { LpzRuntime.PortBridgeFactory = null; }
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        return (p, (LpzRuntime)p.Slot(1).Runtime!, bridge);
    }

    private static int Count(Processor p, string text) => p.RecentLog(1, 500).Count(l => l.Message.Contains(text));

    [Test]
    public static void Ports_are_known_and_nothing_is_unsupported()
    {
        var (p, rt, _) = Start();
        using var _ = p;
        Assert.Equal(0, rt.Unsupported.Count, "no unsupported record types: " + string.Join(",", rt.Unsupported.Keys));
        Assert.True(PrimitiveCatalog.Known[-200].Implemented && PrimitiveCatalog.Known[-202].Implemented, "catalog says both run");
        Assert.True(!LpzReport.Build(rt.Package).Contains("identified"), "report lists neither as 'identified'");
        var paths = rt.Ports.Select(x => x.Path).ToList();
        Assert.True(paths.Contains("IP-ID-11/Slot-08/COM-01") && paths.Contains("IP-ID-11/Slot-08/COM-02"), "COM ports sit under their device: " + string.Join(", ", paths));
        Assert.True(paths.Contains("IP-ID-31/Slot-03/Port-01") && paths.Contains("Slot-06/Port-02"), "IR ports: " + string.Join(", ", paths));
    }

    [Test]
    public static void Ir_rising_input_logs_command_once_and_reaches_the_bridge()
    {
        var (p, rt, bridge) = Start();
        using var _ = p;
        var slot = p.Slot(1);

        slot.Runtime!.TrySetSignal("#10", true);                       // KeyA: params[0] = 3
        Assert.Eventually(() => Count(p, "IR IPID 31 port 1: #10 (cmd 3)") == 1, "IR line with device, port, signal and command");
        slot.Runtime!.TrySetSignal("#10", false);
        slot.Runtime!.TrySetSignal("#11", true);                       // KeyB: params[1] = 22, not 1
        Assert.Eventually(() => Count(p, "IR IPID 31 port 1: #11 (cmd 22)") == 1, "second command uses the compiled command number");
        Thread.Sleep(30);
        Assert.Equal(1, Count(p, "(cmd 3)"), "falling edge and the other key did not log again");

        slot.Runtime!.TrySetSignal("#12", true);                       // KeyC feeds two processor ports
        Assert.Eventually(() => Count(p, "IR processor slot 6 port 1: #12 (cmd 0)") == 1 && Count(p, "IR processor slot 6 port 2: #12 (cmd 7)") == 1,
            "processor IR ports are labelled by slot");
        var calls = bridge.Snapshot();
        Assert.True(calls.Contains("ir IP-ID-31/Slot-03/Port-01 3 #10") && calls.Contains("ir Slot-06/Port-02 7 #12"), "bridge saw the sends: " + string.Join(" | ", calls));
        Assert.Equal(4, calls.Count(c => c.StartsWith("ir ")), "one bridge call per send");
    }

    [Test]
    public static void Com_tx_is_logged_and_rx_can_be_forced()
    {
        var (p, rt, bridge) = Start();
        using var _ = p;
        var slot = p.Slot(1);
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel 03");
        Assert.True(bridge.Snapshot().Contains("open IP-ID-11/Slot-08/COM-01"), "ports opened on the bridge at start");

        slot.Runtime!.TrySetSignal("#13", "PWR ON\r");
        Assert.Eventually(() => Count(p, "COM IPID 11 port 1 TX (7): \"PWR ON\\x0D\"") == 1, "TX logged with escapes");
        slot.Runtime!.TrySetSignal("#13", "PWR ON\r");                 // same text again is a new send
        Assert.Eventually(() => Count(p, "COM IPID 11 port 1 TX") == 2, "serial resend logs again");
        Assert.True(bridge.Snapshot().Contains("com IP-ID-11/Slot-08/COM-01 PWR ON\r"), "bridge got the text");

        slot.Runtime!.TrySetSignal("#14", "OK\r\n");                   // as if forced in the debugger
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 1)) == "OK\r\n", "forced RX reaches the logic");
        Assert.Eventually(() => Count(p, "COM IPID 11 port 1 RX (4): \"OK\\x0D\\x0A\"") == 1, "RX logged");

        bridge.Receive["IP-ID-11/Slot-08/COM-01"]("from bridge");                                // a backend delivering data
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 1)) == "from bridge", "bridge RX reaches the logic");

        slot.Runtime!.TrySetSignal("#16", "X");
        Assert.Eventually(() => Count(p, "COM IPID 11 port 2 TX") == 1, "second port is numbered");
    }

    [Test]
    public static void Printable_escapes_control_characters()
    {
        Assert.Equal("a\\x0D\\x0Ab\\\\\\\"", PortInfo.Printable("a\r\nb\\\""), "escapes");
    }
}
