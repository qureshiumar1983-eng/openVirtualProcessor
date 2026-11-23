using System.Text;
using System.Text.RegularExpressions;

namespace OpenVP.Core.Community;

/// <summary>Takes out of a piece of text everything that could identify a person, a machine or a program: addresses, e-mails, keys and tokens, user and computer names, and the folders of a file path (a file name stays).</summary>
public static class Redactor
{
    /// <summary>Extra words to scrub wherever they appear (the user name and computer name of this machine). Set once at start-up.</summary>
    public static IReadOnlyList<string> Names { get; set; } = [];

    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new(@"\b[a-z][a-z0-9+.\-]*://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "<url>"),
        (new(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.Compiled), "<email>"),
        (new(@"\bovp_[A-Za-z0-9_\-]{6,}", RegexOptions.Compiled), "<token>"),
        (new(@"\b(key|token|password|passwd|pwd|secret|apikey|api[-_ ]key|bearer|authorization)\b(\s*[:=]\s*)(?!<)\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "$1=<redacted>"),
        // File paths: keep the file name only.
        (new(@"\\\\[^\s\\/:*?""<>|]+(?:\\[^\s\\/:*?""<>|]+)+\\([^\s\\/:*?""<>|]+\.[A-Za-z0-9]{1,6})(?![\w.])", RegexOptions.Compiled), "$1"),
        (new(@"\\\\[^\s\\/:*?""<>|]+(?:\\[^\s\\/:*?""<>|]+)*", RegexOptions.Compiled), "<path>"),
        (new(@"\b[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*([^\\/:*?""<>|\r\n]*)", RegexOptions.Compiled), "$1"),
        (new(@"(?<![\w:/.\-])~?/(?:[\w.\-@+ ]+/)+([\w.\-@+]+)", RegexOptions.Compiled), "$1"),
        (new(@"(?<![\w:])(?:[0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled), "<mac>"),
        (new(@"(?<![\w.])(?<!Version=)(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?::\d{1,5})?(?![\w.])", RegexOptions.Compiled), "<ip>"),
        (new(@"(?<![\w:.])(?:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){7})|(?:(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?)|(?:::(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})))(?![\w:])", RegexOptions.Compiled), "<ip>"),
        (new(@"\b(?=[A-Za-z0-9+/_\-]*\d)(?=[A-Za-z0-9+/_\-]*[A-Za-z])[A-Za-z0-9+/_\-]{32,}={0,2}", RegexOptions.Compiled), "<redacted>"),
        (new(@"\b[0-9A-Fa-f]{24,}\b", RegexOptions.Compiled), "<redacted>"),
    ];

    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text;
        foreach (var (p, r) in Rules) s = p.Replace(s, r);
        foreach (var n in Names)
            if (n.Length >= 3) s = Regex.Replace(s, @"(?<![A-Za-z0-9])" + Regex.Escape(n) + @"(?![A-Za-z0-9])", "<name>", RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>What <see cref="Clean"/> would change in text a person typed, as short descriptions. Empty when the text is clean.</summary>
    public static IReadOnlyList<string> Findings(string? text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text)) return found;
        var cleaned = Clean(text);
        if (cleaned == text) return found;
        foreach (var (label, marker) in new[] { ("a web address", "<url>"), ("an e-mail address", "<email>"), ("a token", "<token>"), ("a key or password", "=<redacted>"), ("a network path", "<path>"), ("an IP address", "<ip>"), ("a MAC address", "<mac>"), ("a user or computer name", "<name>"), ("a long key-like string", "<redacted>") })
            if (cleaned.Contains(marker) && !text.Contains(marker)) found.Add(label);
        if (found.Count == 0) found.Add("a file path");
        return found;
    }
}

public sealed record IssueItem(string Kind, string Name, string Detail, int Seen);

/// <param name="Truncated">Not every item fits in the link.</param>
public sealed record IssueDraft(string Title, string Body, int ItemCount, int Included, bool Truncated);

/// <summary>
/// Builds the text for a "new issue" on the project's GitHub repository from the unsupported items a person picked. Nothing is sent: OpenVP shows the
/// text and a link, and the person's browser opens the link if they choose to. Only symbol, type and API names go in, plus the OpenVP version,
/// the operating system family and the program type. Everything goes through <see cref="Redactor"/> first.
/// </summary>
public static class IssueReport
{
    public const string Repo = "qureshiumar1983-eng/openVirtualProcessor";
    /// <summary>GitHub refuses very long addresses (around 8 KB); stay well under.</summary>
    public const int MaxUrlLength = 7000;
    public const int MaxTitle = 120;

    public static string OsName()
    {
        var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Other";
        return $"{os} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
    }

    public static IssueDraft Draft(string version, string os, string? programType, IReadOnlyList<IssueItem> items, string? note = null)
    {
        var clean = items.Select(i => new IssueItem(Redactor.Clean(i.Kind), Redactor.Clean(i.Name), Redactor.Clean(i.Detail), i.Seen)).ToList();
        var title = Title(version, clean);
        // The body is built for the number of items that fits the link, so the preview shows exactly what would be sent.
        var included = clean.Count;
        string body;
        while (true)
        {
            body = Body(version, os, programType, clean, included, note);
            if (UrlLength(title, body) <= MaxUrlLength || included == 0) break;
            included = included > 20 ? included - Math.Max(1, included / 10) : included - 1;
        }
        return new IssueDraft(title, body, clean.Count, included, included < clean.Count);
    }

    private static string Title(string version, IReadOnlyList<IssueItem> items)
    {
        var first = items.FirstOrDefault();
        var t = first == null ? $"Unsupported feature in OpenVP {version}" : $"Unsupported: {first.Name}" + (items.Count > 1 ? $" (+{items.Count - 1} more)" : "");
        t = t.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length > MaxTitle ? t[..(MaxTitle - 1)] + "…" : t;
    }

    private static string Body(string version, string os, string? programType, IReadOnlyList<IssueItem> items, int included, string? note)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(note)) sb.Append(Redactor.Clean(note.Trim())).Append("\n\n");
        sb.Append("**OpenVP version:** ").Append(Redactor.Clean(version)).Append('\n');
        sb.Append("**OS:** ").Append(Redactor.Clean(os)).Append('\n');
        if (!string.IsNullOrWhiteSpace(programType)) sb.Append("**Program type:** ").Append(Redactor.Clean(programType)).Append('\n');
        sb.Append("\n### Unsupported in my program\n\n");
        if (items.Count == 0) sb.Append("_(describe what you were trying to run)_\n");
        for (var i = 0; i < included; i++)
        {
            var it = items[i];
            sb.Append("- **").Append(OneLine(it.Kind)).Append("** `").Append(OneLine(it.Name).Replace("`", "'")).Append('`');
            if (it.Detail.Length > 0) sb.Append(" — ").Append(OneLine(it.Detail));
            if (it.Seen > 1) sb.Append(" (seen in ").Append(it.Seen).Append(" program loads)");
            sb.Append('\n');
        }
        if (included < items.Count) sb.Append($"\n_…and {items.Count - included} more not listed, to keep this link short enough for GitHub. The full list is on the compatibility page of OpenVP._\n");
        sb.Append("\n---\n_Made with OpenVP's report button. It lists symbol and API names only: no program contents, file paths, addresses, keys or names._\n");
        return sb.ToString();
    }

    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    public static string Url(string title, string body) =>
        $"https://github.com/{Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

    public static int UrlLength(string title, string body) => Url(title, body).Length;

    /// <summary>Fits text a person has edited into the link: cuts the body at a line break if needed and says so. Returns the address.</summary>
    public static string Fit(string title, string body, out string finalBody, out bool truncated)
    {
        title = OneLine(title ?? "");
        if (title.Length > MaxTitle) title = title[..(MaxTitle - 1)] + "…";
        body ??= "";
        truncated = false;
        finalBody = body;
        const string note = "\n\n_(Cut short to fit GitHub's link length.)_";
        if (UrlLength(title, body) > MaxUrlLength)
        {
            truncated = true;
            int lo = 0, hi = body.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (UrlLength(title, body[..mid] + note) <= MaxUrlLength) lo = mid; else hi = mid - 1;
            }
            var cut = body[..lo];
            var nl = cut.LastIndexOf('\n');
            if (nl > cut.Length / 2) cut = cut[..nl];
            if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
            finalBody = cut + note;
        }
        return Url(title, finalBody);
    }
}
