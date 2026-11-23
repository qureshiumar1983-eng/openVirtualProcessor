using OpenVP.Core;

namespace OpenVP.Tests;

public static class ShareStoreTests
{
    private sealed class Clock { public DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero); public DateTimeOffset Get() => Now; }

    private static (ShareStore store, Clock clock, string dir) Make()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-shares").FullName;
        var clock = new Clock();
        return (new ShareStore(dir, clock.Get), clock, dir);
    }

    [Test]
    public static void TokensAreLongRandomAndUrlSafe()
    {
        var (s, _, _) = Make();
        var tokens = Enumerable.Range(0, 50).Select(_ => s.Create(1, 3, "x", null, null).Token).ToList();
        Assert.Equal(50, tokens.Distinct().Count(), "all different");
        foreach (var t in tokens)
        {
            Assert.True(t.Length >= 22, "at least 128 bits of base64: " + t.Length);   // 22 chars = 132 bits
            Assert.True(t.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), "URL-safe: " + t);
        }
    }

    [Test]
    public static void CreateValidatesInput()
    {
        var (s, _, _) = Make();
        void Throws(Action a, string what) { try { a(); } catch (ArgumentException) { return; } throw new AssertException("expected ArgumentException: " + what); }
        Throws(() => s.Create(1, 0, "", null, null), "ip id 0");
        Throws(() => s.Create(1, 0xFF, "", null, null), "ip id FF");
        Throws(() => s.Create(0, 3, "", null, null), "slot 0");
        Throws(() => s.Create(1, 3, "", "123", null), "pin too short");
        Throws(() => s.Create(1, 3, "", "123456789", null), "pin too long");
        Throws(() => s.Create(1, 3, "", "12a4", null), "pin not digits");
        Throws(() => ShareStore.ParseExpiry("2d"), "odd expiry");
        Equal(null, ShareStore.ParseExpiry("never"));
        Equal(TimeSpan.FromHours(1), ShareStore.ParseExpiry("1h"));
        Equal(TimeSpan.FromDays(1), ShareStore.ParseExpiry("1d"));
        Equal(TimeSpan.FromDays(7), ShareStore.ParseExpiry("1w"));
        Assert.True(s.Create(1, 3, "ok", "1234", null).HasPin, "4-digit pin ok");
        Assert.True(s.Create(1, 3, "ok", "12345678", null).HasPin, "8-digit pin ok");
    }

    private static void Equal(TimeSpan? expected, TimeSpan? actual) => Assert.Equal(expected, actual, "expiry");

    [Test]
    public static void ExpiryEndsALink()
    {
        var (s, clock, _) = Make();
        var l = s.Create(2, 3, "Client", null, TimeSpan.FromHours(1));
        Assert.True(s.FindByToken(l.Token) != null, "live at first");
        clock.Now += TimeSpan.FromMinutes(59);
        Assert.True(s.FindByToken(l.Token) != null, "live just before");
        Assert.True(s.IsActive(l.Id), "active by id");
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.True(s.FindByToken(l.Token) == null, "gone after expiry");
        Assert.True(!s.IsActive(l.Id), "inactive by id");
        Assert.Equal(0, s.Active().Count, "not listed");
        var never = s.Create(2, 3, "Forever", null, null);
        clock.Now += TimeSpan.FromDays(3650);
        Assert.True(s.FindByToken(never.Token) != null, "never means never");
    }

    [Test]
    public static void RevokeKillsALinkAtOnceAndForGood()
    {
        var (s, _, dir) = Make();
        var a = s.Create(1, 3, "a", null, null);
        var b = s.Create(1, 4, "b", "1234", null);
        Assert.True(s.Revoke(a.Id), "revoked");
        Assert.True(!s.Revoke(a.Id), "second revoke finds nothing");
        Assert.True(s.FindByToken(a.Token) == null, "token dead");
        Assert.True(s.FindByToken(b.Token) != null, "other link untouched");
        var again = new ShareStore(dir);
        Assert.True(again.FindByToken(a.Token) == null, "still dead after restart");
        Assert.True(again.FindByToken(b.Token) != null, "other link survives restart");
        Assert.Equal(1, again.Active().Count, "one left");
    }

    [Test]
    public static void SlotAndIpIdComeFromTheShare()
    {
        var (s, _, dir) = Make();
        var l = s.Create(7, 0x2A, "Boardroom", null, null);
        var back = new ShareStore(dir).FindByToken(l.Token)!;
        Assert.Equal(7, back.Slot, "slot");
        Assert.Equal(0x2Au, back.IpId, "ip id");
        Assert.Equal("Boardroom", back.Label, "label");
        Assert.True(s.FindByToken("not-a-token") == null && s.FindByToken("") == null && s.FindByToken(null) == null, "unknown tokens");
    }

    [Test]
    public static void PinIsStoredHashedAndChecked()
    {
        var (s, _, dir) = Make();
        var l = s.Create(1, 3, "x", "482913", null);
        var text = File.ReadAllText(Path.Combine(dir, "shares.json"));
        Assert.True(!text.Contains("482913"), "the PIN isn't in the file");
        Assert.True(l.PinHash != null && l.PinSalt != null, "hash and salt");
        var l2 = s.Create(1, 3, "y", "482913", null);
        Assert.True(l.PinHash != l2.PinHash, "salted: same PIN, different hash");
        Assert.Equal(PinOutcome.Ok, s.CheckPin(l, "482913", "c1").Outcome, "right PIN");
        Assert.Equal(PinOutcome.Wrong, s.CheckPin(l, "000000", "c1").Outcome, "wrong PIN");
        Assert.Equal(PinOutcome.Wrong, s.CheckPin(l, "abc", "c1").Outcome, "junk PIN");
        Assert.Equal(PinOutcome.Wrong, s.CheckPin(l, null, "c1").Outcome, "no PIN");
        var noPin = s.Create(1, 3, "z", null, null);
        Assert.Equal(PinOutcome.Ok, s.CheckPin(noPin, null, "c1").Outcome, "no PIN needed");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(dir, "shares.json"));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode, "shares.json is owner-only");
        }
    }

    [Test]
    public static void WrongPinsLockTheClientOutThenItRecovers()
    {
        var (s, clock, _) = Make();
        var l = s.Create(1, 3, "x", "1234", null);
        for (var i = 1; i < ShareStore.MaxClientTries; i++)
        {
            var r = s.CheckPin(l, "9999", "evil");
            Assert.Equal(PinOutcome.Wrong, r.Outcome, "wrong #" + i);
            Assert.Equal(ShareStore.MaxClientTries - i, r.AttemptsLeft, "attempts left");
        }
        Assert.Equal(PinOutcome.Locked, s.CheckPin(l, "9999", "evil").Outcome, "5th wrong locks");
        Assert.Equal(PinOutcome.Locked, s.CheckPin(l, "1234", "evil").Outcome, "even the right PIN is refused while locked");
        Assert.True(s.IsLocked(l, "evil", out var wait) && wait > TimeSpan.FromMinutes(4), "reports the wait");
        // Someone else on the same link is not locked out by one guesser.
        Assert.Equal(PinOutcome.Ok, s.CheckPin(l, "1234", "friend").Outcome, "other client still gets in");
        clock.Now += ShareStore.ClientLockout + TimeSpan.FromSeconds(1);
        Assert.Equal(PinOutcome.Ok, s.CheckPin(l, "1234", "evil").Outcome, "unlocked after the wait");
    }

    [Test]
    public static void ManyClientsGuessingLockTheWholeLink()
    {
        var (s, clock, _) = Make();
        var l = s.Create(1, 3, "x", "1234", null);
        for (var i = 0; i < ShareStore.MaxLinkTries; i++) s.CheckPin(l, "0000", "bot" + i);
        Assert.Equal(PinOutcome.Locked, s.CheckPin(l, "1234", "friend").Outcome, "link locked for everyone");
        clock.Now += ShareStore.LinkLockout + TimeSpan.FromSeconds(1);
        Assert.Equal(PinOutcome.Ok, s.CheckPin(l, "1234", "friend").Outcome, "link recovers");
    }

    [Test]
    public static void PinCookieProofIsPerLink()
    {
        var (s, _, dir) = Make();
        var a = s.Create(1, 3, "a", "1234", null);
        var b = s.Create(1, 3, "b", "1234", null);
        var proof = s.ProofFor(a);
        Assert.True(s.Authorized(a, proof), "proof opens its own link");
        Assert.True(!s.Authorized(b, proof), "not another link, even with the same PIN");
        Assert.True(!s.Authorized(a, null) && !s.Authorized(a, "x") && !s.Authorized(a, proof + "x"), "no or bad proof");
        Assert.True(s.Authorized(s.Create(1, 3, "open", null, null), null), "no PIN, no proof needed");
        Assert.True(new ShareStore(dir).Authorized(a, proof), "proof survives a restart");
    }

    [Test]
    public static void ConnectionsAreCountedAndLastUsedIsSet()
    {
        var (s, clock, _) = Make();
        var l = s.Create(1, 3, "x", null, null);
        Assert.True(s.Active()[0].LastUsed == null, "never used");
        var c1 = s.OpenConnection(l.Id);
        var c2 = s.OpenConnection(l.Id);
        Assert.Equal(2, s.Connections(l.Id), "two open");
        c1.Dispose(); c1.Dispose();
        Assert.Equal(1, s.Connections(l.Id), "one left (double dispose counts once)");
        c2.Dispose();
        Assert.Equal(0, s.Connections(l.Id), "none");
        Assert.Equal(clock.Now, s.Active()[0].LastUsed!.Value, "last used set");
    }

    [Test]
    public static void DamagedFileStartsEmptyInsteadOfFailing()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-shares").FullName;
        File.WriteAllText(Path.Combine(dir, "shares.json"), "{ nonsense");
        var s = new ShareStore(dir);
        Assert.Equal(0, s.Active().Count, "empty");
        Assert.True(s.Create(1, 3, "x", null, null).Token.Length > 20, "still works");
    }
}
