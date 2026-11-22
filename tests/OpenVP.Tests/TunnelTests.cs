using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenVP.Core;

namespace OpenVP.Tests;

public static class TunnelTests
{
    // ------------------------------------------------------------------ URL parsing

    [Test]
    public static void Parser_finds_the_quick_tunnel_address_in_the_banner()
    {
        Assert.True(TunnelUrlParser.TryParse("|  https://fluffy-random-words-here.trycloudflare.com                                          |", out var u), "banner line");
        Assert.Equal("https://fluffy-random-words-here.trycloudflare.com", u, "url");
        Assert.True(TunnelUrlParser.TryParse("2025-01-01T10:00:00Z INF |  https://ab-cd.trycloudflare.com  |", out u), "log prefix");
        Assert.Equal("https://ab-cd.trycloudflare.com", u, "url");
        Assert.True(TunnelUrlParser.TryParse("HTTPS://MiXeD-Case.TryCloudflare.com/", out u), "case-insensitive, trailing slash");
        Assert.Equal("https://mixed-case.trycloudflare.com", u, "normalised to lower case, no path");
    }

    [Test]
    public static void Parser_ignores_everything_that_is_not_a_tunnel_address()
    {
        string[] no =
        {
            "", "INF Requesting new quick Tunnel on trycloudflare.com...",
            "ERR failed to request quick Tunnel: Post \"https://api.trycloudflare.com/tunnel\": dial tcp: lookup api.trycloudflare.com",
            "http://plain-http.trycloudflare.com", "https://trycloudflare.com", "https://example.com/foo.trycloudflare.com",
            "https://foo.trycloudflare.com.evil.example", "https://foo.trycloudflare.com@evil.example", "https://foo.trycloudflare.community",
            "https://-bad.trycloudflare.com",
        };
        foreach (var l in no) Assert.True(!TunnelUrlParser.TryParse(l, out var u), $"not an address: '{l}' (got {u})");
        Assert.True(!TunnelUrlParser.TryParse(null, out _), "null");
        // The api host is skipped but a real address on the same line still counts.
        Assert.True(TunnelUrlParser.TryParse("via https://api.trycloudflare.com ready at https://real-one.trycloudflare.com", out var r), "second match");
        Assert.Equal("https://real-one.trycloudflare.com", r, "url");
    }

    // ------------------------------------------------------------------ download

    [Test]
    public static void Download_urls_are_the_official_release_assets()
    {
        const string b = "https://github.com/cloudflare/cloudflared/releases/latest/download/";
        Assert.Equal((b + "cloudflared-linux-amd64", false), CloudflaredDownload.For(OSPlatform.Linux, Architecture.X64)!.Value, "linux x64");
        Assert.Equal((b + "cloudflared-linux-arm64", false), CloudflaredDownload.For(OSPlatform.Linux, Architecture.Arm64)!.Value, "linux arm64");
        Assert.Equal((b + "cloudflared-linux-arm", false), CloudflaredDownload.For(OSPlatform.Linux, Architecture.Arm)!.Value, "linux arm");
        Assert.Equal((b + "cloudflared-windows-amd64.exe", false), CloudflaredDownload.For(OSPlatform.Windows, Architecture.X64)!.Value, "windows x64");
        Assert.Equal((b + "cloudflared-windows-386.exe", false), CloudflaredDownload.For(OSPlatform.Windows, Architecture.X86)!.Value, "windows x86");
        Assert.Equal((b + "cloudflared-darwin-arm64.tgz", true), CloudflaredDownload.For(OSPlatform.OSX, Architecture.Arm64)!.Value, "mac arm64 (tgz)");
        Assert.Equal((b + "cloudflared-darwin-amd64.tgz", true), CloudflaredDownload.For(OSPlatform.OSX, Architecture.X64)!.Value, "mac x64 (tgz)");
        Assert.True(CloudflaredDownload.For(OSPlatform.Windows, Architecture.Arm64) == null, "no windows arm64 build handled");
        Assert.True(CloudflaredDownload.For(OSPlatform.Linux, Architecture.Wasm) == null, "unknown cpu");
    }

    // ------------------------------------------------------------------ running a (fake) cloudflared

    private const string Banner = """
        echo '2025-01-01T10:00:00Z INF Thank you for trying Cloudflare Tunnel.' >&2
        echo '2025-01-01T10:00:00Z INF Requesting new quick Tunnel on trycloudflare.com...' >&2
        echo 'ERR  retrying https://api.trycloudflare.com/tunnel' >&2
        echo '+--------------------------------------------------------------------------------------------+' >&2
        echo '|  Your quick Tunnel has been created! Visit it at (it may take some time to be reachable):  |' >&2
        echo '|  https://fake-test-tunnel.trycloudflare.com                                                |' >&2
        echo '+--------------------------------------------------------------------------------------------+' >&2
        """;

    private static string FakeCloudflared(string dir, string body)
    {
        var path = Path.Combine(dir, "fake-cloudflared");
        File.WriteAllText(path, "#!/bin/sh\necho \"$@\" > \"$0.args\"\necho $$ > \"$0.pid\"\n" + body + "\n");
        File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
        return path;
    }

    private static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; }
    }

    [Test]
    public static void Manager_starts_a_fake_cloudflared_reports_the_url_and_stop_kills_it()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("openvp-tunnel").FullName;
        var fake = FakeCloudflared(dir, Banner + "\nexec sleep 600");
        using var t = new TunnelManager(dir, 8085, fake);
        var states = new List<TunnelState>();
        t.Changed += () => states.Add(t.Status().State);
        var st = t.StartAsync(false).GetAwaiter().GetResult();
        Assert.Equal(TunnelState.On, st.State, "state: " + st.Message);
        Assert.Equal("https://fake-test-tunnel.trycloudflare.com", st.Url, "url");
        Assert.Equal("https://fake-test-tunnel.trycloudflare.com", t.PublicUrl, "public url");
        Assert.True(states.Contains(TunnelState.Starting), "went through starting");
        Assert.Equal("tunnel --url http://127.0.0.1:8085 --no-autoupdate", File.ReadAllText(fake + ".args").Trim(), "arguments: the tunnel port, never the console's, and no auto-update");
        var pid = int.Parse(File.ReadAllText(fake + ".pid").Trim());
        Assert.True(Alive(pid), "process running");
        t.Stop();
        Assert.Equal(TunnelState.Off, t.Status().State, "off");
        Assert.True(t.PublicUrl == null && t.Status().Url == null, "no url once off");
        Assert.Eventually(() => !Alive(pid), "cloudflared killed");
    }

    [Test]
    public static void Manager_reports_cloudflared_exiting()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("openvp-tunnel").FullName;
        var fake = FakeCloudflared(dir, Banner + "\nsleep 1\necho 'ERR connection lost' >&2\nexit 7");
        using var t = new TunnelManager(dir, 8085, fake);
        Assert.Equal(TunnelState.On, t.StartAsync(false).GetAwaiter().GetResult().State, "on at first");
        Assert.Eventually(() => t.Status().State == TunnelState.Error, "error after exit", 6000);
        var st = t.Status();
        Assert.True(st.Message!.Contains("exit code 7") && st.Message.Contains("connection lost"), "says what happened: " + st.Message);
        Assert.True(st.Url == null && t.PublicUrl == null, "no address once it is gone");
    }

    [Test]
    public static void Manager_gives_up_when_no_address_appears()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("openvp-tunnel").FullName;
        var fake = FakeCloudflared(dir, "echo 'INF starting' >&2\nexec sleep 600");
        using var t = new TunnelManager(dir, 8085, fake) { StartTimeout = TimeSpan.FromMilliseconds(700) };
        var st = t.StartAsync(false).GetAwaiter().GetResult();
        Assert.Equal(TunnelState.Error, st.State, "error");
        Assert.True(st.Message!.Contains("didn't report an address"), st.Message);
        Assert.Eventually(() => !Alive(int.Parse(File.ReadAllText(fake + ".pid").Trim())), "process killed");
    }

    [Test]
    public static void Manager_with_a_wrong_configured_path_says_so_and_never_downloads()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-tunnel").FullName;
        var downloaded = false;
        using var t = new TunnelManager(dir, 8085, Path.Combine(dir, "nope"), (_, _) => { downloaded = true; return Task.CompletedTask; });
        var st = t.StartAsync(true).GetAwaiter().GetResult();
        Assert.Equal(TunnelState.Error, st.State, "error");
        Assert.True(st.Message!.Contains("OpenVP:CloudflaredPath") && !downloaded && !st.NeedsDownload, st.Message);
    }

    [Test]
    public static void Missing_cloudflared_asks_first_and_downloads_only_after_a_yes()
    {
        if (!OperatingSystem.IsLinux()) return;
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", Directory.CreateTempSubdirectory("openvp-emptypath").FullName);
        try
        {
            var dir = Directory.CreateTempSubdirectory("openvp-tunnel").FullName;
            string? askedFor = null;
            using var t = new TunnelManager(dir, 8085, null, async (url, dest) =>
            {
                // Stand-in for the network: a script padded past the size check.
                askedFor = url;
                await File.WriteAllTextAsync(dest, "#!/bin/sh\n" + Banner + "\nexec sleep 600\n" + new string('#', 1_100_000) + "\n");
            });
            Assert.True(t.FindBinary() == null, "nothing installed");
            var st = t.StartAsync(false).GetAwaiter().GetResult();
            Assert.True(st.State == TunnelState.Error && st.NeedsDownload, "asks before downloading: " + st.Message);
            Assert.True(askedFor == null, "nothing downloaded without a yes");
            st = t.StartAsync(true).GetAwaiter().GetResult();
            Assert.Equal(TunnelState.On, st.State, "after the yes: " + st.Message);
            Assert.True(askedFor!.StartsWith("https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-"), "official source: " + askedFor);
            var bin = Path.Combine(dir, "tools", "cloudflared");
            Assert.True(File.Exists(bin), "saved in data/tools");
            Assert.True((File.GetUnixFileMode(bin) & UnixFileMode.UserExecute) != 0, "executable bit set");
            Assert.True(t.FindBinary() == bin, "found next time without asking");
            t.Stop();
        }
        finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
    }

    [Test]
    public static void A_download_that_is_too_small_is_rejected()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("openvp-tunnel").FullName;
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", Directory.CreateTempSubdirectory("openvp-emptypath").FullName);
        try
        {
            using var t = new TunnelManager(dir, 8085, null, (_, dest) => { File.WriteAllText(dest, "<html>404 not found</html>"); return Task.CompletedTask; });
            var st = t.StartAsync(true).GetAwaiter().GetResult();
            Assert.Equal(TunnelState.Error, st.State, "error");
            Assert.True(st.Message!.Contains("too small") && t.FindBinary() == null, st.Message);
        }
        finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
    }
}
