using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Core.Ops;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>Daily backups: written in the export format, kept for N days, failures reported, missed runs.</summary>
public static class BackupTests
{
    sealed class Clock { public DateTimeOffset Now = DateTimeOffset.Parse("2025-03-10T12:00:00Z"); }

    static BackupService Make(string data, Clock c, TimeZoneInfo? zone = null, bool real = true) =>
        new(data, (path, o) =>
        {
            if (!real) { File.WriteAllText(path, "fake"); return new ArchiveManifest(); }
            using var fs = new FileStream(path, FileMode.Create);
            return new ProcessorExporter(data, 2, new SecretStore(data), n => n == 1 ? "OpenVP SIMPL sample" : null).Export(fs, o);
        }, new SecretStore(data), zone ?? TimeZoneInfo.Utc, () => c.Now, autoStart: false);

    [Test]
    public static void Backup_now_writes_an_export_that_can_be_previewed()
    {
        var data = ProcessorArchiveTests.Populate(Paths.TempData());
        var c = new Clock();
        using var b = Make(data, c);
        var results = new List<(bool, string, string)>();
        b.Result += (ok, msg, why) => results.Add((ok, msg, why));
        var info = b.RunNow("manual");
        Assert.True(info != null, "backup made");
        Assert.True(File.Exists(Path.Combine(data, "backups", info!.File)), "in data/backups");
        Assert.True(info.Id.StartsWith("openvp-backup-20250310-120000"), "named by time");
        Assert.Equal(1, b.List().Count, "listed");
        Assert.True(results.Count == 1 && results[0].Item1 && results[0].Item3 == "manual", "result event");
        Assert.True(!File.Exists(Path.Combine(data, "backups", info.File + ".partial")), "no partial file left");
        using var p = new Processor(Paths.TempData(), 2);
        var prev = new ProcessorImporter(p, new SlotSnapshots(p.DataDirectory), null, null).Preview(b.PathOf(info.Id)!);
        Assert.Equal(2, prev.Slots.Count, "the backup is a normal export with both slots");
        Assert.True(b.Settings.LastOk == true, "status kept");
        Assert.True(b.Delete(info.Id) && b.List().Count == 0, "delete");
        Assert.True(b.PathOf("../../etc/passwd") == null && b.PathOf("openvp-backup-x") == null, "ids can't point elsewhere");
    }

    [Test]
    public static void Backups_older_than_the_keep_time_are_deleted_but_the_newest_stays()
    {
        var data = Paths.TempData();
        var c = new Clock();
        using var b = Make(data, c, real: false);
        b.Update(b.Settings with { KeepDays = 14 });
        Directory.CreateDirectory(Path.Combine(data, "backups"));
        foreach (var name in new[] { "20250201-030000", "20250215-030000", "20250225-030000", "20250309-030000", "20250310-030000" })
            File.WriteAllText(Path.Combine(data, "backups", $"openvp-backup-{name}.openvp"), "x");
        File.WriteAllText(Path.Combine(data, "backups", "notes.txt"), "keep me");
        Assert.Equal(5, b.List().Count, "five listed");
        var info = b.RunNow("scheduled"); // 2025-03-10 12:00; keep 14 days: cutoff 2025-02-24 12:00
        Assert.True(info != null, "made");
        var names = b.List().Select(x => x.Id.Replace("openvp-backup-", "")).OrderBy(x => x).ToList();
        Assert.Equal("20250225-030000|20250309-030000|20250310-030000|20250310-120000", string.Join("|", names), "the two older than 14 days are gone");
        Assert.True(File.Exists(Path.Combine(data, "backups", "notes.txt")), "other files are never touched");
        // Even when everything is old, the newest backup is kept.
        c.Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.Equal(3, b.Prune(), "all but the newest");
        Assert.Equal(1, b.List().Count, "one left");
    }

    [Test]
    public static void A_failing_backup_is_reported_and_clears_when_it_works_again()
    {
        var data = Paths.TempData();
        var c = new Clock();
        using var b = Make(data, c, real: false);
        var blocker = Path.Combine(data, "blocker");
        File.WriteAllText(blocker, "a file where the folder should be");
        b.Update(b.Settings with { Folder = Path.Combine(blocker, "sub") });
        var results = new List<(bool Ok, string Msg)>();
        b.Result += (ok, msg, _) => results.Add((ok, msg));
        Assert.True(b.RunNow("scheduled") == null, "no backup");
        Assert.True(results.Count == 1 && !results[0].Ok && results[0].Msg.Contains("failed"), "reported: " + string.Join(";", results));
        Assert.True(b.Settings.LastOk == false && b.Settings.LastMessage!.Contains("failed"), "kept in the status");
        b.Update(b.Settings with { Folder = Path.Combine(data, "elsewhere") });
        Assert.True(b.RunNow("scheduled") != null, "works with a good folder");
        Assert.True(results[^1].Ok, "success reported");
        Assert.True(Directory.Exists(Path.Combine(data, "elsewhere")) && b.List().Count == 1, "the chosen folder is used");
    }

    [Test]
    public static void Daily_backup_runs_at_its_time_and_once_after_downtime()
    {
        var data = Paths.TempData();
        var c = new Clock { Now = DateTimeOffset.Parse("2025-03-10T12:00:00Z") };
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        using (var b = Make(data, c, ny, real: false))
        {
            b.Update(b.Settings with { Time = "03:00" });
            Assert.Equal("2025-03-11T07:00:00.0000000+00:00", b.Settings.NextDue!.Value.UtcDateTime.ToString("o").Replace("Z", "+00:00"), "03:00 EDT tomorrow");
            b.Tick();
            Thread.Sleep(100);
            Assert.Equal(0, b.List().Count, "not yet");
        }
        // OpenVP was off for two days; when it starts the missed backup runs once.
        c.Now = DateTimeOffset.Parse("2025-03-12T15:00:00Z");
        using (var b = Make(data, c, ny, real: false))
        {
            b.Tick();
            Assert.Eventually(() => b.List().Count == 1, "ran once after the downtime");
            b.Tick(); Thread.Sleep(100);
            Assert.Equal(1, b.List().Count, "only once");
            Assert.True(b.Settings.NextDue > c.Now, "next one is in the future");
        }
        using (var b = Make(data, c, ny, real: false))
        {
            b.Update(b.Settings with { Enabled = false });
            Assert.True(b.Settings.NextDue == null, "off means no next backup");
        }
    }

    [Test]
    public static void Backups_can_include_secrets_only_with_a_password()
    {
        var data = ProcessorArchiveTests.Populate(Paths.TempData());
        var c = new Clock();
        using var b = Make(data, c);
        Assert.True(Assert.Throws<ArgumentException>(() => b.Update(b.Settings with { IncludeSecrets = true })).Message.Contains("password"), "needs a password");
        b.Update(b.Settings with { IncludeSecrets = true }, "backup password 123");
        var info = b.RunNow("manual")!;
        using var z = ZipFile.OpenRead(b.PathOf(info.Id)!);
        Assert.True(z.GetEntry("secrets.enc") != null, "secrets are in the backup");
        Assert.True(b.HasPassword, "password is kept in the secret store");
        Assert.True(!File.ReadAllText(Path.Combine(data, "backups.json")).Contains("backup password 123"), "but not in backups.json");
    }
}
