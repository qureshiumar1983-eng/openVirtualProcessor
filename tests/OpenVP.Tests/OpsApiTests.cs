using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Ops;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>The export, import, backup, version, editor, schedule and git endpoints on real servers in this process.</summary>
public static class OpsApiTests
{
    static string Tmp(string name = "x.openvp") => Path.Combine(Paths.TempData(), name);

    static byte[] Download(OpsTestServer s, HttpMethod m, string path, object? body = null)
    {
        var req = new HttpRequestMessage(m, s.Url + path);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var r = s.Http.Send(req);
        Assert.True(r.IsSuccessStatusCode, $"{path} -> {(int)r.StatusCode}");
        return r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    static string Smw(Func<string, string> edit) => edit(File.ReadAllText(Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw"), Encoding.Latin1));

    [Test]
    public static void Healthz_and_the_container_health_check()
    {
        using var s = new OpsTestServer();
        var r = s.Http.GetAsync(s.Url + "/healthz").GetAwaiter().GetResult();
        Assert.True(r.IsSuccessStatusCode && r.Content.ReadAsStringAsync().GetAwaiter().GetResult() == "ok", "/healthz answers ok without a key");
        Assert.Equal(0, HealthCheck.Run([$"--urls={s.Url}", "--healthcheck"]), "--healthcheck is 0 when up");
        Assert.Equal(1, HealthCheck.Run([$"--urls=http://127.0.0.1:{IntegrationServiceTests.FreePort()}", "--healthcheck"]), "and 1 when nothing answers");
        var noKey = new HttpClient();
        Assert.Equal(401, (int)noKey.GetAsync(s.Url + "/api/backups").GetAwaiter().GetResult().StatusCode, "the new APIs need the access key");
        Assert.Equal(401, (int)noKey.PostAsync(s.Url + "/api/export", new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult().StatusCode, "export needs it");
    }

    [Test]
    public static void Export_preview_and_import_through_the_api()
    {
        using var a = new OpsTestServer();
        using var b = new OpsTestServer();
        a.LoadSample(1);
        a.Post("/api/slots/1/files/folder", new { root = "nvram", path = "", name = "cfg" });
        File.WriteAllText(Path.Combine(a.Dir, "program01", "nvram", "cfg", "level.json"), "{\"level\":7}");
        var zip = Download(a, HttpMethod.Post, "/api/export", new { });
        var file = Tmp(); File.WriteAllBytes(file, zip);
        using (var z = ZipFile.OpenRead(file)) Assert.True(z.GetEntry("manifest.json") != null && z.GetEntry("slots/01/data/nvram/cfg/level.json") != null, "export has the files");

        var prev = b.Upload("/api/import/preview", file);
        Assert.Equal(200, prev.Status, prev.Text);
        Assert.Equal(1, prev.Json.GetProperty("slots").GetArrayLength(), "one slot in the file");
        Assert.True(prev.Json.GetProperty("slots")[0].GetProperty("program").GetString() == "SampleProgram" && !prev.Json.GetProperty("hasSecrets").GetBoolean(), "preview: " + prev.Text);
        Assert.True(b.Get("/api/slots/1").Json.GetProperty("program").ValueKind == JsonValueKind.Null, "the preview changed nothing");

        var done = b.Post("/api/import", new { previewId = prev.Json.GetProperty("previewId").GetString(), slots = new[] { 1 }, replaceSettings = false });
        Assert.Equal(200, done.Status, done.Text);
        Assert.True(done.Json.GetProperty("slots")[0].GetProperty("started").GetBoolean(), "started: " + done.Text);
        Assert.Equal("Running", b.Get("/api/slots/1").Json.GetProperty("state").GetString(), "running on B");
        Assert.Equal("{\"level\":7}", File.ReadAllText(Path.Combine(b.Dir, "program01", "nvram", "cfg", "level.json")), "the NVRAM file came across");
        Assert.True(b.Get("/api/activity").Json.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "import"), "activity entry");
        Assert.Equal(404, b.Post("/api/import", new { previewId = prev.Json.GetProperty("previewId").GetString() }).Status, "the waiting file is used up");

        // Importing again takes a snapshot of what is there first.
        var again = b.Upload("/api/import", file, fields: [("slots", "1")]);
        Assert.Equal(200, again.Status, again.Text);
        Assert.True(b.Get("/api/slots/1/snapshots").Json.EnumerateArray().Any(x => x.GetProperty("reason").GetString() == "before import"), "a snapshot before the import");

        // Bad files.
        var junk = Tmp("junk.openvp"); File.WriteAllText(junk, "not a zip");
        var bad = b.Upload("/api/import/preview", junk);
        Assert.True(bad.Status == 400 && bad.Json.GetProperty("code").GetString() == "bad-archive", "junk: " + bad.Text);
        var evil = Tmp("evil.openvp");
        using (var zf = new ZipArchive(File.Create(evil), ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zf.CreateEntry("manifest.json").Open())) w.Write("{\"format\":\"openvp-export\",\"version\":1,\"slots\":[{\"slot\":1}]}");
            using var w2 = new StreamWriter(zf.CreateEntry("slots/01/data/../../../../evil.txt").Open()); w2.Write("x");
        }
        var e = b.Upload("/api/import", evil);
        Assert.True(e.Status == 400 && e.Json.GetProperty("code").GetString() == "unsafe-path", "zip-slip refused: " + e.Text);
        Assert.True(!File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")) && !File.Exists(Path.Combine(b.Dir, "..", "evil.txt")), "nothing written outside");
    }

    [Test]
    public static void Export_with_secrets_needs_a_password_and_import_checks_it()
    {
        using var a = new OpsTestServer();
        using var b = new OpsTestServer();
        a.LoadSample(1);
        a.Put("/api/integrations/mqtt", new { enabled = false, host = "broker.local", port = 1883, password = "MQTT-secret-pw-1" });
        var noPw = a.Post("/api/export", new { secrets = true });
        Assert.Equal(400, noPw.Status, "secrets without a password: " + noPw.Text);
        var file = Tmp(); File.WriteAllBytes(file, Download(a, HttpMethod.Post, "/api/export", new { secrets = true, password = "export pass 12345" }));
        Assert.True(!Encoding.Latin1.GetString(File.ReadAllBytes(file)).Contains("MQTT-secret-pw-1"), "the secret isn't readable in the file");
        var prev = b.Upload("/api/import/preview", file, fields: [("password", "wrong password 1")]);
        Assert.True(prev.Json.GetProperty("hasSecrets").GetBoolean() && !prev.Json.GetProperty("passwordOk").GetBoolean(), "preview says the password is wrong: " + prev.Text);
        var id = prev.Json.GetProperty("previewId").GetString();
        var wrong = b.Post("/api/import", new { previewId = id, restoreSecrets = true, password = "wrong password 1" });
        Assert.True(wrong.Status == 422 && wrong.Json.GetProperty("code").GetString() == "wrong-password", "wrong password refused: " + wrong.Text);
        var ok = b.Post("/api/import", new { previewId = id, restoreSecrets = true, replaceSettings = true, password = "export pass 12345" });
        Assert.Equal(200, ok.Status, ok.Text);
        Assert.True(ok.Json.GetProperty("secretsRestored").GetBoolean(), "secrets restored");
        Assert.True(b.Get("/api/integrations/mqtt").Json.GetProperty("hasPassword").GetBoolean(), "B's MQTT password is set again (and still never returned)");
        Assert.True(!b.Get("/api/integrations/mqtt").Text.Contains("MQTT-secret-pw-1"), "never returned");
    }

    [Test]
    public static void Backups_through_the_api_and_the_failure_alert()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        var dest = Path.Combine(Paths.TempData(), "share");
        var set = s.Put("/api/backups/settings", new { enabled = true, time = "04:15", keepDays = 3, folder = dest });
        Assert.Equal(200, set.Status, set.Text);
        Assert.Equal("04:15", set.Json.GetProperty("settings").GetProperty("time").GetString(), "settings saved");
        Assert.True(s.Get("/api/backups").Json.GetProperty("next").ValueKind == JsonValueKind.String, "next backup shown");
        Assert.Equal(400, s.Put("/api/backups/settings", new { time = "99:99" }).Status, "bad time");
        Assert.Equal(400, s.Put("/api/backups/settings", new { keepDays = 0 }).Status, "bad keep time");

        var run = s.Post("/api/backups/run");
        Assert.Equal(200, run.Status, run.Text);
        var id = run.Json.GetProperty("backup").GetProperty("id").GetString()!;
        Assert.True(Directory.GetFiles(dest, "*.openvp").Length == 1, "written to the chosen folder");
        var dl = Download(s, HttpMethod.Get, $"/api/backups/{id}/download");
        Assert.True(dl.Length > 100 && dl[0] == 'P', "download is a zip");
        var prev = s.Post($"/api/backups/{id}/preview");
        Assert.True(prev.Status == 200 && prev.Json.GetProperty("slots").GetArrayLength() == 1, "restore starts at the preview: " + prev.Text);
        var restore = s.Post("/api/import", new { previewId = prev.Json.GetProperty("previewId").GetString(), slots = new[] { 1 }, how = "restore" });
        Assert.True(restore.Status == 200 && restore.Json.GetProperty("slots")[0].GetProperty("loaded").GetBoolean(), "restored: " + restore.Text);
        Assert.True(s.Get("/api/activity").Json.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "restore"), "activity says restore");
        Assert.Equal(404, s.Get("/api/backups/../../etc/passwd/download").Status == 404 ? 404 : 0, "an id can't point elsewhere");

        // A backup that can't be written raises an alert.
        var blocker = Path.Combine(Paths.TempData(), "blocker"); File.WriteAllText(blocker, "x");
        s.Put("/api/backups/settings", new { folder = Path.Combine(blocker, "sub") });
        var fail = s.Post("/api/backups/run");
        Assert.Equal(500, fail.Status, "failure reported: " + fail.Text);
        Assert.Eventually(() => s.Get("/api/alerts").Json.GetProperty("active").EnumerateArray().Any(x => x.GetProperty("kind").GetString() == "backup.failed"), "a backup.failed alert is raised");
        s.Put("/api/backups/settings", new { folder = dest });
        Assert.Equal(200, s.Post("/api/backups/run").Status, "and the next good backup works");
        Assert.Eventually(() => !s.Get("/api/alerts").Json.GetProperty("active").EnumerateArray().Any(x => x.GetProperty("kind").GetString() == "backup.failed"), "clearing the alert");
        Assert.True(s.Delete($"/api/backups/{id}").Status == 200 && s.Get("/api/backups").Json.GetProperty("backups").GetArrayLength() >= 1, "delete one");
    }

    [Test]
    public static void Versions_history_load_and_compare_through_the_api()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        s.LoadSample(1, smwText: Smw(t => t.Replace("P1=\"Hello from SIMPL\"", "P1=\"Hello again\"").Replace("Nm=both_pressed\n", "Nm=both_pressed_v2\n")));
        s.LoadSample(1);  // the first program again: the same version, not a new one
        var v = s.Get("/api/slots/1/versions").Json;
        Assert.Equal(2, v.GetProperty("versions").GetArrayLength(), "two versions (the repeat is not a third): " + v);
        var list = v.GetProperty("versions").EnumerateArray().ToList();
        Assert.True(list[0].GetProperty("current").GetBoolean() && list[0].GetProperty("loads").GetInt32() == 2 && list[0].GetProperty("how").GetString() == "upload", "the first program is current, loaded twice, by upload");
        var older = list[1].GetProperty("id").GetString()!;

        var note = s.Put($"/api/slots/1/versions/{older}", new { note = "variant with v2 signal" });
        Assert.Equal("variant with v2 signal", note.Json.GetProperty("note").GetString(), "note saved");

        var diff = s.Post("/api/slots/1/versions/diff", new { a = "loaded", b = older });
        Assert.Equal(200, diff.Status, diff.Text);
        var sections = diff.Json.GetProperty("sections").EnumerateArray().ToDictionary(x => x.GetProperty("title").GetString()!, x => x);
        Assert.True(sections["Signals"].GetProperty("items").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "both_pressed_v2" && x.GetProperty("change").GetString() == "added"), "signal added: " + diff.Json.GetProperty("text"));
        Assert.True(diff.Json.GetProperty("text").GetString()!.Contains("Hello again"), "readable text mentions the changed parameter");
        Assert.True(s.Post("/api/slots/1/versions/diff", new { a = "loaded", b = "loaded" }).Json.GetProperty("identical").GetBoolean(), "a program against itself is identical");
        Assert.Equal(404, s.Post("/api/slots/1/versions/diff", new { a = "loaded", b = "ffffffffffff" }).Status, "unknown version");

        Assert.Equal(409, s.Delete($"/api/slots/1/versions/{list[0].GetProperty("id").GetString()}").Status, "the loaded program can't be deleted from the history");
        var dl = s.Http.GetAsync($"{s.Url}/api/slots/1/versions/{older}/download").GetAwaiter().GetResult();
        Assert.True(dl.IsSuccessStatusCode && dl.Content.Headers.ContentType!.MediaType == "application/zip", "a program with sidecars downloads as a zip");

        var load = s.Post($"/api/slots/1/versions/{older}/load");
        Assert.Equal(200, load.Status, load.Text);
        Assert.True(s.Get("/api/slots/1/signals").Text.Contains("both_pressed_v2"), "the older version is loaded and running");
        var after = s.Get("/api/slots/1/versions").Json;
        Assert.True(after.GetProperty("current").GetString() == older && after.GetProperty("versions").EnumerateArray().First(x => x.GetProperty("id").GetString() == older).GetProperty("lastHow").GetString() == "version", "now current, last loaded by 'version'");

        Assert.Equal(200, s.Put("/api/versions/settings", new { keep = 5 }).Status, "keep setting");
        Assert.Equal(400, s.Put("/api/versions/settings", new { keep = 0 }).Status, "range");
        // Rolling back from a snapshot is a version load too.
        var snap = s.Get("/api/slots/1/snapshots").Json[0].GetProperty("id").GetString();
        s.Post($"/api/slots/1/snapshots/{snap}/restore");
        Assert.True(s.Get("/api/slots/1/versions").Json.GetProperty("versions").EnumerateArray().Any(x => x.GetProperty("lastHow").GetString() == "roll back"), "roll back is recorded");
    }

    [Test]
    public static void Editing_files_through_the_api()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        var path = Path.Combine(s.Dir, "program01", "nvram", "settings.json");
        File.WriteAllText(path, "{\n  \"volume\": 10\n}\n");
        var list = s.Get("/api/slots/1/files?root=nvram").Json.GetProperty("entries");
        Assert.True(list.EnumerateArray().First(x => x.GetProperty("name").GetString() == "settings.json").GetProperty("editable").GetBoolean(), "the list says a json file is editable");
        var opened = s.Get("/api/slots/1/files/text?root=nvram&path=settings.json").Json;
        var tag = opened.GetProperty("tag").GetString();
        Assert.True(opened.GetProperty("kind").GetString() == "json" && opened.GetProperty("text").GetString()!.Contains("\"volume\": 10"), "opened");

        var bad = s.Put("/api/slots/1/files/text?root=nvram&path=settings.json", new { text = "{\n  \"volume\": ,\n}", tag });
        Assert.True(bad.Status == 400 && bad.Json.GetProperty("invalid").GetBoolean() && bad.Json.GetProperty("line").GetInt32() == 2, "validation error with its line: " + bad.Text);
        var val = s.Post("/api/slots/1/files/validate", new { name = "settings.json", text = "{\"a\": }" });
        Assert.True(!val.Json.GetProperty("ok").GetBoolean() && val.Json.GetProperty("line").GetInt32() == 1, "live validation");

        var good = s.Put("/api/slots/1/files/text?root=nvram&path=settings.json", new { text = "{\n  \"volume\": 55\n}\n", tag });
        Assert.True(good.Status == 200 && good.Json.GetProperty("undo").ValueKind == JsonValueKind.String, "saved with an undo: " + good.Text);
        Assert.Equal("{\n  \"volume\": 55\n}\n", File.ReadAllText(path), "on disk");

        File.WriteAllText(path, "{\"changed\":\"by the program\"}");
        var conflict = s.Put("/api/slots/1/files/text?root=nvram&path=settings.json", new { text = "{\"mine\":1}", tag = good.Json.GetProperty("tag").GetString() });
        Assert.True(conflict.Status == 409 && conflict.Json.GetProperty("conflict").GetBoolean(), "file changed on disk: " + conflict.Text);
        Assert.Equal("{\"changed\":\"by the program\"}", File.ReadAllText(path), "not overwritten");
        var forced = s.Put("/api/slots/1/files/text?root=nvram&path=settings.json", new { text = "{\"mine\":1}", tag = "stale", force = true });
        var undo = forced.Json.GetProperty("undo").GetString();
        Assert.Equal(200, s.Post($"/api/trash/{undo}/restore").Status, "undo through the trash");
        Assert.Equal("{\"changed\":\"by the program\"}", File.ReadAllText(path), "undo brings back what was there");
        Assert.True(s.Get("/api/activity?slot=1").Json.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "file.edit"), "activity entry for the edit");

        File.WriteAllText(Path.Combine(s.Dir, "program01", "nvram", "huge.txt"), new string('a', 2_100_000));
        Assert.Equal(413, s.Get("/api/slots/1/files/text?root=nvram&path=huge.txt").Status, "over 2 MB");
        Assert.Equal(400, s.Get("/api/slots/1/files/text?root=nvram&path=../../apikey.txt").Status, "no way out of the folder");
        Assert.Equal(404, s.Get("/api/slots/1/files/text?root=nvram&path=nothere.txt").Status, "missing");
    }

    [Test]
    public static void Schedules_through_the_api_run_actions_and_keep_history()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        var bad = s.Post("/api/schedules", new { name = "x", mode = "daily", time = "3pm", action = new { type = "restart", slot = 1 } });
        Assert.True(bad.Status == 400 && bad.Text.Contains("HH:mm"), "bad time explained: " + bad.Text);
        var add = s.Post("/api/schedules", new { name = "Nightly restart", enabled = true, mode = "daily", time = "03:30", action = new { type = "restart", slot = 1 } });
        Assert.Equal(200, add.Status, add.Text);
        var sc = add.Json.GetProperty("schedules")[0];
        Assert.True(sc.GetProperty("nextRun").ValueKind == JsonValueKind.String && sc.GetProperty("summary").GetString() == "Every day at 03:30", "next run and summary: " + sc);
        Assert.True(add.Json.GetProperty("zone").GetString()!.Length > 0, "zone shown");
        var id = sc.GetProperty("id").GetString()!;

        var gen = s.P.Slot(1).Generation;
        var run = s.Post($"/api/schedules/{id}/run");
        Assert.Equal(200, run.Status, run.Text);
        Assert.Equal("ok", run.Json.GetProperty("run").GetProperty("status").GetString(), "ran: " + run.Text);
        Assert.True(s.P.Slot(1).Generation != gen, "the slot was restarted");
        var st = s.Get("/api/schedules").Json;
        Assert.True(st.GetProperty("history").GetArrayLength() == 1 && st.GetProperty("schedules")[0].GetProperty("lastOk").GetBoolean(), "history and last result");
        Assert.True(s.Get("/api/activity").Json.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "schedule" && x.GetProperty("who").GetString() == "auto"), "activity entry by auto");

        // set a signal, pulse, stop
        var set = s.Post("/api/schedules", new { name = "Source 1", mode = "cron", cron = "0 7 * * mon-fri", action = new { type = "setSignal", slot = 1, signal = "toggle_press", value = "1" } });
        var setId = set.Json.GetProperty("schedules").EnumerateArray().First(x => x.GetProperty("name").GetString() == "Source 1").GetProperty("id").GetString();
        Assert.Equal("ok", s.Post($"/api/schedules/{setId}/run").Json.GetProperty("run").GetProperty("status").GetString(), "set a signal");
        var broken = s.Post("/api/schedules", new { name = "Wrong signal", mode = "cron", cron = "0 7 * * *", action = new { type = "setSignal", slot = 1, signal = "no_such_signal", value = "1" } });
        var brokenId = broken.Json.GetProperty("schedules").EnumerateArray().First(x => x.GetProperty("name").GetString() == "Wrong signal").GetProperty("id").GetString();
        var failed = s.Post($"/api/schedules/{brokenId}/run").Json.GetProperty("run");
        Assert.True(failed.GetProperty("status").GetString() == "failed" && failed.GetProperty("message").GetString()!.Contains("no signal"), "failure explained: " + failed);
        Assert.Eventually(() => s.Get("/api/alerts").Json.GetProperty("active").EnumerateArray().Any(x => x.GetProperty("kind").GetString() == "schedule.failed"), "and an alert is raised");

        var prev = s.Post("/api/schedules/preview", new { name = "p", mode = "weekly", time = "06:00", days = new[] { 1, 3 }, action = new { type = "backup" } });
        Assert.True(prev.Json.GetProperty("next").GetArrayLength() == 5 && prev.Json.GetProperty("summary").GetString() == "Mon, Wed at 06:00", "preview lists five times: " + prev.Text);
        var off = s.Put($"/api/schedules/{id}", new { name = "Nightly restart", enabled = false, mode = "daily", time = "03:30", action = new { type = "restart", slot = 1 } });
        Assert.True(!off.Json.GetProperty("schedules").EnumerateArray().First(x => x.GetProperty("id").GetString() == id).GetProperty("enabled").GetBoolean(), "disabled");
        Assert.Equal(200, s.Delete($"/api/schedules/{id}").Status, "delete");
        Assert.Equal(404, s.Post($"/api/schedules/{id}/run").Status, "gone");
    }

    [Test]
    public static void Schedules_can_load_a_version_and_a_one_off_runs_by_itself()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        s.LoadSample(1, smwText: Smw(t => t.Replace("Nm=both_pressed\n", "Nm=both_pressed_v2\n")));
        var older = s.Get("/api/slots/1/versions").Json.GetProperty("versions").EnumerateArray().First(x => !x.GetProperty("current").GetBoolean()).GetProperty("id").GetString();
        var when = DateTime.Now.AddSeconds(2);
        var add = s.Post("/api/schedules", new { name = "Back to v1", mode = "once", at = when.ToString("yyyy-MM-dd'T'HH:mm:ss"), action = new { type = "loadVersion", slot = 1, versionId = older } });
        Assert.Equal(200, add.Status, add.Text);
        // The one-off has a minute-resolution time field in the page, but seconds are accepted by the API, so this runs on its own.
        Assert.Eventually(() => s.Get("/api/schedules").Json.GetProperty("history").GetArrayLength() > 0, "the one-off ran by itself", 8000);
        var h = s.Get("/api/schedules").Json.GetProperty("history")[0];
        Assert.True(h.GetProperty("status").GetString() == "ok" && h.GetProperty("message").GetString()!.Contains("Loaded"), "ran fine: " + h);
        Assert.True(!s.Get("/api/slots/1/signals").Text.Contains("both_pressed_v2"), "the older version is loaded again");
        Assert.True(s.Get("/api/schedules").Json.GetProperty("schedules")[0].GetProperty("done").GetBoolean(), "marked done");
    }

    [Test]
    public static void Git_config_through_the_api()
    {
        if (GitCli.Version() == null) { Console.WriteLine("        (git isn't installed here; skipped)"); return; }
        using var s = new OpsTestServer();
        s.LoadSample(1);
        var repo = Path.Combine(Paths.TempData(), "cfg");
        var st = s.Get("/api/git").Json;
        Assert.True(st.GetProperty("available").GetBoolean() && !st.GetProperty("settings").GetProperty("enabled").GetBoolean(), "off by default");
        Assert.Equal(400, s.Put("/api/git", new { enabled = true }).Status, "can't turn on without a folder");
        Assert.Equal(400, s.Put("/api/git", new { repo = Path.Combine(s.Dir, "inside") }).Status, "not inside the data folder");
        var init = s.Post("/api/git/init", new { repo });
        Assert.Equal(200, init.Status, init.Text);
        Assert.Equal(200, s.Put("/api/git", new { enabled = true, repo }).Status, "turned on");
        Assert.Eventually(() => s.Get("/api/git").Json.GetProperty("commits").GetArrayLength() >= 1, "turning it on makes a first commit from the activity entry", 12000);
        File.WriteAllText(Path.Combine(s.Dir, "program01", "nvram", "x.txt"), "x");
        var c = s.Post("/api/git/commit", new { message = "Hand commit" });
        Assert.True(c.Status == 200 && c.Json.GetProperty("committed").GetBoolean(), "commit now: " + c.Text);
        Assert.Equal("Hand commit", GitCli.Run(repo, TimeSpan.FromSeconds(10), "log", "-1", "--pretty=%s").Output, "message");
        var tree = GitCli.Run(repo, TimeSpan.FromSeconds(10), "ls-files").Output;
        Assert.True(tree.Contains("slots/01/nvram/x.txt") && tree.Contains("slots/01/program/SampleProgram.smw") && !tree.Contains("apikey") && !tree.Contains("secrets"), "tree: " + tree);
        Assert.True(!GitCli.Run(repo, TimeSpan.FromSeconds(10), "log", "-p", "--all").Output.Contains(s.Key), "the access key isn't in the history");
        Assert.True(s.Get("/api/activity").Json.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "git"), "commits show in the activity feed");
    }
}
