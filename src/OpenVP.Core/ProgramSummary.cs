using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

public enum DeviceClass { Panel, Eisc, Other }

/// <summary>Tells touch panels, EISCs and other IP devices apart by the name a program or its IP table gives them.</summary>
public static class DeviceKinds
{
    private static readonly Regex PanelName = new(
        @"x?panel|touch|smart graphics|vt-?pro|crestron (app|mobile)|mobile|^(tsw?|tsr|tst|tpcs|dge|tps)[-\s]?[0-9a-z]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Panel, EISC or other, from the device's model or symbol name and, when known, its IP table address (127.0.0.2 is a link between slots).</summary>
    public static DeviceClass Classify(string? name, string? address = null)
    {
        if (address == "127.0.0.2") return DeviceClass.Eisc;
        if (string.IsNullOrWhiteSpace(name)) return DeviceClass.Other;
        if (name.Contains("intersystem", StringComparison.OrdinalIgnoreCase) || name.Contains("eisc", StringComparison.OrdinalIgnoreCase)
            || name.Contains("interprogram", StringComparison.OrdinalIgnoreCase)) return DeviceClass.Eisc;
        return PanelName.IsMatch(name.Trim()) ? DeviceClass.Panel : DeviceClass.Other;
    }
}

/// <summary>
/// What is in a program, in numbers: the touch panels and EISCs it has (by IP ID), its COM and IR ports, SIMPL+ modules,
/// user macros and signals. <see cref="Text"/> turns that into one plain sentence.
/// </summary>
/// <param name="Kind">"SIMPL#" or "SIMPL Windows".</param>
/// <param name="DevicesKnown">False for a SIMPL# program that hasn't started: its devices are made when it runs, so only <paramref name="Uses"/> is known.</param>
/// <param name="Uses">For SIMPL# before it starts: the device types it refers to.</param>
public sealed record ProgramSummary(
    string Kind,
    IReadOnlyList<string> PanelIpIds,
    IReadOnlyList<string> EiscIpIds,
    int OtherDevices,
    int ComPorts,
    int IrPorts,
    int SplusModules,
    int UserMacros,
    int Signals,
    bool DevicesKnown = true,
    IReadOnlyList<string>? Uses = null)
{
    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
    private static string Count(int n, string one, string many) => $"{N(n)} {(n == 1 ? one : many)}";

    /// <summary>"SIMPL Windows · 3 touch panels (IP IDs 03, 04, 05), 2 EISCs, 4 COM ports, 1 IR port, 12 SIMPL+ modules, 2 user macros, 1,284 signals."</summary>
    public string Text
    {
        get
        {
            var label = Kind == "SIMPL#" ? "SIMPL# Pro" : "SIMPL Windows";
            var parts = new List<string>();
            if (!DevicesKnown)
            {
                if (Uses is { Count: > 0 }) parts.Add("uses " + string.Join(", ", Uses.Take(4)) + (Uses.Count > 4 ? $" and {Uses.Count - 4} more" : ""));
                return label + " · " + (parts.Count == 0 ? "no devices found yet" : string.Join(", ", parts)) + ". Its devices appear when it starts.";
            }
            if (PanelIpIds.Count > 0)
                parts.Add(Count(PanelIpIds.Count, "touch panel", "touch panels") + $" ({(PanelIpIds.Count == 1 ? "IP ID" : "IP IDs")} {string.Join(", ", PanelIpIds)})");
            if (EiscIpIds.Count > 0) parts.Add(Count(EiscIpIds.Count, "EISC", "EISCs"));
            if (OtherDevices > 0) parts.Add(Count(OtherDevices, "other IP device", "other IP devices"));
            if (ComPorts > 0) parts.Add(Count(ComPorts, "COM port", "COM ports"));
            if (IrPorts > 0) parts.Add(Count(IrPorts, "IR port", "IR ports"));
            if (SplusModules > 0) parts.Add(Count(SplusModules, "SIMPL+ module", "SIMPL+ modules"));
            if (UserMacros > 0) parts.Add(Count(UserMacros, "user macro", "user macros"));
            if (Signals > 0) parts.Add(Count(Signals, "signal", "signals"));
            return label + " · " + (parts.Count == 0 ? "nothing wired yet" : string.Join(", ", parts)) + ".";
        }
    }
}

/// <summary>Implemented by a runtime that can describe its program.</summary>
public interface IProgramAnalysis
{
    /// <summary>What the program contains, from what the runtime already has after loading.</summary>
    ProgramSummary Summarize();

    /// <summary>Things in the program OpenVP can't run yet, known right after loading (before it starts).</summary>
    IReadOnlyList<ReadinessProblem> Problems();

    /// <summary>Why this program can't run at all (for example a .dll with no CrestronControlSystem class), or null.</summary>
    string? Blocker { get; }
}
