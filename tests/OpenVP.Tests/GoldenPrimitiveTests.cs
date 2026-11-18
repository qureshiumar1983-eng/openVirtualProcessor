using OpenVP.Core;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>
/// Golden traces for the primitive types the first golden tests don't reach. Same rules as GoldenTests:
/// no timers, every step ends at a barrier, and the checked-in files never change to make a test pass.
/// Not covered here because they run on timers: 14 Oscillator, 15/17 One Shot, 16 Pulse Stretcher,
/// 20/223 Analog Ramp, 27 Delay, 29 Multiple One Shots, 56 Debounce, 61 When, 62 Clock Driver,
/// 114 Change Pulse, 139 Stepper and 241 Stepper with Progress.
/// </summary>
public static partial class GoldenTests
{
    /// <summary>Lays out a panel (IPID 03) and numbers signals, so a scenario reads as wiring, not arithmetic.
    /// Press joins are numbered in the order they are created, per kind; so are feedback joins.</summary>
    private sealed class Rig
    {
        private int _next = 10;
        private readonly List<int> _pressD = new(), _pressA = new(), _pressS = new(), _fbD = new(), _fbA = new(), _fbS = new();
        private readonly List<byte> _logic = new();

        public int PressD() { var s = _next++; _pressD.Add(s); return s; }
        public int PressA() { var s = _next++; _pressA.Add(s); return s; }
        public int PressS() { var s = _next++; _pressS.Add(s); return s; }
        /// <summary>A new signal shown on the panel as the next feedback join of that kind.</summary>
        public int OutD() { var s = _next++; _fbD.Add(s); return s; }
        public int OutA() { var s = _next++; _fbA.Add(s); return s; }
        public int OutS() { var s = _next++; _fbS.Add(s); return s; }
        public int[] OutsD(int n) => Enumerable.Range(0, n).Select(_ => OutD()).ToArray();
        /// <summary>A new signal nobody shows.</summary>
        public int Sig() => _next++;
        public void Add(byte[] record) => _logic.AddRange(record);

        public string Build(string name)
        {
            var bin = Header(name);
            bin.AddRange(Panel(3, [.. _fbD, .. _fbA, .. _fbS], [.. _pressD, .. _pressA, .. _pressS],
                inD: _fbD.Count, inAEnd: _fbD.Count + _fbA.Count, outD: _pressD.Count, outAEnd: _pressD.Count + _pressA.Count));
            bin.AddRange(_logic);
            return Pack(name, bin, Dip03);
        }
    }

    private static (Processor p, ProgramSlot[] slots, JoinDevice panel, Recorder rec) Start(string path)
    {
        var (p, slot, panel, rec) = Run(path);
        return (p, [slot], panel, rec);
    }

    // ------------------------------------------------------------ analog

    [Test]
    public static void Golden_analog_primitives()
    {
        var r = new Rig();
        int x = r.PressA(), y = r.PressA(), z = r.PressA();                       // a1 a2 a3
        int clk = r.PressD(), rst = r.PressD(), store = r.PressD(), recall = r.PressD(),
            up = r.PressD(), down = r.PressD(), send = r.PressD();                // d1..d7
        int text = r.PressS();                                                    // s1

        r.Add(Record(30, "S-1 Scaler", [x], [r.OutA()], Int(15, 3), Int(2, 1000), Int(2, 200), Int(2, 5)));
        r.Add(Record(99, "S-2 Scaler signed", [x], [r.OutA()], Int(15, 2), Int(2, 30000), Int(2, 100)));
        r.Add(Record(224, "S-3 Scaler limits", [x], [r.OutA()], Int(15, 4), Int(2, 0), Int(2, 1000), Int(2, 100), Int(2, 500)));
        r.Add(Record(87, "S-4 Flip", [x], [r.OutA()]));
        r.Add(Record(136, "S-5 DivMod", [x], [r.OutA(), r.OutA()], Int(15, 1), Int(2, 7)));
        r.Add(Record(237, "S-6 Clamp", [y, z, x], [r.OutA()]));
        r.Add(Record(80, "S-7 Follow", [x], [r.OutA()]));
        r.Add(Record(100, "S-8 Sum", [x, y, z], [r.OutA()]));
        r.Add(Record(220, "S-9 Compare", [x, y], [r.OutD(), r.OutD(), r.OutD(), r.OutD(), r.OutD(), r.OutD()]));
        r.Add(Record(106, "S-10 Analog to digital", [x], r.OutsD(4)));
        r.Add(Record(52, "S-11 Decade", [x], r.OutsD(30)));
        r.Add(Record(37, "S-12 Analog step", [clk, rst], [r.OutA()], Int(15, 3), Int(2, 100), Int(2, 200), Int(2, 300)));
        r.Add(Record(254, "S-13 String length", [text], [r.OutA()]));
        r.Add(Record(103, "S-14 Serial to analog", [text], [r.OutA()]));
        r.Add(Record(82, "S-15 Analog to serial", [send, x, y], [r.OutS()], Int(15, 3), Str(3, "A"), Str(3, "B"), Str(3, "C")));
        // Held buttons: the first repeat is 10 minutes away, so only the immediate step happens.
        r.Add(Record(230, "S-16 Increment", [up, down], [r.OutA()],
            Int(15, 6), Int(2, 5), Int(2, 60000), Int(2, 10), Int(2, 0), Int(2, 100), Int(2, 50)));
        r.Add(Record(236, "S-17 Increment with feedback", [up, down, 0, y], [r.OutA()],
            Int(15, 6), Int(2, 3), Int(2, 60000), Int(2, 10), Int(2, 0), Int(2, 1000), Int(2, 0)));
        r.Add(Record(36, "S-18 Analog RAM", [x, y, store, recall], [r.OutA(), r.OutA()], Int(5, 100)));

        var (p, slots, panel, rec) = Start(r.Build("analog"));
        using var _ = p;
        Step(rec, slots, panel, "a1 = 1000", (A(1), (ushort)1000));
        Step(rec, slots, panel, "a2 = 500", (A(2), (ushort)500));
        Step(rec, slots, panel, "a3 = 2000", (A(3), (ushort)2000));
        Step(rec, slots, panel, "a1 = 65535", (A(1), (ushort)65535));
        Step(rec, slots, panel, "a1 = 0", (A(1), (ushort)0));
        Step(rec, slots, panel, "a1 = 123", (A(1), (ushort)123));
        Step(rec, slots, panel, "a1 = 123 again (no change)", (A(1), (ushort)123));
        Step(rec, slots, panel, "a3 = 100 (clamp window shrinks)", (A(3), (ushort)100));
        Step(rec, slots, panel, "a2 = 600 (window flips)", (A(2), (ushort)600));
        Step(rec, slots, panel, "a1 = 7", (A(1), (ushort)7));
        // analog step: clock, reset
        Step(rec, slots, panel, "step clk up", (D(1), true));
        Step(rec, slots, panel, "step clk down", (D(1), false));
        Step(rec, slots, panel, "step clk up", (D(1), true));
        Step(rec, slots, panel, "step clk down", (D(1), false));
        Step(rec, slots, panel, "step reset", (D(2), true));
        Step(rec, slots, panel, "step reset release", (D(2), false));
        Step(rec, slots, panel, "step clk up", (D(1), true));
        Step(rec, slots, panel, "step clk down", (D(1), false));
        // analog RAM: recall before anything was stored does nothing; store; change; recall
        Step(rec, slots, panel, "ram recall (empty)", (D(4), true));
        Step(rec, slots, panel, "ram recall release", (D(4), false));
        Step(rec, slots, panel, "ram store (x=7, y=600)", (D(3), true));
        Step(rec, slots, panel, "ram store release", (D(3), false));
        Step(rec, slots, panel, "a1 = 9, a2 = 10", (A(1), (ushort)9), (A(2), (ushort)10));
        Step(rec, slots, panel, "ram recall", (D(4), true));
        Step(rec, slots, panel, "ram recall release", (D(4), false));
        // increment / decrement
        Step(rec, slots, panel, "up press", (D(5), true));
        Step(rec, slots, panel, "up release", (D(5), false));
        Step(rec, slots, panel, "down press", (D(6), true));
        Step(rec, slots, panel, "down release", (D(6), false));
        Step(rec, slots, panel, "a2 = 20 (feedback into increment 2)", (A(2), (ushort)20));
        Step(rec, slots, panel, "up press", (D(5), true));
        Step(rec, slots, panel, "up release", (D(5), false));
        // analog to serial and serial to analog/length
        Step(rec, slots, panel, "a1 = 65, a2 = 66", (A(1), (ushort)65), (A(2), (ushort)66));
        Step(rec, slots, panel, "send", (D(7), true));
        Step(rec, slots, panel, "send release", (D(7), false));
        Step(rec, slots, panel, "s1 = 42", (S(1), "42"));
        Step(rec, slots, panel, "s1 = ' 7 '", (S(1), " 7 "));
        Step(rec, slots, panel, "s1 = abc (not a number)", (S(1), "abc"));
        Step(rec, slots, panel, "s1 = 99999 (too big)", (S(1), "99999"));
        Step(rec, slots, panel, "s1 empty", (S(1), ""));
        Check("analog_primitives", rec.Render());
    }

    // ------------------------------------------------------------ digital and serial

    [Test]
    public static void Golden_digital_and_serial_primitives()
    {
        var r = new Rig();
        int a = r.PressD(), b = r.PressD(), c = r.PressD(), d = r.PressD();       // d1..d4
        int ana = r.PressA(), anaX = r.PressA();                                  // a1, a2 (a2 only feeds the XSIG)
        int s1 = r.PressS(), s2 = r.PressS(), s3 = r.PressS();                    // s1..s3

        r.Add(Record(6, "S-1 Rising gate", [a, 0, b], [r.OutD()]));
        r.Add(Record(7, "S-2 Falling gate", [a, b], [r.OutD()]));
        r.Add(Record(204, "S-3 Wave pulse", [a], [r.OutD()]));
        r.Add(Record(203, "S-4 Wave delay", [a, b], [r.OutD(), r.OutD()], Int(15, 2), Int(2, 2), Int(2, 3)));
        r.Add(Record(207, "S-5 Interlock toggle", [a, b, c], r.OutsD(3)));
        r.Add(Record(23, "S-6 Ring counter", [a, b], r.OutsD(3)));
        r.Add(Record(156, "S-7 Offline", [], [r.OutD(), r.OutD()]));
        r.Add(Record(121, "S-8 Clock adjust", [a], []));
        r.Add(Record(161, "S-9 Idle", [b], []));
        // Truth table: row 1 is "a and b high" -> (high, low), row 2 is "a low, b whatever" -> (low, high).
        r.Add(Record(19, "S-10 Truth table", [a, b], [r.OutD(), r.OutD()], Int(15, 4),
            Str(3, "\u0002\u0002"), Str(3, "\u0001\u0004"), Str(3, "\u0002\u0001"), Str(3, "\u0001\u0002")));
        // Button Presser follows its input into outputs of every kind: this is where a bool meets an
        // analog or serial signal (and the reverse), so the coercions show in the trace.
        r.Add(Record(138, "S-11 Follow digital", [a], [r.OutD(), r.OutA(), r.OutS()]));
        r.Add(Record(138, "S-12 Follow analog", [ana], [r.OutD(), r.OutA(), r.OutS()]));
        r.Add(Record(138, "S-13 Follow serial", [s3], [r.OutD(), r.OutA(), r.OutS()]));
        // Serial
        r.Add(Record(58, "S-14 Serial send", [a, b], [r.OutS()], Int(15, 2), Str(3, "hello"), Str(3, "world")));
        r.Add(Record(58, "S-15 Multiple serial send", [c, d], [r.OutS(), r.OutS()], Int(15, 2), Str(3, "one"), Str(3, "two")));
        r.Add(Record(266, "S-16 Discrete serial send", [c, 1, d], [r.OutS(), r.OutS(), r.OutS()], Int(15, 3), Str(3, "alpha"), Str(3, "beta"), Str(3, "gamma")));
        r.Add(Record(93, "S-17 Gather", [s1], [r.OutS()], Int(15, 2), Int(2, 59), Int(2, 6)));
        r.Add(Record(122, "S-18 Merge", [s1, s2], [r.OutS()]));
        r.Add(Record(97, "S-19 Serial I/O", [s2, c, d], [r.OutS(), r.OutD(), r.OutD()], Int(15, 2), Str(3, "ping"), Str(3, "pong")));
        r.Add(Record(202, "S-20 Console", [s2], [r.OutS()]));
        // Make String Permanent watches its output signal (here the s1 press) and saves it; nothing shows in the trace.
        r.Add(Record(205, "S-23 Permanent", [], [s1]));
        // XSIG (join offset 1, so no join number is 0 and no byte is NUL): inputs 2.. are encoded onto
        // output 1, and text on input 1 is decoded onto outputs 2..
        r.Add(Record(85, "S-21 XSIG", [s1, a, anaX, s2], [r.OutS(), r.OutD(), r.OutA(), r.OutS()], Int(15, 1), Int(2, 1)));
        // The same, with digital, analog and serial joins decoded onto outputs of another kind.
        r.Add(Record(85, "S-22 XSIG crossed", [s3, d], [r.OutS(), r.OutA(), r.OutD(), r.OutD()], Int(15, 1), Int(2, 1)));

        var (p, slots, panel, rec) = Start(r.Build("digital_serial"));
        using var _ = p;
        // Edge gates, wave pulse, wave delay
        Step(rec, slots, panel, "a on", (D(1), true));
        Step(rec, slots, panel, "b on", (D(2), true));
        Step(rec, slots, panel, "a off", (D(1), false));
        Step(rec, slots, panel, "b off", (D(2), false));
        Step(rec, slots, panel, "a and b on together", (D(1), true), (D(2), true));
        Step(rec, slots, panel, "both off", (D(1), false), (D(2), false));
        // Interlock toggle and ring counter share a, b, c
        Step(rec, slots, panel, "c on", (D(3), true));
        Step(rec, slots, panel, "c off", (D(3), false));
        Step(rec, slots, panel, "c on again (toggles off)", (D(3), true));
        Step(rec, slots, panel, "c off", (D(3), false));
        Step(rec, slots, panel, "d on", (D(4), true));
        Step(rec, slots, panel, "d off", (D(4), false));
        // Follow analog
        Step(rec, slots, panel, "a1 = 5", (A(1), (ushort)5));
        Step(rec, slots, panel, "a1 = 0", (A(1), (ushort)0));
        Step(rec, slots, panel, "a1 = 70", (A(1), (ushort)70));
        // Follow serial, serial sends
        Step(rec, slots, panel, "s3 = 1", (S(3), "1"));
        Step(rec, slots, panel, "s3 = true", (S(3), "true"));
        Step(rec, slots, panel, "s3 = 12", (S(3), "12"));
        Step(rec, slots, panel, "s3 = text", (S(3), "text"));
        Step(rec, slots, panel, "s3 = text again (serials always fire)", (S(3), "text"));
        // Gather: delimiter ';' (59), max 6
        Step(rec, slots, panel, "s1 = ab", (S(1), "ab"));
        Step(rec, slots, panel, "s1 = c;de", (S(1), "c;de"));
        Step(rec, slots, panel, "s1 = fghijklm;", (S(1), "fghijklm;"));
        // Merge, Serial I/O, console
        Step(rec, slots, panel, "s2 = ping", (S(2), "ping"));
        Step(rec, slots, panel, "s2 = pong", (S(2), "pong"));
        Step(rec, slots, panel, "s2 = other", (S(2), "other"));
        Step(rec, slots, panel, "s2 empty", (S(2), ""));
        Step(rec, slots, panel, "s2 = nosuchcommand", (S(2), "nosuchcommand"));
        // XSIG: digital join 1 high/low, analog join 2 = 300, serial join 3 = "hi", then the crossed kinds
        Step(rec, slots, panel, "xsig rx digital 1 high", (S(1), "\u0080\u0001"));
        Step(rec, slots, panel, "xsig rx digital 1 low", (S(1), "\u00A0\u0001"));
        Step(rec, slots, panel, "xsig rx analog 2 = 300", (S(1), "\u00C0\u0002\u0002,"));
        Step(rec, slots, panel, "xsig rx serial 3 = hi", (S(1), "\u00C8\u0003hi\u00FF"));
        Step(rec, slots, panel, "xsig rx in two halves (1)", (S(1), "\u00C8\u0003"));
        Step(rec, slots, panel, "xsig rx in two halves (2)", (S(1), "there\u00FF"));
        Step(rec, slots, panel, "xsig crossed rx digital 1 high (into analog)", (S(3), "\u0080\u0001"));
        Step(rec, slots, panel, "xsig crossed rx analog 2 = 193 (into digital)", (S(3), "\u00C0\u0002\u0001A"));
        Step(rec, slots, panel, "xsig crossed rx serial 3 = 1 (into digital)", (S(3), "\u00C8\u0003" + "1\u00FF"));
        // Everything that reads a, d goes back through the gates and encoders once more
        Step(rec, slots, panel, "a on (xsig encode digital)", (D(1), true));
        Step(rec, slots, panel, "a off", (D(1), false));
        Step(rec, slots, panel, "a2 = 193 (xsig encode analog)", (A(2), (ushort)193));
        Step(rec, slots, panel, "s2 = enc (xsig encode serial)", (S(2), "enc"));
        Step(rec, slots, panel, "d on (xsig crossed encode)", (D(4), true));
        Step(rec, slots, panel, "d off", (D(4), false));
        Check("digital_serial_primitives", rec.Render());
    }

    // ------------------------------------------------------------ AND / OR / NOR in every shape

    [Test]
    public static void Golden_gates_in_every_shape()
    {
        var r = new Rig();
        var presses = Enumerable.Range(0, 20).Select(_ => r.PressD()).ToArray();   // d1..d20
        int ana = r.PressA(), txt = r.PressS();                                    // a1, s1
        int p1 = presses[0], p2 = presses[1], p3 = presses[2];

        // Wide gates over the same 20 presses.
        r.Add(Record(0, "S-1 AND 20", presses, [r.OutD()]));
        r.Add(Record(2, "S-2 OR 20", presses, [r.OutD()]));
        r.Add(Record(3, "S-3 NOR 20", presses, [r.OutD()]));
        // Constants: an input tied to 1 is always high, tied to 0 always low.
        r.Add(Record(0, "S-4 AND with 1", [p1, 1, p2], [r.OutD()]));
        r.Add(Record(0, "S-5 AND with 0", [p1, 0], [r.OutD()]));
        r.Add(Record(2, "S-6 OR with 0", [p1, 0, p2], [r.OutD()]));
        r.Add(Record(2, "S-7 OR with 1", [p1, 1], [r.OutD()]));
        r.Add(Record(3, "S-8 NOR with 0", [p1, p2, 0], [r.OutD()]));
        r.Add(Record(3, "S-9 NOR with 1", [p1, 1], [r.OutD()]));
        // The same signal on two pins of one gate.
        r.Add(Record(0, "S-10 AND with a repeated signal", [p1, p1, p2], [r.OutD()]));
        r.Add(Record(2, "S-11 OR with a repeated signal", [p1, p1], [r.OutD()]));
        r.Add(Record(3, "S-12 NOR with repeated signals", [p2, p3, p2, 0, p3], [r.OutD()]));
        r.Add(Record(0, "S-13 AND of one signal", [p3], [r.OutD()]));
        // Nothing wired in.
        r.Add(Record(0, "S-14 AND with no inputs", [], [r.OutD()]));
        r.Add(Record(2, "S-15 OR with no inputs", [], [r.OutD()]));
        r.Add(Record(3, "S-16 NOR with no inputs", [], [r.OutD()]));
        r.Add(Record(0, "S-17 AND, output unconnected", [p1, p2], [0]));
        // Analog and serial signals on gate inputs never read as high, but still make the gate run.
        r.Add(Record(0, "S-18 AND with an analog input", [p1, ana], [r.OutD()]));
        r.Add(Record(2, "S-19 OR with an analog and a serial input", [ana, txt, p2], [r.OutD()]));
        r.Add(Record(3, "S-20 NOR with a serial input", [txt, p3], [r.OutD()]));
        // Gates feeding gates, and a gate feeding one of its own inputs' siblings
        int mid = r.Sig();
        r.Add(Record(2, "S-21 OR into AND", [p1, p2], [mid]));
        r.Add(Record(0, "S-22 AND after OR", [mid, p3], [r.OutD()]));

        var (p, slots, panel, rec) = Start(r.Build("gates"));
        using var _ = p;
        // Raise the first three, one at a time, then the rest in two batches, then lower in another order.
        for (uint j = 1; j <= 3; j++) Step(rec, slots, panel, $"d{j} on", (D(j), true));
        Step(rec, slots, panel, "d4..d10 on", Enumerable.Range(4, 7).Select(j => (D((uint)j), (object)true)).ToArray());
        Step(rec, slots, panel, "d11..d19 on", Enumerable.Range(11, 9).Select(j => (D((uint)j), (object)true)).ToArray());
        Step(rec, slots, panel, "d20 on (AND 20 goes high)", (D(20), true));
        Step(rec, slots, panel, "d7 off", (D(7), false));
        Step(rec, slots, panel, "d7 on", (D(7), true));
        Step(rec, slots, panel, "d1 off", (D(1), false));
        Step(rec, slots, panel, "d1 on", (D(1), true));
        Step(rec, slots, panel, "analog a1 = 5 (gates with analog inputs re-run)", (A(1), (ushort)5));
        Step(rec, slots, panel, "analog a1 = 0", (A(1), (ushort)0));
        Step(rec, slots, panel, "serial s1 = x", (S(1), "x"));
        Step(rec, slots, panel, "serial s1 = x again", (S(1), "x"));
        for (uint j = 20; j >= 4; j--) Step(rec, slots, panel, $"d{j} off", (D(j), false));
        Step(rec, slots, panel, "d1 and d2 off together", (D(1), false), (D(2), false));
        Step(rec, slots, panel, "d3 off", (D(3), false));
        Step(rec, slots, panel, "d2 on", (D(2), true));
        Step(rec, slots, panel, "d2 off", (D(2), false));
        Step(rec, slots, panel, "d3 on", (D(3), true));
        Step(rec, slots, panel, "d1 and d3 flip together", (D(1), true), (D(3), false));
        Step(rec, slots, panel, "all of d1..d3 on", (D(2), true), (D(3), true));
        Step(rec, slots, panel, "all of d1..d3 off", (D(1), false), (D(2), false), (D(3), false));
        Check("gates", rec.Render());
    }

    // ------------------------------------------------------------ crosspoints and Smart Graphics

    [Test]
    public static void Golden_crosspoints_and_smart_objects()
    {
        var r = new Rig();
        int ctlPress = r.PressD(), conn = r.PressD(), disc = r.PressD(), discCtl = r.PressD(), discEq = r.PressD(), discAll = r.PressD(); // d1..d6
        int ctlLevel = r.PressA(), cid = r.PressA(), eid = r.PressA();                                                                  // a1..a3
        int ePressD = r.Sig(), ePressA = r.Sig(), eFbD = r.Sig(), eFbA = r.Sig();
        int cFbD = r.OutD(), cFbA = r.OutA();
        // Equipment crosspoint ID 2 (digital + analog join each way), a control ID 1 on the panel, and the
        // logic behind the equipment: inverter and flip.
        const int D1A1 = 1;
        r.Add(Record(213, "XE", [eFbD, eFbA], [ePressD, ePressA], Int(15, 1), Int(2, 2), Int(10, D1A1), Int(12, 2), Int(11, D1A1), Int(13, 2)));
        r.Add(Record(214, "XC", [ctlPress, ctlLevel], [cFbD, cFbA], Int(15, 1), Int(2, 1), Int(10, D1A1), Int(12, 2), Int(11, D1A1), Int(13, 2)));
        r.Add(Record(215, "XCON", [cid, eid, conn, disc, discCtl, discEq, discAll], [], Int(15, 0)));
        r.Add(Record(1, "S-1 NOT", [ePressD], [eFbD]));
        r.Add(Record(87, "S-2 Flip", [ePressA], [eFbA]));
        // A Smart Graphics object (ID 7) on the panel: digital, analog, serial each way, looped through logic.
        int soOutD = r.Sig(), soOutA = r.Sig(), soOutS = r.Sig(), soInD = r.Sig(), soInA = r.Sig(), soInS = r.Sig();
        r.Add(Record(268, "IP-ID-03.SO", [soInD, soInA, soInS], [soOutD, soOutA, soOutS],
            Int(15, 1), Int(44, 7), Int(10, 1), Int(12, 2), Int(11, 1), Int(13, 2)));
        r.Add(Record(1, "S-3 NOT", [soOutD], [soInD]));
        r.Add(Record(87, "S-4 Flip", [soOutA], [soInA]));
        r.Add(Record(88, "S-5 Serial buffer", [1, soOutS], [soInS]));

        var (p, slots, panel, rec) = Start(r.Build("xpoint"));
        using var _ = p;
        var soD = new JoinKey(SigKind.Digital, 1, 7);
        var soA = new JoinKey(SigKind.Analog, 1, 7);
        var soS = new JoinKey(SigKind.Serial, 1, 7);
        Step(rec, slots, panel, "control press while not connected", (D(1), true));
        Step(rec, slots, panel, "control release", (D(1), false));
        Step(rec, slots, panel, "ids: control 1, equipment 2", (A(2), (ushort)1), (A(3), (ushort)2));
        Step(rec, slots, panel, "connect", (D(2), true));
        Step(rec, slots, panel, "connect release", (D(2), false));
        Step(rec, slots, panel, "control press", (D(1), true));
        Step(rec, slots, panel, "control level 100", (A(1), (ushort)100));
        Step(rec, slots, panel, "control level 0", (A(1), (ushort)0));
        Step(rec, slots, panel, "disconnect (held press is released)", (D(3), true));
        Step(rec, slots, panel, "disconnect release", (D(3), false));
        Step(rec, slots, panel, "control release", (D(1), false));
        Step(rec, slots, panel, "connect again", (D(2), true));
        Step(rec, slots, panel, "connect release", (D(2), false));
        Step(rec, slots, panel, "disconnect the control", (D(4), true));
        Step(rec, slots, panel, "disconnect the control release", (D(4), false));
        Step(rec, slots, panel, "connect a third time", (D(2), true));
        Step(rec, slots, panel, "connect release", (D(2), false));
        Step(rec, slots, panel, "disconnect the equipment", (D(5), true));
        Step(rec, slots, panel, "disconnect the equipment release", (D(5), false));
        Step(rec, slots, panel, "connect a fourth time", (D(2), true));
        Step(rec, slots, panel, "connect release", (D(2), false));
        Step(rec, slots, panel, "disconnect all", (D(6), true));
        Step(rec, slots, panel, "disconnect all release", (D(6), false));
        // Smart object joins travel with the object ID
        Step(rec, slots, panel, "smart object d1 press", (soD, true));
        Step(rec, slots, panel, "smart object d1 release", (soD, false));
        Step(rec, slots, panel, "smart object a1 = 1000", (soA, (ushort)1000));
        Step(rec, slots, panel, "smart object s1 = hi", (soS, "hi"));
        Step(rec, slots, panel, "smart object of another id is ignored", (new JoinKey(SigKind.Digital, 1, 8), true));
        Assert.Equal((ushort)64535, (ushort)panel.GetFeedback(soA), "smart object analog feedback");
        Assert.Equal("hi", (string)panel.GetFeedback(soS), "smart object serial feedback");
        Check("crosspoints_smartobjects", rec.Render());
    }
}
