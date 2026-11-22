using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

public enum TunnelState { Off, Downloading, Starting, On, Error }

public sealed record TunnelStatus(TunnelState State, string? Url, string? Message, bool NeedsDownload, string? DownloadUrl, string? Binary, IReadOnlyList<string> Log);

/// <summary>Reads the public address out of cloudflared's output.</summary>
public static partial class TunnelUrlParser
{
    // cloudflared prints "https://<random-words>.trycloudflare.com" in a box when a quick tunnel is ready.
    // Its log also mentions "https://api.trycloudflare.com" (the service it asks for a tunnel), which is not ours.
    [GeneratedRegex(@"https://(?<host>[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\.trycloudflare\.com(?![a-z0-9@-]|\.[a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRe();

    public static bool TryParse(string? line, out string url)
    {
        url = "";
        if (string.IsNullOrEmpty(line)) return false;
        foreach (Match m in UrlRe().Matches(line))
        {
            var host = m.Groups["host"].Value;
            if (host.Equals("api", StringComparison.OrdinalIgnoreCase)) continue;
            url = "https://" + host.ToLowerInvariant() + ".trycloudflare.com";
            return true;
        }
        return false;
    }
}

/// <summary>Where to get the official cloudflared build for an OS and CPU.</summary>
public static class CloudflaredDownload
{
    public const string BaseUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/";

    /// <summary>The release asset for this platform, or null if Cloudflare publishes none we handle.
    /// <c>Archive</c> is true for the macOS .tgz, which holds the cloudflared binary.</summary>
    public static (string Url, bool Archive)? For(OSPlatform os, Architecture arch)
    {
        string? cpu = arch switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X86 => "386",
            _ => null
        };
        if (cpu == null) return null;
        if (os == OSPlatform.Linux) return (BaseUrl + $"cloudflared-linux-{cpu}", false);
        if (os == OSPlatform.Windows) return cpu is "amd64" or "386" ? (BaseUrl + $"cloudflared-windows-{cpu}.exe", false) : null;
        if (os == OSPlatform.OSX) return cpu is "amd64" or "arm64" ? (BaseUrl + $"cloudflared-darwin-{cpu}.tgz", true) : null;
        return null;
    }

    public static OSPlatform CurrentOs() =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
}

/// <summary>
/// Runs <c>cloudflared tunnel --url http://127.0.0.1:PORT --no-autoupdate</c> (a Cloudflare "quick tunnel":
/// free, no account) and reports its public address. Starting needs a click; the binary is found from
/// <c>OpenVP:CloudflaredPath</c>, then PATH, then <c>data/tools</c>, and only downloaded when the caller says
/// the user agreed. Everything is behind this class so it can be tested with a fake cloudflared script.
/// </summary>
public sealed class TunnelManager : IDisposable
{
    private readonly object _lock = new();
    private readonly string _dataDirectory;
    private readonly string? _configuredPath;
    private readonly Func<string, string, Task>? _downloader;
    private Process? _proc;
    private int _generation;
    private TunnelState _state = TunnelState.Off;
    private string? _url, _message, _binary;
    private bool _needsDownload;
    private readonly List<string> _log = new();

    public TunnelManager(string dataDirectory, int port, string? configuredPath = null, Func<string, string, Task>? downloader = null)
    {
        _dataDirectory = dataDirectory;
        Port = port;
        _configuredPath = string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath;
        _downloader = downloader;
    }

    public int Port { get; }
    /// <summary>How long to wait for cloudflared to print its address.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public event Action? Changed;

    public string ToolsDirectory => Path.Combine(_dataDirectory, "tools");
    private string DownloadedPath => Path.Combine(ToolsDirectory, OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");

    public TunnelStatus Status()
    {
        lock (_lock)
        {
            var dl = CloudflaredDownload.For(CloudflaredDownload.CurrentOs(), RuntimeInformation.OSArchitecture);
            return new TunnelStatus(_state, _url, _message, _needsDownload, dl?.Url, _binary, _log.TakeLast(30).ToList());
        }
    }

    public string? PublicUrl { get { lock (_lock) return _state == TunnelState.On ? _url : null; } }

    /// <summary>Finds an existing cloudflared: the configured path, PATH, then data/tools. Null if there is none.</summary>
    public string? FindBinary()
    {
        if (_configuredPath != null) return File.Exists(_configuredPath) ? Path.GetFullPath(_configuredPath) : null;
        var exe = OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return File.Exists(DownloadedPath) ? DownloadedPath : null;
    }

    private void Set(TunnelState s, string? url = null, string? message = null, bool needsDownload = false)
    {
        lock (_lock) { _state = s; _url = url; _message = message; _needsDownload = needsDownload; }
        Changed?.Invoke();
    }

    private void AddLog(string line)
    {
        lock (_lock) { _log.Add(line.Length > 300 ? line[..300] : line); if (_log.Count > 200) _log.RemoveRange(0, 100); }
    }

    /// <summary>
    /// Start the tunnel. Returns once it has an address, failed, or needs the user to agree to the download
    /// (state Error with NeedsDownload set). <paramref name="allowDownload"/> is only true when the user said yes.
    /// </summary>
    public async Task<TunnelStatus> StartAsync(bool allowDownload, CancellationToken ct = default)
    {
        int gen;
        lock (_lock)
        {
            if (_state is TunnelState.Starting or TunnelState.On or TunnelState.Downloading) return Status();
            _state = TunnelState.Starting; _url = null; _message = "Starting…"; _needsDownload = false; _log.Clear();
            gen = ++_generation;   // Stop() bumps this, which cancels a start that is still downloading
        }
        Changed?.Invoke();

        var bin = FindBinary();
        if (bin == null)
        {
            if (_configuredPath != null)
            {
                Set(TunnelState.Error, message: $"OpenVP:CloudflaredPath is set to '{_configuredPath}', which doesn't exist.");
                return Status();
            }
            if (!allowDownload)
            {
                Set(TunnelState.Error, message: "cloudflared isn't installed. OpenVP can download Cloudflare's official build into the data folder (tools).", needsDownload: true);
                return Status();
            }
            try { bin = await DownloadAsync(ct); }
            catch (Exception e)
            {
                lock (_lock) { if (gen != _generation) return Status(); }
                Set(TunnelState.Error, message: "Couldn't download cloudflared: " + e.Message);
                return Status();
            }
            lock (_lock) { if (gen != _generation) return Status(); }   // stopped while downloading
        }

        Process p;
        try
        {
            var psi = new ProcessStartInfo(bin)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = false,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var a in new[] { "tunnel", "--url", $"http://127.0.0.1:{Port}", "--no-autoupdate" }) psi.ArgumentList.Add(a);
            p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            lock (_lock) { _binary = bin; }
            var urlSeen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLine(string? line)
            {
                if (line == null) return;
                AddLog(line);
                if (TunnelUrlParser.TryParse(line, out var url) && urlSeen.TrySetResult(url))
                {
                    lock (_lock) { if (gen != _generation) return; }
                    Set(TunnelState.On, url, "On. It can take a few seconds before the address answers.");
                }
            }
            p.OutputDataReceived += (_, e) => OnLine(e.Data);
            p.ErrorDataReceived += (_, e) => OnLine(e.Data);
            p.Exited += (_, _) => OnExited(p, gen, urlSeen);
            if (!p.Start()) throw new InvalidOperationException("the process didn't start");
            lock (_lock) _proc = p;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var done = await Task.WhenAny(urlSeen.Task, Task.Delay(StartTimeout, ct));
            if (done != urlSeen.Task)
            {
                lock (_lock) { if (gen != _generation) return Status(); }
                KillProcess(p);
                Set(TunnelState.Error, message: ct.IsCancellationRequested ? "Cancelled." : $"cloudflared didn't report an address within {StartTimeout.TotalSeconds:0} seconds." + LastLogHint());
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Set(TunnelState.Error, message: $"Couldn't run {bin}: {e.Message}");
        }
        return Status();
    }

    private string LastLogHint()
    {
        lock (_lock) return _log.Count == 0 ? "" : " Last output: " + string.Join(" | ", _log.TakeLast(2).Select(l => l.Length > 200 ? l[..200] + "…" : l));
    }

    private void OnExited(Process p, int gen, TaskCompletionSource<string> urlSeen)
    {
        int code;
        try { p.WaitForExit(); code = p.ExitCode; } catch { code = -1; }
        lock (_lock)
        {
            if (gen != _generation) return;   // we stopped it (or started a newer one)
            _proc = null;
        }
        urlSeen.TrySetCanceled();
        Set(TunnelState.Error, message: $"cloudflared stopped (exit code {code})." + LastLogHint());
    }

    /// <summary>Turn the tunnel off: kills cloudflared, so every outside visitor is cut off.</summary>
    public void Stop()
    {
        Process? p;
        lock (_lock)
        {
            _generation++;   // anything the old process reports from here on is ignored
            p = _proc; _proc = null;
        }
        if (p != null) KillProcess(p);
        Set(TunnelState.Off, message: "Off.");
    }

    private static void KillProcess(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        try { p.WaitForExit(3000); } catch { }
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ download

    private async Task<string> DownloadAsync(CancellationToken ct)
    {
        var asset = CloudflaredDownload.For(CloudflaredDownload.CurrentOs(), RuntimeInformation.OSArchitecture)
            ?? throw new NotSupportedException($"No official cloudflared build is known for {RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}. Install cloudflared yourself and set OpenVP:CloudflaredPath.");
        Set(TunnelState.Downloading, message: "Downloading cloudflared from " + asset.Url);
        Directory.CreateDirectory(ToolsDirectory);
        var tmp = Path.Combine(ToolsDirectory, "cloudflared.download");
        try
        {
            if (_downloader != null) await _downloader(asset.Url, tmp);
            else
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenVP");
                using var resp = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                // Only trust the official host after redirects (GitHub hands out files from its own CDN).
                var host = resp.RequestMessage?.RequestUri?.Host ?? "";
                if (!(host.EndsWith(".githubusercontent.com") || host == "github.com"))
                    throw new InvalidOperationException("Unexpected download host " + host);
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs, ct);
            }
            if (new FileInfo(tmp).Length < 1_000_000) throw new InvalidDataException("the download is too small to be cloudflared");
            if (asset.Archive) ExtractTgz(tmp, DownloadedPath);
            else File.Move(tmp, DownloadedPath, true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(DownloadedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return DownloadedPath;
        }
        finally { try { File.Delete(tmp); } catch (IOException) { } }
    }

    /// <summary>The macOS release is a .tgz holding one file, cloudflared.</summary>
    internal static void ExtractTgz(string tgz, string destFile)
    {
        using var gz = new GZipStream(File.OpenRead(tgz), CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        TarEntry? e;
        while ((e = tar.GetNextEntry()) != null)
        {
            if (e.EntryType != TarEntryType.RegularFile || Path.GetFileName(e.Name) != "cloudflared" || e.DataStream == null) continue;
            using var o = File.Create(destFile);
            e.DataStream.CopyTo(o);
            return;
        }
        throw new InvalidDataException("the archive doesn't contain cloudflared");
    }
}
