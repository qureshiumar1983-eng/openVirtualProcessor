using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Core.Ops;

namespace OpenVP.Tests;

/// <summary>Git-backed config: what is committed (and what never is), the commit messages, debounce, and pushing to a remote.</summary>
public static class GitSyncTests
{
    const string AccessKey = "ACCESSKEY-9f3a1c", MqttPw = "MQTT-PW-1234", SmtpPw = "SMTP-PW-5678", HookSecret = "HOOKSECRET-4455", AuthHeader = "TOPSECRET-7788", ShareToken = "SHARETOKEN-ab12cd", IncomingToken = "INCOMINGTOKEN-ef34";
    static readonly string[] Secrets = [AccessKey, MqttPw, SmtpPw, HookSecret, AuthHeader, ShareToken, IncomingToken];

    static bool HaveGit() { if (GitCli.Version() != null) return true; Console.WriteLine("        (git isn't installed here; skipped)"); return false; }

    static GitConfigSync Make(string data, TimeSpan? debounce = null, TimeSpan? settle = null)
    {
        var secrets = new SecretStore(data);
        return new GitConfigSync(data, 2, () => secrets.Snapshot().Values.Append(AccessKey), debounce, settle);
    }

    static string Git(string repo, params string[] args) => GitCli.Run(repo, TimeSpan.FromSeconds(30), args).Output;

    static string NewRepoPath() => Path.Combine(Paths.TempData(), "config-repo");

    [Test]
    public static void Commit_has_settings_text_files_and_programs_but_never_secrets()
    {
        if (!HaveGit()) return;
        var data = ProcessorArchiveTests.Populate(Paths.TempData());
        File.WriteAllText(Path.Combine(data, "program01", "user", "notes.txt"), "the key is " + AccessKey);        // a file that contains the access key
        File.WriteAllText(Path.Combine(data, "program01", "user", "my-password.txt"), "hunter2");                     // a name that looks like a secret
        File.WriteAllBytes(Path.Combine(data, "program01", "nvram", "state.bin"), [1, 2, 3, 0, 4]);                 // binary
        File.WriteAllText(Path.Combine(data, "program01", "nvram", "uses-mqtt.txt"), "broker password " + MqttPw);  // a saved secret copied into a config file
        using var g = Make(data);
        var repo = NewRepoPath();
        Assert.True(g.Init(repo).Contains("Created"), "init");
        g.Update(g.Settings with { Enabled = true, Repo = repo });
        var r = g.CommitNow("Loaded SampleProgram.smw into slot 1");
        Assert.True(r.Committed && r.Hash != null, "committed: " + r.Message);
        Assert.Equal("Loaded SampleProgram.smw into slot 1", Git(repo, "log", "-1", "--pretty=%s"), "the message comes from the activity entry");
        var files = Git(repo, "ls-files").Split('\n').ToHashSet();
        foreach (var want in new[] { "slots/01/slot.json", "slots/01/nvram/settings.json", "slots/01/user/config/rooms.csv", "slots/01/devices/simulators.json", "slots/01/devices/integrations.json", "slots/01/program/SampleProgram.smw", "slots/02/slot.json", "processor/integrations/global.json", "processor/alerts/config.json" })
            Assert.True(files.Contains(want), $"{want} is in the repository. Files: {string.Join(", ", files)}");
        foreach (var never in new[] { "apikey.txt", "secrets.json", "shares.json", "hooks.json", "notes.txt", "my-password.txt", "state.bin", "uses-mqtt.txt" })
            Assert.True(!files.Any(f => f.EndsWith(never)), $"{never} is never committed");
        Assert.True(r.Skipped.Any(s => s.Contains("notes.txt") && s.Contains("secret")) && r.Skipped.Any(s => s.Contains("my-password.txt")) && r.Skipped.Any(s => s.Contains("state.bin")), "what was left out is listed: " + string.Join(" | ", r.Skipped));
        // Nothing secret anywhere in the history, content or messages.
        var everything = Git(repo, "log", "-p", "--all", "--pretty=full");
        foreach (var s in Secrets) Assert.True(!everything.Contains(s), $"'{s}' must not be anywhere in the repository history");
        var integ = File.ReadAllText(Path.Combine(repo, "slots", "01", "devices", "integrations.json"));
        Assert.True(integ.Contains("Lights") && integ.Contains("X-Note") && !integ.Contains("Authorization"), "the webhook is kept, its secret and auth header aren't");
    }

    [Test]
    public static void Changes_are_committed_and_removed_files_leave_the_repository()
    {
        if (!HaveGit()) return;
        var data = ProcessorArchiveTests.Populate(Paths.TempData());
        using var g = Make(data);
        var repo = NewRepoPath();
        g.Init(repo);
        g.Update(g.Settings with { Enabled = true, Repo = repo });
        g.CommitNow("first");
        var again = g.CommitNow("second");
        Assert.True(!again.Committed && again.Message.StartsWith("Nothing"), "no change, no commit");
        File.WriteAllText(Path.Combine(data, "program01", "nvram", "settings.json"), "{\"volume\":99}");
        File.Delete(Path.Combine(data, "program01", "user", "config", "rooms.csv"));
        var r = g.CommitNow("Edited settings.json");
        Assert.True(r.Committed, "committed the edit");
        Assert.Equal("{\"volume\":99}", File.ReadAllText(Path.Combine(repo, "slots", "01", "nvram", "settings.json")), "new content");
        Assert.True(!File.Exists(Path.Combine(repo, "slots", "01", "user", "config", "rooms.csv")), "the deleted file is gone from the repository");
        Assert.Equal(2, g.Log(10).Count, "two commits");
        Assert.True(g.Settings.LastHash == r.Hash && g.Settings.LastMessage == "Edited settings.json", "status");
    }

    [Test]
    public static void Commits_are_debounced_and_messages_are_gathered()
    {
        if (!HaveGit()) return;
        var data = ProcessorArchiveTests.Populate(Paths.TempData());
        using var g = Make(data, debounce: TimeSpan.FromSeconds(2), settle: TimeSpan.FromMilliseconds(150));
        var repo = NewRepoPath();
        g.Init(repo);
        g.Update(g.Settings with { Enabled = true, Repo = repo });
        g.CommitNow("start"); // the first commit stamps the debounce clock
        var n0 = g.Log(50).Count;
        File.WriteAllText(Path.Combine(data, "program01", "nvram", "a.txt"), "1");
        g.RequestCommit("Uploaded a.txt");
        File.WriteAllText(Path.Combine(data, "program01", "nvram", "b.txt"), "2");
        g.RequestCommit("Uploaded b.txt");
        g.RequestCommit("Changed slot 1 settings: name");
        Thread.Sleep(900);
        Assert.Equal(n0, g.Log(50).Count, "not committed yet: a commit waits until the debounce time has passed");
        Assert.Eventually(() => g.Log(50).Count == n0 + 1, "one commit for the whole burst", 6000);
        var subject = Git(repo, "log", "-1", "--pretty=%s");
        Assert.True(subject.StartsWith("Changed slot 1 settings: name") && subject.Contains("2 more changes"), "subject is the last change plus a count: " + subject);
        var body = Git(repo, "log", "-1", "--pretty=%b");
        Assert.True(body.Contains("Uploaded a.txt") && body.Contains("Uploaded b.txt"), "body lists them: " + body);
        Assert.True(File.Exists(Path.Combine(repo, "slots", "01", "nvram", "a.txt")) && File.Exists(Path.Combine(repo, "slots", "01", "nvram", "b.txt")), "both files in the one commit");
    }

    [Test]
    public static void Pushes_to_a_remote_and_validates_settings()
    {
        if (!HaveGit()) return;
        var data = ProcessorArchiveTests.Populate(Paths.TempData());
        var bare = Path.Combine(Paths.TempData(), "remote.git");
        Directory.CreateDirectory(bare);
        Git(bare, "init", "--bare", "-q");
        using var g = Make(data);
        var repo = NewRepoPath();
        g.Init(repo);
        g.Update(g.Settings with { Enabled = true, Repo = repo, Remote = bare, Push = true });
        Assert.True(g.CommitNow("to the remote").Committed, "committed");
        Assert.Eventually(() => g.Settings.LastPushResult == "Pushed.", "pushed in the background", 10000);
        Assert.Equal("to the remote", Git(bare, "log", "-1", "--pretty=%s", "main"), "the remote has the commit");
        Assert.Equal("Pushed.", g.PushNow(), "push now");

        Assert.True(Assert.Throws<ArgumentException>(() => g.Update(g.Settings with { Repo = Path.Combine(data, "repo-inside") })).Message.Contains("data folder"), "repo can't be inside the data folder");
        Assert.True(Assert.Throws<ArgumentException>(() => g.Update(g.Settings with { Repo = "relative/path" })).Message.Contains("full path"), "full path");
        Assert.True(Assert.Throws<ArgumentException>(() => g.Update(g.Settings with { Remote = "--upload-pack=evil" })).Message.Contains("remote"), "a remote can't look like an option");
        Assert.True(Assert.Throws<ArgumentException>(() => g.Update(g.Settings with { Branch = "a..b" })).Message.Contains("branch"), "branch name");
        Assert.True(Assert.Throws<ArgumentException>(() => g.Init(Path.Combine(data, "x"))).Message.Contains("data folder"), "init inside the data folder");
        var notEmpty = Paths.TempData();
        File.WriteAllText(Path.Combine(notEmpty, "something.txt"), "x");
        Assert.True(Assert.Throws<ArgumentException>(() => g.Init(notEmpty)).Message.Contains("isn't empty"), "init refuses a folder with other files");
    }
}
