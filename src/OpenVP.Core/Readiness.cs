namespace OpenVP.Core;

public enum ReadinessLevel { WillRun, PartlyRuns, WontRun }

/// <summary>One thing in a program OpenVP doesn't support yet.</summary>
public sealed record ReadinessProblem(string Kind, string Name, string Detail = "");

/// <summary>
/// The answer to "will this program run?", shown before it is loaded. It is advice: loading is never blocked.
/// </summary>
public sealed record ReadinessVerdict(ReadinessLevel Level, string Headline, string? Reason, IReadOnlyList<ReadinessProblem> Problems)
{
    /// <summary><c>will-run</c>, <c>partly</c> or <c>wont-run</c>, for the page's colours.</summary>
    public string Code => Level switch { ReadinessLevel.WillRun => "will-run", ReadinessLevel.PartlyRuns => "partly", _ => "wont-run" };
}

public static class Readiness
{
    public const string FileTypes = ".cpz or .dll (SIMPL# Pro), .lpz (compiled SIMPL Windows) or .smw (SIMPL Windows source)";

    /// <summary>The verdict from what loading found. <paramref name="blocker"/> and <paramref name="loadError"/> make it "Won't run".</summary>
    public static ReadinessVerdict Evaluate(IEnumerable<ReadinessProblem> problems, string? loadError = null, string? blocker = null)
    {
        if (!string.IsNullOrWhiteSpace(loadError)) return WontRun(loadError.Trim());
        if (!string.IsNullOrWhiteSpace(blocker)) return WontRun(blocker.Trim());
        var list = problems.GroupBy(p => (p.Kind, p.Name)).Select(g => g.First()).OrderBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
        if (list.Count == 0) return new ReadinessVerdict(ReadinessLevel.WillRun, "Will run", null, list);
        var n = list.Count;
        return new ReadinessVerdict(ReadinessLevel.PartlyRuns,
            $"Will partly run — {n} thing{(n == 1 ? "" : "s")} {(n == 1 ? "isn't" : "aren't")} supported yet", null, list);
    }

    public static ReadinessVerdict WontRun(string reason) =>
        new(ReadinessLevel.WontRun, "Won't run", reason, Array.Empty<ReadinessProblem>());

    /// <summary>Said when no runtime handles the file's type.</summary>
    public static ReadinessVerdict UnsupportedFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return WontRun($"OpenVP can't run {(ext.Length == 0 ? "this kind of file" : "'" + ext + "' files")}. Choose a {FileTypes}.");
    }

    /// <summary>A load error in plain words (the exception's own message is kept when it already reads well).</summary>
    public static string Explain(Exception e) => e switch
    {
        BadImageFormatException => "This isn't a .NET program (the file isn't a valid assembly), so it can't be a SIMPL# program.",
        InvalidDataException when e.Message.Contains("zip", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("central directory", StringComparison.OrdinalIgnoreCase) =>
            "The file isn't a valid compressed program archive (it may be damaged or the wrong kind of file).",
        FileNotFoundException => "A file the program needs wasn't found: " + e.Message,
        _ => string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.InnerException is { } i && e.Message.StartsWith("Exception has been thrown", StringComparison.Ordinal) ? i.Message : e.Message
    };
}
