using OpenVP.Core;

namespace OpenVP.Simpl;

/// <summary>
/// Where a device extender's joins sit on its device: panel reserved joins, button toolbars, app control. In a
/// .smw an extender is a symbol under the device (PrH) whose pins carry no join numbers; pin k of a kind is
/// join base + k. The compiler writes the bases into the extender's record (fields 27-32, see
/// <c>LpzRuntime.ExtenderNode</c>), so these were read off real compiled programs (a private test set, not in this repo) by
/// pairing each extender with its record through signal names. Bases are the same for inputs (feedback) and
/// outputs (presses) in every program surveyed. A kind no program showed has no entry, and its pins stay unmapped.
/// </summary>
public static class ExtenderJoins
{
    /// <summary>Base per kind (digital, analog, serial), or null where unknown.</summary>
    public sealed record Bases(uint? Digital, uint? Analog, uint? Serial)
    {
        public uint? Of(SigKind k) => k switch { SigKind.Digital => Digital, SigKind.Analog => Analog, _ => Serial };
    }

    /// <summary>By SmC code, as surveyed.</summary>
    private static readonly Dictionary<int, Bases> ByCode = new()
    {
        [5973] = new(18899, null, null),   // TSW-560 Hard Buttons Reserved Joins
        [5977] = new(18899, null, null),   // TSW-1060 Hard Buttons Reserved Joins
        [6486] = new(18499, null, null),   // UC-ENGINE LYNC Meeting Room Reserved Joins
        [6615] = new(17899, null, null),   // UC-ENGINE System2 Reserved Joins
        [6858] = new(28499, null, null),   // TSW-1070 App Control
        [6862] = new(30200, null, null),   // TSW-1070 Button Toolbar
        [6873] = new(30149, null, null),   // TSW-1070 Zoom Room App Reserved Joins
        [16509] = new(17299, 17299, null), // UC-ENGINE-FLEX-TEAMS Audio Reserved Joins
        [16513] = new(17899, null, null),  // UC-ENGINE-FLEX-TEAMS System2 Reserved Joins
        [16514] = new(27299, null, null),  // UC-ENGINE-FLEX-TEAMS System4 Reserved Joins
        [16515] = new(27726, null, null),  // UC-ENGINE-FLEX-TEAMS UC-Engine Reserved Joins
        [16666] = new(18499, null, null),  // UC-ENGINE-FLEX-TEAMS Meeting Room Reserved Joins
        [17309] = new(28499, null, null),  // TST-1080 App Control
        [17313] = new(30200, null, null),  // TST-1080 Button Toolbar
        [17317] = new(null, 17199, null),  // TST-1080 System Reserved Joins
        [17320] = new(27299, 27199, null), // TST-1080 System4 Reserved Joins
    };

    /// <summary>
    /// Extenders of other panel models, by the end of their name, where every surveyed model with that name agreed
    /// (Button Toolbar: TSW-1070 and TST-1080; App Control: TSW-1070 and TST-1080; Hard Buttons Reserved Joins:
    /// TSW-560 and TSW-1060). Inferred for the models not surveyed (TS-770, TS-1070, TSW-770...).
    /// </summary>
    private static readonly (string Suffix, Bases Bases)[] BySuffix =
    {
        ("Button Toolbar", new(30200, null, null)),
        ("App Control", new(28499, null, null)),
        ("Hard Buttons Reserved Joins", new(18899, null, null)),
    };

    public static Bases? For(int code, string name)
    {
        if (ByCode.TryGetValue(code, out var b)) return b;
        name = name.Trim();
        foreach (var (suffix, bases) in BySuffix)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return bases;
        return null;
    }
}
