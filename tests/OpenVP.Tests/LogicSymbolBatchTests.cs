using OpenVP.Core;
using OpenVP.Lpz;
using static OpenVP.Tests.SymbolCatalogTests;

namespace OpenVP.Tests;

/// <summary>The documented symbols added after the first catalog (math, delays, counters, hex conversions, string
/// compares, Serial RAM, multiplexers, time split/join), run through the .smw interpreter by library name.</summary>
public static class LogicSymbolBatchTests
{
    private static void Press(Smw t, string name) { t.Set(name, true); t.Set(name, false); }

    [Test]
    public static void Analog_math_wraps_skips_unwired_pins_and_divides_safely()
    {
        using var t = new Smw()
            .Sym("Analog Subtract", ["a", "b"], ["sub"])
            .Sym("Analog Multiplier", ["a", "b", ""], ["mul"])     // the unwired third pin is skipped, not multiplied in as 0
            .Sym("Analog Divider", ["a", "b"], ["div"])
            .Sym("Analog Minimum", ["a", "b", ""], ["min"])
            .Sym("Analog Maximum", ["a", "b", ""], ["max"])
            .Sym("Analog Average", ["a", "b"], ["avg"])
            .Run();
        Assert.Equal((ushort)0, t.A("sub"), "0 - 0");
        t.Set("a", (ushort)3);
        t.Set("b", (ushort)5);
        Assert.Equal((ushort)65534, t.A("sub"), "3 - 5 wraps at 16 bits");
        Assert.Equal((ushort)15, t.A("mul"), "3 * 5");
        Assert.Equal((ushort)0, t.A("div"), "3 / 5 = 0");
        Assert.Equal((ushort)3, t.A("min"), "min ignores the unwired pin");
        Assert.Equal((ushort)5, t.A("max"), "max");
        Assert.Equal((ushort)4, t.A("avg"), "(3 + 5) / 2");
        t.Set("a", (ushort)300);
        t.Set("b", (ushort)300);
        Assert.Equal((ushort)(90000 & 0xFFFF), t.A("mul"), "300 * 300 keeps the low 16 bits");
        Assert.Equal((ushort)1, t.A("div"), "300 / 300");
        t.Set("a", (ushort)65535);
        t.Set("b", (ushort)2);
        Assert.Equal((ushort)32767, t.A("div"), "integer division rounds down");
        Assert.Equal((ushort)32768, t.A("avg"), "(65535 + 2) / 2 does not overflow 16 bits");
        t.Set("b", (ushort)0);
        Assert.Equal((ushort)0, t.A("div"), "divide by zero gives 0");
    }

    [Test]
    public static void Analog_and_serial_delay_pass_every_change_later()
    {
        using var t = new Smw()
            .Sym("Analog Delay", ["a"], ["a_late"], "0.2s")
            .Sym("Analog Delay", ["a"], ["a_now"])             // no time: straight through
            .Sym("Serial Delay", ["s"], ["s_late"], "0.2s")
            .Run();
        t.Set("a", (ushort)7);
        t.Set("s", "hi");
        Assert.Equal((ushort)7, t.A("a_now"), "delay 0 passes at once");
        Assert.Equal((ushort)0, t.A("a_late"), "analog not there yet");
        Assert.Equal("", t.S("s_late"), "serial not there yet");
        Assert.Eventually(() => t.A("a_late") == 7 && t.S("s_late") == "hi", "both arrive after the delay");
        t.Set("a", (ushort)9);
        Assert.Equal((ushort)7, t.A("a_late"), "still the old value");
        Assert.Eventually(() => t.A("a_late") == 9, "the new value arrives");
    }

    [Test]
    public static void Up_down_counter_stops_at_limits_and_resets()
    {
        using var t = new Smw()
            .Sym("Up/Down Counter", ["up", "down", "rst"], ["n"], "0d", "3d", "1d")
            .Sym("Analog Counter", ["up", "down", ""], ["wide"])   // defaults: 0 to 65535 from 0
            .Run();
        Assert.Equal((ushort)1, t.A("n"), "starts at the initial value");
        Assert.Equal((ushort)0, t.A("wide"), "default start is 0");
        Press(t, "up");
        Press(t, "up");
        Assert.Equal((ushort)3, t.A("n"), "1 -> 3");
        Press(t, "up");
        Assert.Equal((ushort)3, t.A("n"), "stops at the highest value");
        Assert.Equal((ushort)3, t.A("wide"), "the other counter counts the same presses");
        t.Set("up", true);
        t.Set("up", true);
        Assert.Equal((ushort)3, t.A("n"), "a held input counts once, on its rising edge");
        t.Set("up", false);
        Press(t, "down");
        Assert.Equal((ushort)2, t.A("n"), "down");
        t.Set("rst", true);
        Assert.Equal((ushort)1, t.A("n"), "reset goes to the initial value");
        Press(t, "up");
        Assert.Equal((ushort)1, t.A("n"), "counting is ignored while reset is held");
        t.Set("rst", false);
        Press(t, "down");
        Press(t, "down");
        Assert.Equal((ushort)0, t.A("n"), "stops at the lowest value");
        // The unlimited counter has no reset wired: 3 + 1 (held up) + 1 (up while the other was in reset) - 1 (down) - 2 (downs).
        Assert.Equal((ushort)2, t.A("wide"), "the other counter ignores the first one's reset");
    }

    [Test]
    public static void Hex_and_ascii_conversions()
    {
        using var t = new Smw()
            .Sym("Serial to Hex", ["text"], ["hex"])
            .Sym("Hex to Serial", ["hexin"], ["chars"])
            .Sym("Analog to Hex", ["n"], ["h4"])
            .Sym("Analog to Hex", ["n"], ["h2"], "2d")
            .Sym("Hex to Analog", ["hexnum"], ["value"])
            .Run();
        t.Set("text", "AB\r");
        Assert.Equal("41420D", t.S("hex"), "two upper-case digits per character");
        t.Set("hexin", "414a0d");
        Assert.Equal("AJ\r", t.S("chars"), "either case, pairs of digits");
        t.Set("hexin", "41G2");
        Assert.Equal("A", t.S("chars"), "stops at the first non-hex pair");
        t.Set("hexin", "414");
        Assert.Equal("A", t.S("chars"), "a lone last digit is dropped");
        t.Set("n", (ushort)255);
        Assert.Equal("00FF", t.S("h4"), "zero padded to 4 digits");
        Assert.Equal("FF", t.S("h2"), "2 digits");
        t.Set("n", (ushort)0x1234);
        Assert.Equal("1234", t.S("h4"), "full width");
        Assert.Equal("34", t.S("h2"), "too big for the width keeps the low digits");
        t.Set("hexnum", "ff");
        Assert.Equal((ushort)255, t.A("value"), "ff");
        t.Set("hexnum", "0x1F40");
        Assert.Equal((ushort)8000, t.A("value"), "0x prefix");
        t.Set("hexnum", "12345");
        Assert.Equal((ushort)0x2345, t.A("value"), "more than 4 digits keeps the low 16 bits");
        t.Set("hexnum", "zz");
        Assert.Equal((ushort)0, t.A("value"), "not hex");
        t.Set("hexnum", "");
        Assert.Equal((ushort)0, t.A("value"), "empty");
    }

    [Test]
    public static void Serial_equate_and_compare()
    {
        using var t = new Smw()
            .Sym("Serial Equate", ["s", ""], ["is_on", "is_off", "is_empty"], "\"ON\"", "\"OFF\"", "\"\"")
            .Sym("Serial Equate", ["s", "en"], ["gated"], "\"ON\"")
            .Sym("Serial Compare", ["x", "y"], ["same", "differ"])
            .Run();
        Assert.True(t.D("is_empty") && !t.D("is_on"), "an empty string equals the empty parameter at start");
        t.Set("s", "ON");
        Assert.True(t.D("is_on") && !t.D("is_off") && !t.D("is_empty"), "ON");
        Assert.True(!t.D("gated"), "enable wired and low");
        t.Set("en", true);
        Assert.True(t.D("gated"), "enabled");
        t.Set("s", "on");
        Assert.True(!t.D("is_on") && !t.D("gated"), "case-sensitive");
        t.Set("s", "OFF");
        Assert.True(t.D("is_off") && !t.D("is_on"), "OFF");
        Assert.True(t.D("same"), "both inputs empty are the same");
        t.Set("x", "abc");
        Assert.True(!t.D("same") && t.D("differ"), "abc vs empty");
        t.Set("y", "abc");
        Assert.True(t.D("same") && !t.D("differ"), "abc vs abc");
        t.Set("y", "ABC");
        Assert.True(!t.D("same") && t.D("differ"), "case matters");
    }

    [Test]
    public static void Serial_ram_stores_and_recalls_banks()
    {
        // The two serial inputs are also read by Serial to Hex, which is what tells the interpreter they are serial.
        using var t = new Smw()
            .Sym("Serial to Hex", ["t1"], ["x1"])
            .Sym("Serial to Hex", ["t2"], ["x2"])
            .Sym("Serial RAM", ["t1", "t2", "store", "recall", "b0", "b1"], ["r1", "r2"])
            .Run();
        t.Set("t1", "A");
        t.Set("t2", "B");
        t.Set("b0", true);
        Press(t, "store");
        t.Set("t1", "X");
        t.Set("t2", "Y");
        t.Set("b0", false);
        t.Set("b1", true);
        Press(t, "store");
        Assert.Equal("", t.S("r1"), "nothing is driven until recall");
        Press(t, "recall");
        Assert.True(t.S("r1") == "X" && t.S("r2") == "Y", "bank 1 recalled");
        t.Set("b1", false);
        t.Set("b0", true);
        Press(t, "recall");
        Assert.True(t.S("r1") == "A" && t.S("r2") == "B", "bank 0 recalled");
        t.Set("b0", false);
        t.Set("t1", "changed");
        Press(t, "recall");
        Assert.True(t.S("r1") == "A", "no bank selected: recall does nothing");
    }

    [Test]
    public static void Multiplexers_and_demultiplexers_in_all_three_kinds()
    {
        using var t = new Smw()
            .Sym("Digital Mux", ["sel", "d1", "d2", "d3"], ["dm"])
            .Sym("Analog Mux", ["sel", "a1", "a2"], ["am"])
            .Sym("Serial Mux", ["sel", "s1", "s2"], ["sm"])
            .Sym("Digital Demux", ["sel", "dv"], ["do1", "do2", "do3"])
            .Sym("Analog Demux", ["sel", "av"], ["ao1", "ao2", "ao3"])
            .Sym("Serial Demux", ["sel", "sv"], ["so1", "so2", "so3"])
            .Run();
        t.Set("d2", true);
        t.Set("a2", (ushort)222);
        t.Set("a1", (ushort)111);
        t.Set("s2", "two");
        t.Set("dv", true);
        t.Set("av", (ushort)77);
        t.Set("sv", "v");
        Assert.True(!t.D("dm") && t.A("am") == 0 && t.S("sm") == "", "select 0 gives the defaults");
        Assert.True(!t.D("do1") && t.A("ao1") == 0 && t.S("so1") == "", "select 0 drives no output");
        t.Set("sel", (ushort)2);
        Assert.True(t.D("dm") && t.A("am") == 222 && t.S("sm") == "two", "select 2");
        Assert.True(!t.D("do1") && t.D("do2") && !t.D("do3"), "demux digital: output 2 only");
        Assert.True(t.A("ao1") == 0 && t.A("ao2") == 77 && t.S("so2") == "v" && t.S("so1") == "", "demux analog and serial: output 2 only");
        t.Set("sel", (ushort)1);
        Assert.True(!t.D("dm") && t.A("am") == 111 && t.S("sm") == "", "select 1");
        Assert.True(t.D("do1") && !t.D("do2") && t.A("ao1") == 77 && t.A("ao2") == 0, "demux moved to output 1");
        t.Set("a1", (ushort)112);
        Assert.Equal((ushort)112, t.A("am"), "the selected input is followed live");
        t.Set("sel", (ushort)9);
        Assert.True(t.A("am") == 0 && !t.D("dm") && t.S("sm") == "", "select past the last input gives the defaults");
        Assert.True(!t.D("do1") && !t.D("do2") && !t.D("do3") && t.A("ao1") == 0, "demux with a select past the last output drives none");
    }

    [Test]
    public static void Time_split_and_join()
    {
        using var t = new Smw()
            .Sym("Time Split", ["clock"], ["h", "m", "s"])
            .Sym("Time Join", ["jh", "jm", "js"], ["text"])
            .Run();
        t.Set("clock", "13:45:07");
        Assert.True(t.A("h") == 13 && t.A("m") == 45 && t.A("s") == 7, "13:45:07");
        t.Set("clock", "1:05 PM");
        Assert.True(t.A("h") == 13 && t.A("m") == 5 && t.A("s") == 0, "1:05 PM");
        t.Set("clock", "12:30:00 AM");
        Assert.True(t.A("h") == 0 && t.A("m") == 30, "12:30 AM is half past midnight");
        t.Set("clock", "12:00 PM");
        Assert.Equal((ushort)12, t.A("h"), "12:00 PM is noon");
        t.Set("clock", "9:15");
        Assert.True(t.A("h") == 9 && t.A("m") == 15 && t.A("s") == 0, "no seconds");
        t.Set("clock", "25:00:00");
        Assert.True(t.A("h") == 0 && t.A("m") == 0 && t.A("s") == 0, "hour out of range");
        t.Set("clock", "banana");
        Assert.True(t.A("h") == 0 && t.A("m") == 0, "not a time");
        Assert.Equal("00:00:00", t.S("text"), "join at start");
        t.Set("jh", (ushort)5);
        t.Set("jm", (ushort)7);
        t.Set("js", (ushort)9);
        Assert.Equal("05:07:09", t.S("text"), "two digits each");
        t.Set("jh", (ushort)123);
        Assert.Equal("123:07:09", t.S("text"), "out-of-range values are shown as they are");
    }

    [Test]
    public static void Every_new_symbol_is_listed_documented_and_found_by_name()
    {
        string[] names = ["Analog Subtract", "Analog Multiplier", "Analog Divider", "Analog Minimum", "Analog Maximum", "Analog Average",
            "Analog Delay", "Serial Delay", "Up/Down Counter", "Serial to Hex", "Hex to Serial", "Analog to Hex", "Hex to Analog",
            "Serial Equate", "Serial Compare", "Serial RAM", "Digital Multiplexer", "Analog Multiplexer", "Serial Multiplexer",
            "Digital Demultiplexer", "Analog Demultiplexer", "Serial Demultiplexer", "Time Split", "Time Join"];
        foreach (var n in names)
        {
            var s = LogicSymbolCatalog.ByName(n) ?? throw new AssertException($"{n} is in the catalog");
            Assert.Equal(SymbolEvidence.Documented, s.Evidence, n);
        }
        Assert.Equal(LogicSymbolCatalog.ByName("Analog Mux"), LogicSymbolCatalog.ByName("Analog Multiplexer"), "alias");
        Assert.Equal(LogicSymbolCatalog.ByName("Counter"), LogicSymbolCatalog.ByName("Up/Down Counter"), "alias");
    }

    /// <summary>
    /// A small program combining several of them, the way a real one wires symbols together: a level scaled by a
    /// multiplier, picked by a multiplexer, sent as hex text, read back, counted and compared.
    /// </summary>
    [Test]
    public static void Combined_program_wires_the_new_symbols_together()
    {
        using var t = new Smw()
            .Sym("Analog Multiplier", ["level", "factor"], ["scaled"])
            .Sym("Analog Mux", ["pick", "level", "scaled"], ["chosen"])
            .Sym("Analog to Hex", ["chosen"], ["hexed"], "4d")
            .Sym("Hex to Analog", ["hexed"], ["roundtrip"])
            .Sym("Serial Equate", ["hexed", ""], ["is_ff00"], "\"FF00\"")
            .Sym("Up/Down Counter", ["is_ff00", "", ""], ["hits"])
            .Sym("Analog Divider", ["roundtrip", "divisor"], ["quotient"])
            .Run();
        t.Set("level", (ushort)255);
        t.Set("factor", (ushort)256);
        t.Set("divisor", (ushort)16);
        Assert.Equal((ushort)65280, t.A("scaled"), "255 * 256");
        Assert.Equal("0000", t.S("hexed"), "nothing picked: the mux gives 0");
        t.Set("pick", (ushort)2);
        Assert.Equal("FF00", t.S("hexed"), "scaled value as hex");
        Assert.Equal((ushort)65280, t.A("roundtrip"), "and back");
        Assert.True(t.D("is_ff00"), "the equate saw it");
        Assert.Equal((ushort)1, t.A("hits"), "the counter counted its rising edge");
        Assert.Equal((ushort)4080, t.A("quotient"), "65280 / 16");
        t.Set("pick", (ushort)1);
        Assert.Equal("00FF", t.S("hexed"), "255 as hex");
        Assert.True(!t.D("is_ff00"), "equate dropped");
        t.Set("pick", (ushort)2);
        Assert.Equal((ushort)2, t.A("hits"), "second rising edge");
    }
}
