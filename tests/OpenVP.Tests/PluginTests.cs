using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Plugins;

namespace OpenVP.Tests;

public static class PluginTests
{
    private sealed class Probe : IOpenVpPlugin
    {
        public Action<IPluginContext>? OnInit; public Action? OnStart, OnStop; public Action<PluginSignalChange>? OnChange;
        public int Changes; public bool Stopped;
        public void Initialize(IPluginContext c) => OnInit?.Invoke(c);
        public void OnStarted() => OnStart?.Invoke();
        public void OnStopped() { Stopped = true; OnStop?.Invoke(); }
        public void OnSignalChanged(PluginSignalChange change) { Interlocked.Increment(ref Changes); OnChange?.Invoke(change); }
    }

    private static (PluginHost Host, Processor P, Func<bool> Labs, Action<bool> SetLabs, List<string> Activity, string Dir) Make(Probe probe, string name = "probe", params string[] perms)
    {
        var dir = Paths.TempData();
        var p = new Processor(dir, 2);
        var on = true;
        var activity = new List<string>();
        var host = new PluginHost(p, dir, () => on, (a, t) => { lock (activity) activity.Add(a + ": " + t); });
        host.AddInProcess(new PluginManifest { Name = name, Version = "1.0", Author = "me", Description = "test", Permissions = perms.ToList() }, () => probe);
        host.Start();
        return (host, p, () => on, v => { on = v; host.Apply(); }, activity, dir);
    }

    private static PluginView View(PluginHost h, string name = "probe") => h.List().Single(x => x.Name == name);

    [Test]
    public static void A_plugin_is_off_until_enabled_and_enabling_needs_every_permission_approved()
    {
        var probe = new Probe { OnInit = c => c.AddConsoleCommand("probecmd", "x", a => "hi " + a) };
        var (host, p, _, _, _, _) = Make(probe, perms: [PluginPermissions.Console]);
        Assert.Equal("off", View(host).Status, "off by default");
        Assert.Equal(0, p.ExtraConsoleCommands.Count, "nothing registered");
        Assert.Throws<ArgumentException>(() => host.Enable("probe", [], "admin"));
        Assert.Equal("off", View(host).Status, "a refused approval changes nothing");
        host.Enable("probe", [PluginPermissions.Console], "admin");
        Assert.Eventually(() => p.ExtraConsoleCommands.ContainsKey("probecmd"), "the command appears once enabled");
        Assert.Equal("hi there", p.Slot(1).RunConsoleCommand("probecmd there"), "and runs from any slot's console");
        Assert.True(p.Slot(1).RunConsoleCommand("help").Contains("probecmd"), "listed in help");
        host.Disable("probe");
        Assert.True(!p.ExtraConsoleCommands.ContainsKey("probecmd"), "gone after disabling");
        Assert.True(probe.Stopped, "OnStopped was called");
        Assert.True(p.Slot(1).RunConsoleCommand("probecmd").StartsWith("Bad or incomplete"), "the slot no longer knows it");
    }

    [Test]
    public static void A_plugin_that_throws_is_disabled_and_the_processor_carries_on()
    {
        foreach (var where in new[] { "init", "started", "signal", "command", "route" })
        {
            var probe = new Probe();
            switch (where)
            {
                case "init": probe.OnInit = _ => throw new InvalidOperationException("boom in init"); break;
                case "started": probe.OnStart = () => throw new InvalidOperationException("boom in started"); break;
                case "signal": probe.OnInit = c => c.SubscribeSlot(1); probe.OnChange = _ => throw new InvalidOperationException("boom in signal"); break;
                case "command": probe.OnInit = c => c.AddConsoleCommand("bad", "x", _ => throw new InvalidOperationException("boom in command")); break;
                case "route": probe.OnInit = c => c.AddRoute("GET", "r", _ => throw new InvalidOperationException("boom in route")); break;
            }
            var (host, p, _, _, activity, dir) = Make(probe, perms: [PluginPermissions.SignalsRead, PluginPermissions.Console, PluginPermissions.Routes]);
            host.Enable("probe", [PluginPermissions.SignalsRead, PluginPermissions.Console, PluginPermissions.Routes], "admin");
            Assert.Eventually(() => View(host).Status != "running" || where is "signal" or "command" or "route", where + ": the enable returns without crashing the host");
            if (where == "signal") { Assert.Eventually(() => View(host).Slots.Contains(1), "subscribed"); p.Slot(1).Trace("sig", SigKind.Digital, true, "test"); }
            if (where == "command") Assert.Eventually(() => p.ExtraConsoleCommands.ContainsKey("bad"), "registered");
            if (where == "command") Assert.True(p.Slot(1).RunConsoleCommand("bad").Contains("failed and was disabled"), "the caller is told");
            if (where == "route") { PluginResponse? resp = null; Assert.Eventually(() => (resp = host.Handle("probe", new PluginRequest("GET", "r", new Dictionary<string, string>(), "", ""))) != null, "route registered"); Assert.Equal(500, resp!.Status, "the request gets a 500, not a crash"); }
            Assert.Eventually(() => View(host).Status == "faulted", where + ": disabled after the failure", 5000);
            var v = View(host);
            Assert.True(v.Error!.Contains("boom"), where + ": the reason is kept: " + v.Error);
            Assert.True(!v.Enabled, where + ": not enabled");
            lock (activity) Assert.True(activity.Any(a => a.Contains("failed and was disabled")), where + ": reported to the activity feed");
            Assert.True(p.ExtraConsoleCommands.IsEmpty, where + ": its commands are gone");
            // The processor still works: a trace still fires, a console command still answers.
            p.Slot(1).Trace("again", SigKind.Digital, false, "test");
            Assert.True(p.Slot(1).RunConsoleCommand("help").Contains("Built-in"), where + ": the console still works");
            // And it stays disabled after a restart of the host.
            var again = new PluginHost(p, dir, () => true);
            again.AddInProcess(new PluginManifest { Name = "probe", Version = "1.0", Permissions = [PluginPermissions.SignalsRead, PluginPermissions.Console, PluginPermissions.Routes] }, () => new Probe());
            again.Start();
            Assert.Equal("faulted", again.List().Single().Status, where + ": still off after a restart");
        }
    }

    [Test]
    public static void A_plugin_that_hangs_is_cut_off_without_blocking_the_caller()
    {
        var was = PluginHost.CallTimeout; PluginHost.CallTimeout = TimeSpan.FromMilliseconds(600);
        try
        {
            var probe = new Probe { OnInit = c => c.AddConsoleCommand("hang", "x", _ => { Thread.Sleep(30_000); return "never"; }) };
            var (host, p, _, _, _, _) = Make(probe, perms: [PluginPermissions.Console]);
            host.Enable("probe", [PluginPermissions.Console], "admin");
            Assert.Eventually(() => p.ExtraConsoleCommands.ContainsKey("hang"), "registered");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reply = p.Slot(1).RunConsoleCommand("hang");
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "the caller got an answer in " + sw.Elapsed);
            Assert.True(reply.Contains("failed and was disabled"), "told why: " + reply);
            Assert.Eventually(() => View(host).Status == "faulted", "disabled", 3000);
            Assert.True(View(host).Error!.Contains("second"), "the reason mentions the time limit: " + View(host).Error);
        }
        finally { PluginHost.CallTimeout = was; }
    }

    [Test]
    public static void A_plugin_can_only_use_what_it_declared_and_was_approved_for()
    {
        var probe = new Probe { OnInit = c => c.AddRoute("GET", "x", _ => PluginResponse.Text("hi")) }; // no "routes" permission
        var (host, _, _, _, _, _) = Make(probe, perms: [PluginPermissions.Console]);
        host.Enable("probe", [PluginPermissions.Console], "admin");
        Assert.Eventually(() => View(host).Status == "faulted", "the missing permission is a failure", 3000);
        Assert.True(View(host).Error!.Contains("routes"), "and says which: " + View(host).Error);

        // Signals: reading needs signals.read, setting needs signals.write.
        var p2 = new Probe { OnInit = c => { c.GetSignals(1); } };
        var (h2, _, _, _, _, _) = Make(p2, perms: [PluginPermissions.SignalsWrite]);
        h2.Enable("probe", [PluginPermissions.SignalsWrite], "admin");
        Assert.Eventually(() => View(h2).Status == "faulted", "reading without signals.read fails", 3000);
    }

    [Test]
    public static void Routes_and_signal_hooks_work_and_labs_off_unloads_everything()
    {
        var probe = new Probe { OnInit = c => { c.SubscribeSlot(1); c.AddRoute("GET", "stats", _ => PluginResponse.Json(new { n = 7 })); c.AddRoute("POST", "echo", r => PluginResponse.Text(r.Body)); } };
        var (host, p, labs, setLabs, _, _) = Make(probe, perms: [PluginPermissions.SignalsRead, PluginPermissions.Routes]);
        host.Enable("probe", [PluginPermissions.SignalsRead, PluginPermissions.Routes], "admin");
        Assert.Eventually(() => host.Handle("probe", new PluginRequest("GET", "stats", new Dictionary<string, string>(), "", "")) != null, "route registered");
        Assert.Equal("{\"n\":7}", host.Handle("probe", new PluginRequest("GET", "stats", new Dictionary<string, string>(), "", ""))!.Body, "route answers");
        Assert.Equal("body!", host.Handle("probe", new PluginRequest("POST", "echo", new Dictionary<string, string>(), "body!", "x"))!.Body, "body passed in");
        Assert.True(host.Handle("probe", new PluginRequest("GET", "missing", new Dictionary<string, string>(), "", "")) == null, "unknown route is null (a 404)");
        Assert.True(host.Handle("probe", new PluginRequest("DELETE", "stats", new Dictionary<string, string>(), "", "")) == null, "wrong method too");
        Assert.True(View(host).Slots.SequenceEqual([1]), "subscribed slots are listed");
        p.Slot(1).Trace("sig", SigKind.Digital, true, "t");
        p.Slot(1).Trace("sig", SigKind.Digital, false, "t");
        Assert.Eventually(() => probe.Changes == 2, "both changes delivered on the plugin's thread");
        // Switching Plugins off in Labs unloads it; on again brings it back (it was enabled).
        setLabs(false);
        Assert.True(probe.Stopped, "OnStopped on unload");
        Assert.Equal("blocked", View(host).Status, "shown as blocked");
        Assert.True(host.Handle("probe", new PluginRequest("GET", "stats", new Dictionary<string, string>(), "", "")) == null, "routes are gone");
        Assert.Throws<InvalidOperationException>(() => host.Enable("probe", [PluginPermissions.SignalsRead, PluginPermissions.Routes], "admin"));
        var before = probe.Changes;
        p.Slot(1).Trace("sig", SigKind.Digital, true, "t");
        Thread.Sleep(100);
        Assert.Equal(before, probe.Changes, "no more signal hooks");
        setLabs(true);
        Assert.Eventually(() => View(host).Status == "running", "enabled plugins come back", 3000);
    }

    [Test]
    public static void A_plugin_that_asks_for_more_after_an_update_stays_off_until_approved_again()
    {
        var dir = Paths.TempData();
        var p = new Processor(dir, 1);
        var host = new PluginHost(p, dir, () => true);
        host.AddInProcess(new PluginManifest { Name = "probe", Version = "1.0", Permissions = [PluginPermissions.Console] }, () => new Probe());
        host.Start();
        host.Enable("probe", [PluginPermissions.Console], "admin");
        Assert.Equal("running", View(host).Status, "running");
        host.Dispose();
        // The next version asks for signals.write too.
        var host2 = new PluginHost(p, dir, () => true);
        host2.AddInProcess(new PluginManifest { Name = "probe", Version = "2.0", Permissions = [PluginPermissions.Console, PluginPermissions.SignalsWrite] }, () => new Probe());
        host2.Start();
        var v = View(host2);
        Assert.Equal("needs-approval", v.Status, "not loaded");
        Assert.True(v.NewPermissions.SequenceEqual([PluginPermissions.SignalsWrite]), "and the new permission is named");
        host2.Enable("probe", [PluginPermissions.Console, PluginPermissions.SignalsWrite], "admin");
        Assert.Equal("running", View(host2).Status, "enabled again with the new permission approved");
    }

    [Test]
    public static void Plugin_manifests_are_checked_and_a_bad_one_is_listed_not_loaded()
    {
        var dir = Paths.TempData();
        void Put(string folder, string json) { Directory.CreateDirectory(Path.Combine(dir, "plugins", folder)); File.WriteAllText(Path.Combine(dir, "plugins", folder, "plugin.json"), json); }
        Put("good", """{ "name":"good","version":"1","permissions":["ui"],"ui":{"page":"index.html","menu":"Good"} }""");
        Put("wrongname", """{ "name":"other","version":"1","ui":{} }""");
        Put("badperm", """{ "name":"badperm","version":"1","permissions":["root"],"ui":{} }""");
        Put("broken", "{ nope");
        Put("Upper", """{ "name":"Upper","version":"1","ui":{} }""");
        Put("noparts", """{ "name":"noparts","version":"1" }""");
        Put("traversal", """{ "name":"traversal","version":"1","assembly":"../x.dll" }""");
        using var p = new Processor(dir, 1);
        var host = new PluginHost(p, dir, () => true); host.Start();
        var list = host.List().ToDictionary(x => x.Name);
        Assert.Equal("off", list["good"].Status, "the good one is available");
        Assert.True(list["good"].HasUi && list["good"].Menu == "Good", "with its menu entry");
        foreach (var bad in new[] { "wrongname", "badperm", "broken", "Upper", "noparts", "traversal" })
            Assert.Equal("invalid", list[bad].Status, bad + " is invalid");
        Assert.Throws<KeyNotFoundException>(() => host.Enable("wrongname", [], "admin"));
        // A UI-only plugin serves its page but not files outside its ui folder.
        Directory.CreateDirectory(Path.Combine(dir, "plugins", "good", "ui"));
        File.WriteAllText(Path.Combine(dir, "plugins", "good", "ui", "index.html"), "<p>hi</p>");
        File.WriteAllText(Path.Combine(dir, "plugins", "good", "secret.txt"), "no");
        Assert.True(host.UiFile("good", "index.html") == null, "not served while off");
        host.Enable("good", ["ui"], "admin");
        Assert.True(host.UiFile("good", "")?.ContentType.StartsWith("text/html") == true, "the page is served once enabled");
        Assert.True(host.UiFile("good", "../secret.txt") == null && host.UiFile("good", "..\\secret.txt") == null && host.UiFile("good", "/etc/passwd") == null, "no way out of the ui folder");
    }

    // ------------------------------------------------------------------ the real example plugin, through the real server

    private static string HelloBuild()
    {
        var bin = Path.Combine(Paths.RepoRoot, "examples", "plugins", "hello", "bin");
        var dll = Directory.GetFiles(bin, "Hello.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
        return Path.GetDirectoryName(dll)!;
    }

    private static void InstallHello(string dataDir)
    {
        var src = HelloBuild(); var dst = Path.Combine(dataDir, "plugins", "hello");
        Directory.CreateDirectory(Path.Combine(dst, "ui"));
        foreach (var f in new[] { "Hello.dll", "plugin.json" }) File.Copy(Path.Combine(src, f), Path.Combine(dst, f), true);
        File.Copy(Path.Combine(src, "ui", "index.html"), Path.Combine(dst, "ui", "index.html"), true);
    }

    [Test]
    public static void The_example_plugin_loads_from_its_folder_runs_and_a_crash_does_not_take_the_server_down()
    {
        using var s = new OpsTestServer();
        InstallHello(s.Dir);
        Assert.Equal(200, s.Post("/api/users", new { name = "admin", password = "admin-password-1", role = "admin" }).Status, "users on");
        Assert.Equal(200, s.Post("/api/users", new { name = "viewer", password = "viewer-password-1", role = "viewer" }).Status, "viewer");
        Assert.Equal(200, s.Post("/api/users", new { name = "tech", password = "tech-password-1", role = "technician" }).Status, "technician");
        var admin = new SecClient(s); admin.Login("admin", "admin-password-1");
        var viewer = new SecClient(s); viewer.Login("viewer", "viewer-password-1");
        var tech = new SecClient(s); tech.Login("tech", "tech-password-1");

        // Plugins is off in Labs by default: it is listed as nothing loaded, and enabling is refused.
        var off = admin.Get("/api/plugins").Json;
        Assert.True(!off.GetProperty("active").GetBoolean(), "off by default");
        Assert.Equal("blocked", off.GetProperty("plugins").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "hello").GetProperty("status").GetString(), "blocked by Labs");
        Assert.Equal(409, admin.Post("/api/plugin-admin/hello/enable", new { permissions = new[] { "signals.read", "console", "routes", "ui" } }).Status, "can't enable while Plugins is off");
        Assert.Equal(404, admin.Get("/api/plugins/hello/greeting").Status, "no routes while off");
        Assert.Equal(200, admin.Put("/api/labs/plugins", new { enabled = true }).Status, "an Admin switches Plugins on");

        var hello = admin.Get("/api/plugins").Json.GetProperty("plugins").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "hello");
        Assert.Equal("off", hello.GetProperty("status").GetString(), "found and still off");
        Assert.Equal("1.0.0", hello.GetProperty("version").GetString(), "version shown");
        Assert.True(hello.GetProperty("author").GetString()!.Length > 0, "author shown");
        Assert.Equal(4, hello.GetProperty("permissions").GetArrayLength(), "permissions shown");

        // Enabling is Admin only, and needs every permission approved.
        foreach (var (who, c) in new[] { ("viewer", viewer), ("technician", tech) })
            Assert.Equal(403, c.Post("/api/plugin-admin/hello/enable", new { permissions = new[] { "signals.read", "console", "routes", "ui" } }).Status, who + " can't enable a plugin");
        Assert.Equal(400, admin.Post("/api/plugin-admin/hello/enable", new { permissions = new[] { "console" } }).Status, "a partial approval is refused");
        var en = admin.Post("/api/plugin-admin/hello/enable", new { permissions = new[] { "signals.read", "console", "routes", "ui" } });
        Assert.Equal(200, en.Status, "enabled: " + en.Text);
        Assert.Eventually(() => admin.Get("/api/plugins/hello/greeting").Status == 200, "its route answers", 5000);
        Assert.Equal("Hello from the example plugin", admin.Get("/api/plugins/hello/greeting").Json.GetProperty("message").GetString(), "the greeting");
        Assert.Equal(200, viewer.Get("/api/plugins/hello/greeting").Status, "a viewer can read a plugin route");
        Assert.Equal(403, viewer.Post("/api/plugins/hello/echo", new { a = 1 }).Status, "but not make it change things");
        Assert.Equal(200, tech.Post("/api/plugins/hello/echo", new { a = 1 }).Status, "a technician (Operate) can");
        Assert.True(admin.Post("/api/slots/1/console", new { command = "hello there" }).Text.Contains("Hello from the hello plugin! You said: there"), "its console command works");
        Assert.True(admin.Get("/api/plugins/hello/ui/index.html").Text.Contains("Hello plugin"), "its page is served");
        Assert.Equal(404, admin.Get("/api/plugins/hello/ui/..%2Fplugin.json").Status, "not outside its ui folder");
        Assert.Equal(404, admin.Get("/api/plugins/hello/ui/../Hello.dll").Status, "not its assembly either");

        // The command that fails on purpose: the plugin is disabled, the server keeps answering.
        var crash = admin.Post("/api/slots/1/console", new { command = "hello-crash" }).Text;
        Assert.True(crash.Contains("failed and was disabled"), "told: " + crash);
        Assert.Equal(200, admin.Get("/api/slots").Status, "the server is fine");
        var after = admin.Get("/api/plugins").Json.GetProperty("plugins").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "hello");
        Assert.Equal("faulted", after.GetProperty("status").GetString(), "shown as failed");
        Assert.True(after.GetProperty("error").GetString()!.Contains("on purpose"), "with the reason");
        Assert.Equal(404, admin.Get("/api/plugins/hello/greeting").Status, "its routes are gone");
        Assert.True(admin.Post("/api/slots/1/console", new { command = "hello" }).Text.StartsWith("Bad or incomplete"), "and its commands");
        Assert.True(admin.Get("/api/activity").Text.Contains("failed and was disabled"), "the activity feed says so");

        // Enable it again and disable it by hand.
        Assert.Equal(200, admin.Post("/api/plugin-admin/hello/enable", new { permissions = new[] { "signals.read", "console", "routes", "ui" } }).Status, "enable again");
        Assert.Eventually(() => admin.Get("/api/plugins/hello/greeting").Status == 200, "back", 5000);
        Assert.Equal(200, admin.Post("/api/plugin-admin/hello/disable").Status, "disable");
        Assert.Equal(404, admin.Get("/api/plugins/hello/greeting").Status, "gone");
    }

    [Test]
    public static void Plugin_endpoints_have_permission_lines_and_nothing_is_open()
    {
        Assert.Equal(OpenVP.Core.Security.Perm.Admin, OpenVP.Server.EndpointPolicy.For("POST", "/api/plugin-admin/{name}/enable")!.Perm, "enable is Admin");
        Assert.Equal(OpenVP.Core.Security.Perm.Admin, OpenVP.Server.EndpointPolicy.For("POST", "/api/plugin-admin/{name}/disable")!.Perm, "disable is Admin");
        Assert.Equal(OpenVP.Core.Security.Perm.View, OpenVP.Server.EndpointPolicy.For("GET", "/api/plugins")!.Perm, "the list is View");
        Assert.Equal("perm", OpenVP.Server.EndpointPolicy.For("PUT", "/api/plugins/{name}/{**path}")!.Kind, "plugin routes need a sign-in");
    }
}
