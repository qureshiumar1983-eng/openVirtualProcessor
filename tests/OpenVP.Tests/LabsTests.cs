using OpenVP.Core.Labs;
using OpenVP.Server;

namespace OpenVP.Tests;

public static class LabsTests
{
    [Test]
    public static void Every_feature_is_off_by_default_and_the_switch_is_remembered()
    {
        var dir = Paths.TempData();
        var store = new LabsStore(dir);
        foreach (var f in LabsCatalog.All)
        {
            Assert.True(!f.Default, f.Id + " is off by default");
            Assert.True(!store.IsOn(f.Id), f.Id + " starts off");
            Assert.True(f.Description.Length > 40 && f.Warning.Length > 20, f.Id + " has a description and a may-change warning");
        }
        var changes = new List<string>();
        store.Changed += (id, on) => changes.Add(id + "=" + on);
        store.Set(LabsCatalog.Plugins, true);
        store.Set(LabsCatalog.Plugins, true); // no change: no event
        Assert.Equal(1, changes.Count, "one change reported");
        Assert.True(new LabsStore(dir).IsOn(LabsCatalog.Plugins) && !new LabsStore(dir).IsOn(LabsCatalog.RemoteEisc), "kept across a restart, per feature");
        Assert.Throws<ArgumentException>(() => store.Set("nope", true));
    }

    [Test]
    public static void The_old_cip_heartbeat_key_and_the_labs_key_still_force_a_feature_on()
    {
        var dir = Paths.TempData();
        var legacy = new LabsStore(dir, k => k == "CipHeartbeatProbe" ? true : null);
        Assert.True(legacy.IsOn(LabsCatalog.CipHeartbeatProbe), "OpenVP:CipHeartbeatProbe=true keeps working");
        Assert.True(!legacy.IsOn(LabsCatalog.Plugins), "and only that feature");
        Assert.Equal("OpenVP:CipHeartbeatProbe", legacy.ForcedBy(LabsCatalog.CipHeartbeatProbe)!.Value.Key, "the page can say why it is locked");
        Assert.Throws<InvalidOperationException>(() => legacy.Set(LabsCatalog.CipHeartbeatProbe, false));
        var off = new LabsStore(dir, k => k == "CipHeartbeatProbe" ? false : null);
        off.Set(LabsCatalog.CipHeartbeatProbe, true);
        Assert.True(off.IsOn(LabsCatalog.CipHeartbeatProbe), "CipHeartbeatProbe=false is just the default: the switch can still turn it on");
        var forcedOff = new LabsStore(dir, k => k == "Labs:remote-eisc" ? false : null);
        Assert.True(!forcedOff.IsOn(LabsCatalog.RemoteEisc), "OpenVP:Labs:<id>=false keeps it off");
    }

    [Test]
    public static void The_cip_probe_follows_the_switch_and_the_old_config_key_on_a_real_server()
    {
        using var legacy = new OpsTestServer(more: ["--OpenVP:CipEnabled=true", "--OpenVP:CipPort=" + IntegrationServiceTests.FreePort(), "--OpenVP:CipHeartbeatProbe=true"]);
        var f = legacy.Get("/api/labs").Json.GetProperty("features").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "cip-heartbeat-probe");
        Assert.True(f.GetProperty("enabled").GetBoolean() && f.GetProperty("forcedBy").GetString() == "OpenVP:CipHeartbeatProbe", "on, and says the config key did it");
        Assert.Equal(409, legacy.Call(HttpMethod.Put, "/api/labs/cip-heartbeat-probe", new { enabled = false }).Status, "can't be switched off from the page");
        Assert.True(legacy.Get("/api/rack").Json.GetProperty("cip").GetProperty("heartbeatProbe").GetBoolean(), "the rack reports the probe on");

        using var s = new OpsTestServer(more: ["--OpenVP:CipEnabled=true", "--OpenVP:CipPort=" + IntegrationServiceTests.FreePort()]);
        Assert.True(!s.Get("/api/rack").Json.GetProperty("cip").GetProperty("heartbeatProbe").GetBoolean(), "off by default");
        Assert.Equal(200, s.Call(HttpMethod.Put, "/api/labs/cip-heartbeat-probe", new { enabled = true }).Status, "switch on");
        Assert.True(s.Get("/api/rack").Json.GetProperty("cip").GetProperty("heartbeatProbe").GetBoolean(), "the CIP listener picks it up at once");
        Assert.Equal(200, s.Call(HttpMethod.Put, "/api/labs/cip-heartbeat-probe", new { enabled = false }).Status, "and off again");
        Assert.True(!s.Get("/api/rack").Json.GetProperty("cip").GetProperty("heartbeatProbe").GetBoolean(), "back to off");
    }

    [Test]
    public static void Everyone_can_read_the_labs_but_only_an_admin_can_change_them()
    {
        using var s = new OpsTestServer();
        foreach (var r in new[] { "viewer", "technician", "programmer", "admin" })
            Assert.Equal(200, s.Post("/api/users", new { name = r, password = r + "-password-1", role = r }).Status, "user " + r);
        foreach (var r in new[] { "viewer", "technician", "programmer", "admin" })
        {
            var c = new SecClient(s); Assert.Equal(200, c.Login(r, r + "-password-1"), "login " + r);
            var list = c.Get("/api/labs");
            Assert.True(list.Status == 200 && list.Json.GetProperty("features").GetArrayLength() == LabsCatalog.All.Count, r + " can see them");
            var put = c.Put("/api/labs/plugins", new { enabled = true }).Status;
            Assert.Equal(r == "admin" ? 200 : 403, put, r + " switching");
        }
        var bad = s.Call(HttpMethod.Put, "/api/labs/plugins", new { enabled = "yes" });
        Assert.Equal(400, bad.Status, "enabled must be true or false");
        Assert.Equal(404, s.Call(HttpMethod.Put, "/api/labs/nope", new { enabled = true }).Status, "unknown feature");
        Assert.True(s.Get("/api/activity").Text.Contains("Labs feature"), "the change is in the activity feed");
        Assert.Equal(EndpointPolicyPerm("PUT", "/api/labs/{id}"), OpenVP.Core.Security.Perm.Admin, "policy: Admin");
        Assert.Equal(EndpointPolicyPerm("GET", "/api/labs"), OpenVP.Core.Security.Perm.View, "policy: View");
    }

    private static OpenVP.Core.Security.Perm EndpointPolicyPerm(string m, string p) => EndpointPolicy.For(m, p)!.Perm;

    [Test]
    public static void With_no_users_nothing_changes_for_the_desktop_app_and_localhost()
    {
        // No users, localhost needs no key: the Labs page can be read and switched, exactly like every other settings page.
        using var s = new OpsTestServer(localRequiresKey: false);
        using var c = new HttpClient();
        var r = c.GetAsync(s.Url + "/api/labs").GetAwaiter().GetResult();
        Assert.Equal(200, (int)r.StatusCode, "readable from this computer with no key");
        var put = c.PutAsync(s.Url + "/api/labs/plugins", new StringContent("{\"enabled\":true}", System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        Assert.Equal(200, (int)put.StatusCode, "and switchable, as this computer is the Admin");
        Assert.True(c.GetAsync(s.Url + "/labs.html").GetAwaiter().GetResult().IsSuccessStatusCode, "the page is served");
    }

    [Test]
    public static void Nothing_new_is_served_on_the_tunnel_port()
    {
        foreach (var path in new[] { "/ws/eisc", "/api/eisc-links", "/eisc.html", "/api/labs", "/labs.html", "/api/plugins", "/api/plugins/hello/greeting", "/api/plugins/hello/ui/index.html", "/api/plugin-admin/hello/enable",
                                     "/plugins.html", "/api/compat", "/compat.html", "/api/report/draft", "/api/report/url", "/api/examples", "/api/examples/source-select/load", "/examples.html" })
            Assert.True(!TunnelGate.PathAllowed(path), path + " is not served on the tunnel port");
        Assert.True(TunnelGate.PathAllowed("/s/abc"), "shared panels still are");
        // A request on the tunnel port to the link socket is a plain 404 whatever its credentials.
        var gate = new TunnelGate(8085);
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Path = "/ws/eisc";
        ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpConnectionFeature>(new Microsoft.AspNetCore.Http.Features.HttpConnectionFeature { LocalPort = 8085 });
        var reached = false;
        gate.Handle(ctx, _ => { reached = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
        Assert.True(!reached && ctx.Response.StatusCode == 404, "the gate answers 404 and goes no further");
    }
}
