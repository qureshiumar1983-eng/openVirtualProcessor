using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>COM and IR port symbols in SIMPL Windows source, logged and handed to the port bridge like compiled programs' ports.</summary>
public static class SimplPortTests
{
    private sealed class RecordingBridge : IPortBridge
    {
        public readonly List<string> Calls = new();
        public Action<string>? Receive;
        public void Open(PortInfo port, Action<string> received) { lock (Calls) Calls.Add($"open {port.Path}"); Receive = received; }
        public void Close(PortInfo port) { }
        public void SendCom(PortInfo port, string data) { lock (Calls) Calls.Add($"com {port.Path} {data}"); }
        public void SendIr(PortInfo port, int command, string name) { lock (Calls) Calls.Add($"ir {port.Path} {command} {name}"); }
        public List<string> Snapshot() { lock (Calls) return Calls.ToList(); }
    }

    // Processor COM port 1 (TX "tx", RX "rx") and an IR device on port 2 with POWER and MUTE wired to "power", "mute".
    private const string Program =
        "[\nObjTp=Hd\nPgmNm=Ports\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
        "[\nObjTp=Sg\nH=20\nNm=tx\n]\n[\nObjTp=Sg\nH=21\nNm=rx\n]\n[\nObjTp=Sg\nH=30\nNm=power\n]\n[\nObjTp=Sg\nH=31\nNm=mute\n]\n" +
        "[\nObjTp=Sm\nH=8\nSmC=6581\nNm=CP4 COM\nDvH=516\nPrH=1\n]\n" +
        "[\nObjTp=Sm\nH=9\nSmC=6301\nNm=COM 2-Way Serial Driver\nDvH=518\nPrH=8\nn1I=5\nn1O=3\nmI=5\nmO=3\nI1=20\nO1=21\n]\n" +
        "[\nObjTp=Dv\nNm=COM 2-Way Serial Driver\nH=518\nPrH=517\nAd=01\nSmH=9\n]\n" +
        "[\nObjTp=Sm\nH=10\nSmC=107\nNm=Crestron DB IR-controlled device\nDvH=519\nPrH=8\nn1I=3\nmI=3\nI2=30\nI3=31\nmP=2\nP1=POWER\nP2=MUTE\n]\n" +
        "[\nObjTp=Dv\nNm=IR Port\nH=519\nPrH=517\nAd=02\nSmH=10\n]\n";

    [Test]
    public static void Com_and_ir_ports_are_logged_and_reach_the_bridge()
    {
        var path = Path.Combine(Paths.TempData(), "ports.smw");
        File.WriteAllText(path, Program);
        var bridge = new RecordingBridge();
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        LpzRuntime.PortBridgeFactory = _ => bridge;
        try { p.LoadProgram(1, path); }
        finally { LpzRuntime.PortBridgeFactory = null; }
        var slot = p.Slot(1);
        Assert.Equal(SlotState.Running, slot.State, "running: " + slot.LastError);
        var rt = (SimplRuntime)slot.Runtime!;
        Assert.Equal(0, rt.Unsupported.Count, "nothing unsupported");
        Assert.True(rt.Ports.Any(x => x.Path == "CP4 COM/COM-01") && rt.Ports.Any(x => x.Path == "CP4 COM/IR-02"),
            "ports named by their parent and number: " + string.Join(", ", rt.Ports.Select(x => x.Path)));

        rt.TrySetSignal("tx", "PWR ON\r");
        Assert.Eventually(() => bridge.Snapshot().Contains("com CP4 COM/COM-01 PWR ON\r"), "TX reaches the bridge");
        Assert.True(p.RecentLog(1, 200).Any(l => l.Message.Contains("TX (7): \"PWR ON\\x0D\"")), "TX logged with escapes");

        bridge.Receive!("OK\r");
        Assert.Eventually(() => p.RecentLog(1, 200).Any(l => l.Message.Contains("RX (3): \"OK\\x0D\"")), "bridge text drives RX and is logged");

        rt.TrySetSignal("mute", true);
        Assert.Eventually(() => bridge.Snapshot().Contains("ir CP4 COM/IR-02 1 MUTE"), "IR command by its index and name");
        Assert.True(!bridge.Snapshot().Any(c => c.Contains("POWER")), "only the pressed command fires");
    }
}
