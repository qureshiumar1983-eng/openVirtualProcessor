using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace OpenVP.Core.Ops;

/// <summary>A text file as the editor shows it.</summary>
/// <param name="Text">The content with <c>\n</c> line breaks, whatever the file used.</param>
/// <param name="Tag">Identifies this content (a hash). The editor sends it back when saving; if the file on disk has a different one, somebody else changed it.</param>
public sealed record EditableText(string Text, string Tag, long Size, DateTimeOffset Modified, string Newline, string Encoding, bool Bom);

/// <summary>Where a JSON or XML file stops being valid.</summary>
public sealed record TextProblem(int Line, int Column, string Message);

public sealed class EditConflictException(string message) : Exception(message);
public sealed class EditInvalidException(TextProblem problem) : Exception(problem.Message) { public TextProblem Problem { get; } = problem; }
public sealed class EditTooLargeException(string message) : Exception(message);

/// <summary>
/// Reads and saves small text files (config files in a slot's NVRAM and User folders) for the in-browser editor. It keeps the file's line breaks, byte order
/// mark and encoding; refuses files over <see cref="MaxBytes"/> and anything with binary content; checks JSON and XML and says which line is wrong; refuses
/// to overwrite a file that changed on disk since it was opened; and keeps the previous content in the trash (see <see cref="Trash.Keep"/>) so a save can be undone.
/// </summary>
public static class TextFileEditor
{
    public const long MaxBytes = 2L * 1024 * 1024;

    /// <summary>File types the editor opens: plain text and the config formats programs use.</summary>
    public static readonly string[] Extensions =
    [
        ".json", ".xml", ".txt", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".config", ".usp", ".ush", ".inc", ".smw", ".log", ".md", ".yaml", ".yml", ".properties", ".html", ".htm", ".css", ".js", ".sql", ".bat", ".sh", ".ps1"
    ];

    public static bool IsEditableName(string? name) => !string.IsNullOrEmpty(name) && Extensions.Contains(Path.GetExtension(name).ToLowerInvariant());

    public static string TagOf(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..20].ToLowerInvariant();

    public static EditableText Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No such file.");
        if (!IsEditableName(info.Name)) throw new ArgumentException($"The editor opens text files ({string.Join(", ", Extensions.Take(8))} and similar), not '{info.Extension}' files.");
        if (info.Length > MaxBytes) throw new EditTooLargeException($"This file is {info.Length / 1048576.0:0.#} MB; the editor opens files up to {MaxBytes / 1048576} MB. Download it to edit it.");
        byte[] data;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            data = new byte[fs.Length];
            fs.ReadExactly(data);
        }
        return Decode(data, info.LastWriteTimeUtc);
    }

    public static EditableText Decode(byte[] data, DateTime modifiedUtc)
    {
        if (Array.IndexOf(data, (byte)0) >= 0) throw new ArgumentException("This file has binary content, so it can't be edited as text.");
        var bom = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;
        string text, enc;
        try { text = new UTF8Encoding(false, true).GetString(data, bom ? 3 : 0, data.Length - (bom ? 3 : 0)); enc = "utf-8"; }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(data); enc = "latin1"; }
        var crlf = text.Split("\r\n").Length - 1;
        var lf = text.Count(c => c == '\n') - crlf;
        var nl = crlf > lf ? "crlf" : "lf";
        text = text.Replace("\r\n", "\n");
        return new EditableText(text, TagOf(data), data.Length, new DateTimeOffset(DateTime.SpecifyKind(modifiedUtc, DateTimeKind.Utc)), nl, enc, bom);
    }

    /// <summary>Null if the text is fine (or the file type isn't checked); else the line and message of the first error.</summary>
    public static TextProblem? Validate(string fileName, string text)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".json":
                try
                {
                    var r = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
                    if (text.Trim().Length == 0) return new TextProblem(1, 1, "The file is empty. JSON needs a value, for example {} or [].");
                    while (r.Read()) { }
                    return null;
                }
                catch (JsonException e)
                {
                    var line = (int)(e.LineNumber ?? 0) + 1;
                    var col = (int)(e.BytePositionInLine ?? 0) + 1;
                    return new TextProblem(line, col, Plain(e.Message));
                }
            case ".xml":
                try
                {
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                    using var xr = XmlReader.Create(new StringReader(text), settings);
                    while (xr.Read()) { }
                    return null;
                }
                catch (XmlException e) { return new TextProblem(Math.Max(1, e.LineNumber), Math.Max(1, e.LinePosition), e.Message.Split('.')[0].Trim() + "."); }
            default:
                return null;
        }
    }

    private static string Plain(string m)
    {
        // "'}' is invalid after a property name. Expected a ':'. Path: $.a | LineNumber: 3 | BytePositionInLine: 4."
        var i = new[] { m.IndexOf(" Path:", StringComparison.Ordinal), m.IndexOf(" LineNumber:", StringComparison.Ordinal) }.Where(x => x > 0).DefaultIfEmpty(-1).Min();
        return (i > 0 ? m[..i] : m).Trim();
    }

    /// <summary>
    /// Saves <paramref name="text"/> over the file. <paramref name="expectedTag"/> is the tag the editor opened (null skips the check, for a new file).
    /// Throws <see cref="EditConflictException"/> if the file changed on disk, <see cref="EditInvalidException"/> for bad JSON or XML (unless <paramref name="allowInvalid"/>),
    /// <see cref="EditTooLargeException"/> over the limit. Returns the new state and the trash entry id that undoes the save.
    /// </summary>
    public static (EditableText State, string? UndoId) Save(string path, string text, string? expectedTag, Trash trash, int? slot, bool force = false, bool allowInvalid = false)
    {
        var name = Path.GetFileName(path);
        if (!IsEditableName(name)) throw new ArgumentException("The editor only saves text files.");
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new EditTooLargeException($"The text is over the {MaxBytes / 1048576} MB limit.");
        if (!allowInvalid && Validate(name, text) is { } problem) throw new EditInvalidException(problem);
        EditableText? cur = null;
        if (File.Exists(path))
        {
            cur = Read(path);
            if (!force && expectedTag != null && cur.Tag != expectedTag)
                throw new EditConflictException("This file changed on disk after you opened it. Reload it to see the new content, or save anyway to replace it.");
        }
        else if (!force && expectedTag != null)
            throw new EditConflictException("This file was deleted or renamed after you opened it. Save anyway to create it again.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string? undo = null;
        if (cur != null) undo = trash.Keep("edit", $"earlier content of {name}", slot, path)?.Id;
        var nl = cur?.Newline == "crlf" ? "\r\n" : "\n";
        var enc = cur?.Encoding == "latin1" && text.All(c => c <= 255) ? Encoding.Latin1 : new UTF8Encoding(false);
        var body = enc.GetBytes(nl == "\n" ? text : text.Replace("\n", "\r\n"));
        if (cur?.Bom == true) body = [0xEF, 0xBB, 0xBF, .. body];
        var tmp = path + ".openvp-tmp";
        File.WriteAllBytes(tmp, body);
        File.Move(tmp, path, true);
        return (Decode(body, File.GetLastWriteTimeUtc(path)), undo);
    }
}
