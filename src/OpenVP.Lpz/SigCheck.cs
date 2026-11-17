namespace OpenVP.Lpz;

/// <summary>
/// Checks that a .sig belongs to the same compile as the logic, by comparing the signal types it claims
/// with the types the IP device records imply for their joins (digital, analog, serial groups).
/// </summary>
public static class SigCheck
{
    public static bool Matches(BinProgram logic, SigTable sig, out double agreement)
    {
        int checkedCount = 0, agree = 0;
        foreach (var d in logic.Symbols.Where(s => s.Type == -12 && s.IpId != null))
        {
            var dig = d.FieldInt(10) ?? 0;
            var anaEnd = d.FieldInt(12) ?? dig;
            for (int i = 0; i < d.Inputs.Length; i++)
            {
                var s = d.Inputs[i];
                if (s <= 1 || !sig.Entries.TryGetValue(s, out var e) || e.Kind == SigFileKind.Constant) continue;
                var expected = i < dig ? SigFileKind.Digital : i < anaEnd ? SigFileKind.Analog : SigFileKind.Serial;
                checkedCount++;
                if (e.Kind == expected) agree++;
            }
        }
        agreement = checkedCount == 0 ? 1 : (double)agree / checkedCount;
        return checkedCount == 0 || agreement >= 0.98;
    }
}
