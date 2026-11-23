using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenVP.Core;

/// <summary>The joins of one smart-graphics object. 0 = not set.</summary>
public sealed record SgJoins(uint Press = 0, uint Feedback = 0, uint AnalogIn = 0, uint AnalogOut = 0, uint SerialIn = 0, uint SerialOut = 0, uint SmartObject = 0);

public sealed record SgObject(string Type, string Name, int X, int Y, int W, int H, string? Text, SgJoins Joins, string? Image, string? Detail, IReadOnlyList<string>? Items = null);

public sealed record SgPage(string Name, string Kind, int Width, int Height, IReadOnlyList<SgObject> Objects);

/// <summary>What the importer understood and what it did not. <see cref="Unsupported"/> lines are shown to the person, never swallowed.</summary>
public sealed record SgReport(string Format, IReadOnlyDictionary<string, int> Supported, IReadOnlyList<string> Unsupported, IReadOnlyList<string> Notes);

public sealed record SgProject(string Name, string Format, IReadOnlyList<SgPage> Pages, SgReport Report);

public sealed record SgInfo(int Slot, uint IpId, string Name, string Format, int Pages, int Objects, DateTimeOffset UploadedAt);

/// <summary>
/// Reads a touch-panel project (a VT Pro-e <c>.vtz</c>, a Crestron Core 3 UI <c>.c3p</c>, a bare XML/JSON file, or OpenVP's own
/// <c>.openvp-sg.json</c>) into a small model the browser can draw: pages and subpages, buttons, text, images, sliders, gauges, lists
/// and boxes, with their joins. Nothing here is Crestron code. The two Crestron formats are not publicly documented, so they are read
/// by looking for pages and objects with names, positions, sizes and join numbers in whatever XML or JSON the archive holds; a binary
/// project file (the classic <c>.vtp</c> inside a <c>.vtz</c>) cannot be decoded and is reported as such. Everything not drawn is listed in the report.
/// </summary>
public static class SmartGraphics
{
    public const int MaxTextBytes = 24 * 1024 * 1024;
    public const int MaxObjects = 20000;
    private const int MaxDepth = 80;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public static JsonSerializerOptions JsonOptions => Web;

    private static readonly HashSet<string> ImageExt = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    /// <summary>Reads a project. <paramref name="images"/> receives (original file name, bytes) of every raster image found, for the caller to keep.</summary>
    public static SgProject Read(Stream file, string fileName, List<(string name, byte[] data)>? images = null)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ctx = new Ctx();
        // Try a zip first (vtz, c3p), then a bare text file.
        var head = new byte[4];
        var n = file.Read(head, 0, 4);
        file.Position = 0;
        if (n == 4 && head[0] == 'P' && head[1] == 'K')
        {
            ReadZip(file, ctx, images, ext.TrimStart('.'));
            ctx.Format = ext switch { ".vtz" => "VT Pro-e project (.vtz)", ".c3p" => "Crestron Core 3 UI project (.c3p)", _ => "zip archive" };
        }
        else
        {
            using var ms = new MemoryStream();
            file.CopyTo(ms);
            if (ms.Length > MaxTextBytes) throw new Ch5Exception("That file is too big to read.");
            var text = ReadText(ms.ToArray());
            if (!ReadText(fileName, text, ctx))
                throw new Ch5Exception("That isn't a project OpenVP can read. Use a .vtz, .c3p, or an .xml/.json export of one.");
            ctx.Format = ext switch { ".xml" => "XML project", ".json" => "JSON project", _ => "text project" };
        }
        return ctx.Finish(name);
    }

    private sealed class Ctx
    {
        public string Format = "";
        public readonly List<SgPage> Pages = new();
        public readonly Dictionary<string, int> Supported = new();
        public readonly Dictionary<string, int> Unsupported = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Notes = new();
        public readonly Dictionary<string, string> ImageNames = new(StringComparer.OrdinalIgnoreCase);
        public int Objects;
        public SgProject? NativeProject;   // a ready-made OpenVP model
        public PageBuilder? DefaultPage;   // objects found outside any page
        public PageBuilder Default => DefaultPage ??= new PageBuilder { Name = "Project", Kind = "page" };

        public void Count(string type) { Supported[type] = Supported.GetValueOrDefault(type) + 1; Objects++; }
        public void Skip(string what) { Unsupported[what] = Unsupported.GetValueOrDefault(what) + 1; }

        public SgProject Finish(string name)
        {
            if (NativeProject != null) return NativeProject with { Name = string.IsNullOrWhiteSpace(NativeProject.Name) ? name : NativeProject.Name };
            if (DefaultPage is { Objects.Count: > 0 }) Pages.Insert(0, DefaultPage.Build());
            var unsup = Unsupported.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ×{k.Value}: not drawn").ToList();
            if (Pages.Count == 0)
            {
                unsup.Insert(0, "No pages or objects could be found in this project. If it is a binary VT Pro-e file, OpenVP can't read it: export the project as XML or JSON, or use a CH5 project instead.");
            }
            Notes.Add("Joins and positions are read from the file's own XML/JSON and have not been checked against a real Crestron panel.");
            return new SgProject(name, Format, Pages, new SgReport(Format, Supported, unsup, Notes));
        }
    }

    private static string ReadText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static void ReadZip(Stream file, Ctx ctx, List<(string name, byte[] data)>? images, string ext)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new Ch5Exception("That isn't a valid archive."); }
        using (zip)
        {
            if (zip.Entries.Count > Ch5Panels.MaxFiles) throw new Ch5Exception($"The archive has more than {Ch5Panels.MaxFiles} files.");
            long total = 0;
            var binary = new List<string>();
            var imgIndex = 0;
            foreach (var e in zip.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (e.FullName.EndsWith('/') || e.Length == 0) continue;
                var lower = e.FullName.ToLowerInvariant();
                var x = Path.GetExtension(lower);
                if (e.Length > Ch5Panels.MaxExtractedBytes) throw new Ch5Exception("A file in the archive is too big.");
                if (ImageExt.Contains(x))
                {
                    if (images != null && e.Length <= 8 * 1024 * 1024)
                    {
                        byte[] data = ReadAll(e, ref total);
                        var stored = $"img{++imgIndex}{x}";
                        images.Add((stored, data));
                        ctx.ImageNames[Path.GetFileName(e.FullName)] = stored;
                        ctx.ImageNames[Path.GetFileNameWithoutExtension(e.FullName)] = stored;
                    }
                    continue;
                }
                if (x is ".xml" or ".json" or ".vtpx" or ".openvp-sg" or ".cse2j")
                {
                    if (e.Length > MaxTextBytes) { ctx.Skip($"file {Path.GetFileName(e.FullName)} (too big to read)"); continue; }
                    var text = ReadText(ReadAll(e, ref total));
                    if (!ReadText(e.FullName, text, ctx)) ctx.Skip($"file {Path.GetFileName(e.FullName)} (not XML or JSON OpenVP understands)");
                    continue;
                }
                if (x is ".vtp" or ".sgd" or ".dat" or ".bin" or ".db" or ".sg" or ".cpz" or ".pck") binary.Add(Path.GetFileName(e.FullName));
                else if (x is ".svg" or ".ttf" or ".otf" or ".wav" or ".mp3" or ".mp4" or ".css" or ".js" or ".html") ctx.Skip($"{x} file ({Path.GetFileName(e.FullName)})");
            }
            if (binary.Count > 0)
                ctx.Skip("binary project file" + (binary.Count > 1 ? "s" : "") + " (" + string.Join(", ", binary.Take(4)) + (binary.Count > 4 ? ", …" : "") + ") can't be decoded; the pages in it are not shown unless the archive also holds XML or JSON");
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry e, ref long total)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            total += n;
            if (total > Ch5Panels.MaxExtractedBytes) throw new Ch5Exception("The project is too big once unpacked.");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- text → nodes

    private sealed class Node
    {
        public string Tag = "";
        public readonly Dictionary<string, string> Props = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Node> Children = new();
        public readonly List<string> Items = new();
    }

    private static bool ReadText(string fileName, string text, Ctx ctx)
    {
        text = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.Length == 0) return false;
        try
        {
            if (text[0] == '{' || text[0] == '[')
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = MaxDepth });
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("openvpSmartGraphics", out _) || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("pages", out var pg) && pg.ValueKind == JsonValueKind.Array && fileName.EndsWith(".openvp-sg.json", StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.NativeProject = ReadNative(root);
                    return true;
                }
                var n = FromJson("root", root, 0);
                Walk(n, null, ctx, fileName, 0);
                return true;
            }
            if (text[0] == '<')
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
                using var sr = new StringReader(text);
                using var xr = XmlReader.Create(sr, settings);
                var doc = XDocument.Load(xr);
                if (doc.Root == null) return false;
                Walk(FromXml(doc.Root, 0), null, ctx, fileName, 0);
                return true;
            }
        }
        catch (Exception e) when (e is JsonException or XmlException) { return false; }
        return false;
    }

    private static Node FromXml(XElement e, int depth)
    {
        if (depth > MaxDepth) throw new XmlException("Too deeply nested.");
        var n = new Node { Tag = e.Name.LocalName };
        foreach (var a in e.Attributes()) n.Props[a.Name.LocalName] = a.Value;
        foreach (var c in e.Elements())
        {
            if (!c.HasElements && !c.HasAttributes && !string.IsNullOrWhiteSpace(c.Value) && c.Value.Length < 400) n.Props.TryAdd(c.Name.LocalName, c.Value.Trim());
            else n.Children.Add(FromXml(c, depth + 1));
        }
        return n;
    }

    private static Node FromJson(string tag, JsonElement el, int depth)
    {
        if (depth > MaxDepth) throw new JsonException("Too deeply nested.");
        var n = new Node { Tag = tag };
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    switch (p.Value.ValueKind)
                    {
                        case JsonValueKind.String: n.Props.TryAdd(p.Name, p.Value.GetString() ?? ""); break;
                        case JsonValueKind.Number: n.Props.TryAdd(p.Name, p.Value.GetRawText()); break;
                        case JsonValueKind.True: n.Props.TryAdd(p.Name, "true"); break;
                        case JsonValueKind.False: n.Props.TryAdd(p.Name, "false"); break;
                        case JsonValueKind.Object: n.Children.Add(FromJson(p.Name, p.Value, depth + 1)); break;
                        case JsonValueKind.Array:
                            foreach (var item in p.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.Object) n.Children.Add(FromJson(p.Name, item, depth + 1));
                                else if (item.ValueKind == JsonValueKind.String && n.Items.Count < 200) n.Items.Add(item.GetString() ?? "");
                            }
                            break;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) n.Children.Add(FromJson(tag, item, depth + 1));
                break;
        }
        return n;
    }

    // ---------------------------------------------------------------- the heuristic reader

    private static string K(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");

    private static string? Pick(Node n, params string[] keys)
    {
        foreach (var k in keys)
            foreach (var p in n.Props)
                if (K(p.Key) == k && p.Value.Length > 0) return p.Value;
        return null;
    }

    private static int? Num(Node n, params string[] keys)
    {
        var v = Pick(n, keys);
        return v != null && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && Math.Abs(d) < 100000 ? (int)Math.Round(d) : null;
    }

    private static string Classify(Node n)
    {
        var t = K(Pick(n, "type", "objecttype", "controltype", "class", "kind", "objclass") ?? "") + "|" + K(n.Tag);
        bool Has(params string[] w) => w.Any(t.Contains);
        if (Has("subpagereflist", "dynamiclist", "buttonlist", "objectlist", "iconlist", "listbox", "smartlist") || (Has("list") && !Has("playlist"))) return "list";
        if (Has("subpagerefer", "subpageref")) return "subpageref";
        if (Has("subpage", "popup", "modal", "overlay")) return "subpage";
        if (Has("page") && !Has("pagelayout") && !Has("pageref")) return "page";
        if (Has("gauge", "meter", "bargraph", "progress")) return "gauge";
        if (Has("slider", "fader", "levelcontrol", "knob", "dial")) return "slider";
        if (Has("button", "btn", "toggle", "switch", "checkbox")) return "button";
        if (Has("textinput", "textentry", "keyboard", "dynamictext", "text", "label", "caption")) return "text";
        if (Has("image", "picture", "icon", "bitmap", "graphic", "logo")) return "image";
        if (Has("box", "rect", "shape", "frame", "group", "panel", "background", "line", "ellipse", "circle")) return "box";
        if (Has("smartobject", "smartgraphics")) return "smartobject";
        return "";
    }

    private static bool HasGeometry(Node n) => Num(n, "width", "w") != null && Num(n, "height", "h") != null;

    private static (uint v, string key)? JoinProp(KeyValuePair<string, string> p)
    {
        var k = K(p.Key);
        if (!k.Contains("join") && !k.EndsWith("jn")) return null;
        return uint.TryParse(p.Value, out var v) && v is > 0 and < 65536 ? (v, k) : null;
    }

    private static SgJoins JoinsOf(Node n, string type)
    {
        uint press = 0, fb = 0, ain = 0, aout = 0, sin = 0, sout = 0;
        uint so = (uint)(Num(n, "smartobjectid", "soid", "smartobject", "objectid") ?? 0);
        foreach (var p in n.Props)
        {
            if (JoinProp(p) is not { } j) continue;
            var (v, k) = j;
            if (k.Contains("serial") || k.Contains("text") || k.Contains("indirect") || k.Contains("label") || k.Contains("caption"))
            {
                if (k.Contains("out") || k.Contains("entry") || k.Contains("send")) sout = sout == 0 ? v : sout;
                else sin = sin == 0 ? v : sin;
            }
            else if (k.Contains("analog") || k.Contains("level") || k.Contains("value") || k.Contains("percent"))
            {
                if (k.Contains("fb") || k.Contains("feedback") || k.Contains("analogin") || k.Contains("receive")) ain = ain == 0 ? v : ain;
                else if (k.Contains("out") || k.Contains("send") || k.Contains("set") || k.Contains("press")) aout = aout == 0 ? v : aout;
                else { ain = ain == 0 ? v : ain; aout = aout == 0 ? v : aout; }
            }
            else if (k.Contains("fb") || k.Contains("feedback") || k.Contains("state") || k.Contains("select") || k.Contains("digitalin"))
                fb = fb == 0 ? v : fb;
            else if (k.Contains("press") || k.Contains("click") || k.Contains("touch") || k.Contains("digital") || k.Contains("output") || k.Contains("action"))
                press = press == 0 ? v : press;
        }
        if (type is "gauge" && ain == 0 && aout != 0) { ain = aout; aout = 0; }
        return new SgJoins(press, fb, ain, aout, sin, sout, so);
    }

    private static SgObject? MakeObject(Node n, string type, Ctx ctx)
    {
        var x = Num(n, "x", "left", "xpos", "posx", "positionx") ?? 0;
        var y = Num(n, "y", "top", "ypos", "posy", "positiony") ?? 0;
        var w = Num(n, "width", "w") ?? 100;
        var h = Num(n, "height", "h") ?? 40;
        var name = Pick(n, "name", "objectname", "id", "objectid", "title") ?? type;
        var text = Pick(n, "text", "caption", "label", "title", "content", "value", "defaulttext") ;
        var img = Pick(n, "image", "icon", "bitmap", "picture", "src", "imagefile", "file", "pressedimage", "normalimage");
        string? stored = null;
        if (img != null)
        {
            var file = Path.GetFileName(img.Replace('\\', '/'));
            if (ctx.ImageNames.TryGetValue(file, out var s1) || ctx.ImageNames.TryGetValue(Path.GetFileNameWithoutExtension(file), out s1)) stored = s1;
        }
        string? detail = null;
        IReadOnlyList<string>? items = n.Items.Count > 0 ? n.Items.ToList() : null;
        if (type == "list")
        {
            var count = Num(n, "items", "itemcount", "numberofitems", "count", "rows");
            if (items == null && count is > 0 and <= 200) items = Enumerable.Range(1, count.Value).Select(i => "Item " + i).ToList();
            detail = "Item joins are numbered from the smart object's own numbering; presses are sent as digital joins 1..n.";
        }
        if (type == "smartobject") { type = "box"; detail = "Smart object " + (Pick(n, "smartobjectid", "soid", "objectid") ?? "?"); }
        return new SgObject(type, name, x, y, Math.Max(1, w), Math.Max(1, h), text, JoinsOf(n, type), stored, detail, items);
    }

    private static void Walk(Node n, PageBuilder? page, Ctx ctx, string fileName, int depth)
    {
        if (ctx.Objects >= MaxObjects) return;
        var cls = Classify(n);
        if (cls is "page" or "subpage")
        {
            var pb = new PageBuilder
            {
                Name = Pick(n, "name", "pagename", "title", "id") ?? Path.GetFileNameWithoutExtension(fileName),
                Kind = cls,
                Width = Num(n, "width", "w", "pagewidth", "screenwidth") ?? 0,
                Height = Num(n, "height", "h", "pageheight", "screenheight") ?? 0
            };
            foreach (var c in n.Children) Walk(c, pb, ctx, fileName, depth + 1);
            if (pb.Objects.Count > 0 || cls == "page") ctx.Pages.Add(pb.Build());
            return;
        }
        if (cls.Length > 0)
        {
            if (HasGeometry(n) || cls is "list" or "subpageref")
            {
                var o = MakeObject(n, cls, ctx);
                if (o != null)
                {
                    (page ?? ctx.Default).Objects.Add(o);
                    ctx.Count(o.Type);
                    if (cls == "subpageref") ctx.Notes.Add($"“{o.Name}” refers to another page; it is drawn as a box and does not open the page.");
                }
            }
            foreach (var c in n.Children) Walk(c, page, ctx, fileName, depth + 1);
            return;
        }
        // Unknown type with a size and a type name: a placeholder, and a line in the report.
        var typeName = Pick(n, "type", "objecttype", "controltype", "class", "kind")
            ?? (K(n.Tag) is "object" or "item" or "control" or "element" or "node" or "root" or "child" or "children" or "objects" or "controls" ? null : n.Tag);
        if (typeName != null && HasGeometry(n) && K(typeName).Length > 0 && !K(typeName).All(char.IsDigit))
        {
            ctx.Skip($"object type “{typeName}”");
            (page ?? ctx.Default).Objects.Add(new SgObject("unsupported", Pick(n, "name", "objectname", "id") ?? typeName, Num(n, "x", "left") ?? 0, Num(n, "y", "top") ?? 0, Num(n, "width", "w") ?? 100, Num(n, "height", "h") ?? 40, typeName, new SgJoins(), null, "Not supported: " + typeName));
        }
        foreach (var c in n.Children) Walk(c, page, ctx, fileName, depth + 1);
    }

    internal sealed class PageBuilder
    {
        public string Name = "", Kind = "page";
        public int Width, Height;
        public readonly List<SgObject> Objects = new();
        public SgPage Build()
        {
            var w = Width > 0 ? Width : Math.Max(320, Objects.Select(o => o.X + o.W).DefaultIfEmpty(0).Max());
            var h = Height > 0 ? Height : Math.Max(240, Objects.Select(o => o.Y + o.H).DefaultIfEmpty(0).Max());
            return new SgPage(Name, Kind, w, h, Objects);
        }
    }

    // ---------------------------------------------------------------- OpenVP's own format

    private static SgProject ReadNative(JsonElement root)
    {
        var pages = new List<SgPage>();
        var counts = new Dictionary<string, int>();
        if (root.TryGetProperty("pages", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
            {
                var objs = new List<SgObject>();
                if (p.TryGetProperty("objects", out var os) && os.ValueKind == JsonValueKind.Array)
                    foreach (var o in os.EnumerateArray().Take(MaxObjects))
                    {
                        var d = JsonSerializer.Deserialize<SgObject>(o.GetRawText(), Web);
                        if (d == null) continue;
                        d = d with { Joins = d.Joins ?? new SgJoins() };
                        objs.Add(d);
                        counts[d.Type] = counts.GetValueOrDefault(d.Type) + 1;
                    }
                pages.Add(new SgPage(S(p, "name") ?? "Page", S(p, "kind") ?? "page", I(p, "width", 800), I(p, "height", 480), objs));
            }
        return new SgProject(S(root, "name") ?? "", "OpenVP smart graphics (.openvp-sg.json)", pages, new SgReport("OpenVP smart graphics (.openvp-sg.json)", counts, [], []));

        static string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int I(JsonElement e, string k, int d) => e.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : d;
    }
}
