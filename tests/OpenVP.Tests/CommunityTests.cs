using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Community;
using OpenVP.Server;

namespace OpenVP.Tests;

public static class CommunityTests
{
    // ------------------------------------------------------------------ the report text

    [Test]
    public static void The_redactor_removes_addresses_keys_names_and_folders_but_keeps_symbol_names()
    {
        var was = Redactor.Names; Redactor.Names = ["jsmith", "STUDIO-PC"];
        try
        {
            var dirty = "Failed at C:\\Users\\jsmith\\Documents\\Client Acme\\Room1.smw and /home/jsmith/work/acme/room.lpz; panel 192.168.1.50:41794 and fe80::1ff:fe23:4567:890a, mac 00:10:7F:12:34:56; " +
                        "mail bob@acme.example; https://user:pw@intranet.acme.example/x?token=abc; password: hunter2; key=ABCDEF; " +
                        "ovp_AbCdEfGhIjKlMnOpQrStUv; secret 4f3c2b1a9e8d7c6b5a4f3c2b1a9e8d7c6b5a4f3c; \\\\fileserver\\share\\proj\\a.smw; owner jsmith on STUDIO-PC";
            var clean = Redactor.Clean(dirty);
            foreach (var leak in new[] { "jsmith", "STUDIO-PC", "Client Acme", "Documents", "192.168", "fe80", "00:10:7F", "bob@", "acme.example", "intranet", "hunter2", "ABCDEF", "AbCdEfGh", "4f3c2b1a", "fileserver", "/home/" })
                Assert.True(!clean.Contains(leak), $"'{leak}' is gone from: {clean}");
            Assert.True(clean.Contains("Room1.smw") && clean.Contains("room.lpz") && clean.Contains("a.smw"), "file names stay: " + clean);
            foreach (var keep in new[] { "Analog Ramp (SmC 64)", "Crestron.SimplSharp.CrestronIO.File.Copy", "Crestron.SimplSharpPro.EthernetCommunication.ThreeSeriesTcpIpEthernetIntersystemCommunications", "pins in=3 out=1", "mscorlib, Version=4.0.0.0", "type 12", "SIMPL+ module Hello_Splus.dll" })
                Assert.Equal(keep, Redactor.Clean(keep), "'" + keep + "' is not touched");
            Assert.True(Redactor.Findings("see 10.0.0.5 and bob@acme.example").Count == 2, "what a person types is flagged: " + string.Join(", ", Redactor.Findings("see 10.0.0.5 and bob@acme.example")));
            Assert.True(Redactor.Findings("Analog Ramp (SmC 64) is missing").Count == 0, "plain text is clean");
        }
        finally { Redactor.Names = was; }
    }

    [Test]
    public static void The_draft_holds_only_what_is_allowed_and_fits_the_link()
    {
        var items = new List<IssueItem>
        {
            new("SIMPL symbol", "Analog Ramp (SmC 64)", "pins in=3 out=1", 3),
            new("SIMPL# member", "Crestron.SimplSharp.CrestronIO.File.Copy", "C:\\Users\\bob\\proj\\x.dll at 10.1.2.3", 1)
        };
        var d = IssueReport.Draft("0.1.0", "Linux X64", "SIMPL Windows", items, "Hi, I tried my room program.");
        Assert.True(d.Body.Contains("**OpenVP version:** 0.1.0") && d.Body.Contains("**OS:** Linux X64") && d.Body.Contains("**Program type:** SIMPL Windows"), "version, OS and program type");
        Assert.True(d.Body.Contains("Analog Ramp (SmC 64)") && d.Body.Contains("File.Copy"), "the items");
        Assert.True(!d.Body.Contains("bob") && !d.Body.Contains("10.1.2.3") && !d.Body.Contains("Users"), "redacted: " + d.Body);
        Assert.True(d.Body.Contains("x.dll"), "the file name stays");
        Assert.True(!d.Truncated && d.Included == 2, "everything fits");
        var url = IssueReport.Url(d.Title, d.Body);
        Assert.True(url.StartsWith("https://github.com/qureshiumar1983-eng/openVirtualProcessor/issues/new?title="), "the project's new-issue page: " + url[..90]);
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var u) && System.Web.HttpUtility.ParseQueryString(u.Query)["body"] == d.Body, "the body round-trips through the address");

        // A long list is cut to fit, and the text says so.
        var many = Enumerable.Range(1, 600).Select(i => new IssueItem("SIMPL symbol", $"Some Symbol Number {i} (SmC {i})", "pins in=2 out=2 and a longer detail to take space", 2)).ToList();
        var big = IssueReport.Draft("0.1.0", "Windows X64", "SIMPL Windows", many);
        Assert.True(big.Truncated && big.Included < 600 && big.Included > 20, "cut to " + big.Included);
        Assert.True(IssueReport.UrlLength(big.Title, big.Body) <= IssueReport.MaxUrlLength, "the address is within the limit: " + IssueReport.UrlLength(big.Title, big.Body));
        Assert.True(big.Body.Contains($"and {600 - big.Included} more not listed"), "with a note saying how many are missing");

        // Text a person edited is cut at a line break and flagged too.
        var edited = new string('x', 5000) + "\n" + string.Join("\n", Enumerable.Range(0, 400).Select(i => "line " + i + " with some words in it"));
        var fitted = IssueReport.Fit("A title", edited, out var body, out var cut);
        Assert.True(cut && fitted.Length <= IssueReport.MaxUrlLength && body.EndsWith("link length.)_"), "an over-long edit is cut and says so");
        Assert.True(!IssueReport.Fit("t", "short", out _, out var c2).Contains("truncated") && !c2, "a short one isn't touched");
    }

    // ------------------------------------------------------------------ the endpoints

    private static string BadSmw(string name) =>
        "[\nObjTp=Hd\nPgmNm=" + name + "\nCltNm=Acme Client Name\n]\n[\nObjTp=Sg\nH=1\nNm=0\n]\n[\nObjTp=Sg\nH=2\nNm=1\n]\n[\nObjTp=Sg\nH=10\nNm=in1\n]\n[\nObjTp=Sg\nH=11\nNm=out1\n]\n" +
        "[\nObjTp=Sm\nH=40\nSmC=31337\nNm=Totally Unknown Symbol\nmI=1\nI1=10\nmO=1\nO1=11\n]\n";

    [Test]
    public static void The_compatibility_dashboard_and_report_say_what_is_unsupported_and_nothing_else()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        var secretName = "AcmeSecretRoom";
        var path = Path.Combine(s.Dir, "uploads-in", "C-Users-jsmith"); Directory.CreateDirectory(path);
        var file = Path.Combine(path, "Bad.smw"); File.WriteAllText(file, BadSmw(secretName));
        Assert.Equal(200, s.Upload("/api/slots/2/program", file).Status, "load the program with an unknown symbol");

        var c = s.Get("/api/compat");
        Assert.Equal(200, c.Status, "compat");
        var j = c.Json;
        Assert.Equal(2, j.GetProperty("programs").GetArrayLength(), "both loaded programs");
        var bad = j.GetProperty("programs").EnumerateArray().Single(x => x.GetProperty("slot").GetInt32() == 2);
        Assert.Equal("partly", bad.GetProperty("verdict").GetProperty("code").GetString(), "partly runs");
        Assert.True(bad.GetProperty("totals").GetProperty("unsupported").GetInt32() >= 1, "counts unsupported");
        Assert.True(bad.GetProperty("problems").EnumerateArray().Any(p => p.GetProperty("name").GetString()!.StartsWith("Totally Unknown Symbol")), "names the symbol");
        Assert.Equal("will-run", j.GetProperty("programs").EnumerateArray().Single(x => x.GetProperty("slot").GetInt32() == 1).GetProperty("verdict").GetProperty("code").GetString(), "the sample runs fully");
        Assert.True(j.GetProperty("logged").GetProperty("categories").EnumerateArray().Any(x => x.GetProperty("category").GetString()!.StartsWith("Primitives")), "the log is grouped by category");
        var statuses = j.GetProperty("panelFeatures").EnumerateArray().Select(f => f.GetProperty("status").GetString()).Distinct().Order().ToArray();
        Assert.True(statuses.SequenceEqual(["partial", "supported", "unsupported"]), "panel features have all three statuses: " + string.Join(",", statuses));
        Assert.Equal(1, j.GetProperty("counts").GetProperty("programs").GetProperty("partly").GetInt32(), "counted");

        // The draft for slot 2: names the symbol, never the program name, client name, folders or this machine.
        var d = s.Post("/api/report/draft", new { slot = 2, note = "My room " + s.Dir });
        Assert.Equal(200, d.Status, "draft: " + d.Text);
        var body = d.Json.GetProperty("body").GetString()!; var title = d.Json.GetProperty("title").GetString()!; var url = d.Json.GetProperty("url").GetString()!;
        Assert.True(body.Contains("Totally Unknown Symbol") && body.Contains("SIMPL Windows"), "the symbol and the program type");
        foreach (var leak in new[] { secretName, "Acme", "jsmith", s.Dir, Environment.UserName.Length >= 3 ? Environment.UserName : "\u0001", "127.0.0.1" })
            Assert.True(!body.Contains(leak) && !title.Contains(leak) && !Uri.UnescapeDataString(url).Contains(leak), $"'{leak}' isn't in what would be sent");
        Assert.True(url.StartsWith("https://github.com/qureshiumar1983-eng/openVirtualProcessor/issues/new?title=") && url.Length <= IssueReport.MaxUrlLength, "a GitHub new-issue link within the limit");
        Assert.True(body.Contains("OpenVP version"), "version");

        // From the log: items are looked up on the server (a client can't make it say something else).
        var fromLog = s.Post("/api/report/draft", new { items = new object[] { new { kind = "SIMPL symbol", name = "Totally Unknown Symbol (SmC 31337)" }, new { kind = "SIMPL symbol", name = "Invented By The Client" } } });
        Assert.Equal(200, fromLog.Status, "from the log");
        Assert.Equal(1, fromLog.Json.GetProperty("itemCount").GetInt32(), "only what the server knows");
        Assert.True(!fromLog.Json.GetProperty("body").GetString()!.Contains("Invented"), "not what the client made up");
        Assert.Equal(400, s.Post("/api/report/draft", new { items = new object[0] }).Status, "nothing chosen");
        Assert.Equal(404, s.Post("/api/report/draft", new { slot = 99 }).Status, "no such slot");

        // The edited text is fitted and checked, nothing is sent.
        var fit = s.Post("/api/report/url", new { title = "My issue", body = "I run at 10.20.30.40 and mail me at x@y.example\n" + new string('z', 9000) });
        Assert.Equal(200, fit.Status, "url");
        Assert.True(fit.Json.GetProperty("truncated").GetBoolean() && fit.Json.GetProperty("urlLength").GetInt32() <= IssueReport.MaxUrlLength, "cut to fit");
        Assert.True(fit.Json.GetProperty("warnings").GetArrayLength() >= 2, "an address and an e-mail are flagged: " + fit.Json.GetProperty("warnings"));
        Assert.Equal(400, s.Post("/api/report/url", new { title = " ", body = "x" }).Status, "a title is needed");
    }

    [Test]
    public static void The_report_and_dashboard_follow_the_roles_and_slot_limits()
    {
        using var s = new OpsTestServer();
        s.LoadSample(1);
        Assert.Equal(200, s.Post("/api/users", new { name = "viewer", password = "viewer-password-1", role = "viewer" }).Status, "viewer");
        Assert.Equal(200, s.Post("/api/users", new { name = "limited", password = "limited-password-1", role = "programmer", slots = new[] { 3 } }).Status, "limited");
        var v = new SecClient(s); v.Login("viewer", "viewer-password-1");
        Assert.Equal(200, v.Get("/api/compat").Status, "a viewer can read the dashboard");
        Assert.Equal(200, v.Post("/api/report/url", new { title = "t", body = "b" }).Status, "and compose a link: nothing is sent by the server");
        var lim = new SecClient(s); lim.Login("limited", "limited-password-1");
        Assert.Equal(0, lim.Get("/api/compat").Json.GetProperty("programs").GetArrayLength(), "an account limited to slot 3 sees no other slot's program");
        Assert.Equal(404, lim.Post("/api/report/draft", new { slot = 1 }).Status, "nor drafts a report from it");
        Assert.Equal(EndpointPolicy.For("GET", "/api/compat")!.Perm, OpenVP.Core.Security.Perm.View, "policy");
    }

    [Test]
    public static void Every_logged_kind_has_a_category_and_the_panel_list_is_consistent()
    {
        Assert.Equal(CompatCatalog.Primitives, CompatCatalog.CategoryOf("SIMPL symbol"), "symbol");
        Assert.Equal(CompatCatalog.Primitives, CompatCatalog.CategoryOf("Compiled primitive"), "compiled primitive");
        Assert.Equal(CompatCatalog.SimplSharp, CompatCatalog.CategoryOf("SIMPL# member"), "member");
        Assert.Equal(CompatCatalog.SimplSharp, CompatCatalog.CategoryOf("SIMPL# runtime error"), "runtime error");
        Assert.Equal(CompatCatalog.Splus, CompatCatalog.CategoryOf("SIMPL+ module"), "splus");
        Assert.Equal(CompatCatalog.Panels, CompatCatalog.CategoryOf("CH5 component"), "ch5");
        Assert.Equal(CompatCatalog.Other, CompatCatalog.CategoryOf("something new"), "other");
        Assert.Equal(Support.Partial, CompatCatalog.StatusOf("SIMPL# member"), "a missing member is partial support of a type");
        Assert.Equal(Support.Unsupported, CompatCatalog.StatusOf("SIMPL symbol"), "a missing symbol is unsupported");
        var f = CompatCatalog.PanelFeatures;
        Assert.True(f.Select(x => x.Area + x.Feature).Distinct().Count() == f.Count, "no duplicates");
        Assert.True(f.All(x => x.Note.Length > 10 && x.Feature.Length > 5), "each has text");
        Assert.True(f.Any(x => x.Status == Support.Supported) && f.Any(x => x.Status == Support.Partial) && f.Any(x => x.Status == Support.Unsupported), "all three");
    }

    // ------------------------------------------------------------------ the example library

    private static OpsTestServer Server(params string[] more) =>
        new(true, more: new[] { "--OpenVP:ExamplesDirectory=" + Path.Combine(Paths.RepoRoot, "examples", "programs") }.Concat(more).ToArray());

    [Test]
    public static void Every_bundled_example_loads_runs_and_has_nothing_unsupported()
    {
        using var s = Server();
        var list = s.Get("/api/examples").Json.EnumerateArray().ToList();
        Assert.True(list.Count >= 5, "at least five examples: " + list.Count);
        Assert.True(list.All(e => e.GetProperty("description").GetString()!.Length > 40 && e.GetProperty("name").GetString()!.Length > 0), "each is described");
        var ids = list.Select(e => e.GetProperty("id").GetString()!).ToList();
        foreach (var id in new[] { "source-select", "logic-gates", "eisc-a", "eisc-b" })
        {
            Assert.True(ids.Contains(id) && list.Single(e => e.GetProperty("id").GetString() == id).GetProperty("available").GetBoolean(), id + " is listed and installed");
            Assert.Equal(200, s.Post($"/api/examples/{id}/load", new { slot = 1, replace = true }).Status, "load " + id);
            Assert.Equal("Running", s.P.Slot(1).State.ToString(), id + " runs: " + s.P.Slot(1).LastError);
            Assert.Equal("will-run", s.Get("/api/compat").Json.GetProperty("programs")[0].GetProperty("verdict").GetProperty("code").GetString(), id + " has nothing unsupported");
        }
        Assert.True(ids.Contains("simpl-sharp"), "the SIMPL# sample is listed");
    }

    [Test]
    public static void The_examples_do_what_they_say()
    {
        using var s = Server();
        JoinKey D(uint j) => new(SigKind.Digital, j);
        Assert.Equal(200, s.Post("/api/examples/source-select/load", new { slot = 1 }).Status, "load source select");
        var panel = s.P.Slot(1).FindDevice(3)!;
        Assert.Eventually(() => (string)panel.GetFeedback(new JoinKey(SigKind.Serial, 3)) == "Apple TV", "labels are sent at start");
        panel.ReceiveInput(D(1), true); panel.ReceiveInput(D(1), false);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(1)), "source 1 on");
        panel.ReceiveInput(D(3), true); panel.ReceiveInput(D(3), false);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(3)) && !(bool)panel.GetFeedback(D(1)), "source 3 on, source 1 released (interlock)");
        panel.ReceiveInput(D(5), true); panel.ReceiveInput(D(5), false);
        Assert.Eventually(() => (bool)panel.GetFeedback(D(5)), "power toggled on");
        panel.ReceiveInput(new JoinKey(SigKind.Analog, 1), (ushort)1000);
        Assert.Eventually(() => (ushort)panel.GetFeedback(new JoinKey(SigKind.Analog, 1)) == 1000, "volume comes back");

        Assert.Equal(200, s.Post("/api/examples/logic-gates/load", new { slot = 2 }).Status, "load gates");
        var g = s.P.Slot(2).FindDevice(3)!;
        g.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)g.GetFeedback(D(4)) && (bool)g.GetFeedback(D(5)) && !(bool)g.GetFeedback(D(3)) && !(bool)g.GetFeedback(D(6)) && (bool)g.GetFeedback(D(7)), "A only: OR, XOR, NOT B");
        g.ReceiveInput(D(2), true);
        Assert.Eventually(() => (bool)g.GetFeedback(D(3)) && !(bool)g.GetFeedback(D(5)), "A and B: AND, not XOR");
    }

    [Test]
    public static void The_eisc_pair_examples_link_on_one_machine_too()
    {
        using var s = Server();
        JoinKey D(uint j) => new(SigKind.Digital, j);
        Assert.Equal(200, s.Post("/api/examples/eisc-a/load", new { slot = 1 }).Status, "A");
        Assert.Equal(200, s.Post("/api/examples/eisc-b/load", new { slot = 2 }).Status, "B");
        s.P.Slot(1).FindDevice(3)!.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)s.P.Slot(2).FindDevice(3)!.GetFeedback(D(1)), "A's toggle reaches B");
        s.P.Slot(2).FindDevice(3)!.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)s.P.Slot(1).FindDevice(3)!.GetFeedback(D(2)), "B's button reaches A");
    }

    [Test]
    public static void Loading_an_example_needs_programmer_respects_locks_and_asks_before_replacing()
    {
        using var s = Server();
        foreach (var r in new[] { "viewer", "technician", "programmer" })
            Assert.Equal(200, s.Post("/api/users", new { name = r, password = r + "-password-1", role = r }).Status, "user " + r);
        var viewer = new SecClient(s); viewer.Login("viewer", "viewer-password-1");
        var tech = new SecClient(s); tech.Login("technician", "technician-password-1");
        var prog = new SecClient(s); prog.Login("programmer", "programmer-password-1");
        Assert.Equal(200, viewer.Get("/api/examples").Status, "everyone can look");
        Assert.Equal(403, viewer.Post("/api/examples/source-select/load", new { slot = 1 }).Status, "viewer can't load");
        Assert.Equal(403, tech.Post("/api/examples/source-select/load", new { slot = 1 }).Status, "technician can't load");
        Assert.Equal(200, prog.Post("/api/examples/source-select/load", new { slot = 1 }).Status, "programmer can");
        var again = prog.Post("/api/examples/logic-gates/load", new { slot = 1 });
        Assert.Equal(409, again.Status, "an occupied slot is refused without replace");
        Assert.True(again.Json.GetProperty("occupied").GetBoolean(), "and says so");
        Assert.Equal(200, prog.Post("/api/examples/logic-gates/load", new { slot = 1, replace = true }).Status, "replace works");
        Assert.Equal(404, prog.Post("/api/examples/nope/load", new { slot = 2 }).Status, "unknown example");
        Assert.Equal(400, prog.Post("/api/examples/logic-gates/load", new { slot = 99 }).Status, "bad slot");
        Assert.Equal(200, tech.Post("/api/slots/2/lock", new { reason = "live event" }).Status, "technician locks slot 2");
        Assert.Equal(423, prog.Post("/api/examples/logic-gates/load", new { slot = 2 }).Status, "a locked slot is refused");
        Assert.Equal(EndpointPolicy.For("POST", "/api/examples/{id}/load")!.Perm, OpenVP.Core.Security.Perm.LoadProgram, "policy: Programmer");
    }
}
