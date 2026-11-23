namespace OpenVP.Core;

/// <summary>
/// A starting point for a simulator. These are generic, made-up plain-text protocols of the kind small AV devices commonly use
/// (a command, a space, a value, a carriage return). They are not any manufacturer's protocol: copy one and edit its rules to
/// match your device's documentation.
/// </summary>
public sealed record SimProfile(string Id, string Name, string Description, Func<SimDef> Create);

public static class SimProfiles
{
    public static IReadOnlyList<SimProfile> All { get; } = new[]
    {
        new SimProfile("projector", "Generic projector",
            "Power on/off/query, input select and lamp hours. Commands end with \\r: PWR ON, PWR OFF, PWR?, INPUT HDMI1, INPUT?, LAMP?.", Projector),
        new SimProfile("display", "Generic display",
            "Power, input, volume and mute. POWER ON/OFF/?, INPUT n, INPUT?, VOL n, VOL?, MUTE ON/OFF, MUTE?.", Display),
        new SimProfile("dsp", "Generic DSP",
            "Volume and mute on the named levels Program, Mic1, Mic2 and Aux. SET Program VOL 60, GET Mic1 MUTE.", Dsp),
        new SimProfile("switcher", "Generic video switcher",
            "A 4x4 matrix. ROUTE 2 3 sends input 2 to output 3, ROUTE? 3 asks what output 3 shows, STATUS lists all outputs.", Switcher),
    };

    public static SimDef? Create(string id) => All.FirstOrDefault(p => p.Id == id)?.Create();

    private static SimRule R(string name, string match, string reply, int delay = 50, bool regex = false, params (string, string)[] set) =>
        new() { Name = name, Match = match, Regex = regex, IgnoreCase = true, Reply = reply, DelayMs = delay, Set = set.ToDictionary(s => s.Item1, s => s.Item2) };

    private static SimRule Unknown() => R("Anything else", @"[^\r\n]*[\r\n]+", "ERR\\r", 20, true);

    private static SimDef Projector() => new()
    {
        Profile = "projector",
        Name = "Generic projector",
        Kind = "com",
        State = new() { ["power"] = "0", ["input"] = "HDMI1", ["lamp"] = "1234" },
        Rules =
        {
            R("Power on", "PWR ON\\r", "PWR=1\\r", 50, false, ("power", "1")),
            R("Power off", "PWR OFF\\r", "PWR=0\\r", 50, false, ("power", "0")),
            R("Power query", "PWR?\\r", "PWR={power}\\r"),
            R("Select input", @"INPUT (\w+)\r", "INPUT={1}\\r", 50, true, ("input", "{1}")),
            R("Input query", "INPUT?\\r", "INPUT={input}\\r"),
            R("Lamp hours", "LAMP?\\r", "LAMP={lamp}\\r"),
            Unknown(),
        },
    };

    private static SimDef Display() => new()
    {
        Profile = "display",
        Name = "Generic display",
        Kind = "com",
        State = new() { ["power"] = "OFF", ["input"] = "1", ["vol"] = "20", ["mute"] = "OFF" },
        Rules =
        {
            R("Power on", "POWER ON\\r", "POWER=ON\\r", 50, false, ("power", "ON")),
            R("Power off", "POWER OFF\\r", "POWER=OFF\\r", 50, false, ("power", "OFF")),
            R("Power query", "POWER?\\r", "POWER={power}\\r"),
            R("Select input", @"INPUT (\d+)\r", "INPUT={1}\\r", 50, true, ("input", "{1}")),
            R("Input query", "INPUT?\\r", "INPUT={input}\\r"),
            R("Set volume", @"VOL (\d+)\r", "VOL={1}\\r", 50, true, ("vol", "{1}")),
            R("Volume query", "VOL?\\r", "VOL={vol}\\r"),
            R("Mute on", "MUTE ON\\r", "MUTE=ON\\r", 50, false, ("mute", "ON")),
            R("Mute off", "MUTE OFF\\r", "MUTE=OFF\\r", 50, false, ("mute", "OFF")),
            R("Mute query", "MUTE?\\r", "MUTE={mute}\\r"),
            Unknown(),
        },
    };

    private static SimDef Dsp()
    {
        var names = "(Program|Mic1|Mic2|Aux)";
        var def = new SimDef
        {
            Profile = "dsp",
            Name = "Generic DSP",
            Kind = "com",
            State = new()
            {
                ["Program.vol"] = "50", ["Program.mute"] = "0", ["Mic1.vol"] = "40", ["Mic1.mute"] = "0",
                ["Mic2.vol"] = "40", ["Mic2.mute"] = "0", ["Aux.vol"] = "30", ["Aux.mute"] = "0",
            },
        };
        def.Rules.Add(R("Set level", $@"SET {names} VOL (-?\d+)\r", "{1} VOL {2}\\r", 50, true, ("{1}.vol", "{2}")));
        def.Rules.Add(R("Set mute", $@"SET {names} MUTE ([01])\r", "{1} MUTE {2}\\r", 50, true, ("{1}.mute", "{2}")));
        def.Rules.Add(R("Level up", $@"UP {names}\r", "{1} VOL {{1}.vol}\\r", 50, true, ("{1}.vol", "{{1}.vol}+5")));
        def.Rules.Add(R("Level down", $@"DOWN {names}\r", "{1} VOL {{1}.vol}\\r", 50, true, ("{1}.vol", "{{1}.vol}-5")));
        def.Rules.Add(R("Get level", $@"GET {names} VOL\r", "{1} VOL {{1}.vol}\\r", 50, true));
        def.Rules.Add(R("Get mute", $@"GET {names} MUTE\r", "{1} MUTE {{1}.mute}\\r", 50, true));
        def.Rules.Add(Unknown());
        return def;
    }

    private static SimDef Switcher()
    {
        var def = new SimDef
        {
            Profile = "switcher",
            Name = "Generic video switcher",
            Kind = "com",
            State = new() { ["out1"] = "1", ["out2"] = "2", ["out3"] = "3", ["out4"] = "4" },
        };
        def.Rules.Add(R("Route", @"ROUTE ([1-4]) ([1-4])\r", "ROUTE {1} {2} OK\\r", 50, true, ("out{2}", "{1}")));
        def.Rules.Add(R("Route query", @"ROUTE\? ([1-4])\r", "OUT{1}={out{1}}\\r", 50, true));
        def.Rules.Add(R("Status", "STATUS\\r", "OUT1={out1} OUT2={out2} OUT3={out3} OUT4={out4}\\r"));
        def.Rules.Add(Unknown());
        return def;
    }
}
