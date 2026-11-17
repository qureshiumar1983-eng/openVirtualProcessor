namespace OpenVP.Lpz;

/// <summary>What OpenVP knows about one logic-engine primitive type.</summary>
public sealed record PrimitiveInfo(int Type, string Name, string Notes, bool Implemented);

/// <summary>
/// The logic engine's primitive types, as identified so far by matching compiled records against
/// their SIMPL source (signal names on both sides). Names are working labels, not Crestron's.
/// Implemented = OpenVP can run it. Types not listed are unidentified.
/// </summary>
public static class PrimitiveCatalog
{
    public static readonly IReadOnlyDictionary<int, PrimitiveInfo> Known = new[]
    {
        new PrimitiveInfo(31, "Leading REM", "Compile header text; no runtime behavior.", true),
        new PrimitiveInfo(-204, "Processor", "Control system root record.", true),
        new PrimitiveInfo(-205, "Processor slot", "", true),
        new PrimitiveInfo(-203, "Card slot", "", true),
        new PrimitiveInfo(-201, "Card slot", "", true),
        new PrimitiveInfo(-12, "Device", "Slots, cards and IP-ID devices (panels, DM, NVX). Joins in inputs/outputs.", false),
        new PrimitiveInfo(-48, "SIMPL+ module", "Runs a compiled SIMPL+ .dll named in a string field.", false),
        new PrimitiveInfo(-200, "COM port", "", false),
        new PrimitiveInfo(-242, "IP serial device", "e.g. TCP/IP client to a camera; RX$/TX$ joins.", false),
        new PrimitiveInfo(1, "Pass-through", "", false),
        new PrimitiveInfo(2, "Clear/reset fan-out", "Seen for Interlock clear and one-shot groups.", false),
        new PrimitiveInfo(9, "One shot (edge)", "", false),
        new PrimitiveInfo(10, "Toggle", "", false),
        new PrimitiveInfo(11, "Interlock / preset select", "", false),
        new PrimitiveInfo(14, "Oscillator / pulse train", "Two time parameters.", false),
        new PrimitiveInfo(18, "Logic gate / OR group", "", false),
        new PrimitiveInfo(19, "Analog to digital pattern match", "", false),
        new PrimitiveInfo(27, "Delay to constant", "", false),
        new PrimitiveInfo(29, "Delay", "Time parameter in 1/100 s.", false),
        new PrimitiveInfo(36, "Analog RAM", "", false),
        new PrimitiveInfo(38, "Analog equate / init", "Numeric parameters per output.", false),
        new PrimitiveInfo(51, "Analog value select", "Digital inputs choose an analog value.", false),
        new PrimitiveInfo(58, "Serial one-shot / string compare", "String parameter.", false),
        new PrimitiveInfo(61, "Clock driver", "", false),
        new PrimitiveInfo(62, "Time of day consumer", "", false),
        new PrimitiveInfo(67, "Analog buffer / fan-out", "Most common primitive overall.", false),
        new PrimitiveInfo(88, "Make string permanent", "", false),
        new PrimitiveInfo(97, "Serial I/O", "String parameters for send/receive matching.", false),
        new PrimitiveInfo(114, "One shot (timed)", "", false),
        new PrimitiveInfo(138, "Stepper / sequence", "", false),
        new PrimitiveInfo(139, "Analog/serial selector", "", false),
        new PrimitiveInfo(202, "Console", "", false),
        new PrimitiveInfo(204, "One shot (sub-symbol)", "", false),
        new PrimitiveInfo(205, "Serial initialize", "", false),
        new PrimitiveInfo(206, "NOT group", "", false),
        new PrimitiveInfo(220, "Serial length / zero detect", "", false),
        new PrimitiveInfo(254, "Serial gather", "", false),
    }.ToDictionary(p => p.Type);
}
