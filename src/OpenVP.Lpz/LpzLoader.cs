namespace OpenVP.Lpz;

/// <summary>
/// Placeholder until the compiled-logic engine lands: recognises .lpz files and explains how much of
/// the program OpenVP can run, instead of failing with "unsupported file type".
/// </summary>
public static class LpzInspect
{
    public static bool IsLpz(string path) => Path.GetExtension(path).Equals(".lpz", StringComparison.OrdinalIgnoreCase);

    /// <summary>Report for an .lpz, using a .sig next to it (same name) if present.</summary>
    public static string Report(string path, string? sigPath = null)
    {
        var pkg = LpzPackage.Open(path);
        sigPath ??= Path.ChangeExtension(path, ".sig");
        var sig = File.Exists(sigPath) ? SigTable.Parse(File.ReadAllBytes(sigPath)) : null;
        return LpzReport.Build(pkg, sig, LpzSurvey.ReadSourceSmw(path));
    }
}
