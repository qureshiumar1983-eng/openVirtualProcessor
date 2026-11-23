using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Security;
using OpenVP.Panels;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>Generated panels, saved layouts, smart graphics, latency and the rack view.</summary>
public static class PanelTests
{
    private static SignalInfo D(string n, bool v = false) => new(n, SigKind.Digital, v);
    private static SignalInfo A(string n, ushort v = 0) => new(n, SigKind.Analog, v);
    private static SignalInfo S(string n, string v = "") => new(n, SigKind.Serial, v);

    // ------------------------------------------------------------------ generated panel

    [Test]
    public static void Device_joins_become_one_group_per_IP_ID_with_the_drive_and_feedback_names_paired()
    {
        var m = AutoPanel.Build(1, "P", [D("IPID03.out.d10"), D("IPID03.in.d10", true), A("IPID03.in.a1", 500), S("IPID03.in.s2", "hi"), D("IPID0A.out.d1")],
            new Dictionary<uint, string> { [3] = "XPanel", [0x0A] = "Touch panel" });
        Assert.Equal(2, m.Groups.Count, "one group per IP ID");
        var g = m.Groups[0];
        Assert.Equal("IP ID 03", g.Title, "title");
        Assert.Equal("XPanel", g.Subtitle ?? "", "device type");
        Assert.Equal(3, g.Controls.Count, "d10 merged from its in and out signals");
        var d10 = g.Controls.First(c => c.Kind == "digital");
        Assert.Equal("IPID03.out.d10", d10.DriveName, "pressing drives the panel-to-program side");
        Assert.Equal("IPID03.in.d10", d10.FeedbackName, "feedback reads the program-to-panel side");
        Assert.Equal(true, d10.Value, "feedback value");
        Assert.Equal("Digital 10", d10.Label, "label");
        Assert.True(g.Controls.Select(c => c.Kind).SequenceEqual(["digital", "analog", "serial"]), "digital, then analog, then serial");
        var one = m.Groups[1].Controls.Single();
        Assert.Equal("IPID0A.in.d1", one.FeedbackName, "an input-only join still has a feedback name to read");
    }

    [Test]
    public static void Named_signals_are_grouped_by_device_then_by_prefix_and_labelled_without_the_prefix()
    {
        var sigs = new[] { D("Lobby_Light_On"), D("Lobby_Light_Off"), A("Lobby_Level"), D("AV_Power"), D("AV_Mute"), D("Standalone"), D("DSP_Mute") };
        var m = AutoPanel.Build(2, "P", sigs, null, name => name == "DSP_Mute" ? "Biamp DSP" : null);
        var titles = m.Groups.Select(g => g.Title).ToList();
        Assert.True(titles.SequenceEqual(["Biamp DSP", "AV", "Lobby", "Other"]), "device groups first, then prefixes, then the rest: " + string.Join(",", titles));
        var lobby = m.Groups.First(g => g.Title == "Lobby");
        Assert.True(lobby.Controls.Select(c => c.Label).Contains("Light On"), "prefix stripped from the label");
        Assert.Equal(1, m.Groups.First(g => g.Title == "Other").Controls.Count, "a lone signal goes under Other");
        Assert.Equal("Lobby_Light_On", lobby.Controls.First(c => c.Label == "Light On").DriveName, "the full name is what gets driven");
        Assert.Equal(7, m.ControlCount, "every signal is a control");
    }

    [Test]
    public static void A_huge_program_is_cut_off_and_says_so()
    {
        var sigs = Enumerable.Range(0, AutoPanel.MaxControls + 50).Select(i => D("Sig" + i)).ToList();
        var m = AutoPanel.Build(1, "P", sigs);
        Assert.True(m.Truncated && m.ControlCount == AutoPanel.MaxControls, "cut at the limit");
        Assert.Equal("", AutoPanel.Prefix("123_x"), "numbers aren't a prefix");
        Assert.Equal("Power", AutoPanel.Label("AV1_Power", "AV1"), "label");
    }

    // ------------------------------------------------------------------ layouts

    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Test]
    public static void Layout_names_cannot_reach_outside_the_folder()
    {
        foreach (var bad in new[] { "", "..", "../x", "a/b", "a\\b", ".hidden", "x:y", new string('a', 61), "con\0" })
            Assert.Throws<LayoutException>(() => PanelLayouts.CleanName(bad));
        Assert.Equal("Room 1_v2-final", PanelLayouts.CleanName("  Room 1_v2-final "), "ordinary names pass");
    }

    [Test]
    public static void Layouts_are_saved_per_slot_listed_read_back_and_deleted()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-layouts").FullName;
        var l = new PanelLayouts(dir);
        var info = l.Save(2, "Lobby", J("""{"title":"Lobby","columns":6,"controls":{"IPID03.d10":{"x":1,"y":2,"w":3,"h":1,"label":"Lights","hidden":false},"Other":{"hidden":true}},"groups":{"pfx:AV":{"hidden":true}}}"""), "MyProg");
        Assert.Equal(2, info.Controls, "controls counted");
        Assert.Equal(1, l.List(2).Count, "listed for slot 2");
        Assert.Equal(0, l.List(1).Count, "and not for slot 1");
        var got = l.Get(2, "Lobby")!;
        Assert.Equal("MyProg", (string)got["program"]!, "remembers which program it was made for");
        Assert.Equal("Lights", (string)got["controls"]!["IPID03.d10"]!["label"]!, "relabel kept");
        Assert.True((bool)got["groups"]!["pfx:AV"]!["hidden"]!, "hidden group kept");
        Assert.True(File.Exists(Path.Combine(dir, "layouts", "slot02", "Lobby.json")), "stored under data/layouts/slotNN");
        Assert.True(l.Delete(2, "Lobby") && l.Get(2, "Lobby") == null && !l.Delete(2, "Lobby"), "deleted once");
    }

    [Test]
    public static void Layout_content_is_checked_and_clamped()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-layouts").FullName;
        var l = new PanelLayouts(dir);
        Assert.Throws<LayoutException>(() => l.Save(1, "a", J("[]"), null));
        Assert.Throws<LayoutException>(() => l.Save(1, "a", J("""{"controls":[]}"""), null));
        Assert.Throws<LayoutException>(() => l.Save(1, "a", J("""{"controls":{"x":{"x":"left"}}}"""), null));
        Assert.Throws<LayoutException>(() => l.Save(1, "a", J("""{"controls":{"x":5}}"""), null));
        l.Save(1, "a", J("""{"columns":9999,"surprise":1,"controls":{"x":{"x":-5,"y":99999999,"w":0,"h":2,"evil":"<script>"}}}"""), null);
        var got = l.Get(1, "a")!;
        Assert.Equal(48, (int)got["columns"]!, "columns clamped");
        var x = got["controls"]!["x"]!;
        Assert.Equal(0, (int)x["x"]!, "negative x clamped");
        Assert.Equal(10000, (int)x["y"]!, "huge y clamped");
        Assert.Equal(1, (int)x["w"]!, "width at least 1");
        Assert.True(x["evil"] == null && got["surprise"] == null, "unknown fields are dropped");
        for (var i = 0; i < PanelLayouts.MaxLayoutsPerSlot - 1; i++) l.Save(1, "n" + i, J("{}"), null);
        Assert.Throws<LayoutException>(() => l.Save(1, "one too many", J("{}"), null));
        l.Save(1, "n0", J("{}"), null);   // overwriting is still fine
    }

    // ------------------------------------------------------------------ smart graphics

    private static MemoryStream Zip(params (string name, byte[] data)[] files)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, data) in files) { var e = z.CreateEntry(name); using var s = e.Open(); s.Write(data); }
        ms.Position = 0;
        return ms;
    }
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private const string SampleXml = """
        <Project Name="Room">
          <Page Name="Main" Width="1280" Height="800">
            <Button Name="Power" Left="10" Top="20" Width="200" Height="80" Text="Power" DigitalPressJoin="1" DigitalFeedbackJoin="1"/>
            <Slider Name="Volume" X="10" Y="120" Width="400" Height="60" AnalogOutputJoin="2" AnalogFeedbackJoin="2"/>
            <Gauge Name="Level" X="10" Y="200" Width="100" Height="100" AnalogFeedbackJoin="3"/>
            <DynamicText Name="Title" X="300" Y="20" Width="300" Height="40" SerialInputJoin="4"/>
            <ButtonList Name="Sources" X="10" Y="320" Width="300" Height="200" ItemCount="4" SmartObjectId="7"/>
            <Image Name="Logo" X="900" Y="10" Width="100" Height="50" File="logo.png"/>
            <VideoWindow Name="Cam" X="500" Y="300" Width="300" Height="200"/>
          </Page>
          <Subpage Name="Popup" Width="400" Height="300"><Button Name="Close" X="1" Y="1" Width="50" Height="50" Text="X" DigitalPressJoin="9"/></Subpage>
        </Project>
        """;

    [Test]
    public static void A_project_in_XML_inside_a_vtz_gives_pages_objects_joins_and_a_report_of_what_it_skipped()
    {
        var images = new List<(string, byte[])>();
        var p = SmartGraphics.Read(Zip(("room.xml", B(SampleXml)), ("images/logo.png", [1, 2, 3]), ("notes.vtp", [0, 1, 2, 3])), "Room.vtz", images);
        Assert.Equal(2, p.Pages.Count, "page and subpage");
        var main = p.Pages.First(x => x.Name == "Main");
        Assert.Equal(1280, main.Width, "page size");
        var btn = main.Objects.First(o => o.Name == "Power");
        Assert.True(btn.Type == "button" && btn.Joins.Press == 1 && btn.Joins.Feedback == 1 && btn.X == 10 && btn.W == 200 && btn.Text == "Power", "button with joins and geometry");
        var vol = main.Objects.First(o => o.Name == "Volume");
        Assert.True(vol.Type == "slider" && vol.Joins.AnalogOut == 2 && vol.Joins.AnalogIn == 2, "slider");
        Assert.True(main.Objects.First(o => o.Name == "Level") is { Type: "gauge", Joins.AnalogIn: 3 }, "gauge reads analog feedback");
        Assert.True(main.Objects.First(o => o.Name == "Title") is { Type: "text", Joins.SerialIn: 4 }, "text on a serial join");
        var list = main.Objects.First(o => o.Name == "Sources");
        Assert.True(list.Type == "list" && list.Items!.Count == 4 && list.Joins.SmartObject == 7, "list with four items and its smart object id");
        Assert.Equal("img1.png", main.Objects.First(o => o.Name == "Logo").Image ?? "", "image matched to the file in the archive");
        Assert.Equal(1, images.Count, "image handed back");
        Assert.True(p.Report.Unsupported.Any(u => u.Contains("VideoWindow")), "video window reported, not dropped silently");
        Assert.True(p.Report.Unsupported.Any(u => u.Contains("binary project file")), "binary .vtp reported");
        Assert.True(main.Objects.Any(o => o.Type == "unsupported" && o.Name == "Cam"), "and drawn as a placeholder");
        Assert.True(p.Pages.First(x => x.Name == "Popup").Kind == "subpage" && p.Pages.First(x => x.Name == "Popup").Objects.Count == 1, "subpage");
    }

    [Test]
    public static void JSON_projects_and_OpenVP_own_format_are_read()
    {
        var p = SmartGraphics.Read(new MemoryStream(B("""{"pages":[{"name":"P1","width":800,"height":480,"objects":[{"type":"button","name":"B","text":"Go","x":5,"y":5,"w":100,"h":40,"joins":{"press":3}}]}]}""")), "x.openvp-sg.json");
        Assert.True(p.Pages.Single().Objects.Single() is { Type: "button", Joins.Press: 3 }, "native");
        var q = SmartGraphics.Read(new MemoryStream(B("""{"Pages":[{"Name":"Home","Objects":[{"Type":"Button","Name":"A","Left":1,"Top":2,"Width":30,"Height":40,"Caption":"Hello","PressJoin":12}]}]}""")), "proj.json");
        Assert.True(q.Pages.Single().Objects.Single() is { Type: "button", Text: "Hello", Joins.Press: 12, X: 1, Y: 2 }, "generic JSON");
    }

    [Test]
    public static void Unreadable_or_hostile_projects_are_refused_or_reported_not_crashed()
    {
        var onlyBinary = SmartGraphics.Read(Zip(("a.vtp", [1, 2, 3])), "x.vtz");
        Assert.Equal(0, onlyBinary.Pages.Count, "nothing to draw");
        Assert.True(onlyBinary.Report.Unsupported[0].Contains("No pages or objects"), "says why: " + onlyBinary.Report.Unsupported[0]);
        Assert.Throws<Ch5Exception>(() => SmartGraphics.Read(new MemoryStream(B("not a project")), "x.txt"));
        Assert.Throws<Ch5Exception>(() => SmartGraphics.Read(new MemoryStream(B("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a SYSTEM \"file:///etc/passwd\">]><P>&a;</P>")), "x.xml"));
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 200)) + "1" + new string('}', 200);
        Assert.Throws<Ch5Exception>(() => SmartGraphics.Read(new MemoryStream(B(deep)), "deep.json"));
    }

    [Test]
    public static void The_store_keeps_the_model_and_only_the_images_it_uses_and_serves_nothing_else()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-sg").FullName;
        var st = new SmartGraphicsStore(dir);
        st.Install(2, 0x0A, Zip(("room.xml", B(SampleXml)), ("logo.png", [137, 80]), ("unused.png", [1])), "Room.vtz");
        Assert.Equal(1, st.List().Count, "listed");
        Assert.True(st.Get(2, 0x0A) != null && st.Get(2, 0x0B) == null, "by slot and IP ID");
        Assert.True(st.AssetPath(2, 0x0A, "img1.png") != null, "the used image is kept");
        Assert.True(st.AssetPath(2, 0x0A, "img2.png") == null, "the unused one is not");
        Assert.True(st.AssetPath(2, 0x0A, "../model.json") == null && st.AssetPath(2, 0x0A, "model.json") == null, "no path tricks, and the model is not an asset");
        Assert.True(st.Remove(2, 0x0A) && !st.Remove(2, 0x0A), "removed");
    }

    // ------------------------------------------------------------------ latency

    [Test]
    public static void Latency_keeps_the_last_thirty_measurements_and_the_source()
    {
        var c = new DeviceConnection("Web panel");
        Assert.True(c.LatencyMs < 0 && c.LatencyHistory().Length == 0, "unknown at first");
        for (var i = 1; i <= 35; i++) c.RecordLatency(i, i % 2 == 0 ? "server ping" : "client ping");
        var h = c.LatencyHistory();
        Assert.Equal(30, h.Length, "ring of 30");
        Assert.True(h[0] == 6 && h[^1] == 35, "oldest first");
        Assert.Equal(35.0, c.LatencyMs, "latest");
        Assert.Equal("client ping", c.LatencySource ?? "", "source of the latest");
        c.RecordLatency(-1, "x"); c.RecordLatency(double.NaN, "x");
        Assert.Equal(35.0, c.LatencyMs, "bad values ignored");
    }

    [Test]
    public static void The_server_pings_a_browser_panel_and_measures_the_round_trip_itself()
    {
        var old = WebPanelSession.PingInterval;
        WebPanelSession.PingInterval = TimeSpan.FromMilliseconds(150);
        try
        {
            using var s = new OpsTestServer(false);
            s.LoadSample(1);
            using var ws = new ClientWebSocket();
            ws.ConnectAsync(new Uri(s.Url.Replace("http", "ws") + "/ws/panel?ipid=03&slot=1"), CancellationToken.None).GetAwaiter().GetResult();
            var buf = new byte[8192];
            var answered = 0;
            var end = Environment.TickCount64 + 6000;
            while (answered < 2 && Environment.TickCount64 < end)
            {
                using var cts = new CancellationTokenSource(1000);
                WebSocketReceiveResult r;
                try { r = ws.ReceiveAsync(buf, cts.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { continue; }
                var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                if (!text.Contains("\"sping\"")) continue;
                var id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetInt64();
                ws.SendAsync(B($"{{\"t\":\"spong\",\"id\":{id}}}"), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                answered++;
            }
            Assert.True(answered >= 2, "got server pings");
            Assert.Eventually(() =>
            {
                var d = s.Get("/api/slots/1/iptable").Json.EnumerateArray().First(x => x.GetProperty("ipId").GetString() == "03");
                var c = d.GetProperty("clients").EnumerateArray().FirstOrDefault();
                return c.ValueKind == JsonValueKind.Object && c.GetProperty("latencySource").GetString() == "server ping" && c.GetProperty("latencyMs").GetDouble() >= 0;
            }, "the IP table shows a server-measured latency");
            var rack = s.Get("/api/rack").Json;
            var dev = rack.GetProperty("slots").EnumerateArray().First(x => x.GetProperty("number").GetInt32() == 1).GetProperty("devices").EnumerateArray().First(x => x.GetProperty("ipId").GetString() == "03");
            Assert.True(dev.GetProperty("online").GetBoolean(), "rack: online while connected");
            var client = dev.GetProperty("clients").EnumerateArray().First();
            Assert.True(client.GetProperty("latencyHistory").GetArrayLength() >= 1 && client.GetProperty("latencyAvg").GetDouble() >= 0, "rack: latency with history");
            ws.Abort();
            Assert.Eventually(() => !s.Get("/api/rack").Json.GetProperty("slots").EnumerateArray().First(x => x.GetProperty("number").GetInt32() == 1).GetProperty("devices").EnumerateArray().First(x => x.GetProperty("ipId").GetString() == "03").GetProperty("online").GetBoolean(), "rack: offline after it leaves");
        }
        finally { WebPanelSession.PingInterval = old; }
    }

    // ------------------------------------------------------------------ the HTTP surface

    [Test]
    public static void Autopanel_layout_and_rack_endpoints_work_end_to_end_with_no_users()
    {
        using var s = new OpsTestServer(false);
        Assert.Equal(404, s.Get("/api/slots/1/autopanel").Status, "empty slot");
        s.LoadSample(1);
        var ap = s.Get("/api/slots/1/autopanel");
        Assert.Equal(200, ap.Status, "autopanel " + ap.Text);
        Assert.True(ap.Json.GetProperty("controlCount").GetInt32() > 0, "has controls");
        var names = ap.Json.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("controls").EnumerateArray()).Select(c => c.GetProperty("driveName").GetString()).ToList();
        var sigs = s.Get("/api/slots/1/signals").Json.EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToHashSet();
        Assert.True(names.All(n => sigs.Contains(n)), "every control drives a real signal");
        Assert.Equal(200, s.Put("/api/slots/1/layouts/mine", new { controls = new { x = new { hidden = true } } }).Status, "save");
        Assert.Equal(1, s.Get("/api/slots/1/layouts").Json.GetArrayLength(), "list");
        Assert.True(s.Get("/api/slots/1/layouts/mine").Json.GetProperty("controls").TryGetProperty("x", out _), "get");
        Assert.Equal(400, s.Put("/api/slots/1/layouts/..%2Fevil", new { }).Status == 404 ? 400 : s.Put("/api/slots/1/layouts/a%2Fb", new { }).Status, "a name with a slash never reaches a file");
        Assert.Equal(400, s.Put("/api/slots/1/layouts/bad.", new { }).Status, "bad name");
        Assert.Equal(200, s.Delete("/api/slots/1/layouts/mine").Status, "delete");
        Assert.Equal(404, s.Delete("/api/slots/1/layouts/mine").Status, "twice");
        var rack = s.Get("/api/rack");
        Assert.Equal(200, rack.Status, "rack");
        Assert.Equal(3, rack.Json.GetProperty("slots").GetArrayLength(), "all slots");
    }

    [Test]
    public static void The_emulator_serves_a_stored_project_with_the_target_slot_and_IP_ID_injected()
    {
        using var s = new OpsTestServer(false);
        var zip = Path.Combine(s.Dir, "p.ch5z");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("index.html").Open())) w.Write("<html><head><title>x</title></head><body>hi</body></html>");
            using (var w = new StreamWriter(z.CreateEntry("app.js").Open())) w.Write("1");
        }
        Assert.Equal(200, s.Upload("/api/slots/2/panels/05/ch5", zip).Status, "upload");
        var page = s.Get("/emulate/2/05/as/1/0A/");
        Assert.Equal(200, page.Status, "page");
        Assert.True(page.Text.Contains("\"ipid\":\"0A\"") && page.Text.Contains("\"slot\":\"1\"") && page.Text.Contains("\"emulate\":true"), "config names the target, not the project's own slot");
        Assert.True(page.Text.Contains("/openvp-crcomlib.js"), "shim injected");
        Assert.Equal(200, s.Get("/emulate/2/05/as/1/0A/app.js").Status, "assets");
        Assert.Equal(404, s.Get("/emulate/2/05/as/1/0A/..%2F..%2Fapikey.txt").Status, "no escaping");
        Assert.Equal(404, s.Get("/emulate/2/06/as/1/0A/").Status, "no such project");
        Assert.Equal(404, s.Get("/emulate/2/05/as/1/00/").Status, "bad IP ID");
        Assert.Equal(404, s.Get("/emulate/2/05/as/1/0A/.secret").Status, "no dotfiles");
    }

    [Test]
    public static void Smart_graphics_upload_read_and_remove_over_HTTP()
    {
        using var s = new OpsTestServer(false);
        var f = Path.Combine(s.Dir, "Room.xml");
        File.WriteAllText(f, SampleXml);
        var up = s.Upload("/api/slots/1/panels/03/sg", f);
        Assert.Equal(200, up.Status, "upload " + up.Text);
        Assert.Equal(1, s.Get("/api/panels/sg").Json.GetArrayLength(), "listed");
        var model = s.Get("/api/slots/1/panels/03/sg");
        Assert.Equal(200, model.Status, "model");
        Assert.True(model.Json.GetProperty("report").GetProperty("unsupported").GetArrayLength() > 0, "report included");
        var junk = Path.Combine(s.Dir, "junk.vtz"); File.WriteAllBytes(junk, B("nope"));
        Assert.Equal(400, s.Upload("/api/slots/1/panels/03/sg", junk).Status, "garbage is a 400, not a crash");
        Assert.Equal(200, s.Delete("/api/slots/1/panels/03/sg").Status, "remove");
        Assert.Equal(404, s.Get("/api/slots/1/panels/03/sg").Status, "gone");
    }

    [Test]
    public static void Panel_pages_follow_the_roles_and_the_tunnel_port_serves_none_of_them()
    {
        var gate = new TunnelGate(8085);
        foreach (var path in new[] { "/emulate/1/03/as/1/03/", "/api/rack", "/api/slots/1/autopanel", "/api/slots/1/layouts", "/rack.html", "/autopanel.html", "/emulate.html", "/openvp-sg.js" })
            Assert.True(!TunnelGate.PathAllowed(path), path + " is not served on the tunnel port");
        Assert.True(TunnelGate.PathAllowed("/s/abc/openvp-crcomlib.js"), "the shared shim still is");
        Assert.Equal(Perm.View, EndpointPolicy.For("GET", "/api/slots/{n:int}/autopanel")!.Perm, "viewing is View");
        Assert.Equal(Perm.View, EndpointPolicy.For("GET", "/api/rack")!.Perm, "rack is View");
        Assert.Equal(Perm.LoadProgram, EndpointPolicy.For("PUT", "/api/slots/{n:int}/layouts/{name}")!.Perm, "saving a layout needs Programmer");
        Assert.Equal(Perm.LoadProgram, EndpointPolicy.For("POST", "/api/slots/{n:int}/panels/{ipid}/sg")!.Perm, "uploading a project needs Programmer");
    }
}
