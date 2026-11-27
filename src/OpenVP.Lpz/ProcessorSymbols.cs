namespace OpenVP.Lpz;

/// <summary>
/// The processor's own built-in symbols, the ones SIMPL Windows puts under the System Monitor card instead of in the
/// logic: "System Control" (SmC 6543 on an RMC4, DvC 6097 as a device). Its digital outputs are the processor telling
/// the program about itself; the program in slot N reads them as feedback.
///
/// How a compiled program carries it, checked against a real RMC4 program and its .smw: the System Monitor card is
/// the top-level hardware record "Slot-08" (type -12, no IP ID), and System Control is its child "Slot-01" (the fixed
/// slot 1 of the card; System Information is "Slot-02"). Like every hardware record it holds only the pins that are
/// wired: field 11 counts the digital outputs up to the highest wired one, and output n is the symbol's On.
///
/// What the outputs mean is not in Crestron's public symbol help beyond their names, which programmers repeat in
/// their signal names (a "program 1 start feedback" signal on output 1). Only output 1 is driven: program-start
/// feedback, held high while the program runs. The other outputs stay low and the inputs (reboot, restart a
/// program, ...) are not acted on.
/// </summary>
public static class ProcessorSymbols
{
    /// <summary>The slot the System Monitor card sits at, by processor: 8 on an RMC4, 7 on a CP4 (its .smw has
    /// "System Monitor" at Ad=07). A program for a processor that puts it elsewhere would need its slot added here.</summary>
    private static readonly int[] SystemMonitorSlots = [7, 8];

    /// <summary>The digital output that is the program-start feedback.</summary>
    public const int ProgramStartOutput = 1;

    /// <summary>Whether a .smw symbol library name ("RMC4 System Control", "CP3 System Control") is the processor's System Control.</summary>
    public static bool IsSystemControlName(string? name) =>
        name != null && name.Contains("System Control", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a compiled hardware record is the processor's System Control (see the class remarks).</summary>
    public static bool IsSystemControl(BinProgram logic, BinSymbol sym)
    {
        if (sym.Type != -12 || sym.Path != "Slot-01" || sym.IpId != null) return false;
        if (sym.FieldInt(7) is not int parent || parent <= 0 || parent >= logic.Symbols.Count) return false;
        var card = logic.Symbols[parent];
        return card.Type == -12 && card.Path.StartsWith("Slot-", StringComparison.Ordinal) && card.FieldInt(6) is int slot && SystemMonitorSlots.Contains(slot) &&card.FieldInt(7) is 0 or null;
    }
}
