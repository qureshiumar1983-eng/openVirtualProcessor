using System.Security.Cryptography.X509Certificates;
using System.Text;
using OpenVP.Core;
using OpenVP.Core.Security;

namespace OpenVP.Tests;

/// <summary>Passwords, users, sessions, tokens, the audit chain, slot locks, presence, claim mapping and certificates, without a server.</summary>
public static class SecurityStoreTests
{
    private sealed class Clock { public DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero); public DateTimeOffset Get() => Now; }
    private static string Tmp() => Directory.CreateTempSubdirectory("openvp-sec").FullName;

    [Test]
    public static void Password_hashes_are_salted_verify_and_reject_wrong_ones()
    {
        var a = PasswordHasher.Hash("correct horse battery");
        var b = PasswordHasher.Hash("correct horse battery");
        Assert.True(a != b, "a random salt makes every hash different");
        Assert.True(a.StartsWith("pbkdf2-sha256$210000$"), "the cost is in the hash");
        Assert.True(PasswordHasher.Verify("correct horse battery", a), "the right password verifies");
        Assert.True(!PasswordHasher.Verify("correct horse batterY", a), "a wrong one doesn't");
        Assert.True(!PasswordHasher.Verify("x", null) && !PasswordHasher.Verify("x", "") && !PasswordHasher.Verify("x", "garbage$$$"), "damaged hashes verify nothing");
        var cheap = PasswordHasher.Hash("old password!", 2000);
        Assert.True(PasswordHasher.Verify("old password!", cheap) && PasswordHasher.NeedsRehash(cheap), "an older, cheaper hash still verifies and is marked for upgrade");
        Assert.True(PasswordHasher.Problem("short") != null && PasswordHasher.Problem("longenough1") == null && PasswordHasher.Problem("Sam.Lee.1234", "sam.lee.1234") != null, "length and name rules");
    }

    [Test]
    public static void Users_lock_after_five_wrong_passwords_and_unlock_with_time()
    {
        var dir = Tmp(); var clock = new Clock();
        var users = new UserStore(dir, clock.Get);
        var u = users.Create("sam", "right-password-1", Role.Technician);
        for (var i = 0; i < 4; i++) Assert.Equal(LoginOutcome.Wrong, users.Authenticate("sam", "nope").Outcome, "wrong password " + i);
        Assert.Equal(LoginOutcome.Locked, users.Authenticate("sam", "nope").Outcome, "the fifth wrong one locks the account");
        Assert.Equal(LoginOutcome.Locked, users.Authenticate("sam", "right-password-1").Outcome, "even the right password is refused while locked");
        clock.Now += TimeSpan.FromMinutes(16);
        var ok = users.Authenticate("sam", "right-password-1");
        Assert.Equal(LoginOutcome.Ok, ok.Outcome, "after the lock-out time it works again");
        Assert.Equal(0, users.Get(u.Id)!.Failed, "and the count is cleared");
        Assert.Equal(LoginOutcome.Wrong, users.Authenticate("nobody", "whatever1").Outcome, "an unknown user is just wrong");
        users.Unlock(u.Id);
        Assert.Equal(LoginOutcome.Ok, users.Authenticate("SAM", "right-password-1").Outcome, "names are not case-sensitive");
        Assert.True(!File.ReadAllText(users.FilePath).Contains("right-password-1"), "no clear password in the file");
    }

    [Test]
    public static void Users_keep_an_admin_and_refuse_duplicates()
    {
        var users = new UserStore(Tmp());
        var admin = users.Create("root", "admin-password", Role.Admin);
        var viewer = users.Create("view", "viewer-password", Role.Viewer);
        Assert.Throws<ArgumentException>(() => users.Create("ROOT", "another-password", Role.Viewer));
        Assert.Throws<ArgumentException>(() => users.Create("bad/name", "another-password", Role.Viewer));
        Assert.Throws<ArgumentException>(() => users.Create("short", "1234", Role.Viewer));
        Assert.Throws<InvalidOperationException>(() => users.Update(admin.Id, role: Role.Viewer));
        Assert.Throws<InvalidOperationException>(() => users.Update(admin.Id, disabled: true));
        Assert.Throws<InvalidOperationException>(() => users.Delete(admin.Id));
        users.Delete(viewer.Id);
        users.Delete(admin.Id);
        Assert.True(!users.Any, "deleting the last user returns to the no-users mode");
        var again = new UserStore(Path.GetDirectoryName(Path.GetDirectoryName(users.FilePath))!);
        Assert.True(!again.Any, "and that is saved");
    }

    [Test]
    public static void A_damaged_users_file_stops_start_up_instead_of_opening_everything()
    {
        var dir = Tmp();
        Directory.CreateDirectory(Path.Combine(dir, "security"));
        File.WriteAllText(Path.Combine(dir, "security", "users.json"), "{ not json");
        Assert.Throws<InvalidDataException>(() => new UserStore(dir));
    }

    [Test]
    public static void Sessions_end_after_the_idle_and_the_absolute_timeouts()
    {
        var clock = new Clock();
        var s = new SessionStore(Tmp(), clock.Get) { Idle = TimeSpan.FromMinutes(30), Absolute = TimeSpan.FromHours(8) };
        var t = s.Create("u1", "sam", "10.0.0.5");
        Assert.True(s.Validate(t) != null, "valid at once");
        Assert.True(s.Validate("not-a-token") == null && s.Validate(null) == null, "unknown or empty cookie");
        clock.Now += TimeSpan.FromMinutes(20); Assert.True(s.Validate(t) != null, "20 minutes idle is fine and refreshes it");
        clock.Now += TimeSpan.FromMinutes(20); Assert.True(s.Validate(t) != null, "20 more minutes since the last request is fine");
        clock.Now += TimeSpan.FromMinutes(31); Assert.True(s.Validate(t) == null, "31 idle minutes ends the session");
        var t2 = s.Create("u1", "sam", "10.0.0.5");
        for (var i = 0; i < 16; i++) { clock.Now += TimeSpan.FromMinutes(29); Assert.True(s.Validate(t2) != null, "kept alive, round " + i); }
        clock.Now += TimeSpan.FromMinutes(29);
        Assert.True(s.Validate(t2) == null, "a session that is always busy still ends after the absolute timeout (8 h)");
        var t3 = s.Create("u2", "kim", "10.0.0.6"); var t4 = s.Create("u2", "kim", "10.0.0.7");
        Assert.Equal(1, s.DestroyFor("u2", t3), "ending a user's other sessions");
        Assert.True(s.Validate(t3) != null && s.Validate(t4) == null, "the one asked to stay is still there");
    }

    [Test]
    public static void Sessions_are_kept_hashed_and_survive_a_restart()
    {
        var dir = Tmp(); var clock = new Clock();
        var s = new SessionStore(dir, clock.Get);
        var t = s.Create("u1", "sam", "10.0.0.5");
        Assert.True(!File.ReadAllText(Path.Combine(dir, "security", "sessions.json")).Contains(t), "the cookie value isn't stored, only its hash");
        Assert.True(new SessionStore(dir, clock.Get).Validate(t) != null, "a restart doesn't sign people out");
    }

    [Test]
    public static void Tokens_have_scopes_expire_revoke_and_are_stored_hashed()
    {
        var dir = Tmp(); var clock = new Clock();
        var store = new TokenStore(dir, clock.Get);
        var (rec, secret) = store.Create("CI", Scope.Programmer, [1, 3], clock.Now.AddDays(3), "root");
        Assert.True(secret.StartsWith("ovp_") && secret.Length > 40, "a long random value with a recognisable prefix");
        Assert.True(!File.ReadAllText(Path.Combine(dir, "security", "tokens.json")).Contains(secret), "only the hash is stored");
        var r = store.Validate(secret, "10.0.0.9");
        Assert.Equal(TokenCheck.Ok, r.Check, "valid"); Assert.True(r.FirstUseToday, "the first use of the day is flagged");
        Assert.True(!store.Validate(secret, "10.0.0.9").FirstUseToday, "the second isn't");
        Assert.True(r.Token!.Slots!.SequenceEqual([1, 3]) && r.Token.Scope == Scope.Programmer, "scope and slots come back");
        clock.Now += TimeSpan.FromDays(1); Assert.True(store.Validate(secret).FirstUseToday, "the first use on the next day is flagged again");
        clock.Now += TimeSpan.FromDays(2);
        Assert.Equal(TokenCheck.Expired, store.Validate(secret).Check, "expired after its time");
        Assert.Equal(TokenCheck.Unknown, store.Validate("ovp_" + new string('x', 43)).Check, "an unknown token");
        Assert.Equal(TokenCheck.Unknown, store.Validate("hunter2").Check, "not even shaped like one");
        var (r2, s2) = store.Create("Panel", Scope.Operator, null, null, "root");
        Assert.Equal(TokenCheck.Ok, store.Validate(s2).Check, "a token without expiry");
        Assert.True(store.Revoke(r2.Id), "revoke");
        Assert.Equal(TokenCheck.Revoked, store.Validate(s2).Check, "a revoked token stops working");
        Assert.True(store.List().First(t => t.Id == r2.Id).LastUsed != null, "last-used time is kept");
        Assert.Throws<ArgumentException>(() => store.Create("", Scope.Admin, null, null, null));
        Assert.Throws<ArgumentException>(() => store.Create("late", Scope.Admin, null, clock.Now.AddDays(-1), null));
        Assert.True(new TokenStore(dir, clock.Get).Validate(s2).Check == TokenCheck.Revoked, "revocation survives a restart");
    }

    [Test]
    public static void Token_scopes_and_roles_map_to_the_permissions_the_docs_promise()
    {
        var ro = Permissions.ForScope(Scope.ReadOnly); var op = Permissions.ForScope(Scope.Operator); var pr = Permissions.ForScope(Scope.Programmer);
        Assert.True(ro.Count == 1 && ro.Contains(Perm.View), "read-only is View");
        Assert.True(op.Contains(Perm.Operate) && op.Contains(Perm.Signal) && !op.Contains(Perm.LoadProgram) && !op.Contains(Perm.Share), "operator: start/stop/restart and signals");
        Assert.True(pr.Contains(Perm.LoadProgram) && pr.Contains(Perm.Debug) && pr.Contains(Perm.Files) && !pr.Contains(Perm.Admin), "programmer: programs, debugger, files, not admin");
        Assert.True(Enum.GetValues<Perm>().All(p => Permissions.ForScope(Scope.Admin).Contains(p)), "admin has everything");
        var tech = Permissions.ForRole(Role.Technician);
        Assert.True(tech.Contains(Perm.IpTable) && tech.Contains(Perm.Share) && tech.Contains(Perm.AlertAck) && tech.Contains(Perm.Operate) && !tech.Contains(Perm.LoadProgram), "technician");
        Assert.True(Permissions.ForRole(Role.Programmer).Contains(Perm.Recording) && Permissions.ForRole(Role.Programmer).Contains(Perm.Simulator) && !Permissions.ForRole(Role.Programmer).Contains(Perm.Admin), "programmer role");
        Assert.True(Permissions.TryParseScope("read-only", out var s) && s == Scope.ReadOnly && Permissions.TryParseRole("Viewer", out var r) && r == Role.Viewer, "names parse");
    }

    [Test]
    public static void The_audit_chain_verifies_and_catches_every_kind_of_tampering()
    {
        var dir = Tmp(); var clock = new Clock();
        var log = new AuditLog(dir, clock.Get);
        Assert.True(log.Verify().Ok, "an empty log is fine");
        for (var i = 0; i < 8; i++) { clock.Now += TimeSpan.FromSeconds(5); log.Add(i % 2 == 0 ? "login" : "load", "entry " + i, "sam", "10.0.0.5", i % 3 == 0 ? 1 : null, "d" + i); }
        var v = log.Verify();
        Assert.True(v.Ok && v.Entries == 8, "eight intact entries: " + v.Message);
        Assert.Equal(8L, log.LastSeq, "numbered");
        Assert.Equal(log.Head, new AuditLog(dir, clock.Get).Head, "a restart carries the chain on");
        var file = log.CurrentFile;
        var original = File.ReadAllLines(file);

        // 1. changed text
        File.WriteAllLines(file, original.Select((l, i) => i == 3 ? l.Replace("entry 3", "entry X") : l));
        var bad = log.Verify(); Assert.True(!bad.Ok && bad.Message!.Contains("was changed") && bad.Line == 4, "an edited entry is found at its line: " + bad.Message);
        // 2. a line removed from the middle
        File.WriteAllLines(file, original.Where((l, i) => i != 4));
        bad = log.Verify(); Assert.True(!bad.Ok, "a removed line breaks the chain: " + bad.Message);
        // 3. lines swapped
        var swapped = original.ToArray(); (swapped[2], swapped[3]) = (swapped[3], swapped[2]);
        File.WriteAllLines(file, swapped);
        Assert.True(!log.Verify().Ok, "swapped lines break it");
        // 4. the end cut off (head.json remembers the newest)
        File.WriteAllLines(file, original.Take(6));
        bad = log.Verify(); Assert.True(!bad.Ok && bad.Message!.Contains("cut off"), "cutting the end off is caught: " + bad.Message);
        // 5. an entry whose hash was recomputed but whose predecessor link is wrong
        var forged = original.ToList();
        var e5 = System.Text.Json.JsonSerializer.Deserialize<AuditEntry>(forged[5], new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        e5.Text = "forged"; e5.Hash = AuditLog.ComputeHash(e5);
        forged[5] = System.Text.Json.JsonSerializer.Serialize(e5, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        File.WriteAllLines(file, forged);
        bad = log.Verify(); Assert.True(!bad.Ok && bad.Message!.Contains("doesn't follow"), "re-hashing one entry doesn't help, the next one no longer follows: " + bad.Message);
        File.WriteAllLines(file, original);
        Assert.True(log.Verify().Ok, "the original is intact again");
    }

    [Test]
    public static void The_audit_log_rotates_keeps_the_chain_filters_and_writes_safe_csv()
    {
        var dir = Tmp(); var clock = new Clock();
        var log = new AuditLog(dir, clock.Get) { MaxBytes = 600, KeepFiles = 3 };
        for (var i = 0; i < 40; i++) { clock.Now += TimeSpan.FromSeconds(1); log.Add(i % 5 == 0 ? "permission.denied" : "load", $"entry number {i} with some padding to fill the file", i % 2 == 0 ? "sam" : "kim", "10.0.0.5", 1 + i % 3); }
        var files = Directory.GetFiles(Path.Combine(dir, "audit"), "audit-*.jsonl");
        Assert.True(files.Length >= 2 && files.Length <= 3, $"it rotated into archives and kept only the newest few (found {files.Length})");
        var v = log.Verify();
        Assert.True(v.Ok, "the chain runs across the files: " + v.Message);
        Assert.True(v.Files >= 3, "several files were checked");
        var sam = log.Query(new AuditFilter { User = "sam", Limit = 1000 });
        Assert.True(sam.Count > 0 && sam.All(e => e.User == "sam") && sam[0].Seq > sam[^1].Seq, "filtered by user, newest first");
        Assert.True(log.Query(new AuditFilter { Action = "permission", Limit = 1000 }).All(e => e.Action.StartsWith("permission")), "filtered by action");
        Assert.True(log.Query(new AuditFilter { Slot = 2, Limit = 1000 }).All(e => e.Slot == 2), "by slot");
        Assert.True(log.Query(new AuditFilter { From = clock.Now.AddSeconds(-5), Limit = 1000 }).Count <= 6, "by time");
        var e1 = log.Add("settings", "=HYPERLINK(\"http://evil\")", "a,b", "1.2.3.4");
        var csv = AuditLog.Csv([e1]);
        Assert.True(csv.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"") && csv.Contains("\"a,b\""), "a spreadsheet can't run an entry as a formula, and commas are quoted: " + csv);
        Assert.True(new AuditLog(dir, clock.Get).LastSeq == 41, "numbering continues after a restart and rotation");
    }

    [Test]
    public static void Slot_locks_allow_admins_and_the_locker_and_expire()
    {
        var clock = new Clock();
        var locks = new SlotLocks(Tmp(), clock.Get);
        var sam = Principal.ForRole(Role.Programmer, "sam", "1.1.1.1"); var kim = Principal.ForRole(Role.Programmer, "kim", "1.1.1.2"); var root = Principal.ForRole(Role.Admin, "root", "1.1.1.3");
        var changes = new List<string>(); locks.Changed += (n, on) => changes.Add($"{n}:{on}");
        Assert.True(locks.Blocking(1, kim) == null, "an unlocked slot blocks nobody");
        locks.Lock(1, sam, "Live event", clock.Now.AddHours(2));
        Assert.True(locks.Blocking(1, kim) != null, "another programmer is blocked");
        Assert.True(locks.Blocking(1, sam) == null && locks.Blocking(1, root) == null, "the locker and an Admin are not");
        Assert.True(locks.Blocking(1, null) != null, "automatic things (the scheduler) are blocked");
        Assert.True(locks.Blocking(2, kim) == null, "other slots are unaffected");
        Assert.Throws<InvalidOperationException>(() => locks.Lock(1, kim, "mine", null));
        Assert.Throws<UnauthorizedAccessException>(() => locks.Unlock(1, kim));
        clock.Now += TimeSpan.FromHours(3);
        Assert.True(locks.Get(1) == null && locks.Blocking(1, kim) == null, "the lock expires by itself");
        locks.Lock(2, kim, null, null);
        Assert.True(locks.Unlock(2, root), "an Admin can unlock someone else's lock");
        Assert.True(!locks.Unlock(2, root), "unlocking an unlocked slot says so");
        Assert.True(changes.SequenceEqual(["1:True", "1:False", "2:True", "2:False"]), "changes are announced (auto-reload resumes on them): " + string.Join(",", changes));
        var dir = Tmp();
        var first = new SlotLocks(dir, clock.Get); first.Lock(3, sam, "persist", null);
        Assert.True(new SlotLocks(dir, clock.Get).Get(3)?.Reason == "persist", "a lock survives a restart");
    }

    [Test]
    public static void Presence_counts_a_person_once_and_leaves_when_the_connection_closes()
    {
        var p = new PresenceTracker();
        var changed = new List<int>(); p.Changed += changed.Add;
        var a1 = p.Enter(1, "user:sam", "Sam Lee", "debugging");
        var a2 = p.Enter(1, "user:sam", "Sam Lee", "debugging");
        var b = p.Enter(1, "ip:10.0.0.7", "10.0.0.7", "viewing");
        var list = p.ForSlot(1);
        Assert.Equal(2, list.Count, "two tabs of one person are one entry");
        Assert.Equal("SL", list.First(e => e.Key == "user:sam").Initials, "initials for an avatar");
        Assert.Equal(1, p.OthersDebugging(1, "ip:10.0.0.7").Count, "someone else is debugging");
        Assert.Equal(0, p.OthersDebugging(1, "user:sam").Count, "not counting yourself");
        a1.Dispose(); Assert.Equal(2, p.ForSlot(1).Count, "still there with one tab open");
        a2.Dispose(); b.Dispose(); a2.Dispose();
        Assert.Equal(0, p.ForSlot(1).Count, "gone when every connection closed");
        Assert.True(changed.Count >= 6 && changed.All(n => n == 1), "changes are announced for the slot");
        Assert.Equal("1", Principal.Initials("192.168.1.5"), "an IP address gets a digit");
    }

    [Test]
    public static void Oidc_claims_map_to_a_user_and_role()
    {
        var s = new OidcSettings
        {
            Enabled = true, RoleClaim = "groups", AllowedDomains = ["example.com"],
            Mappings = [new() { Value = "openvp-admins", Role = Role.Admin }, new() { Value = "av-team", Role = Role.Programmer }, new() { Value = "floor", Role = Role.Technician }]
        };
        KeyValuePair<string, string> C(string k, string v) => new(k, v);
        var r = OidcMapper.Map([C("preferred_username", "sam.lee"), C("email", "sam@example.com"), C("name", "Sam Lee"), C("groups", "floor"), C("groups", "av-team")], s);
        Assert.True(r.Ok && r.Name == "sam.lee" && r.Role == Role.Programmer && r.Display == "Sam Lee", "the highest matching role wins");
        r = OidcMapper.Map([C("preferred_username", "root"), C("email", "root@example.com"), C("groups", "OpenVP-Admins")], s);
        Assert.True(r.Ok && r.Role == Role.Admin, "matching ignores case");
        r = OidcMapper.Map([C("preferred_username", "x"), C("email", "x@example.com"), C("groups", "random")], s);
        Assert.True(!r.Ok && r.Error!.Contains("group"), "no mapping and no default role: refused");
        s.DefaultRole = Role.Viewer;
        r = OidcMapper.Map([C("preferred_username", "x"), C("email", "x@example.com"), C("groups", "random")], s);
        Assert.True(r.Ok && r.Role == Role.Viewer, "the default role");
        r = OidcMapper.Map([C("preferred_username", "x"), C("email", "x@evil.test"), C("groups", "openvp-admins")], s);
        Assert.True(!r.Ok && r.Error!.Contains("domain"), "a domain that isn't allowed is refused whatever the groups say");
        r = OidcMapper.Map([C("preferred_username", "x"), C("email", "x@example.com"), C("groups", "[\"floor\",\"av-team\"]")], s);
        Assert.True(r.Ok && r.Role == Role.Programmer, "a group list that arrived as JSON text is split");
        s.AllowedDomains = [];
        r = OidcMapper.Map([C("sub", "abc-123"), C("groups", "floor")], s);
        Assert.True(r.Ok && r.Name == "abc-123", "falls back to another claim for the name");
        r = OidcMapper.Map([C("groups", "floor")], s);
        Assert.True(!r.Ok, "no name at all is refused");
    }

    [Test]
    public static void A_self_signed_certificate_covers_the_host_the_lan_addresses_and_openvp_local()
    {
        var (dns, ips) = CertificateTools.LocalNames("pc-7.example");
        using var cert = CertificateTools.CreateSelfSigned(dns, ips);
        var names = CertificateTools.Names(cert);
        Assert.True(names.Contains("openvp.local") && names.Contains("localhost") && names.Contains(Environment.MachineName.ToLowerInvariant()) && names.Contains("pc-7.example"), "host names: " + string.Join(",", names));
        Assert.True(names.Contains("127.0.0.1"), "loopback");
        foreach (var ip in ips) Assert.True(names.Contains(ip.ToString()), "every address of this computer: " + ip);
        Assert.True(cert.HasPrivateKey && cert.NotAfter > DateTime.Now.AddYears(1), "usable and valid for two years");
        var fp = CertificateTools.Fingerprint(cert);
        Assert.True(fp.Length == 95 && fp.Contains(':'), "a SHA-256 fingerprint with colons");
        var pfx = CertificateTools.ToPfx(cert, "pw");
        Assert.True(CertificateTools.FromPfx(pfx, "pw").Thumbprint == cert.Thumbprint, "a PFX round-trips");
        Assert.Throws<ArgumentException>(() => CertificateTools.FromPfx(pfx, "wrong"));
        var pem = cert.ExportCertificatePem(); var key = cert.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem();
        Assert.True(CertificateTools.FromPem(pem, key, null).Thumbprint == cert.Thumbprint, "PEM certificate and key");
        Assert.Throws<ArgumentException>(() => CertificateTools.FromPem(pem, "not a key", null));
    }
}
