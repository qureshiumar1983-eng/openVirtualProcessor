using OpenVP.Core;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>.smw symbols that hang off a panel (Smart Graphics objects, reserved-join extenders) and crosspoints.</summary>
public static class SimplPanelChildTests
{
    // Panel 03 (symbol 5): d2 press → 50 (control crosspoint in), d3 press → 62 (connect), feedback d2 ← 51.
    // Smart object 3 (symbol 6) and a TSW-1070 Button Toolbar (symbol 7, joins from 30201) loop each press back
    // as feedback. Equipment crosspoint 501 loops its press back as feedback; control crosspoint 201 is wired
    // to the panel. Analog Initialize gives the connect symbol control 201 / equipment 501 at start.
    private const string Program =
        "[\nObjTp=Hd\nPgmNm=PanelChildren\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n" +
        "[\nObjTp=Sm\nH=5\nSmC=6840\nNm=XPanel\nDvH=589\nn1I=2\nn1O=3\nmI=2\nmO=3\nI2=51\nO1=10\nO2=50\nO3=62\n]\n" +
        "[\nObjTp=Sm\nH=6\nSmC=2942\nNm=Keypad.ced\nPrH=5\nn1I=2\nn1O=2\nmI=2\nmO=2\nI1=20\nI2=21\nO1=20\nO2=21\nmP=1\nP1=3d\nCedH=1\n]\n" +
        "[\nObjTp=Sm\nH=7\nSmC=6862\nNm=TSW-1070 Button Toolbar\nPrH=5\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=30\nO1=30\n]\n" +
        "[\nObjTp=Sm\nH=8\nSmC=446\nNm=Equipment Crosspoint Routing.\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=41\nO1=41\nmP=1\nP1=501d\n]\n" +
        "[\nObjTp=Sm\nH=9\nSmC=447\nNm=Control Crosspoint Routing.\nn1I=1\nn1O=1\nmI=1\nmO=1\nI1=50\nO1=51\nmP=1\nP1=201d\n]\n" +
        "[\nObjTp=Sm\nH=10\nSmC=448\nNm=Equipment/Control Crosspoint Connect.\nn1I=3\nmI=3\nI1=60\nI2=61\nI3=62\n]\n" +
        "[\nObjTp=Sm\nH=11\nSmC=32\nNm=Analog Initialize\nmI=1\nI1=2\nmO=2\nO1=60\nO2=61\nmP=2\nP1=201d\nP2=501d\n]\n";

    private static (Processor p, JoinDevice panel) Start()
    {
        var path = Path.Combine(Paths.TempData(), "children.smw");
        File.WriteAllText(path, Program);
        File.WriteAllText(Path.ChangeExtension(path, ".openvp.json"), "{ \"ipids\": { \"5\": \"03\" } }");
        var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        Assert.Equal(SlotState.Running, p.Slot(1).State, "running: " + p.Slot(1).LastError);
        Assert.Equal(0, ((SimplRuntime)p.Slot(1).Runtime!).Unsupported.Count, "nothing unsupported: " +
            string.Join(", ", ((SimplRuntime)p.Slot(1).Runtime!).Unsupported));
        return (p, p.Slot(1).FindDevice(3) ?? throw new AssertException("panel 03"));
    }

    [Test]
    public static void Smart_object_joins_travel_with_the_object_id()
    {
        var (p, panel) = Start();
        using var _ = p;
        var so = new JoinKey(SigKind.Digital, 2, 3);
        panel.ReceiveInput(so, true);
        Assert.Eventually(() => (bool)panel.GetFeedback(so), "so3.d2 press comes back as so3.d2 feedback");
        Assert.True(!(bool)panel.GetFeedback(new JoinKey(SigKind.Digital, 2)), "the panel's own d2 is untouched");
        panel.ReceiveInput(so, false);
        Assert.Eventually(() => !(bool)panel.GetFeedback(so), "release");
    }

    [Test]
    public static void Button_toolbar_sits_at_its_reserved_joins()
    {
        var (p, panel) = Start();
        using var _ = p;
        var j = new JoinKey(SigKind.Digital, 30201);
        panel.ReceiveInput(j, true);
        Assert.Eventually(() => (bool)panel.GetFeedback(j), "d30201 press comes back as d30201 feedback");
    }

    [Test]
    public static void Crosspoints_route_once_connected()
    {
        var (p, panel) = Start();
        using var _ = p;
        var d2 = new JoinKey(SigKind.Digital, 2);
        panel.ReceiveInput(d2, true);
        Thread.Sleep(50);
        Assert.True(!(bool)panel.GetFeedback(d2), "not connected yet: nothing comes back");
        panel.ReceiveInput(d2, false);

        panel.ReceiveInput(new JoinKey(SigKind.Digital, 3), true);   // connect control 201 to equipment 501
        panel.ReceiveInput(d2, true);
        Assert.Eventually(() => (bool)panel.GetFeedback(d2), "press reaches the equipment and its feedback comes back");
        panel.ReceiveInput(d2, false);
        Assert.Eventually(() => !(bool)panel.GetFeedback(d2), "release follows");
    }
}
