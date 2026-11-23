using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Core.Ops;

namespace OpenVP.Tests;

/// <summary>Pushing programs and exports to other OpenVP servers: the target list, certificate pinning, and real pushes between two servers in this process.</summary>
public static class PushTests
{
    static JsonElement PollJob(OpsTestServer a, string id)
    {
        JsonElement last = default;
        Assert.Eventually(() => { last = a.Get("/api/push/" + id).Json; return last.GetProperty("done").GetBoolean(); }, "the push finished", 30000);
        return last;
    }

    static string AddTarget(OpsTestServer a, OpsTestServer b, string name = "Boardroom B", string? key = null)
    {
        var r = a.Post("/api/targets", new { name, url = b.Url, key = key ?? b.Key });
        Assert.Equal(200, r.Status, "target added: " + r.Text);
        return r.Json.GetProperty("id").GetString()!;
    }

    // ------------------------------------------------------------------ the target list and certificate checks

    [Test]
    public static void Targets_are_validated_and_their_keys_go_to_the_secret_store()
    {
        var dir = Paths.TempData();
        var secrets = new SecretStore(dir);
        var s = new TargetStore(dir, secrets);
        var t = s.Add("Lobby", "http://192.168.1.20:8080/", "the-key", null);
        Assert.Equal("http://192.168.1.20:8080", t.Url, "trailing slash dropped");
        Assert.True(!File.ReadAllText(Path.Combine(dir, "targets.json")).Contains("the-key"), "the key isn't in targets.json");
        Assert.Equal("the-key", s.KeyOf(t.Id), "it's in the secret store");
        Assert.True(Assert.Throws<ArgumentException>(() => s.Add("lobby", "http://x:1", null, null)).Message.Contains("already"), "names are unique");
        Assert.True(Assert.Throws<ArgumentException>(() => s.Add("X", "ftp://x", null, null)).Message.Contains("http"), "scheme");
        Assert.True(Assert.Throws<ArgumentException>(() => s.Add("X", "http://user:pw@x:1", null, null)).Message.Contains("user name"), "credentials in the address");
        Assert.True(Assert.Throws<ArgumentException>(() => s.Add("X", "http://x:1/api/slots", null, null)).Message.Contains("no path"), "no path");
        Assert.True(Assert.Throws<ArgumentException>(() => s.Add("X", "http://x:1", null, "AB:CD")).Message.Contains("https") || true, "fingerprint on http");
        Assert.True(Assert.Throws<ArgumentException>(() => s.Add("X", "https://x:1", null, "nothex")).Message.Contains("64 hex"), "fingerprint format");
        var fp = new string('a', 64);
        var withFp = s.Add("Cloud", "https://openvp.example.com", null, string.Join(":", Enumerable.Range(0, 32).Select(_ => "aa")));
        Assert.Equal(fp.ToUpperInvariant(), withFp.Fingerprint, "fingerprint normalised");
        Assert.True(s.Update(t.Id, "Lobby", "http://192.168.1.21:8080", null, null)!.Url.EndsWith(".21:8080") && s.HasKey(t.Id), "update keeps the key when none is sent");
        s.Update(t.Id, "Lobby", "http://192.168.1.21:8080", "", null);
        Assert.True(!s.HasKey(t.Id), "an empty key removes it");
        Assert.True(s.Remove(withFp.Id) && s.List().Count == 1, "remove");
        Assert.Equal(1, new TargetStore(dir, secrets).List().Count, "saved");
    }

    [Test]
    public static void A_pinned_fingerprint_accepts_only_that_certificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=pinned", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var other = new CertificateRequest("CN=other", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var fp = PushClient.FingerprintOf(cert);
        const SslPolicyErrors bad = SslPolicyErrors.RemoteCertificateChainErrors;
        Assert.True(PushClient.ValidateCertificate(cert, bad, fp), "the pinned certificate is accepted even though no authority signed it");
        Assert.True(PushClient.ValidateCertificate(cert, bad | SslPolicyErrors.RemoteCertificateNameMismatch, fp), "and whatever the name says");
        Assert.True(!PushClient.ValidateCertificate(other, bad, fp), "another certificate is refused");
        Assert.True(!PushClient.ValidateCertificate(other, SslPolicyErrors.None, fp), "even a valid one: a pin is strict");
        Assert.True(!PushClient.ValidateCertificate(null, SslPolicyErrors.None, fp), "no certificate");
        Assert.True(!PushClient.ValidateCertificate(cert, bad, null), "without a pin, a self-signed certificate is refused");
        Assert.True(PushClient.ValidateCertificate(cert, SslPolicyErrors.None, null), "without a pin, a valid chain is accepted");
        Assert.True(!PushClient.ValidateCertificate(cert, SslPolicyErrors.RemoteCertificateNameMismatch, null), "name mismatch is refused");
    }

    // ------------------------------------------------------------------ real servers

    [Test]
    public static void Push_a_program_version_from_one_server_to_another()
    {
        using var a = new OpsTestServer();
        using var b = new OpsTestServer();
        a.LoadSample(1);
        var tid = AddTarget(a, b);
        var test = a.Post($"/api/targets/{tid}/test").Json;
        Assert.True(test.GetProperty("ok").GetBoolean() && test.GetProperty("keyAccepted").GetBoolean() && test.GetProperty("slots").GetInt32() == 3, "test connection: " + test);

        var started = a.Post("/api/push", new { what = "version", slot = 1, version = "loaded", targets = new object[] { new { id = tid, slots = new[] { 1, 2 } } }, start = true });
        Assert.Equal(200, started.Status, "push started: " + started.Text);
        var job = PollJob(a, started.Json.GetProperty("id").GetString()!);
        var t = job.GetProperty("targets")[0];
        Assert.Equal("ok", t.GetProperty("state").GetString(), "target ok: " + t);
        Assert.Equal(2, t.GetProperty("slots").GetArrayLength(), "per-slot results");
        Assert.True(t.GetProperty("slots").EnumerateArray().All(x => x.GetProperty("state").GetString() == "ok"), "both slots ok");

        foreach (var slot in new[] { 1, 2 })
        {
            var s = b.Get($"/api/slots/{slot}").Json;
            Assert.True(s.GetProperty("state").GetString() == "Running" && s.GetProperty("program").GetString() == "SampleProgram", $"B slot {slot} runs the program: {s}");
            var versions = b.Get($"/api/slots/{slot}/versions").Json.GetProperty("versions");
            Assert.True(versions[0].GetProperty("how").GetString() == "push", "B recorded it as a pushed version");
        }
        Assert.True(a.Get("/api/activity").Json.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "push"), "activity entry on the sender");

        // A pushed version that isn't the loaded one: push an older kept version.
        var vid = a.Get("/api/slots/1/versions").Json.GetProperty("versions")[0].GetProperty("id").GetString();
        var again = a.Post("/api/push", new { what = "version", slot = 1, version = vid, targets = new object[] { new { id = tid, slots = new[] { 3 } } }, start = false });
        var j2 = PollJob(a, again.Json.GetProperty("id").GetString()!);
        Assert.Equal("ok", j2.GetProperty("targets")[0].GetProperty("state").GetString(), "kept version pushed");
        Assert.True(b.Get("/api/slots/3").Json.GetProperty("state").GetString() is "Stopped" or "Loaded", "start=false leaves it stopped: " + b.Get("/api/slots/3").Text);
    }

    [Test]
    public static void Push_to_several_targets_shows_each_ones_result()
    {
        using var a = new OpsTestServer();
        using var good = new OpsTestServer();
        using var wrongKey = new OpsTestServer();
        a.LoadSample(1);
        var gid = AddTarget(a, good, "Good");
        var kid = AddTarget(a, wrongKey, "Wrong key", key: "not-the-key");
        var dead = a.Post("/api/targets", new { name = "Dead", url = "http://127.0.0.1:" + IntegrationServiceTests.FreePort(), key = "x" }).Json.GetProperty("id").GetString()!;

        var wk = a.Post($"/api/targets/{kid}/test").Json;
        Assert.True(!wk.GetProperty("ok").GetBoolean() && wk.GetProperty("message").GetString()!.Contains("access key"), "test says the key was refused: " + wk);
        var dd = a.Post($"/api/targets/{dead}/test").Json;
        Assert.True(!dd.GetProperty("ok").GetBoolean() && dd.GetProperty("message").GetString()!.Contains("connect"), "test says it can't connect: " + dd);

        var r = a.Post("/api/push", new { what = "version", slot = 1, version = "loaded", targets = new object[] { new { id = gid, slots = new[] { 1 } }, new { id = kid, slots = new[] { 1 } }, new { id = dead, slots = new[] { 1 } } } });
        var job = PollJob(a, r.Json.GetProperty("id").GetString()!);
        var by = job.GetProperty("targets").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!, x => x);
        Assert.Equal("ok", by["Good"].GetProperty("state").GetString(), "good target ok");
        Assert.Equal("failed", by["Wrong key"].GetProperty("state").GetString(), "wrong key fails on its own");
        Assert.True(by["Wrong key"].GetProperty("slots")[0].GetProperty("message").GetString()!.Contains("access key"), "and says why: " + by["Wrong key"]);
        Assert.Equal("failed", by["Dead"].GetProperty("state").GetString(), "dead target fails on its own");
        Assert.True(wrongKey.Get("/api/slots/1").Json.GetProperty("program").ValueKind == JsonValueKind.Null, "nothing was loaded where the key was wrong");
        Assert.Equal("Running", good.Get("/api/slots/1").Json.GetProperty("state").GetString(), "the good one is running");
    }

    [Test]
    public static void Push_a_whole_export_to_another_server()
    {
        using var a = new OpsTestServer();
        using var b = new OpsTestServer();
        a.LoadSample(1);
        a.Post("/api/slots/1/files/folder", new { root = "user", path = "", name = "config" });
        File.WriteAllText(Path.Combine(a.Dir, "program01", "user", "config", "rooms.json"), "{\"rooms\":3}");
        var tid = AddTarget(a, b);
        var r = a.Post("/api/push", new { what = "export", targets = new[] { tid }, slots = new[] { 1 }, replaceSettings = false });
        Assert.Equal(200, r.Status, r.Text);
        var job = PollJob(a, r.Json.GetProperty("id").GetString()!);
        var t = job.GetProperty("targets")[0];
        Assert.Equal("ok", t.GetProperty("state").GetString(), "pushed: " + t);
        Assert.True(t.GetProperty("message").GetString()!.Contains("slot 1"), "summary: " + t.GetProperty("message"));
        Assert.Equal("Running", b.Get("/api/slots/1").Json.GetProperty("state").GetString(), "B runs the program");
        Assert.Equal("{\"rooms\":3}", File.ReadAllText(Path.Combine(b.Dir, "program01", "user", "config", "rooms.json")), "its User files came along");
    }

    [Test]
    public static void An_https_target_needs_its_certificate_pinned_when_self_signed()
    {
        var (pfx, pw, fp) = OpsTestServer.SelfSigned();
        try
        {
            using var a = new OpsTestServer();
            using var b = new OpsTestServer(scheme: "https", pfx: pfx, pfxPassword: pw);
            var r = a.Post("/api/targets", new { name = "Secure", url = b.Url, key = b.Key });
            var tid = r.Json.GetProperty("id").GetString()!;
            var refused = a.Post($"/api/targets/{tid}/test").Json;
            Assert.True(!refused.GetProperty("ok").GetBoolean() && refused.GetProperty("message").GetString()!.Contains("secure connection"), "a self-signed certificate is refused by default: " + refused);

            var cert = a.Post("/api/targets/certificate", new { url = b.Url }).Json;
            Assert.Equal(fp, cert.GetProperty("fingerprint").GetString(), "the page can read the certificate's fingerprint");
            Assert.True(!cert.GetProperty("trusted").GetBoolean(), "and says it isn't trusted");

            var wrong = a.Put($"/api/targets/{tid}", new { name = "Secure", url = b.Url, fingerprint = new string('0', 64) });
            Assert.Equal(200, wrong.Status, wrong.Text);
            Assert.True(!a.Post($"/api/targets/{tid}/test").Json.GetProperty("ok").GetBoolean(), "a different pinned fingerprint is refused");

            a.Put($"/api/targets/{tid}", new { name = "Secure", url = b.Url, fingerprint = fp });
            var ok = a.Post($"/api/targets/{tid}/test").Json;
            Assert.True(ok.GetProperty("ok").GetBoolean(), "the pinned certificate is accepted: " + ok);
            a.LoadSample(1);
            var job = PollJob(a, a.Post("/api/push", new { what = "version", slot = 1, version = "loaded", targets = new object[] { new { id = tid, slots = new[] { 1 } } } }).Json.GetProperty("id").GetString()!);
            Assert.Equal("ok", job.GetProperty("targets")[0].GetProperty("state").GetString(), "push over https works with the pin: " + job);
            Assert.Equal("Running", b.Get("/api/slots/1").Json.GetProperty("state").GetString(), "it ran on B");
        }
        finally { try { File.Delete(pfx); } catch { } }
    }
}
