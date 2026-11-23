using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Ops;

/// <summary>
/// Git-backed config settings, in <c>data/git.json</c>.
/// </summary>
/// <param name="Repo">A folder on this computer (outside the data folder). OpenVP can create it and run <c>git init</c>.</param>
/// <param name="Remote">Optional remote to push to after each commit (the <c>origin</c> remote). Credentials come from your own git setup (ssh keys or a credential helper), never from OpenVP.</param>
public sealed record GitSettings
{
    public bool Enabled { get; init; }
    public string? Repo { get; init; }
    public string? Remote { get; init; }
    public bool Push { get; init; }
    public string Branch { get; init; } = "main";
    /// <summary>Include the slots' program files (up to <see cref="MaxProgramMb"/> MB each).</summary>
    public bool IncludePrograms { get; init; } = true;
    public int MaxProgramMb { get; init; } = 50;
    public DateTimeOffset? LastCommit { get; init; }
    public string? LastHash { get; init; }
    public string? LastMessage { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? LastPush { get; init; }
    public string? LastPushResult { get; init; }

    public GitSettings Validated(string dataDirectory)
    {
        var repo = string.IsNullOrWhiteSpace(Repo) ? null : Repo.Trim();
        if (repo != null)
        {
            if (!Path.IsPathFullyQualified(repo) || repo.Any(char.IsControl)) throw new ArgumentException("Enter the full path of a folder on this computer.");
            var full = Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar);
            var data = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (full == data || full.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.Ordinal) || data.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("The repository folder can't be the data folder, inside it, or a folder that contains it.");
            if (full == Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar)) throw new ArgumentException("Choose a folder, not a drive root.");
            repo = full;
        }
        var remote = string.IsNullOrWhiteSpace(Remote) ? null : Remote.Trim();
        if (remote != null && (remote.StartsWith('-') || remote.Any(c => char.IsControl(c) || c == ' ') || remote.Length > 500))
            throw new ArgumentException("That remote address isn't valid. Use something like git@host:me/config.git or https://host/me/config.git.");
        if (!Regex.IsMatch(Branch ?? "", @"^[A-Za-z0-9][A-Za-z0-9._/-]{0,60}$") || Branch!.Contains("..")) throw new ArgumentException("The branch name isn't valid.");
        if (MaxProgramMb is < 1 or > 500) throw new ArgumentException("The program size limit is 1 to 500 MB.");
        if (Enabled && repo == null) throw new ArgumentException("Choose the repository folder first.");
        return this with { Repo = repo, Remote = remote };
    }
}

public sealed record GitCommit(string Hash, string Author, DateTimeOffset Time, string Subject);
public sealed record GitCommitResult(bool Committed, string? Hash, string Message, int Files, IReadOnlyList<string> Skipped);

/// <summary>Runs the <c>git</c> command line. Nothing is ever read from a terminal: prompts are turned off.</summary>
public static class GitCli
{
    public static (bool Ok, string Output) Run(string? repo, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        if (repo != null) { psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(repo); }
        foreach (var a in new[] { "-c", "user.name=OpenVP", "-c", "user.email=openvp@localhost", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", "-c", "core.quotepath=false" }) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "echo";
        if (Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") == null) psi.Environment["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes -o ConnectTimeout=15";
        try
        {
            using var p = Process.Start(psi)!;
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch { }
                return (false, "git took too long and was stopped.");
            }
            var o = stdout.GetAwaiter().GetResult().Trim();
            var e = stderr.GetAwaiter().GetResult().Trim();
            return (p.ExitCode == 0, p.ExitCode == 0 ? o : (e.Length > 0 ? e : o));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (false, "git isn't installed on this computer (or isn't on the PATH).");
        }
    }

    /// <summary>The git version text, or null if git can't be run.</summary>
    public static string? Version()
    {
        var (ok, o) = Run(null, TimeSpan.FromSeconds(10), "--version");
        return ok ? o : null;
    }
}

/// <summary>
/// Keeps a git repository in step with the processor's configuration. After a change (a program load, settings, files), a text-friendly copy of what matters is written to the
/// repository and committed, with a message taken from the Activity entry. Copied: each slot's <c>slot.json</c>, device simulators and integrations (with webhook secrets and auth headers
/// removed), watches, the text files in NVRAM and User, and the program files; and the processor-wide alert and integration settings, schedules and push targets. Never copied:
/// the access key, saved secrets, share-link and incoming-webhook tokens, push-target keys, and any file whose name suggests a secret or whose text contains a saved secret or the access key.
/// Commits are debounced: at most one every <see cref="Debounce"/> (30 s by default), so a burst of changes is one commit.
/// </summary>
public sealed class GitConfigSync : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(30);
    private static readonly Regex SecretName = new(@"secret|password|passwd|apikey|api[-_]key|\.key$|\.pem$|\.pfx$|\.p12$|\.crt$|id_rsa|id_ed25519|token", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _data, _file;
    private readonly int _slotCount;
    private readonly Func<IEnumerable<string>> _secretValues;
    private readonly object _gate = new();
    private readonly List<string> _pending = new();
    private readonly SemaphoreSlim _busy = new(1, 1);
    private GitSettings _settings;
    private Timer? _timer;
    private DateTimeOffset _lastCommitAt = DateTimeOffset.MinValue;
    private string? _version;
    private bool _versionKnown;

    public TimeSpan Debounce { get; }
    /// <summary>How long to wait after the first change, to gather a burst into one commit (never longer than the debounce).</summary>
    public TimeSpan Settle { get; }

    /// <summary>Raised after a commit try with the result (also when nothing changed).</summary>
    public event Action<GitCommitResult>? Committed;

    /// <param name="secretValues">The values of saved secrets and the access key; a text file containing one is not copied.</param>
    public GitConfigSync(string dataDirectory, int slotCount, Func<IEnumerable<string>>? secretValues = null, TimeSpan? debounce = null, TimeSpan? settle = null)
    {
        _data = dataDirectory; _slotCount = slotCount; _secretValues = secretValues ?? (() => []);
        Debounce = debounce ?? DefaultDebounce;
        Settle = settle ?? TimeSpan.FromSeconds(Math.Min(3, Debounce.TotalSeconds));
        _file = Path.Combine(dataDirectory, "git.json");
        try { _settings = IntegrationFiles.Load<GitSettings>(_file).Validated(dataDirectory); } catch (ArgumentException) { _settings = new GitSettings(); }
    }

    public GitSettings Settings { get { lock (_gate) return _settings; } }

    /// <summary>The git version, or null if git can't be run. Checked once.</summary>
    public string? GitVersion
    {
        get
        {
            lock (_gate) { if (!_versionKnown) { _version = GitCli.Version(); _versionKnown = true; } return _version; }
        }
    }

    public bool Available => GitVersion != null;

    public GitSettings Update(GitSettings next)
    {
        var v = next.Validated(_data);
        lock (_gate)
        {
            v = v with { LastCommit = _settings.LastCommit, LastHash = _settings.LastHash, LastMessage = _settings.LastMessage, LastError = _settings.LastError, LastPush = _settings.LastPush, LastPushResult = _settings.LastPushResult };
            if (v.Enabled && !Available) throw new ArgumentException("git isn't installed on this computer, so git-backed config can't be turned on.");
            _settings = v;
            SaveLocked();
        }
        if (v.Enabled && v.Repo != null && Directory.Exists(Path.Combine(v.Repo, ".git"))) ApplyRemote(v);
        return v;
    }

    public void Reload()
    {
        lock (_gate) try { _settings = IntegrationFiles.Load<GitSettings>(_file).Validated(_data); } catch (ArgumentException) { _settings = new GitSettings(); }
    }

    private void SaveLocked()
    {
        try { IntegrationFiles.Save(_file, _settings); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Makes the repository folder (if needed) and runs <c>git init</c> in it. Safe on an existing repository.</summary>
    public string Init(string? repo = null)
    {
        if (!Available) throw new InvalidOperationException("git isn't installed on this computer.");
        var s = Settings;
        var path = repo ?? s.Repo ?? throw new ArgumentException("Choose the repository folder first.");
        var v = (s with { Repo = path }).Validated(_data);
        path = v.Repo!;
        if (Directory.Exists(Path.Combine(path, ".git"))) return "That folder is already a git repository.";
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            throw new ArgumentException("That folder isn't empty and isn't a git repository. Choose an empty folder, and OpenVP will set it up.");
        Directory.CreateDirectory(path);
        var (ok, o) = GitCli.Run(path, TimeSpan.FromSeconds(30), "init", "-q");
        if (!ok) throw new InvalidOperationException("git init failed: " + o);
        GitCli.Run(path, TimeSpan.FromSeconds(10), "symbolic-ref", "HEAD", "refs/heads/" + v.Branch);
        File.WriteAllText(Path.Combine(path, "README.md"),
            "# OpenVP configuration\n\nThis repository is kept by OpenVP (Git-backed config). Each commit is a copy of the processor's settings, the text files in NVRAM and User, and the program files at that time.\n\n" +
            "- `slots/NN/slot.json`: the slot's settings\n- `slots/NN/nvram`, `slots/NN/user`: text files\n- `slots/NN/devices`: simulators, redirects, integrations (secrets removed)\n- `slots/NN/program`: the program files\n- `processor`: alert, integration and schedule settings\n\nSecrets and the access key are never written here.\n");
        lock (_gate) { _settings = v; SaveLocked(); }
        return "Created the repository.";
    }

    private void ApplyRemote(GitSettings v)
    {
        if (v.Remote == null) return;
        var (has, _) = GitCli.Run(v.Repo, TimeSpan.FromSeconds(10), "remote", "get-url", "origin");
        GitCli.Run(v.Repo, TimeSpan.FromSeconds(10), "remote", has ? "set-url" : "add", "origin", v.Remote);
    }

    // ---------------------------------------------------------------- committing

    /// <summary>Notes a change. A commit follows after the settle time, but not sooner than the debounce after the last one.</summary>
    public void RequestCommit(string message)
    {
        var s = Settings;
        if (!s.Enabled || s.Repo == null || !Available) return;
        lock (_gate)
        {
            _pending.Add(string.IsNullOrWhiteSpace(message) ? "Configuration changed" : message.Trim());
            if (_pending.Count > 200) _pending.RemoveRange(0, _pending.Count - 200);
            if (_timer != null) return; // already waiting; this change joins the same commit
            var due = DateTimeOffset.UtcNow + Settle;
            var earliest = _lastCommitAt + Debounce;
            if (earliest > due) due = earliest;
            _timer = new Timer(_ => { lock (_gate) { _timer?.Dispose(); _timer = null; } try { CommitNow(null); } catch { } }, null, due - DateTimeOffset.UtcNow, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes the copy and commits it now. <paramref name="message"/> null uses the changes noted since the last commit.</summary>
    public GitCommitResult CommitNow(string? message)
    {
        var s = Settings;
        if (s.Repo == null || !Directory.Exists(Path.Combine(s.Repo, ".git"))) throw new InvalidOperationException("The repository isn't set up yet. Choose a folder and click Create repository.");
        if (!Available) throw new InvalidOperationException("git isn't installed on this computer.");
        _busy.Wait();
        try
        {
            List<string> pending;
            lock (_gate) { pending = _pending.ToList(); _pending.Clear(); }
            var skipped = new List<string>();
            var files = Mirror(s, skipped);
            var (ok, status) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(60), "status", "--porcelain");
            if (!ok) return Fail(s, "git status failed: " + status, skipped);
            if (status.Length == 0)
            {
                var r0 = new GitCommitResult(false, null, "Nothing changed.", files, skipped);
                try { Committed?.Invoke(r0); } catch { }
                return r0;
            }
            var subject = message ?? (pending.Count == 0 ? "Configuration changed" : pending[^1] + (pending.Count > 1 ? $" (and {pending.Count - 1} more change{(pending.Count == 2 ? "" : "s")})" : ""));
            if (subject.Length > 200) subject = subject[..200];
            var args = new List<string> { "commit", "-q", "-m", subject };
            if (pending.Count > 1) { args.Add("-m"); args.Add(string.Join("\n", pending.TakeLast(40).Select(p => "- " + p))); }
            var (a, ao) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(60), "add", "-A");
            if (!a) return Fail(s, "git add failed: " + ao, skipped);
            var (c, co) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(60), [.. args]);
            if (!c) return Fail(s, "git commit failed: " + co, skipped);
            var (_, hash) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(10), "rev-parse", "HEAD");
            _lastCommitAt = DateTimeOffset.UtcNow;
            lock (_gate) { _settings = _settings with { LastCommit = DateTimeOffset.UtcNow, LastHash = hash, LastMessage = subject, LastError = null }; SaveLocked(); }
            var r = new GitCommitResult(true, hash, subject, files, skipped);
            if (s.Push && s.Remote != null) PushAsync(s);
            try { Committed?.Invoke(r); } catch { }
            return r;
        }
        finally { _busy.Release(); }
    }

    private GitCommitResult Fail(GitSettings s, string error, List<string> skipped)
    {
        lock (_gate) { _settings = _settings with { LastError = error }; SaveLocked(); }
        var r = new GitCommitResult(false, null, error, 0, skipped);
        try { Committed?.Invoke(r); } catch { }
        return r;
    }

    private void PushAsync(GitSettings s)
    {
        _ = Task.Run(() =>
        {
            ApplyRemote(s);
            var (ok, o) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(90), "push", "-q", "origin", $"HEAD:refs/heads/{s.Branch}");
            lock (_gate) { _settings = _settings with { LastPush = DateTimeOffset.UtcNow, LastPushResult = ok ? "Pushed." : "Push failed: " + o }; SaveLocked(); }
        });
    }

    /// <summary>Pushes the current branch to the remote now. Returns the result text.</summary>
    public string PushNow()
    {
        var s = Settings;
        if (s.Repo == null || s.Remote == null) throw new InvalidOperationException("Enter a remote address first.");
        ApplyRemote(s);
        var (ok, o) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(90), "push", "-q", "origin", $"HEAD:refs/heads/{s.Branch}");
        lock (_gate) { _settings = _settings with { LastPush = DateTimeOffset.UtcNow, LastPushResult = ok ? "Pushed." : "Push failed: " + o }; SaveLocked(); }
        return ok ? "Pushed." : "Push failed: " + o;
    }

    public List<GitCommit> Log(int limit = 30)
    {
        var s = Settings;
        var list = new List<GitCommit>();
        if (s.Repo == null || !Directory.Exists(Path.Combine(s.Repo, ".git"))) return list;
        var (ok, o) = GitCli.Run(s.Repo, TimeSpan.FromSeconds(20), "log", $"-n{Math.Clamp(limit, 1, 200)}", "--pretty=format:%H%x1f%an%x1f%aI%x1f%s");
        if (!ok) return list;
        foreach (var line in o.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\x1f');
            if (p.Length == 4 && DateTimeOffset.TryParse(p[2], out var t)) list.Add(new GitCommit(p[0], p[1], t, p[3]));
        }
        return list;
    }

    // ---------------------------------------------------------------- the copy

    /// <summary>The relative paths (inside the repository) of everything the copy should hold, with a way to read each. Public for tests.</summary>
    public Dictionary<string, Func<byte[]>> Plan(GitSettings s, List<string> skipped)
    {
        var plan = new Dictionary<string, Func<byte[]>>(StringComparer.Ordinal);
        var secrets = _secretValues().Where(v => v.Length >= 6).Distinct().ToList();
        bool Leaks(byte[] data)
        {
            if (secrets.Count == 0 || data.Length > TextFileEditor.MaxBytes) return false;
            var text = Encoding.UTF8.GetString(data);
            return secrets.Any(v => text.Contains(v, StringComparison.Ordinal));
        }
        void AddText(string rel, string path, bool strip = false)
        {
            if (!File.Exists(path)) return;
            if (SecretName.IsMatch(Path.GetFileName(path))) { skipped.Add($"{rel}: the name looks like a secret"); return; }
            var info = new FileInfo(path);
            if (info.Length > TextFileEditor.MaxBytes) { skipped.Add($"{rel}: bigger than {TextFileEditor.MaxBytes / 1048576} MB"); return; }
            byte[] data;
            try { data = ReadShared(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add($"{rel}: {e.Message}"); return; }
            if (Array.IndexOf(data, (byte)0) >= 0) { skipped.Add($"{rel}: binary file"); return; }
            if (strip) data = Encoding.UTF8.GetBytes(DataLayout.StripIntegrationSecrets(Encoding.UTF8.GetString(data)));
            if (Leaks(data)) { skipped.Add($"{rel}: contains a saved secret or the access key"); return; }
            plan[rel] = () => data;
        }
        foreach (var rel in new[] { "integrations/global.json", "alerts/config.json", "schedules.json", "targets.json", "backups.json", "versions/settings.json", "symbols.json", "primitives.json", "comports.json" })
            AddText("processor/" + rel, Path.Combine(_data, rel.Replace('/', Path.DirectorySeparatorChar)));
        for (var n = 1; n <= _slotCount; n++)
        {
            var slotDir = DataLayout.SlotFolder(_data, n);
            var prefix = $"slots/{n:00}/";
            AddText(prefix + "slot.json", Path.Combine(slotDir, "slot.json"));
            AddText(prefix + "debug.json", Path.Combine(slotDir, "debug.json"));
            foreach (var d in new[] { "devices", "nvram", "user" })
            {
                var dir = Path.Combine(slotDir, d);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var rel = prefix + d + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/');
                    if (d != "devices" && !TextFileEditor.IsEditableName(f)) { if (!f.EndsWith(".tmp")) skipped.Add($"{rel}: not a text file"); continue; }
                    AddText(rel, f, strip: d == "devices" && Path.GetFileName(f).Equals("integrations.json", StringComparison.OrdinalIgnoreCase));
                }
            }
            if (s.IncludePrograms)
            {
                var up = DataLayout.Uploads(_data, n);
                if (Directory.Exists(up))
                    foreach (var f in Directory.EnumerateFiles(up).OrderBy(f => f, StringComparer.Ordinal))
                    {
                        var rel = prefix + "program/" + Path.GetFileName(f);
                        if (new FileInfo(f).Length > (long)s.MaxProgramMb * 1048576) { skipped.Add($"{rel}: bigger than {s.MaxProgramMb} MB"); continue; }
                        var path = f;
                        plan[rel] = () => ReadShared(path);
                    }
            }
        }
        return plan;
    }

    // Brings the repository's managed folders (slots, processor) in line with the plan. Returns the number of files written or kept.
    private int Mirror(GitSettings s, List<string> skipped)
    {
        var repo = s.Repo!;
        var plan = Plan(s, skipped);
        foreach (var (rel, read) in plan)
        {
            var dest = Path.Combine(repo, rel.Replace('/', Path.DirectorySeparatorChar));
            byte[] data;
            try { data = read(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add($"{rel}: {e.Message}"); continue; }
            if (File.Exists(dest) && new FileInfo(dest).Length == data.Length && File.ReadAllBytes(dest).AsSpan().SequenceEqual(data)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, data);
        }
        foreach (var top in new[] { "slots", "processor" })
        {
            var dir = Path.Combine(repo, top);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
                if (!plan.ContainsKey(Path.GetRelativePath(repo, f).Replace('\\', '/'))) File.Delete(f);
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
        return plan.Count;
    }

    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var b = new byte[fs.Length];
        fs.ReadExactly(b);
        return b;
    }

    public void Dispose() { lock (_gate) { _timer?.Dispose(); _timer = null; } }
}
