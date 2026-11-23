using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// Smart-graphics projects read by <see cref="SmartGraphics"/>, kept per slot and IP ID in <c>data/panels/slotNN/ipidXX.sg/</c>
/// (<c>model.json</c> plus the project's raster images as <c>imgN.ext</c>). Only the parsed model and the images are kept, never the
/// uploaded archive, so nothing in the archive is served as it came.
/// </summary>
public sealed class SmartGraphicsStore
{
    private readonly string _root;
    public SmartGraphicsStore(string dataDirectory) => _root = Path.GetFullPath(Path.Combine(dataDirectory, "panels"));

    private string Dir(int slot, uint ip) => Path.Combine(_root, $"slot{slot:00}", $"ipid{ip:X2}.sg");

    public SgProject? Get(int slot, uint ip)
    {
        var f = Path.Combine(Dir(slot, ip), "model.json");
        if (!File.Exists(f)) return null;
        try { return JsonSerializer.Deserialize<SgProject>(File.ReadAllText(f), SmartGraphics.JsonOptions); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    public IReadOnlyList<SgInfo> List()
    {
        var res = new List<SgInfo>();
        if (!Directory.Exists(_root)) return res;
        foreach (var sd in Directory.GetDirectories(_root, "slot*"))
        {
            if (!int.TryParse(Path.GetFileName(sd)[4..], out var slot)) continue;
            foreach (var pd in Directory.GetDirectories(sd, "ipid*.sg"))
            {
                var hex = Path.GetFileName(pd)[4..^3];
                if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var ip)) continue;
                var m = Get(slot, ip);
                if (m != null) res.Add(new SgInfo(slot, ip, m.Name, m.Format, m.Pages.Count, m.Pages.Sum(p => p.Objects.Count), Directory.GetLastWriteTimeUtc(pd)));
            }
        }
        return res.OrderBy(i => i.Slot).ThenBy(i => i.IpId).ToList();
    }

    public SgProject Install(int slot, uint ip, Stream file, string fileName)
    {
        if (slot < 1) throw new ArgumentException("Slot must be 1 or more.");
        if (ip is 0 or > 0xFE) throw new ArgumentException("IP ID must be 01-FE.");
        var images = new List<(string name, byte[] data)>();
        var project = SmartGraphics.Read(file, fileName, images);
        var final = Dir(slot, ip);
        var scratch = final + ".incoming-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(scratch);
            // Only images that are actually used are kept.
            var used = project.Pages.SelectMany(p => p.Objects).Select(o => o.Image).Where(i => i != null).ToHashSet();
            foreach (var (name, data) in images) if (used.Contains(name)) File.WriteAllBytes(Path.Combine(scratch, name), data);
            File.WriteAllText(Path.Combine(scratch, "model.json"), JsonSerializer.Serialize(project, SmartGraphics.JsonOptions));
            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(scratch, final);
        }
        finally { try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch (IOException) { } }
        return project;
    }

    public bool Remove(int slot, uint ip)
    {
        var d = Dir(slot, ip);
        if (!Directory.Exists(d)) return false;
        Directory.Delete(d, true);
        return true;
    }

    /// <summary>The path of a kept image, or null. The name must be exactly what the model refers to (imgN.ext), nothing with a slash.</summary>
    public string? AssetPath(int slot, uint ip, string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^img\d{1,6}\.(png|jpg|jpeg|gif|webp|bmp)$")) return null;
        var f = Path.Combine(Dir(slot, ip), name);
        return File.Exists(f) ? f : null;
    }
}
