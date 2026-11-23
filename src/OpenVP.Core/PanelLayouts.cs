using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenVP.Core;

/// <summary>Thrown for a layout that is refused (bad name, not the expected shape, too big).</summary>
public sealed class LayoutException(string message) : Exception(message);

public sealed record LayoutInfo(string Name, int Slot, string? Program, DateTimeOffset SavedAt, int Controls);

/// <summary>
/// Saved layouts of a generated panel, kept per slot in <c>data/layouts/slotNN/NAME.json</c>. A layout holds, per control id, where it
/// sits (x, y, w, h in grid cells), a new label and whether it's hidden, plus the program name it was made for. The server only
/// checks shape and size; the panel page decides what to do with a layout whose controls are no longer there.
/// Layout names are limited to letters, digits, space, dot, dash and underscore, so a name can never reach outside the folder.
/// </summary>
public sealed class PanelLayouts
{
    public const int MaxBytes = 512 * 1024;
    public const int MaxControls = 8000;
    public const int MaxLayoutsPerSlot = 100;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly object _gate = new();

    public PanelLayouts(string dataDirectory) => _root = Path.GetFullPath(Path.Combine(dataDirectory, "layouts"));

    public static string CleanName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 60) throw new LayoutException("Give the layout a name of 1-60 characters.");
        foreach (var c in n)
            if (!(char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_')) throw new LayoutException("A layout name can use letters, digits, spaces, dots, dashes and underscores.");
        if (n.StartsWith('.') || n.EndsWith('.')) throw new LayoutException("A layout name can't start or end with a dot.");
        return n;
    }

    private string Dir(int slot) => Path.Combine(_root, $"slot{slot:00}");
    private string FileFor(int slot, string name) => Path.Combine(Dir(slot), CleanName(name) + ".json");

    public IReadOnlyList<LayoutInfo> List(int slot)
    {
        var res = new List<LayoutInfo>();
        var dir = Dir(slot);
        if (!Directory.Exists(dir)) return res;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var n = JsonNode.Parse(File.ReadAllText(f))?.AsObject();
                if (n == null) continue;
                var controls = n["controls"] as JsonObject;
                res.Add(new LayoutInfo(Path.GetFileNameWithoutExtension(f), slot, (string?)n["program"], DateTimeOffset.TryParse((string?)n["savedAt"], out var t) ? t : File.GetLastWriteTimeUtc(f), controls?.Count ?? 0));
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException) { }
        }
        return res.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The layout as stored, or null.</summary>
    public JsonObject? Get(int slot, string name)
    {
        var f = FileFor(slot, name);
        if (!File.Exists(f)) return null;
        try { return JsonNode.Parse(File.ReadAllText(f))?.AsObject(); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    /// <summary>Checks a layout's shape and returns it normalised (only the fields we know, numbers in range). Throws <see cref="LayoutException"/>.</summary>
    public static JsonObject Validate(JsonElement body, string? program)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new LayoutException("A layout is a JSON object.");
        if (body.GetRawText().Length > MaxBytes) throw new LayoutException("That layout is too big.");
        var o = new JsonObject { ["version"] = 1, ["program"] = program, ["savedAt"] = DateTimeOffset.UtcNow.ToString("O") };
        if (body.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) o["title"] = Clip(t.GetString());
        if (body.TryGetProperty("columns", out var c) && c.TryGetInt32(out var cols)) o["columns"] = Math.Clamp(cols, 1, 48);
        if (body.TryGetProperty("groups", out var gr) && gr.ValueKind == JsonValueKind.Object)
        {
            var go = new JsonObject();
            foreach (var p in gr.EnumerateObject().Take(500))
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                var g = new JsonObject();
                if (p.Value.TryGetProperty("title", out var gt) && gt.ValueKind == JsonValueKind.String) g["title"] = Clip(gt.GetString());
                if (p.Value.TryGetProperty("hidden", out var gh) && gh.ValueKind is JsonValueKind.True or JsonValueKind.False) g["hidden"] = gh.GetBoolean();
                if (p.Value.TryGetProperty("collapsed", out var gc) && gc.ValueKind is JsonValueKind.True or JsonValueKind.False) g["collapsed"] = gc.GetBoolean();
                go[Clip(p.Name, 200)!] = g;
            }
            o["groups"] = go;
        }
        var controls = new JsonObject();
        if (body.TryGetProperty("controls", out var cs))
        {
            if (cs.ValueKind != JsonValueKind.Object) throw new LayoutException("\"controls\" must be an object keyed by control id.");
            foreach (var p in cs.EnumerateObject())
            {
                if (controls.Count >= MaxControls) throw new LayoutException($"More than {MaxControls} controls.");
                if (p.Value.ValueKind != JsonValueKind.Object) throw new LayoutException($"Control \"{Clip(p.Name, 40)}\" must be an object.");
                var e = new JsonObject();
                foreach (var f in new[] { "x", "y", "w", "h" })
                    if (p.Value.TryGetProperty(f, out var v))
                    {
                        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) throw new LayoutException($"Control \"{Clip(p.Name, 40)}\": \"{f}\" must be a whole number.");
                        e[f] = Math.Clamp(n, f is "x" or "y" ? 0 : 1, 10000);
                    }
                if (p.Value.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String) e["label"] = Clip(l.GetString());
                if (p.Value.TryGetProperty("hidden", out var h) && h.ValueKind is JsonValueKind.True or JsonValueKind.False) e["hidden"] = h.GetBoolean();
                controls[Clip(p.Name, 300)!] = e;
            }
        }
        o["controls"] = controls;
        return o;
    }

    private static string? Clip(string? s, int max = 120) => s == null ? null : (s.Length > max ? s[..max] : s);

    public LayoutInfo Save(int slot, string name, JsonElement body, string? program)
    {
        var clean = CleanName(name);
        var layout = Validate(body, program);
        lock (_gate)
        {
            var exists = File.Exists(FileFor(slot, clean));
            if (!exists && List(slot).Count >= MaxLayoutsPerSlot) throw new LayoutException($"A slot can keep {MaxLayoutsPerSlot} layouts. Delete one first.");
            Directory.CreateDirectory(Dir(slot));
            var f = FileFor(slot, clean);
            var tmp = f + ".tmp";
            File.WriteAllText(tmp, layout.ToJsonString(Web));
            File.Move(tmp, f, true);
        }
        return new LayoutInfo(clean, slot, program, DateTimeOffset.Parse((string)layout["savedAt"]!), ((JsonObject)layout["controls"]!).Count);
    }

    public bool Delete(int slot, string name)
    {
        var f = FileFor(slot, name);
        lock (_gate) { if (!File.Exists(f)) return false; File.Delete(f); return true; }
    }
}
