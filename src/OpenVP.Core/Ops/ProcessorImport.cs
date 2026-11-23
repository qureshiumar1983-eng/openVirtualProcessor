using System.IO.Compression;
using System.Text.Json;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

public sealed record PreviewSlot(int Slot, string? Name, string? Program, string? File, bool Running, bool Autostart, int ProgramFiles, long ProgramBytes, int NvramFiles, int UserFiles, int Simulators, int Recordings, int Versions, int Snapshots, bool Integrations, bool Watches, string? Current);

/// <summary>What is inside an export file, shown before anything is imported.</summary>
public sealed record ImportPreview(
    int FormatVersion, DateTimeOffset Created, string? Machine, string? OpenVpVersion, string? Reason, bool HasSecrets, int SecretCount, bool? PasswordOk,
    bool IncludesHistory, bool IncludesSnapshots, IReadOnlyList<PreviewSlot> Slots, IReadOnlyList<string> Settings, int RecentPrograms, long Size, IReadOnlyList<string> Warnings);

/// <summary>What to import.</summary>
public sealed record ImportRequest
{
    /// <summary>Which slots; null means every slot in the file.</summary>
    public IReadOnlyCollection<int>? Slots { get; init; }
    /// <summary>Replace the processor-wide settings (integrations, alerts, schedules, targets, backups, symbol maps, recent programs).</summary>
    public bool ReplaceSettings { get; init; }
    /// <summary>Restore the secrets and share links in <c>secrets.enc</c> (needs <see cref="Password"/>).</summary>
    public bool RestoreSecrets { get; init; }
    public string? Password { get; init; }
    /// <summary>Also restore history files and the snapshots and program versions in the file.</summary>
    public bool RestoreHistory { get; init; }
    /// <summary>Who started it, for the version history.</summary>
    public string Who { get; init; } = Actors.ThisComputer;
    /// <summary>Why: <c>import</c> or <c>restore</c> (shown in the version history).</summary>
    public string How { get; init; } = "import";
}

public sealed record ImportedSlot(int Slot, bool Ok, bool Loaded, bool Started, string Message);

public sealed record ImportResult(IReadOnlyList<ImportedSlot> Slots, bool SettingsReplaced, bool SecretsRestored, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads an <c>.openvp</c> export. <see cref="Preview"/> only reads the manifest. <see cref="Apply"/> unpacks everything chosen into a staging folder first (checking names and sizes
/// as it goes, so a bad file changes nothing), then for each chosen slot takes a snapshot, stops the program, swaps in the files, restores the slot's settings and loads the
/// program again, starting it as the slot's autostart setting and remembered state say. A slot with autostart off gets its files and settings back and stays unloaded, as after a restart.
/// </summary>
public sealed class ProcessorImporter
{
    public const string BeforeImport = "before import";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly Processor _processor;
    private readonly SlotSnapshots _snapshots;
    private readonly SecretStore? _secrets;
    private readonly ProgramVersions? _versions;
    private readonly ArchiveLimits _limits;

    /// <summary>Called before a slot's files are replaced (stop watching its file, and so on).</summary>
    public Action<int>? BeforeSlot { get; set; }
    /// <summary>Called after a slot's files and settings are back, before its program is loaded (reload its simulators and integrations).</summary>
    public Action<int>? AfterSlotFiles { get; set; }
    /// <summary>Called after processor-wide settings were replaced; the names say which (<c>integrations</c>, <c>alerts</c>, <c>shares</c>, ...). Reload the stores.</summary>
    public Action<IReadOnlyList<string>>? SettingsReplaced { get; set; }
    /// <summary>Called with the recent-programs list from the file when processor settings are replaced.</summary>
    public Action<IReadOnlyList<RecentEntry>>? RecentRestored { get; set; }
    /// <summary>Loads a program into a slot; returns an error text or null. Default is <see cref="Processor.LoadProgram"/>.</summary>
    public Func<int, string, bool, string?>? Loader { get; set; }

    public ProcessorImporter(Processor processor, SlotSnapshots snapshots, SecretStore? secrets, ProgramVersions? versions, ArchiveLimits? limits = null)
    {
        _processor = processor; _snapshots = snapshots; _secrets = secrets; _versions = versions; _limits = limits ?? SafeZip.DefaultLimits;
    }

    private string Data => _processor.DataDirectory;

    // ---------------------------------------------------------------- preview

    public static ArchiveManifest ReadManifest(ZipArchive zip, Dictionary<string, ZipArchiveEntry> entries, ArchiveLimits limits)
    {
        if (!entries.TryGetValue("manifest.json", out var e)) throw new ArchiveException("This isn't an OpenVP export: it has no manifest.json.");
        ArchiveManifest? m;
        try { m = JsonSerializer.Deserialize<ArchiveManifest>(SafeZip.ReadAll(e, limits.MaxManifestBytes), Json); }
        catch (JsonException) { throw new ArchiveException("The manifest in this file is damaged."); }
        if (m == null || m.Format != ArchiveManifest.FormatName) throw new ArchiveException("This isn't an OpenVP export.");
        if (m.Version < 1 || m.Version > ArchiveManifest.CurrentVersion)
            throw new ArchiveException($"This file is export format {m.Version}, made by a different version of OpenVP. This OpenVP reads format {ArchiveManifest.CurrentVersion}.", "wrong-version");
        foreach (var s in m.Slots) if (s.Slot < 1) throw new ArchiveException("The manifest lists an invalid slot.");
        return m;
    }

    private ZipArchive OpenZip(string file)
    {
        try { return new ZipArchive(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read, false); }
        catch (InvalidDataException) { throw new ArchiveException("This isn't a valid OpenVP export (it isn't a readable zip file)."); }
    }

    public ImportPreview Preview(string file, string? password = null)
    {
        using var zip = OpenZip(file);
        var entries = SafeZip.Validate(zip, _limits);
        var m = ReadManifest(zip, entries, _limits);
        bool? pwOk = null;
        var warnings = new List<string>();
        if (m.HasSecrets)
        {
            if (!entries.ContainsKey("secrets.enc")) warnings.Add("The manifest says there are secrets, but the file has none.");
            else if (!string.IsNullOrEmpty(password))
            {
                try { ArchiveCrypto.Decrypt(SafeZip.ReadAll(entries["secrets.enc"], 64 << 20), password); pwOk = true; }
                catch (ArchiveException e) when (e.Code == "wrong-password") { pwOk = false; }
            }
        }
        var slots = m.Slots.Where(s => s.Slot <= _processor.Slots.Count).Select(s => new PreviewSlot(s.Slot, s.Name, s.Program, s.File, s.Running, s.Autostart, s.ProgramFiles, s.ProgramBytes, s.NvramFiles, s.UserFiles, s.Simulators, s.Recordings, s.Versions, s.Snapshots, s.Integrations, s.Watches,
            _processor.Slot(s.Slot).Runtime?.ProgramName ?? (_processor.Slot(s.Slot).State == SlotState.Error ? "a program that failed to load" : null))).ToList();
        foreach (var s in m.Slots.Where(s => s.Slot > _processor.Slots.Count))
            warnings.Add($"The file has slot {s.Slot}, but this processor only has {_processor.Slots.Count} slots. It can't be imported.");
        return new ImportPreview(m.Version, m.Created, m.Machine, m.OpenVpVersion, m.Reason, m.HasSecrets, m.SecretCount, pwOk, m.IncludesHistory, m.IncludesSnapshots, slots, m.Settings, m.RecentPrograms, new FileInfo(file).Length, warnings);
    }

    // ---------------------------------------------------------------- apply

    public ImportResult Apply(string file, ImportRequest req)
    {
        using var zip = OpenZip(file);
        var entries = SafeZip.Validate(zip, _limits);
        var m = ReadManifest(zip, entries, _limits);
        var warnings = new List<string>();

        // Secrets first: a wrong password stops everything before any file is touched.
        ExportBlob? blob = null;
        if (req.RestoreSecrets)
        {
            if (!m.HasSecrets || !entries.ContainsKey("secrets.enc")) throw new ArchiveException("This file has no secrets to restore.");
            if (string.IsNullOrEmpty(req.Password)) throw new ArchiveException("Enter the export password to restore the secrets.", "need-password");
            var plain = ArchiveCrypto.Decrypt(SafeZip.ReadAll(entries["secrets.enc"], 64 << 20), req.Password);
            try { blob = JsonSerializer.Deserialize<ExportBlob>(plain, Json); }
            catch (JsonException) { throw new ArchiveException("The secrets in this file are damaged."); }
        }

        var chosen = m.Slots.Where(s => s.Slot <= _processor.Slots.Count && (req.Slots == null || req.Slots.Contains(s.Slot))).OrderBy(s => s.Autostart ? 0 : 1).ThenBy(s => s.Slot).ToList();
        var staging = Path.Combine(Data, "tmp", "import-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            Directory.CreateDirectory(staging);
            long budget = _limits.MaxTotalBytes;

            // Phase 1: unpack what was chosen into the staging folder. Nothing live is touched yet.
            foreach (var s in chosen)
            {
                var prefix = $"slots/{s.Slot:00}/";
                foreach (var (name, entry) in entries.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    var rel = name[prefix.Length..];
                    var top = rel.Split('/')[0];
                    if (top is "snapshots" or "versions" && !req.RestoreHistory) continue;
                    var dest = SafeZip.SafeJoin(Path.Combine(staging, $"slot{s.Slot:00}"), rel);
                    SafeZip.Extract(entry, dest, _limits.MaxEntryBytes, ref budget);
                }
                if (blob != null)
                    foreach (var (path, b64) in blob.Files.Where(kv => kv.Key.StartsWith(prefix + "data/", StringComparison.Ordinal)))
                        WriteBlobFile(Path.Combine(staging, $"slot{s.Slot:00}"), path[prefix.Length..], b64);
            }
            if (req.ReplaceSettings)
                foreach (var (name, entry) in entries.Where(kv => kv.Key.StartsWith("processor/", StringComparison.Ordinal)))
                    SafeZip.Extract(entry, SafeZip.SafeJoin(Path.Combine(staging, "processor"), name["processor/".Length..]), _limits.MaxEntryBytes, ref budget);
            if (req.ReplaceSettings && blob != null)
                foreach (var (path, b64) in blob.Files.Where(kv => kv.Key.StartsWith("processor/", StringComparison.Ordinal)))
                    WriteBlobFile(Path.Combine(staging, "processor"), path["processor/".Length..], b64);
            if (req.RestoreHistory)
                foreach (var (name, entry) in entries.Where(kv => kv.Key.StartsWith("history/", StringComparison.Ordinal)))
                    SafeZip.Extract(entry, SafeZip.SafeJoin(Path.Combine(staging, "history"), name["history/".Length..]), _limits.MaxEntryBytes, ref budget);

            // Phase 2: slot by slot.
            var results = new List<ImportedSlot>();
            foreach (var s in chosen)
            {
                try { results.Add(ApplySlot(s, Path.Combine(staging, $"slot{s.Slot:00}"), req, entries)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArchiveException or JsonException)
                {
                    _processor.Slot(s.Slot).Log("Error", $"Import failed for this slot: {e.Message}");
                    results.Add(new ImportedSlot(s.Slot, false, false, false, e.Message));
                }
            }

            // Phase 3: processor-wide settings.
            var replaced = new List<string>();
            var secretsRestored = false;
            if (req.ReplaceSettings) ReplaceSettings(Path.Combine(staging, "processor"), blob, replaced, warnings);
            if (req.RestoreHistory && Directory.Exists(Path.Combine(staging, "history")))
            {
                CopyTree(Path.Combine(staging, "history"), Data);
                replaced.Add("history");
            }
            if (blob != null && _secrets != null)
            {
                foreach (var (k, v) in blob.Secrets) _secrets.Set(k, v);
                secretsRestored = blob.Secrets.Count > 0;
                if (req.ReplaceSettings) replaced.Add("secrets");
            }
            if (replaced.Count > 0) try { SettingsReplaced?.Invoke(replaced); } catch (Exception e) { warnings.Add("Some settings couldn't be reloaded until OpenVP restarts: " + e.Message); }
            return new ImportResult(results, req.ReplaceSettings, secretsRestored, warnings);
        }
        finally { try { Directory.Delete(staging, true); } catch { } }
    }

    public sealed class ExportBlob
    {
        public Dictionary<string, string> Secrets { get; set; } = new();
        public Dictionary<string, string> Files { get; set; } = new();
    }

    private static void WriteBlobFile(string root, string rel, string b64)
    {
        var safe = SafeZip.SafeName(rel);
        byte[] data;
        try { data = Convert.FromBase64String(b64); } catch (FormatException) { throw new ArchiveException("The secrets in this file are damaged."); }
        var dest = SafeZip.SafeJoin(root, safe);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, data);
    }

    private ImportedSlot ApplySlot(ManifestSlot ms, string stage, ImportRequest req, Dictionary<string, ZipArchiveEntry> entries)
    {
        var n = ms.Slot;
        var slot = _processor.Slot(n);
        var slotDir = DataLayout.SlotFolder(Data, n);
        var uploads = DataLayout.Uploads(Data, n);
        SlotSettings imported = new();
        var stagedSettings = Path.Combine(stage, "data", "slot.json");
        if (File.Exists(stagedSettings))
            try { imported = SlotSettings.FromJson(File.ReadAllText(stagedSettings)); } catch (JsonException) { }
        var programFile = imported.Program?.File ?? ms.File;
        var stagedProgram = Path.Combine(stage, "program");
        if (programFile != null) programFile = Path.GetFileName(programFile);
        if (programFile != null && !File.Exists(Path.Combine(stagedProgram, programFile)))
            programFile = Directory.Exists(stagedProgram) ? Directory.EnumerateFiles(stagedProgram).Select(Path.GetFileName).FirstOrDefault(f => f != null && Path.GetExtension(f).ToLowerInvariant() is ".cpz" or ".dll" or ".lpz" or ".smw") : null;

        BeforeSlot?.Invoke(n);
        return _processor.WithSlotLocked(n, _ =>
        {
            // The safety net: what is in the slot now, before anything changes.
            try { _snapshots.Take(n, slot.Runtime?.ProgramName, slot.ProgramPath is null ? null : Path.GetFileName(slot.ProgramPath), BeforeImport); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { slot.Log("Warning", $"Couldn't save a snapshot before importing: {e.Message}"); }
            slot.Unload();

            // Program files.
            Replace(uploads, stagedProgram);
            // The slot's own folders: nvram, user, devices are replaced; anything else in the archive (debug.json, html, romdisk) is laid over.
            var stagedData = Path.Combine(stage, "data");
            foreach (var d in new[] { "nvram", "user", "devices" })
            {
                var target = Path.Combine(slotDir, d);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                if (Directory.Exists(Path.Combine(stagedData, d))) CopyTree(Path.Combine(stagedData, d), target);
            }
            if (File.Exists(Path.Combine(stagedData, "debug.json"))) File.Copy(Path.Combine(stagedData, "debug.json"), Path.Combine(slotDir, "debug.json"), true);
            foreach (var d in new[] { "html", "romdisk" })
                if (Directory.Exists(Path.Combine(stagedData, d))) CopyTree(Path.Combine(stagedData, d), Path.Combine(slotDir, d));
            // Recordings.
            if (Directory.Exists(Path.Combine(stage, "recordings"))) Replace(DataLayout.Recordings(Data, n), Path.Combine(stage, "recordings"));
            // Bookmarks, under the program's name.
            if (File.Exists(Path.Combine(stage, "bookmarks.json")) && !string.IsNullOrEmpty(ms.Program))
            {
                var dir = Path.Combine(Data, "bookmarks");
                Directory.CreateDirectory(dir);
                File.Copy(Path.Combine(stage, "bookmarks.json"), Path.Combine(dir, BookmarkStore.FileNameFor(ms.Program)), true);
            }
            // Snapshots and versions are added to, never replaced: the snapshot just taken must stay.
            if (req.RestoreHistory)
            {
                var snaps = Path.Combine(stage, "snapshots");
                if (Directory.Exists(snaps))
                    foreach (var d in Directory.EnumerateDirectories(snaps).Where(d => SlotSnapshots.IsValidId(Path.GetFileName(d))))
                    {
                        var target = Path.Combine(Data, "snapshots", $"slot{n:00}", Path.GetFileName(d));
                        if (!Directory.Exists(target)) CopyTree(d, target);
                    }
                _versions?.Merge(n, Path.Combine(stage, "versions"));
            }

            // The slot's settings: the program is remembered as an uploaded copy (a path on the old computer means nothing here), and a Room ID someone else holds is dropped.
            var remembered = programFile == null ? null : new ProgramMemory { Source = ProgramMemory.Upload, File = programFile, Running = imported.Program?.Running ?? true, Watch = false };
            var settings = (imported with { Program = remembered }).Validated();
            slot.UpdateSettings(_ => settings);
            if (!string.IsNullOrEmpty(imported.RoomId))
            {
                try { slot.SetRoomId(imported.RoomId); }
                catch (ArgumentException e) { slot.Log("Warning", $"The Room ID '{imported.RoomId}' from the file wasn't used: {e.Message}"); }
            }
            AfterSlotFiles?.Invoke(n);

            if (programFile == null) return new ImportedSlot(n, true, false, false, "Files and settings restored (the file has no program for this slot).");
            _versions?.RecordFolder(n, uploads, programFile, req.How, req.Who);
            if (!settings.Autostart) return new ImportedSlot(n, true, false, false, $"Files and settings restored. {programFile} is not loaded because this slot's autostart is off.");
            var start = remembered!.Running;
            var path = Path.Combine(uploads, programFile);
            try
            {
                var error = Loader != null ? Loader(n, path, start) : LoadDirect(n, path, start);
                if (error != null) return new ImportedSlot(n, true, false, false, $"Files restored, but {programFile} didn't load: {error}");
            }
            catch (Exception e) { return new ImportedSlot(n, true, false, false, $"Files restored, but {programFile} didn't load: {Readiness.Explain(e)}"); }
            slot.Log("Notice", $"Imported {programFile} from an export file ({(start ? "started" : "left stopped, as it was")}).");
            return new ImportedSlot(n, true, true, start, start ? $"{programFile} restored and started." : $"{programFile} restored (stopped, as it was exported).");
        });
    }

    private string? LoadDirect(int n, string path, bool start)
    {
        _processor.LoadProgram(n, path, start);
        return null;
    }

    private void ReplaceSettings(string stage, ExportBlob? blob, List<string> replaced, List<string> warnings)
    {
        if (!Directory.Exists(stage)) return;
        var backup = Path.Combine(Data, "tmp", "settings-before-import-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        foreach (var rel in DataLayout.SettingsFiles.Concat(DataLayout.SecretFiles))
        {
            var src = Path.Combine(stage, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(Data, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dst)) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(backup, rel))!); File.Copy(dst, Path.Combine(backup, rel), true); }
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, true);
            replaced.Add(rel switch
            {
                "integrations/global.json" => "integrations", "alerts/config.json" => "alerts", "schedules.json" => "schedules", "targets.json" => "targets",
                "backups.json" => "backups", "versions/settings.json" => "versions", "shares.json" => "shares", "integrations/hooks.json" => "hooks", _ => "symbols"
            });
        }
        if (File.Exists(Path.Combine(stage, "recent.json")))
        {
            replaced.Add("recent");
            try { RecentRestored?.Invoke(ReadRecent(Path.Combine(stage, "recent.json")).ToList()); } catch (Exception e) { warnings.Add("The recent programs list couldn't be restored: " + e.Message); }
        }
        if (File.Exists(Path.Combine(stage, "options.json")))
        {
            File.Copy(Path.Combine(stage, "options.json"), Path.Combine(Data, "imported-options.json"), true);
            warnings.Add("The console and mDNS options in the file (telnet, SSH, mDNS) are set in OpenVP's configuration, not here. They were saved as imported-options.json in the data folder for you to read.");
        }
    }

    public IEnumerable<RecentEntry> ReadRecent(string stageFile)
    {
        try { return JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(stageFile), Json) ?? new(); }
        catch (Exception e) when (e is JsonException or IOException) { return []; }
    }

    // Makes `target` hold exactly the content of `source` (or be empty if there is none).
    private static void Replace(string target, string source)
    {
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Directory.Exists(source)) CopyTree(source, target); else Directory.CreateDirectory(target);
    }

    internal static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var f in Directory.EnumerateFiles(source))
            File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
        foreach (var d in Directory.EnumerateDirectories(source))
            CopyTree(d, Path.Combine(target, Path.GetFileName(d)));
    }
}
