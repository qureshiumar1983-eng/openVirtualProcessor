namespace OpenVP.Lpz;

/// <summary>What OpenVP knows about one logic-engine primitive type.</summary>
public sealed record PrimitiveInfo(int Type, string Name, string Notes, bool Implemented);

/// <summary>
/// Logic-engine primitive types and the SIMPL symbol each comes from. Identified by matching every
/// compiled record's output signals (by name, via the .sig) to the source symbol driving the same
/// signals, across 36 programs that ship their source. Implemented = OpenVP can run it.
/// </summary>
public static class PrimitiveCatalog
{
    public static readonly IReadOnlyDictionary<int, PrimitiveInfo> Known = new[]
    {
        new PrimitiveInfo(31, "Leading REM (compile header)", "", true),
        new PrimitiveInfo(-204, "Processor", "", true),
        new PrimitiveInfo(-205, "Processor slot", "", true),
        new PrimitiveInfo(-203, "Card slot", "", true),
        new PrimitiveInfo(-201, "Card slot", "", true),
        new PrimitiveInfo(-12, "IP device / slot / card", "IP-ID records map joins: fields 10-13 split digital/analog/serial.", true),
        new PrimitiveInfo(-48, "SIMPL+ module", "Runs a compiled SIMPL+ .dll named in a string field.", true), // runs as a dedicated node, not a library primitive
        new PrimitiveInfo(-200, "COM port", "Input 1 = serial TX, output 1 = RX; under the device's card slot (IP-ID-xx > Slot > COM-nn). Logged and handed to the port bridge.", true),
        new PrimitiveInfo(-202, "IR port", "Crestron DB IR-controlled device (SmC 107): input 2+k triggers IR command params[k]. Logged and handed to the port bridge.", true),
        new PrimitiveInfo(-242, "IP serial device", "", true), // runs as a dedicated node, not a library primitive
        new PrimitiveInfo(-310, "Hardware", "", true),
        new PrimitiveInfo(-316, "Hardware", "", true),
        new PrimitiveInfo(-320, "Hardware", "", true),
        new PrimitiveInfo(-15, "Hardware", "", true),
        new PrimitiveInfo(0, "AND", "SmC 1", PrimitiveLibrary.Supports(0)),
        new PrimitiveInfo(1, "NOT", "SmC 205", PrimitiveLibrary.Supports(1)),
        new PrimitiveInfo(2, "OR", "SmC 3", PrimitiveLibrary.Supports(2)),
        new PrimitiveInfo(3, "NOR", "SmC 4", PrimitiveLibrary.Supports(3)),
        new PrimitiveInfo(6, "Transition Gate", "SmC 6", PrimitiveLibrary.Supports(6)),
        new PrimitiveInfo(9, "Set/Reset Latch", "SmC 12", PrimitiveLibrary.Supports(9)),
        new PrimitiveInfo(10, "Toggle", "SmC 13", PrimitiveLibrary.Supports(10)),
        new PrimitiveInfo(11, "Interlock", "SmC 19", PrimitiveLibrary.Supports(11)),
        new PrimitiveInfo(14, "Oscillator", "SmC 8", PrimitiveLibrary.Supports(14)),
        new PrimitiveInfo(15, "One Shot", "SmC 9", PrimitiveLibrary.Supports(15)),
        new PrimitiveInfo(16, "Pulse Stretcher", "SmC 11", PrimitiveLibrary.Supports(16)),
        new PrimitiveInfo(17, "Retriggerable One Shot", "SmC 10", PrimitiveLibrary.Supports(17)),
        new PrimitiveInfo(18, "Buffer", "SmC 20", PrimitiveLibrary.Supports(18)),
        new PrimitiveInfo(19, "Truth Table", "SmC 28", PrimitiveLibrary.Supports(19)),
        new PrimitiveInfo(23, "Ring Counter", "SmC 17/451", PrimitiveLibrary.Supports(23)),
        new PrimitiveInfo(27, "Delay", "SmC 16", PrimitiveLibrary.Supports(27)),
        new PrimitiveInfo(29, "Multiple One Shots", "SmC 21", PrimitiveLibrary.Supports(29)),
        new PrimitiveInfo(30, "Analog Scaler", "SmC 24", PrimitiveLibrary.Supports(30)),
        new PrimitiveInfo(36, "Analog RAM", "SmC 34", PrimitiveLibrary.Supports(36)),
        new PrimitiveInfo(37, "Analog Step", "SmC 45", PrimitiveLibrary.Supports(37)),
        new PrimitiveInfo(38, "Analog Initialize", "SmC 32", PrimitiveLibrary.Supports(38)),
        new PrimitiveInfo(51, "Analog Equate", "SmC 38", PrimitiveLibrary.Supports(51)),
        new PrimitiveInfo(52, "Decade", "SmC 78", PrimitiveLibrary.Supports(52)),
        new PrimitiveInfo(56, "Debounce", "SmC 50", PrimitiveLibrary.Supports(56)),
        new PrimitiveInfo(58, "Serial Send / Multiple Serial Send", "SmC 89/1477", PrimitiveLibrary.Supports(58)),
        new PrimitiveInfo(61, "When (time of day)", "SmC 74", PrimitiveLibrary.Supports(61)),
        new PrimitiveInfo(62, "Clock Driver", "SmC 43", PrimitiveLibrary.Supports(62)),
        new PrimitiveInfo(67, "Analog Buffer", "SmC 46", PrimitiveLibrary.Supports(67)),
        new PrimitiveInfo(80, "Analog Rate Limiter", "SmC 66", PrimitiveLibrary.Supports(80)),
        new PrimitiveInfo(82, "Analog to Serial", "SmC 77", PrimitiveLibrary.Supports(82)),
        new PrimitiveInfo(85, "Intersystem Communications", "SmC 327", PrimitiveLibrary.Supports(85)),
        new PrimitiveInfo(87, "Analog Flip", "SmC 58", PrimitiveLibrary.Supports(87)),
        new PrimitiveInfo(88, "Serial Buffer", "SmC 56", PrimitiveLibrary.Supports(88)),
        new PrimitiveInfo(93, "Serial Gather", "SmC 62", PrimitiveLibrary.Supports(93)),
        new PrimitiveInfo(97, "Serial I/O", "SmC 101", PrimitiveLibrary.Supports(97)),
        new PrimitiveInfo(99, "Analog Scaler w/o Zero Pass", "SmC 65", PrimitiveLibrary.Supports(99)),
        new PrimitiveInfo(100, "Analog Sum", "SmC 57", PrimitiveLibrary.Supports(100)),
        new PrimitiveInfo(101, "Time-of-day serial relay (provisional)", "Not identified. Alarm Clock v2.0 macro: Clock Driver TOD$ in, one serial out to an Equipment Crosspoint join, P1 = 1. Run as a pass-through", PrimitiveLibrary.Supports(101)),
        new PrimitiveInfo(103, "Serial to Analog", "SmC 96", PrimitiveLibrary.Supports(103)),
        new PrimitiveInfo(106, "Analog To Digital", "SmC 84", PrimitiveLibrary.Supports(106)),
        new PrimitiveInfo(114, "Serial/Analog One-Shot", "SmC 72", PrimitiveLibrary.Supports(114)),
        new PrimitiveInfo(136, "Analog DivMod", "SmC 114", PrimitiveLibrary.Supports(136)),
        new PrimitiveInfo(138, "Button Presser", "SmC 99", PrimitiveLibrary.Supports(138)),
        new PrimitiveInfo(139, "Stepper", "SmC 98", PrimitiveLibrary.Supports(139)),
        new PrimitiveInfo(156, "Ethernet Offline Manager", "SmC 511/633", PrimitiveLibrary.Supports(156)),
        new PrimitiveInfo(203, "Logic Wave Delay", "SmC 408", PrimitiveLibrary.Supports(203)),
        new PrimitiveInfo(204, "Logic Wave Pulse", "SmC 409/1309", PrimitiveLibrary.Supports(204)),
        new PrimitiveInfo(205, "Make String Permanent", "SmC 858", PrimitiveLibrary.Supports(205)),
        new PrimitiveInfo(206, "Multiple NOT", "SmC 418", PrimitiveLibrary.Supports(206)),
        new PrimitiveInfo(207, "Interlock-Toggle", "SmC 419", PrimitiveLibrary.Supports(207)),
        new PrimitiveInfo(213, "Equipment Crosspoint Routing", "SmC 446", true), // runs as a dedicated node, not a library primitive
        new PrimitiveInfo(214, "Control Crosspoint Routing", "SmC 447", true), // runs as a dedicated node, not a library primitive
        new PrimitiveInfo(215, "Crosspoint Connect", "Connects a control crosspoint to an equipment crosspoint", true), // runs as a dedicated node, not a library primitive
        new PrimitiveInfo(220, "Analog Comparison (Full Set)", "SmC 492; output order unconfirmed", PrimitiveLibrary.Supports(220)),
        new PrimitiveInfo(223, "Analog Ramp (Bounds Limited)", "SmC 540", PrimitiveLibrary.Supports(223)),
        new PrimitiveInfo(224, "Analog Scaler with I/O Limits", "SmC 541", PrimitiveLibrary.Supports(224)),
        new PrimitiveInfo(230, "Analog Increment", "SmC 543", PrimitiveLibrary.Supports(230)),
        new PrimitiveInfo(236, "Analog Increment with Optional Feedback", "SmC 605", PrimitiveLibrary.Supports(236)),
        new PrimitiveInfo(241, "Stepper with Progress & Reset", "SmC 777", PrimitiveLibrary.Supports(241)),
        new PrimitiveInfo(254, "String Length", "SmC 2506", PrimitiveLibrary.Supports(254)),
        new PrimitiveInfo(268, "Touch panel page / smart object link", "Seen with Smart Graphics panels", true), // runs as a dedicated node, not a library primitive
    }.ToDictionary(p => p.Type);
}
