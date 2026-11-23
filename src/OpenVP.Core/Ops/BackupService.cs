using System.Text.Json;
using System.Text.RegularExpressions;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

/// <summary>
/// Backup settings, in <c>data/backups.json</c>.
/// </summary>
/// <param name="Folder">Where backups are written: empty for <c>data/backups</c>, or any folder this computer can write to (a network share path works).</param>
/// <param name="Time">Daily time, <c>HH:mm</c> local.</param>
/// <param name="IncludeSecrets">Include secrets, encrypted with the password kept in the secret store under <c>backup.password</c>.</param>
public sealed record BackupSettings
{
    public bool Enabled { get; init; } = true;
    public string Time { get; init; } = "03:00";
    public int KeepDays { get; init; } = 14;
    public string? Folder { get; init; }
    public bool IncludeHistory { get; init; }
    public bool IncludeSnapshots { get; init; }
    public bool IncludeSecrets { get; init; }
    public DateTimeOffset? NextDue { get; init; }
    public DateTimeOffset? LastRun { get; init; }
    public bool? LastOk { get; init; }
    public string? LastMessage { get; init; }

    public BackupSettings Validated()
    {
        ScheduleDef.ParseTime(Time);
        if (KeepDays is < 1 or > 3650) throw new ArgumentException("Keep backups for 1 to 3650 days.");
        var f = string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim();
        if (f != null && (f.Length > 500 || f.Any(char.IsControl))) throw new ArgumentException("That folder path isn't valid.");
        return this with { Folder = f };
    }
}

public sealed record BackupInfo(string Id, string File, DateTimeOffset Time, long Size, string Reason);

/// <summary>
/// Backs the whole processor up in the export format (see <see cref="ProcessorExporter"/>) once a day at a chosen time, or when asked, into <c>data/backups</c> or a folder you choose, and
/// deletes backups older than the keep time. A backup that fails (folder not writable, disk full) is reported through <see cref="Result"/>, which the server connects to an alert.
/// If OpenVP wasn't running at the daily time, the backup runs once when it starts (a missed backup is worse than a late one).
/// </summary>
public sealed class BackupService : IDisposable
{
    private static readonly Regex IdRe = new(@"^openvp-backup-\d{8}-\d{6}(-[a-z]+)?(-\d+)?$", RegexOptions.Compiled);
    private readonly string _data, _file;
    private readonly Func<string, ExportOptions, ArchiveManifest> _writeExport; // (path, options)
    private readonly SecretStore? _secrets;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _zone;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _running = new(1, 1);
    private BackupSettings _settings;
    private Timer? _timer;

    /// <summary>Raised after every backup try: success, the message (the file name, or what went wrong) and the reason (<c>scheduled</c>, <c>manual</c>, ...).</summary>
    public event Action<bool, string, string>? Result;

    public BackupService(string dataDirectory, Func<string, ExportOptions, ArchiveManifest> writeExport, SecretStore? secrets = null, TimeZoneInfo? zone = null, Func<DateTimeOffset>? clock = null, bool autoStart = true)
    {
        _data = dataDirectory; _file = Path.Combine(dataDirectory, "backups.json"); _writeExport = writeExport; _secrets = secrets;
        _zone = zone ?? TimeZoneInfo.Local; _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _settings = Load();
        if (autoStart) _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    private BackupSettings Load()
    {
        var s = IntegrationFiles.Load<BackupSettings>(_file);
        try { s = s.Validated(); } catch (ArgumentException) { s = new BackupSettings(); }
        return Schedule(s, null);
    }

    public void Reload() { lock (_gate) _settings = Load(); }

    public BackupSettings Settings { get { lock (_gate) return _settings; } }

    /// <summary>Where backups go.</summary>
    public string Folder => Path.GetFullPath(string.IsNullOrWhiteSpace(Settings.Folder) ? Path.Combine(_data, "backups") : Settings.Folder!);

    private BackupSettings Schedule(BackupSettings s, DateTimeOffset? after)
    {
        if (!s.Enabled) return s with { NextDue = null };
        var (h, m) = ScheduleDef.ParseTime(s.Time);
        var cron = CronExpression.Parse($"{m} {h} * * *");
        // Keep a due time that is already set (it may be in the past: OpenVP was off, so the backup runs once now).
        if (after == null && s.NextDue != null) return s;
        return s with { NextDue = cron.Next(after ?? _clock(), _zone) };
    }

    public BackupSettings Update(BackupSettings next, string? password = null)
    {
        var v = next.Validated();
        if (v.IncludeSecrets)
        {
            if (!string.IsNullOrEmpty(password))
            {
                if (password.Length < ArchiveCrypto.MinPasswordLength) throw new ArgumentException($"The backup password needs at least {ArchiveCrypto.MinPasswordLength} characters.");
                _secrets?.Set("backup.password", password);
            }
            else if (_secrets == null || !_secrets.Has("backup.password")) throw new ArgumentException("Choose a password to protect the secrets in backups.");
        }
        lock (_gate)
        {
            v = v with { LastRun = _settings.LastRun, LastOk = _settings.LastOk, LastMessage = _settings.LastMessage, NextDue = null };
            _settings = Schedule(v, _clock());
            Save();
            return _settings;
        }
    }

    public bool HasPassword => _secrets?.Has("backup.password") == true;

    private void Save()
    {
        try { IntegrationFiles.Save(_file, _settings); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Starts a backup if one is due (it also runs after downtime). Called every 30 seconds.</summary>
    public void Tick()
    {
        BackupSettings s;
        lock (_gate) s = _settings;
        if (s.Enabled && s.NextDue is { } due && _clock() >= due) _ = Task.Run(() => RunNow("scheduled"));
    }

    /// <summary>Makes a backup now. Never throws: a failure is recorded, raised through <see cref="Result"/> and returned as null.</summary>
    public BackupInfo? RunNow(string reason = "manual")
    {
        if (!_running.Wait(0)) return null; // one at a time
        try
        {
            var s = Settings;
            var stamp = _clock().ToUniversalTime();
            var folder = Folder;
            var name = $"openvp-backup-{stamp:yyyyMMdd-HHmmss}" + (reason == "scheduled" ? "" : reason == "manual" ? "-manual" : "-" + Regex.Replace(reason.ToLowerInvariant(), "[^a-z]", ""));
            for (var n = 2; File.Exists(Path.Combine(folder, name + ".openvp")); n++) name = Regex.Replace(name, @"-\d+$", "") + "-" + n; // two in the same second
            var path = Path.Combine(folder, name + ".openvp");
            var tmp = path + ".partial";
            try
            {
                Directory.CreateDirectory(folder);
                string? password = s.IncludeSecrets ? _secrets?.Get("backup.password") : null;
                if (s.IncludeSecrets && password == null) throw new InvalidOperationException("The backup password is missing, so secrets can't be included. Set it again in the backup settings.");
                var opts = new ExportOptions { IncludeHistory = s.IncludeHistory, IncludeSnapshots = s.IncludeSnapshots, IncludeSecrets = s.IncludeSecrets, Password = password, Reason = reason };
                _writeExport(tmp, opts);
                File.Move(tmp, path, true);
                var removed = Prune();
                Record(true, reason, $"{name}.openvp" + (removed > 0 ? $" (deleted {removed} old backup{(removed == 1 ? "" : "s")})" : ""));
                return new BackupInfo(name, name + ".openvp", stamp, new FileInfo(path).Length, reason);
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                Record(false, reason, $"The backup to {folder} failed: {e.Message}");
                return null;
            }
        }
        finally { _running.Release(); }
    }

    private void Record(bool ok, string reason, string message)
    {
        lock (_gate)
        {
            var now = _clock();
            _settings = Schedule(_settings with { LastRun = now, LastOk = ok, LastMessage = message, NextDue = null }, now);
            Save();
        }
        try { Result?.Invoke(ok, message, reason); } catch { }
    }

    public List<BackupInfo> List()
    {
        var list = new List<BackupInfo>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in Directory.EnumerateFiles(Folder, "openvp-backup-*.openvp"))
            {
                var id = Path.GetFileNameWithoutExtension(f);
                if (!IdRe.IsMatch(id)) continue;
                var fi = new FileInfo(f);
                var m = Regex.Match(id, @"(\d{8})-(\d{6})(?:-([a-z]+))?(?:-\d+)?$");
                var time = DateTimeOffset.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : fi.LastWriteTimeUtc;
                list.Add(new BackupInfo(id, fi.Name, time, fi.Length, m.Groups[3].Success ? m.Groups[3].Value : "scheduled"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list.OrderByDescending(b => b.Time).ToList();
    }

    public string? PathOf(string? id) => id != null && IdRe.IsMatch(id) && File.Exists(Path.Combine(Folder, id + ".openvp")) ? Path.Combine(Folder, id + ".openvp") : null;

    public bool Delete(string? id)
    {
        var p = PathOf(id);
        if (p == null) return false;
        File.Delete(p);
        return true;
    }

    /// <summary>Deletes backups older than the keep time (the newest backup always stays). Returns how many.</summary>
    public int Prune()
    {
        var cutoff = _clock() - TimeSpan.FromDays(Settings.KeepDays);
        var all = List();
        var n = 0;
        foreach (var b in all.Skip(1).Where(b => b.Time < cutoff))
            try { File.Delete(Path.Combine(Folder, b.File)); n++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return n;
    }

    public void Dispose() { _timer?.Dispose(); _timer = null; }
}
