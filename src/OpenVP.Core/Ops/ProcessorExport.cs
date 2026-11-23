using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

public sealed class ManifestSlot
{
    public int Slot { get; set; }
    public string? Name { get; set; }
    public string? RoomId { get; set; }
    public string? Program { get; set; }
    public string? File { get; set; }
    public bool Running { get; set; }
    public bool Autostart { get; set; } = true;
    public int ProgramFiles { get; set; }
    public long ProgramBytes { get; set; }
    public int NvramFiles { get; set; }
    public int UserFiles { get; set; }
    public int Simulators { get; set; }
    public int Recordings { get; set; }
    public int Versions { get; set; }
    public int Snapshots { get; set; }
    public bool Integrations { get; set; }
    public bool Watches { get; set; }
    public bool Bookmarks { get; set; }
}

/// <summary>The <c>manifest.json</c> at the top of an <c>.openvp</c> file.</summary>
public sealed class ArchiveManifest
{
    public const string FormatName = "openvp-export";
    public const int CurrentVersion = 1;
    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset Created { get; set; }
    public string? Machine { get; set; }
    public string? OpenVpVersion { get; set; }
    public string? Reason { get; set; }
    public bool IncludesHistory { get; set; }
    public bool IncludesSnapshots { get; set; }
    /// <summary>True when the file has a <c>secrets.enc</c>: secrets, share links and webhook secrets, encrypted with the export password.</summary>
    public bool HasSecrets { get; set; }
    public int SecretCount { get; set; }
    public List<ManifestSlot> Slots { get; set; } = new();
    /// <summary>Which processor-wide settings are in the file (<c>integrations</c>, <c>alerts</c>, <c>schedules</c> ...).</summary>
    public List<string> Settings { get; set; } = new();
    public int RecentPrograms { get; set; }
}

/// <summary>What goes into an export. Everything defaults to the safe choice: no history, no snapshots, no secrets.</summary>
public sealed record ExportOptions
{
    public bool IncludeHistory { get; init; }
    public bool IncludeSnapshots { get; init; }
    public bool IncludeSecrets { get; init; }
    /// <summary>Needed when <see cref="IncludeSecrets"/> is on. At least <see cref="ArchiveCrypto.MinPasswordLength"/> characters.</summary>
    public string? Password { get; init; }
    /// <summary>Only these slots; null or empty means all that have something in them.</summary>
    public IReadOnlyCollection<int>? Slots { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Where each kind of data lives, shared by the exporter, the importer and the git sync.</summary>
public static class DataLayout
{
    /// <summary>The slot's own folder (<c>program01</c>): <c>slot.json</c>, <c>nvram</c>, <c>user</c>, <c>devices</c>, <c>debug.json</c>.</summary>
    public static string SlotFolder(string data, int slot) => Path.Combine(data, $"program{slot:00}");
    public static string Uploads(string data, int slot) => Path.Combine(data, "uploads", $"slot{slot:00}");
    public static string Recordings(string data, int slot) => Path.Combine(data, "recordings", $"slot{slot:00}");

    /// <summary>Processor-wide settings files (relative to the data folder) that are always exported.</summary>
    public static readonly string[] SettingsFiles =
    [
        "integrations/global.json", "alerts/config.json", "schedules.json", "targets.json", "backups.json", "versions/settings.json", "symbols.json", "primitives.json", "comports.json"
    ];

    /// <summary>Files that hold credentials of their own (share-link tokens, incoming-webhook tokens). Exported only inside the encrypted secrets.</summary>
    public static readonly string[] SecretFiles = ["shares.json", "integrations/hooks.json"];

    /// <summary>History and metrics files, exported when asked.</summary>
    public static readonly string[] HistoryPaths = ["history", "activity.jsonl", "alerts/state.json", "alerts/report-state.json", "schedules-history.jsonl", "traces"];

    /// <summary>
    /// Takes the secrets out of a slot's <c>integrations.json</c> (webhook signing secrets and header values such as <c>Authorization</c>) so the file is safe to export or commit.
    /// Returns the text unchanged if it isn't the JSON expected.
    /// </summary>
    public static string StripIntegrationSecrets(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return json;
            Strip(root);
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { return json; }
    }

    private static void Strip(JsonNode node)
    {
        if (node is JsonObject o)
        {
            foreach (var key in o.Select(p => p.Key).ToList())
            {
                if (key.Equals("secret", StringComparison.OrdinalIgnoreCase)) { o.Remove(key); continue; }
                if (key.Equals("headers", StringComparison.OrdinalIgnoreCase) && o[key] is JsonObject h)
                {
                    foreach (var hk in h.Select(p => p.Key).ToList())
                        if (System.Text.RegularExpressions.Regex.IsMatch(hk, "auth|key|token|secret|password|cookie", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) h.Remove(hk);
                    continue;
                }
                if (o[key] is { } child) Strip(child);
            }
        }
        else if (node is JsonArray a) foreach (var c in a) if (c != null) Strip(c);
    }
}

/// <summary>
/// Writes a whole-processor export: one <c>.openvp</c> file (a zip) with a <c>manifest.json</c> and, for every slot, its program files, NVRAM and User folders,
/// <c>slot.json</c>, device simulators and integrations, recordings, bookmarks and watches; and the processor-wide settings (integrations, alerts and thresholds,
/// health report, schedules, push targets, the recent programs list). Never the access key. Secrets (SMTP and MQTT passwords, webhook secrets, share-link and incoming-webhook
/// tokens, push-target keys) are left out unless <see cref="ExportOptions.IncludeSecrets"/> is on, and then only inside <c>secrets.enc</c>, encrypted with the export password.
/// </summary>
public sealed class ProcessorExporter
{
    private readonly string _data;
    private readonly int _slotCount;
    private readonly SecretStore? _secrets;
    private readonly Func<int, string?> _programName;
    private readonly Func<object?>? _options;
    private readonly RecentPrograms? _recent;

    public ProcessorExporter(string dataDirectory, int slotCount, SecretStore? secrets, Func<int, string?>? programName = null, Func<object?>? options = null, RecentPrograms? recent = null)
    {
        _data = dataDirectory; _slotCount = slotCount; _secrets = secrets; _programName = programName ?? (_ => null); _options = options; _recent = recent;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Writes the export to <paramref name="output"/> (a seekable or write-only stream) and returns its manifest.</summary>
    public ArchiveManifest Export(Stream output, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        if (options.IncludeSecrets && (string.IsNullOrEmpty(options.Password) || options.Password.Length < ArchiveCrypto.MinPasswordLength))
            throw new ArgumentException($"Secrets can only be included with an export password of at least {ArchiveCrypto.MinPasswordLength} characters.");
        var manifest = new ArchiveManifest
        {
            Created = DateTimeOffset.UtcNow, Machine = Environment.MachineName, OpenVpVersion = "0.1.0", Reason = options.Reason,
            IncludesHistory = options.IncludeHistory, IncludesSnapshots = options.IncludeSnapshots
        };
        var secretFiles = new Dictionary<string, byte[]>(); // zip path -> real content, for secrets.enc
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            for (var n = 1; n <= _slotCount; n++)
            {
                if (options.Slots is { Count: > 0 } only && !only.Contains(n)) continue;
                var ms = ExportSlot(zip, n, options, secretFiles);
                if (ms != null) manifest.Slots.Add(ms);
            }
            ExportSettings(zip, manifest, options, secretFiles);
            if (options.IncludeSecrets)
            {
                var secrets = _secrets?.Snapshot() ?? new Dictionary<string, string>();
                foreach (var f in DataLayout.SecretFiles)
                {
                    var p = Path.Combine(_data, f.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(p)) secretFiles["processor/" + f] = ReadShared(p);
                }
                var blob = JsonSerializer.SerializeToUtf8Bytes(new SecretBlob
                {
                    Secrets = secrets,
                    Files = secretFiles.ToDictionary(kv => kv.Key, kv => Convert.ToBase64String(kv.Value))
                }, Json);
                var enc = ArchiveCrypto.Encrypt(blob, options.Password!);
                Put(zip, "secrets.enc", enc);
                manifest.HasSecrets = true;
                manifest.SecretCount = secrets.Count;
            }
            Put(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
        }
        return manifest;
    }

    public sealed class SecretBlob
    {
        public Dictionary<string, string> Secrets { get; set; } = new();
        public Dictionary<string, string> Files { get; set; } = new();
    }

    private ManifestSlot? ExportSlot(ZipArchive zip, int n, ExportOptions options, Dictionary<string, byte[]> secretFiles)
    {
        var slotDir = DataLayout.SlotFolder(_data, n);
        var uploads = DataLayout.Uploads(_data, n);
        var hasFiles = HasFiles(uploads) || HasFiles(slotDir) || HasFiles(DataLayout.Recordings(_data, n));
        if (!hasFiles) return null;
        var prefix = $"slots/{n:00}/";
        var ms = new ManifestSlot { Slot = n };
        SlotSettings? settings = null;
        var settingsPath = Path.Combine(slotDir, "slot.json");
        if (File.Exists(settingsPath))
            try { settings = SlotSettings.FromJson(File.ReadAllText(settingsPath)); } catch (JsonException) { }
        if (settings != null)
        {
            ms.Name = settings.Name; ms.RoomId = settings.RoomId; ms.Autostart = settings.Autostart;
            ms.File = settings.Program?.File; ms.Running = settings.Program?.Running ?? false;
        }
        ms.Program = _programName(n);
        foreach (var f in Files(uploads))
        {
            ms.ProgramFiles++; ms.ProgramBytes += new FileInfo(f).Length;
            Add(zip, prefix + "program/" + Rel(uploads, f), f);
        }
        foreach (var f in Files(slotDir))
        {
            var rel = Rel(slotDir, f);
            var top = rel.Split('/')[0];
            if (rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            if (top == "nvram") ms.NvramFiles++;
            else if (top == "user") ms.UserFiles++;
            else if (rel == "devices/simulators.json") ms.Simulators = CountItems(f);
            if (rel == "devices/integrations.json")
            {
                ms.Integrations = true;
                var raw = ReadShared(f);
                if (options.IncludeSecrets) secretFiles[prefix + "data/" + rel] = raw;
                Put(zip, prefix + "data/" + rel, Encoding.UTF8.GetBytes(DataLayout.StripIntegrationSecrets(Encoding.UTF8.GetString(raw))));
                continue;
            }
            if (rel == "debug.json") ms.Watches = true;
            Add(zip, prefix + "data/" + rel, f);
        }
        var rec = DataLayout.Recordings(_data, n);
        foreach (var f in Files(rec))
        {
            if (!f.EndsWith(".reports.json", StringComparison.OrdinalIgnoreCase)) ms.Recordings++;
            Add(zip, prefix + "recordings/" + Rel(rec, f), f);
        }
        if (ms.Program != null)
        {
            var bm = Path.Combine(_data, "bookmarks", BookmarkStore.FileNameFor(ms.Program));
            if (File.Exists(bm)) { ms.Bookmarks = true; Add(zip, prefix + "bookmarks.json", bm); }
        }
        if (options.IncludeSnapshots)
        {
            var snaps = Path.Combine(_data, "snapshots", $"slot{n:00}");
            ms.Snapshots = Directory.Exists(snaps) ? Directory.EnumerateDirectories(snaps).Count() : 0;
            foreach (var f in Files(snaps))
            {
                // A snapshot holds the slot's integrations file as it was, webhook secrets included; they go the way they do from the slot itself.
                if (Path.GetFileName(f).Equals("integrations.json", StringComparison.OrdinalIgnoreCase))
                    Put(zip, prefix + "snapshots/" + Rel(snaps, f), Encoding.UTF8.GetBytes(DataLayout.StripIntegrationSecrets(Encoding.UTF8.GetString(ReadShared(f)))));
                else Add(zip, prefix + "snapshots/" + Rel(snaps, f), f);
            }
            var vers = Path.Combine(_data, "versions", $"slot{n:00}");
            if (File.Exists(Path.Combine(vers, "index.json")))
            {
                try { ms.Versions = JsonDocument.Parse(File.ReadAllText(Path.Combine(vers, "index.json"))).RootElement.GetProperty("versions").GetArrayLength(); } catch { }
                foreach (var f in Files(vers)) Add(zip, prefix + "versions/" + Rel(vers, f), f);
            }
        }
        return ms;
    }

    private void ExportSettings(ZipArchive zip, ArchiveManifest manifest, ExportOptions options, Dictionary<string, byte[]> secretFiles)
    {
        foreach (var rel in DataLayout.SettingsFiles)
        {
            var p = Path.Combine(_data, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) continue;
            Add(zip, "processor/" + rel, p);
            var label = rel switch
            {
                "integrations/global.json" => "integrations", "alerts/config.json" => "alerts", "schedules.json" => "schedules", "targets.json" => "targets",
                "backups.json" => "backups", "versions/settings.json" => "versions", "symbols.json" or "primitives.json" or "comports.json" => "symbol maps", _ => rel
            };
            if (!manifest.Settings.Contains(label)) manifest.Settings.Add(label);
        }
        if (_options?.Invoke() is { } opt)
        {
            Put(zip, "processor/options.json", JsonSerializer.SerializeToUtf8Bytes(opt, Json));
            manifest.Settings.Add("console options");
        }
        if (_recent != null)
        {
            var list = _recent.List().Select(r => r with { HasCopy = false }).ToList();
            if (list.Count > 0)
            {
                Put(zip, "processor/recent.json", JsonSerializer.SerializeToUtf8Bytes(list, Json));
                manifest.RecentPrograms = list.Count;
                manifest.Settings.Add("recent programs");
            }
        }
        if (options.IncludeSecrets)
        {
            foreach (var f in DataLayout.SecretFiles)
                if (File.Exists(Path.Combine(_data, f.Replace('/', Path.DirectorySeparatorChar))))
                    manifest.Settings.Add(f == "shares.json" ? "shared panel links" : "incoming webhooks");
        }
        if (options.IncludeHistory)
        {
            foreach (var h in DataLayout.HistoryPaths)
            {
                var p = Path.Combine(_data, h.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) Add(zip, "history/" + h, p);
                else foreach (var f in Files(p)) Add(zip, "history/" + h + "/" + Rel(p, f), f);
            }
            manifest.Settings.Add("history");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static bool HasFiles(string dir) => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();

    private static IEnumerable<string> Files(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Where(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0) : [];

    private static string Rel(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static int CountItems(string file)
    {
        try
        {
            var root = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
            if (root.ValueKind == JsonValueKind.Array) return root.GetArrayLength();
            foreach (var p in root.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Array) return p.Value.GetArrayLength();
        }
        catch (Exception e) when (e is JsonException or IOException) { }
        return 0;
    }

    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var b = new byte[fs.Length];
        fs.ReadExactly(b);
        return b;
    }

    private static void Add(ZipArchive zip, string name, string path)
    {
        try
        {
            using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var e = zip.CreateEntry(name, CompressionLevel.Fastest);
            e.LastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(path).Year < 1980 ? DateTime.UtcNow : File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            using var dst = e.Open();
            src.CopyTo(dst);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void Put(ZipArchive zip, string name, byte[] data)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var s = e.Open();
        s.Write(data);
    }
}
