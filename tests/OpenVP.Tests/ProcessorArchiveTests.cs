using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Core.Ops;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>Whole-processor export and import: the contents, secrets and passwords, a round trip between two data folders, and files that try to escape or are too big.</summary>
public static class ProcessorArchiveTests
{
    const string AccessKey = "ACCESSKEY-9f3a1c", MqttPw = "MQTT-PW-1234", SmtpPw = "SMTP-PW-5678", HookSecret = "HOOKSECRET-4455", AuthHeader = "Bearer TOPSECRET-7788", ShareToken = "SHARETOKEN-ab12cd", IncomingToken = "INCOMINGTOKEN-ef34";
    static readonly string[] AllSecrets = [AccessKey, MqttPw, SmtpPw, HookSecret, "TOPSECRET-7788", ShareToken, IncomingToken];

    static void W(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

    /// <summary>A processor folder with two slots filled in, settings, secrets and history.</summary>
    public static string Populate(string data, bool slot2 = true)
    {
        var smw = Path.Combine(Paths.RepoRoot, "samples", "SimplWindows");
        Directory.CreateDirectory(Path.Combine(data, "uploads", "slot01"));
        File.Copy(Path.Combine(smw, "SampleProgram.smw"), Path.Combine(data, "uploads", "slot01", "SampleProgram.smw"));
        File.Copy(Path.Combine(smw, "SampleProgram.openvp.json"), Path.Combine(data, "uploads", "slot01", "SampleProgram.openvp.json"));
        W(Path.Combine(data, "program01", "slot.json"), "{\"name\":\"Boardroom\",\"roomId\":\"BOARD\",\"autostart\":true,\"program\":{\"source\":\"upload\",\"file\":\"SampleProgram.smw\",\"running\":true}}");
        W(Path.Combine(data, "program01", "nvram", "settings.json"), "{\"volume\":40}");
        W(Path.Combine(data, "program01", "user", "config", "rooms.csv"), "id,name\n1,Boardroom\n");
        W(Path.Combine(data, "program01", "devices", "simulators.json"), "[{\"id\":\"s1\",\"name\":\"Projector\"},{\"id\":\"s2\",\"name\":\"DSP\"}]");
        W(Path.Combine(data, "program01", "devices", "integrations.json"),
            "{\"webhooks\":[{\"id\":\"w1\",\"name\":\"Lights\",\"url\":\"http://hook.local/x\",\"secret\":\"" + HookSecret + "\",\"headers\":{\"Authorization\":\"" + AuthHeader + "\",\"X-Note\":\"hello\"}}]}");
        W(Path.Combine(data, "program01", "debug.json"), "{\"watches\":[]}");
        W(Path.Combine(data, "recordings", "slot01", "abc123.json"), "{\"id\":\"abc123\"}");
        if (slot2)
        {
            Directory.CreateDirectory(Path.Combine(data, "uploads", "slot02"));
            File.Copy(Path.Combine(smw, "SampleProgram.smw"), Path.Combine(data, "uploads", "slot02", "Second.smw"));
            W(Path.Combine(data, "program02", "slot.json"), "{\"name\":\"Lobby\",\"autostart\":false,\"program\":{\"source\":\"upload\",\"file\":\"Second.smw\",\"running\":true}}");
            W(Path.Combine(data, "program02", "nvram", "lobby.txt"), "lobby");
        }
        W(Path.Combine(data, "integrations", "global.json"), "{\"mqtt\":{\"host\":\"broker.local\",\"port\":1883}}");
        W(Path.Combine(data, "alerts", "config.json"), "{\"settings\":{\"repeatMinutes\":15}}");
        W(Path.Combine(data, "schedules.json"), "{\"schedules\":[]}");
        W(Path.Combine(data, "shares.json"), "{\"links\":[{\"id\":\"aa\",\"token\":\"" + ShareToken + "\"}]}");
        W(Path.Combine(data, "integrations", "hooks.json"), "{\"hooks\":[{\"id\":\"h1\",\"token\":\"" + IncomingToken + "\"}]}");
        W(Path.Combine(data, "apikey.txt"), AccessKey + "\n");
        W(Path.Combine(data, "history", "uptime-20250101.jsonl"), "{\"t\":1}\n");
        W(Path.Combine(data, "activity.jsonl"), "{\"action\":\"load\"}\n");
        var secrets = new SecretStore(data);
        secrets.Set("mqtt.password", MqttPw);
        secrets.Set("alert.c1.password", SmtpPw);
        return data;
    }

    static ProcessorExporter Exporter(string data) => new(data, 2, new SecretStore(data), n => n == 1 ? "OpenVP SIMPL sample" : null, () => new { telnetEnabled = false, mdnsName = "openvp" });

    public static byte[] Export(string data, ExportOptions? o = null)
    {
        using var ms = new MemoryStream();
        Exporter(data).Export(ms, o);
        return ms.ToArray();
    }

    static Dictionary<string, byte[]> Entries(byte[] zip)
    {
        using var z = new ZipArchive(new MemoryStream(zip));
        return z.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }

    static bool Contains(byte[] hay, string needle) => hay.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) >= 0;

    sealed class Target : IDisposable
    {
        public string Dir = Paths.TempData();
        public Processor P;
        public SecretStore Secrets;
        public SlotSnapshots Snaps;
        public ProgramVersions Versions;
        public ProcessorImporter Importer;
        public List<string> Reloaded = new();
        public Target()
        {
            P = new Processor(Dir, 2);
            P.AddLoader(new SimplLoader(Path.Combine(Dir, "symbols.json")));
            Secrets = new SecretStore(Dir);
            Snaps = new SlotSnapshots(Dir);
            Versions = new ProgramVersions(Dir);
            Importer = new ProcessorImporter(P, Snaps, Secrets, Versions) { SettingsReplaced = w => Reloaded.AddRange(w) };
        }
        public string Save(byte[] zip) { var p = Path.Combine(Dir, "in.openvp"); File.WriteAllBytes(p, zip); return p; }
        public void Dispose() { P.Dispose(); }
    }

    // ------------------------------------------------------------------ crypto

    [Test]
    public static void Crypto_round_trip_and_wrong_password()
    {
        var blob = ArchiveCrypto.Encrypt(Encoding.UTF8.GetBytes("top secret text"), "correct horse battery", iterations: 2000);
        Assert.True(!Contains(blob, "top secret"), "ciphertext hides the text");
        Assert.Equal("top secret text", Encoding.UTF8.GetString(ArchiveCrypto.Decrypt(blob, "correct horse battery")), "round trip");
        Assert.Equal("wrong-password", Assert.Throws<ArchiveException>(() => ArchiveCrypto.Decrypt(blob, "wrong horse battery")).Code, "wrong password");
        var tampered = (byte[])blob.Clone(); tampered[^1] ^= 1;
        Assert.Equal("wrong-password", Assert.Throws<ArchiveException>(() => ArchiveCrypto.Decrypt(tampered, "correct horse battery")).Code, "tampered data is refused");
        var header = (byte[])blob.Clone(); header[6] ^= 1;
        Assert.Equal("wrong-password", Assert.Throws<ArchiveException>(() => ArchiveCrypto.Decrypt(header, "correct horse battery")).Code, "the header is authenticated");
        Assert.True(Assert.Throws<ArgumentException>(() => ArchiveCrypto.Encrypt([1], "short")).Message.Contains("at least"), "short password refused");
        Assert.Equal("bad-archive", Assert.Throws<ArchiveException>(() => ArchiveCrypto.Decrypt([1, 2, 3], "x")).Code, "garbage");
    }

    // ------------------------------------------------------------------ what is exported

    [Test]
    public static void Export_default_has_no_secrets_history_snapshots_or_access_key()
    {
        var data = Populate(Paths.TempData());
        var zip = Export(data);
        var e = Entries(zip);
        foreach (var secret in AllSecrets)
            foreach (var (name, bytes) in e)
                Assert.True(!Contains(bytes, secret), $"'{secret}' must not be in {name}");
        Assert.True(!e.Keys.Any(k => k.StartsWith("history/")), "no history");
        Assert.True(!e.Keys.Any(k => k.Contains("snapshots/")), "no snapshots");
        Assert.True(!e.ContainsKey("secrets.enc") && !e.Keys.Any(k => k.Contains("shares.json") || k.Contains("hooks.json") || k.Contains("apikey")), "no secrets file, share links or key");
        foreach (var want in new[] { "manifest.json", "slots/01/program/SampleProgram.smw", "slots/01/program/SampleProgram.openvp.json", "slots/01/data/slot.json", "slots/01/data/nvram/settings.json", "slots/01/data/user/config/rooms.csv",
                     "slots/01/data/devices/simulators.json", "slots/01/data/devices/integrations.json", "slots/01/data/debug.json", "slots/01/recordings/abc123.json", "slots/02/program/Second.smw", "processor/integrations/global.json", "processor/alerts/config.json", "processor/options.json" })
            Assert.True(e.ContainsKey(want), "has " + want);
        var integ = Encoding.UTF8.GetString(e["slots/01/data/devices/integrations.json"]);
        Assert.True(integ.Contains("hello") && integ.Contains("Lights") && !integ.Contains("Authorization"), "the webhook stays, its secret and Authorization header go");
        var m = JsonSerializer.Deserialize<ArchiveManifest>(e["manifest.json"], new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("openvp-export", m.Format, "format");
        Assert.Equal(1, m.Version, "version");
        Assert.Equal(2, m.Slots.Count, "two slots");
        var s1 = m.Slots[0];
        Assert.True(s1.Name == "Boardroom" && s1.File == "SampleProgram.smw" && s1.Running && s1.NvramFiles == 1 && s1.UserFiles == 1 && s1.Simulators == 2 && s1.Recordings == 1 && s1.ProgramFiles == 2, "slot 1 summary");
        Assert.True(!m.HasSecrets && !m.IncludesHistory && !m.IncludesSnapshots, "flags");
    }

    [Test]
    public static void Export_history_and_snapshots_are_opt_in_and_secrets_need_a_password()
    {
        var data = Populate(Paths.TempData());
        new SlotSnapshots(data).Take(1, "OpenVP SIMPL sample", "SampleProgram.smw", "manual");
        var e = Entries(Export(data, new ExportOptions { IncludeHistory = true, IncludeSnapshots = true }));
        Assert.True(e.ContainsKey("history/history/uptime-20250101.jsonl") && e.ContainsKey("history/activity.jsonl"), "history included when asked");
        Assert.True(e.Keys.Any(k => k.StartsWith("slots/01/snapshots/") && k.EndsWith("snapshot.json")), "snapshots included when asked");
        foreach (var (name, bytes) in e) foreach (var secret in AllSecrets) Assert.True(!Contains(bytes, secret), $"{secret} not in {name}");
        Assert.True(Assert.Throws<ArgumentException>(() => Export(data, new ExportOptions { IncludeSecrets = true })).Message.Contains("password"), "secrets without a password are refused");
        Assert.True(Assert.Throws<ArgumentException>(() => Export(data, new ExportOptions { IncludeSecrets = true, Password = "short" })).Message.Contains("password"), "short password");
        var enc = Entries(Export(data, new ExportOptions { IncludeSecrets = true, Password = "correct horse battery" }));
        Assert.True(enc.ContainsKey("secrets.enc"), "secrets.enc present");
        foreach (var (name, bytes) in enc) foreach (var secret in AllSecrets) Assert.True(!Contains(bytes, secret), $"{secret} is not readable in {name}");
    }

    // ------------------------------------------------------------------ import

    [Test]
    public static void Round_trip_between_two_data_folders()
    {
        var a = Populate(Paths.TempData());
        var zip = Export(a);
        using var t = new Target();
        // The target already has something in slot 1; importing must snapshot it first and replace it.
        W(Path.Combine(t.Dir, "program01", "nvram", "old.txt"), "old");
        var prev = t.Importer.Preview(t.Save(zip));
        Assert.Equal(2, prev.Slots.Count, "preview lists the slots");
        Assert.Equal("OpenVP SIMPL sample", prev.Slots[0].Program, "preview program name");
        Assert.True(!prev.HasSecrets && prev.Settings.Contains("integrations"), "preview settings");

        var result = t.Importer.Apply(Path.Combine(t.Dir, "in.openvp"), new ImportRequest { ReplaceSettings = true });
        Assert.True(result.Slots.All(s => s.Ok), "every slot imported: " + string.Join("; ", result.Slots.Select(s => s.Message)));
        Assert.True(result.Slots[0].Loaded && result.Slots[0].Started, "slot 1 loaded and started: " + result.Slots[0].Message);
        Assert.Equal(SlotState.Running, t.P.Slot(1).State, "the program is running");
        Assert.True(!result.Slots[1].Loaded && result.Slots[1].Message.Contains("autostart"), "slot 2 has autostart off, so it isn't loaded");
        Assert.Equal(SlotState.Empty, t.P.Slot(2).State, "slot 2 stays empty");

        foreach (var rel in new[] { "program01/nvram/settings.json", "program01/user/config/rooms.csv", "program01/devices/simulators.json", "program01/debug.json", "uploads/slot01/SampleProgram.smw", "uploads/slot01/SampleProgram.openvp.json", "recordings/slot01/abc123.json", "uploads/slot02/Second.smw", "program02/nvram/lobby.txt", "integrations/global.json", "alerts/config.json" })
            Assert.True(File.Exists(Path.Combine(t.Dir, rel)) && File.ReadAllBytes(Path.Combine(t.Dir, rel)).SequenceEqual(File.ReadAllBytes(Path.Combine(a, rel))), $"{rel} arrived unchanged");
        Assert.True(!File.Exists(Path.Combine(t.Dir, "program01", "nvram", "old.txt")), "the old nvram file is gone");
        Assert.True(!File.ReadAllText(Path.Combine(t.Dir, "program01", "devices", "integrations.json")).Contains(HookSecret), "no webhook secret came across");
        Assert.True(!File.Exists(Path.Combine(t.Dir, "shares.json")) && !File.Exists(Path.Combine(t.Dir, "apikey.txt")), "no share links or access key came across");
        Assert.True(!t.Secrets.Has("mqtt.password"), "no secrets came across");

        var snaps = t.Snaps.List(1);
        Assert.True(snaps.Count == 1 && snaps[0].Reason == ProcessorImporter.BeforeImport, "a snapshot was taken before the import");
        Assert.True(File.Exists(Path.Combine(t.Snaps.PathOf(1, snaps[0].Id)!, "nvram", "old.txt")), "and it holds what was there");
        var s1 = t.P.Slot(1);
        Assert.True(s1.Name == "Boardroom" && s1.RoomId == "BOARD" && s1.Settings.Program is { Source: "upload", File: "SampleProgram.smw", Running: true }, "slot settings restored, remembered as an upload");
        Assert.True(t.Versions.List(1).Any(v => v.How == "import"), "recorded as a program version");
        Assert.True(t.Reloaded.Contains("integrations") && t.Reloaded.Contains("alerts"), "stores were told to reload");
    }

    [Test]
    public static void Import_chosen_slots_only_and_settings_only_when_asked()
    {
        var a = Populate(Paths.TempData());
        var zip = Export(a);
        using var t = new Target();
        W(Path.Combine(t.Dir, "program01", "nvram", "keep.txt"), "keep");
        W(Path.Combine(t.Dir, "integrations", "global.json"), "{\"mqtt\":{\"host\":\"mine\"}}");
        var r = t.Importer.Apply(t.Save(zip), new ImportRequest { Slots = [2] });
        Assert.Equal(1, r.Slots.Count, "one slot");
        Assert.True(File.Exists(Path.Combine(t.Dir, "program01", "nvram", "keep.txt")), "slot 1 untouched");
        Assert.True(File.Exists(Path.Combine(t.Dir, "program02", "nvram", "lobby.txt")), "slot 2 imported");
        Assert.True(File.ReadAllText(Path.Combine(t.Dir, "integrations", "global.json")).Contains("mine"), "settings not replaced unless asked");
        Assert.True(!r.SettingsReplaced, "reported");
    }

    [Test]
    public static void Secrets_come_back_only_with_the_right_password()
    {
        var a = Populate(Paths.TempData());
        var zip = Export(a, new ExportOptions { IncludeSecrets = true, Password = "correct horse battery" });
        using var t = new Target();
        W(Path.Combine(t.Dir, "program01", "nvram", "old.txt"), "old");
        var path = t.Save(zip);
        var prev = t.Importer.Preview(path, "wrong horse battery");
        Assert.True(prev.HasSecrets && prev.PasswordOk == false, "preview says the password is wrong");
        Assert.True(t.Importer.Preview(path, "correct horse battery").PasswordOk == true, "and when it is right");
        Assert.True(t.Importer.Preview(path).PasswordOk == null, "and when none was tried");

        var bad = Assert.Throws<ArchiveException>(() => t.Importer.Apply(path, new ImportRequest { ReplaceSettings = true, RestoreSecrets = true, Password = "wrong horse battery" }));
        Assert.Equal("wrong-password", bad.Code, "wrong password");
        Assert.True(File.Exists(Path.Combine(t.Dir, "program01", "nvram", "old.txt")) && !t.Secrets.Has("mqtt.password"), "nothing changed after a wrong password");
        Assert.Equal("need-password", Assert.Throws<ArchiveException>(() => t.Importer.Apply(path, new ImportRequest { RestoreSecrets = true })).Code, "no password");

        var ok = t.Importer.Apply(path, new ImportRequest { ReplaceSettings = true, RestoreSecrets = true, Password = "correct horse battery" });
        Assert.True(ok.SecretsRestored, "secrets restored");
        Assert.Equal(MqttPw, t.Secrets.Get("mqtt.password"), "mqtt password");
        Assert.Equal(SmtpPw, t.Secrets.Get("alert.c1.password"), "smtp password");
        Assert.True(File.ReadAllText(Path.Combine(t.Dir, "program01", "devices", "integrations.json")).Contains(HookSecret), "webhook secret restored in the slot");
        Assert.True(File.ReadAllText(Path.Combine(t.Dir, "shares.json")).Contains(ShareToken) && File.ReadAllText(Path.Combine(t.Dir, "integrations", "hooks.json")).Contains(IncomingToken), "share links and incoming webhooks restored");
        Assert.True(!File.Exists(Path.Combine(t.Dir, "apikey.txt")), "the access key is never part of an export");
    }

    // ------------------------------------------------------------------ files that try something

    static byte[] Zip(Action<ZipArchive> fill)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true)) fill(z);
        return ms.ToArray();
    }

    static void Put(ZipArchive z, string name, string text) { using var s = z.CreateEntry(name).Open(); s.Write(Encoding.UTF8.GetBytes(text)); }

    static string GoodManifest(int slot = 1, int version = 1) =>
        $"{{\"format\":\"openvp-export\",\"version\":{version},\"created\":\"2025-01-01T00:00:00Z\",\"slots\":[{{\"slot\":{slot},\"file\":\"A.smw\",\"autostart\":false}}]}}";

    [Test]
    public static void Zip_slip_names_are_refused_and_nothing_is_written()
    {
        foreach (var evil in new[] { "slots/01/data/../../../evil.txt", "slots/01/data/..\\..\\..\\evil.txt", "/etc/evil.txt", "C:\\evil.txt", "slots/01/data/nvram/../../../../evil.txt", "slots/01/program/a\0b" })
        {
            using var t = new Target();
            var zip = Zip(z => { Put(z, "manifest.json", GoodManifest()); Put(z, evil, "pwned"); });
            var path = t.Save(zip);
            var parent = Directory.GetParent(t.Dir)!.FullName;
            var ex = Assert.Throws<ArchiveException>(() => t.Importer.Apply(path, new ImportRequest()));
            Assert.Equal("unsafe-path", ex.Code, $"'{evil.Replace('\0', '?')}' is refused");
            Assert.True(!File.Exists(Path.Combine(parent, "evil.txt")) && !File.Exists(Path.Combine(t.Dir, "evil.txt")) && !File.Exists("/etc/evil.txt"), "nothing was written outside");
            Assert.Equal("unsafe-path", Assert.Throws<ArchiveException>(() => t.Importer.Preview(path)).Code, "the preview refuses it too");
        }
        Assert.Equal("unsafe-path", Assert.Throws<ArchiveException>(() => SafeZip.SafeJoin(Path.GetTempPath(), "../x")).Code, "SafeJoin");
    }

    [Test]
    public static void Oversized_and_wrong_files_are_refused()
    {
        using var t = new Target();
        var big = Zip(z => { Put(z, "manifest.json", GoodManifest()); Put(z, "slots/01/data/nvram/big.txt", new string('x', 5000)); });
        var tiny = new ProcessorImporter(t.P, t.Snaps, t.Secrets, t.Versions, new ArchiveLimits(MaxEntryBytes: 1000));
        Assert.Equal("too-big", Assert.Throws<ArchiveException>(() => tiny.Apply(t.Save(big), new ImportRequest())).Code, "an entry over the limit");
        var total = new ProcessorImporter(t.P, t.Snaps, t.Secrets, t.Versions, new ArchiveLimits(MaxTotalBytes: 3000));
        Assert.Equal("too-big", Assert.Throws<ArchiveException>(() => total.Apply(t.Save(big), new ImportRequest())).Code, "over the total");
        var many = new ProcessorImporter(t.P, t.Snaps, t.Secrets, t.Versions, new ArchiveLimits(MaxEntries: 1));
        Assert.Equal("too-big", Assert.Throws<ArchiveException>(() => many.Apply(t.Save(big), new ImportRequest())).Code, "too many entries");
        Assert.True(!Directory.Exists(Path.Combine(t.Dir, "program01", "nvram")) || !File.Exists(Path.Combine(t.Dir, "program01", "nvram", "big.txt")), "nothing was imported");

        Assert.Equal("wrong-version", Assert.Throws<ArchiveException>(() => t.Importer.Preview(t.Save(Zip(z => Put(z, "manifest.json", GoodManifest(1, 99)))))).Code, "a newer format");
        Assert.Equal("bad-archive", Assert.Throws<ArchiveException>(() => t.Importer.Preview(t.Save(Zip(z => Put(z, "readme.txt", "hi"))))).Code, "a zip with no manifest");
        Assert.Equal("bad-archive", Assert.Throws<ArchiveException>(() => t.Importer.Preview(t.Save("not a zip at all"u8.ToArray()))).Code, "not a zip");
        Assert.Equal("bad-archive", Assert.Throws<ArchiveException>(() => t.Importer.Preview(t.Save(Zip(z => Put(z, "manifest.json", "{\"format\":\"something-else\",\"version\":1}"))))).Code, "another kind of file");
        Assert.Equal("bad-archive", Assert.Throws<ArchiveException>(() => t.Importer.Preview(t.Save(Zip(z => { Put(z, "manifest.json", GoodManifest()); Put(z, "a/b", "1"); Put(z, "a/b", "2"); })))).Code, "duplicate names");
    }

    [Test]
    public static void Slots_the_processor_doesnt_have_are_noted_not_imported()
    {
        using var t = new Target();
        var zip = Zip(z => { Put(z, "manifest.json", GoodManifest(slot: 7)); Put(z, "slots/07/data/nvram/x.txt", "x"); });
        var prev = t.Importer.Preview(t.Save(zip));
        Assert.True(prev.Slots.Count == 0 && prev.Warnings.Any(w => w.Contains("slot 7")), "warned");
        Assert.Equal(0, t.Importer.Apply(Path.Combine(t.Dir, "in.openvp"), new ImportRequest()).Slots.Count, "nothing to import");
    }
}
