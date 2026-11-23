using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Plugins;

public sealed class PluginUiManifest
{
    /// <summary>The page to open, relative to the plugin's <c>ui</c> folder (default <c>index.html</c>).</summary>
    public string Page { get; set; } = "index.html";
    /// <summary>The text of the console's menu link.</summary>
    public string Menu { get; set; } = "";
}

/// <summary><c>plugin.json</c>.</summary>
public sealed class PluginManifest
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    /// <summary>The plugin's .NET assembly (a file name in its folder). Leave out for a JavaScript-only plugin.</summary>
    public string? Assembly { get; set; }
    /// <summary>The full name of the class that implements <see cref="IOpenVpPlugin"/>; optional when the assembly has only one.</summary>
    public string? Entry { get; set; }
    public PluginUiManifest? Ui { get; set; }
}

/// <summary>The saved on/off state of each plugin, in <c>data/plugins-state.json</c>.</summary>
public sealed class PluginState
{
    public bool Enabled { get; set; }
    public List<string> ApprovedPermissions { get; set; } = new();
    public string? ApprovedVersion { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public string? EnabledBy { get; set; }
    public DateTimeOffset? EnabledAt { get; set; }
}

/// <summary>One plugin as the Plugins page shows it. <see cref="Status"/>: off, running, faulted (it crashed and was disabled), needs-approval (its permissions changed), invalid, blocked (Plugins is off in Labs).</summary>
public sealed record PluginView(string Name, string Version, string Author, string Description, IReadOnlyList<string> Permissions, string Kind, string Status, string? Error,
    bool Enabled, bool HasUi, string? UiPage, string? Menu, IReadOnlyList<string> Commands, IReadOnlyList<string> Routes, IReadOnlyList<int> Slots, IReadOnlyList<string> Log, IReadOnlyList<string> NewPermissions);

/// <summary>
/// Finds plugins in <c>data/plugins/&lt;name&gt;/plugin.json</c>, loads the ones an Admin enabled, and keeps each one from hurting the processor: its code
/// runs on its own thread, every call is guarded, and a plugin that throws or hangs is unloaded and disabled. Nothing loads while Plugins is off in Labs.
/// <para>This is not a sandbox. A plugin is ordinary .NET code in this process, so its permissions are what OpenVP's plugin API lets it do, not what it can do.</para>
/// </summary>
public sealed class PluginHost : IDisposable
{
    public static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9_-]{0,31}$", RegexOptions.Compiled);
    public static TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] Reserved = ["ui"];

    private sealed class StateFile { public Dictionary<string, PluginState> Plugins { get; set; } = new(); }

    private readonly Processor _processor;
    private readonly string _root, _stateFile, _dataRoot;
    private readonly Func<bool> _active;
    private readonly Action<string, string>? _activity;
    private readonly object _gate = new();
    private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IOpenVpPlugin>> _inProcess = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginManifest> _inProcessManifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Kind, string Message)> _invalid = new(StringComparer.OrdinalIgnoreCase);
    private StateFile _state;
    private bool _started;

    /// <param name="active">Whether Plugins is on in Labs.</param>
    /// <param name="activity">Called with an action name and text when a plugin is enabled, disabled or faults (the server puts it in the activity feed).</param>
    public PluginHost(Processor processor, string dataDirectory, Func<bool> active, Action<string, string>? activity = null)
    {
        _processor = processor; _active = active; _activity = activity;
        _root = Path.Combine(dataDirectory, "plugins");
        _dataRoot = Path.Combine(dataDirectory, "plugin-data");
        _stateFile = Path.Combine(dataDirectory, "plugins-state.json");
        _state = IntegrationFiles.Load<StateFile>(_stateFile);
    }

    public string Folder => _root;
    public bool Active => _active();

    /// <summary>For tests and for embedding: a plugin that lives in code instead of a folder. It goes through the same guards.</summary>
    public void AddInProcess(PluginManifest manifest, Func<IOpenVpPlugin> factory)
    {
        lock (_gate) { _inProcess[manifest.Name] = factory; _inProcessManifests[manifest.Name] = manifest; }
    }

    /// <summary>The processor is up: load every enabled plugin (if Plugins is on) and tell them.</summary>
    public void Start()
    {
        lock (_gate) _started = true;
        Apply();
    }

    /// <summary>Loads what should be loaded and unloads what shouldn't (call after Labs changes).</summary>
    public void Apply()
    {
        lock (_gate)
        {
            if (!_started) return;
            if (!_active())
            {
                foreach (var l in _loaded.Values.ToList()) Unload(l, graceful: true);
                return;
            }
            foreach (var m in ScanManifests())
                if (_state.Plugins.TryGetValue(m.Name, out var st) && st.Enabled && !_loaded.ContainsKey(m.Name)) TryLoad(m, st);
        }
    }

    private List<PluginManifest> ScanManifests()
    {
        var list = new List<PluginManifest>();
        _invalid.Clear();
        lock (_gate) foreach (var m in _inProcessManifests.Values) list.Add(m);
        if (!Directory.Exists(_root)) return list;
        foreach (var dir in Directory.GetDirectories(_root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(dir);
            var file = Path.Combine(dir, "plugin.json");
            if (!File.Exists(file)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                        ?? throw new JsonException("empty");
                if (!NamePattern.IsMatch(m.Name) || Reserved.Contains(m.Name)) throw new InvalidDataException($"The name \"{m.Name}\" isn't allowed: use 1 to 32 lowercase letters, digits, - or _.");
                if (!string.Equals(m.Name, folder, StringComparison.Ordinal)) throw new InvalidDataException($"The name in plugin.json (\"{m.Name}\") must match its folder (\"{folder}\").");
                if (string.IsNullOrWhiteSpace(m.Version)) throw new InvalidDataException("plugin.json needs a version.");
                if (m.Permissions.FirstOrDefault(p => !PluginPermissions.All.Contains(p)) is { } bad) throw new InvalidDataException($"Unknown permission \"{bad}\".");
                m.Permissions = m.Permissions.Distinct().ToList();
                if (m.Assembly == null && m.Ui == null) throw new InvalidDataException("A plugin needs an assembly, a ui, or both.");
                if (m.Assembly != null && (m.Assembly != Path.GetFileName(m.Assembly) || !m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("assembly is a .dll file name in the plugin's folder.");
                if (m.Ui != null && !m.Permissions.Contains(PluginPermissions.Ui)) m.Permissions.Add(PluginPermissions.Ui);
                list.RemoveAll(x => x.Name == m.Name);
                list.Add(m);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _invalid[folder] = ("invalid", "plugin.json: " + e.Message);
            }
        }
        return list;
    }

    private string FolderOf(string name) => Path.Combine(_root, name);

    // ------------------------------------------------------------------ list and state

    public IReadOnlyList<PluginView> List()
    {
        lock (_gate)
        {
            var result = new List<PluginView>();
            var active = _active();
            foreach (var m in ScanManifests())
            {
                _state.Plugins.TryGetValue(m.Name, out var st);
                _loaded.TryGetValue(m.Name, out var l);
                var added = st == null ? Array.Empty<string>() : m.Permissions.Except(st.ApprovedPermissions).ToArray();
                var status = !active ? "blocked" : l is { Faulted: false } ? "running" : st is { Enabled: true } && l == null && added.Length > 0 && st.ApprovedVersion != null ? "needs-approval"
                    : st?.LastError != null && st.Enabled == false ? "faulted" : "off";
                result.Add(new PluginView(m.Name, m.Version, m.Author, m.Description, m.Permissions, m.Assembly != null ? (m.Ui != null ? "assembly + ui" : "assembly") : "ui", status,
                    st?.LastError, st?.Enabled == true && l is { Faulted: false }, m.Ui != null, m.Ui?.Page, string.IsNullOrWhiteSpace(m.Ui?.Menu) ? null : m.Ui!.Menu,
                    l?.CommandNames() ?? [], l?.RouteNames() ?? [], l?.Slots() ?? [], l?.LogTail() ?? [], added));
            }
            foreach (var (name, (_, msg)) in _invalid)
                result.Add(new PluginView(name, "", "", "", [], "", "invalid", msg, false, false, null, null, [], [], [], [], []));
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>The manifest of an installed plugin, or null.</summary>
    public PluginManifest? Manifest(string name) { lock (_gate) return ScanManifests().FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)); }

    /// <summary>Enables a plugin (an Admin did). <paramref name="approved"/> must list every permission the plugin asks for: the Admin saw them on the Plugins page.</summary>
    public PluginView Enable(string name, IEnumerable<string> approved, string by)
    {
        lock (_gate)
        {
            if (!_active()) throw new InvalidOperationException("Plugins is switched off in Labs.");
            var m = Manifest(name) ?? throw new KeyNotFoundException($"There's no plugin called {name}.");
            var ok = approved.ToHashSet();
            if (m.Permissions.Any(p => !ok.Contains(p))) throw new ArgumentException("Approve every permission the plugin asks for: " + string.Join(", ", m.Permissions) + ".");
            _state.Plugins[m.Name] = new PluginState { Enabled = true, ApprovedPermissions = m.Permissions.ToList(), ApprovedVersion = m.Version, EnabledBy = by, EnabledAt = DateTimeOffset.Now };
            if (_loaded.TryGetValue(m.Name, out var old)) Unload(old, graceful: true);
            Save();
            TryLoad(m, _state.Plugins[m.Name]);
            _activity?.Invoke("plugin", $"Enabled the plugin {m.Name} {m.Version}");
            return List().First(p => p.Name == m.Name);
        }
    }

    public bool Disable(string name)
    {
        lock (_gate)
        {
            if (!_state.Plugins.TryGetValue(name, out var st)) return false;
            st.Enabled = false; st.LastError = null; st.LastErrorAt = null;
            if (_loaded.TryGetValue(name, out var l)) Unload(l, graceful: true);
            Save();
            _activity?.Invoke("plugin", $"Disabled the plugin {name}");
            return true;
        }
    }

    private void Save() { try { IntegrationFiles.Save(_stateFile, _state); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    // ------------------------------------------------------------------ loading

    private void TryLoad(PluginManifest m, PluginState st)
    {
        if (m.Permissions.Any(p => !st.ApprovedPermissions.Contains(p)))
        {
            Console.WriteLine($"Plugin {m.Name} asks for permissions that weren't approved ({string.Join(", ", m.Permissions.Except(st.ApprovedPermissions))}); it stays off until an Admin enables it again.");
            return;
        }
        Loaded? l = null;
        try
        {
            l = new Loaded(this, m);
            l.Load();
            _loaded[m.Name] = l;
            l.Post("Initialize", () => l.Instance!.Initialize(l.Context));
            l.Post("OnStarted", () => l.Instance!.OnStarted());
            Console.WriteLine($"Plugin {m.Name} {m.Version} loaded.");
        }
        catch (Exception e)
        {
            l?.Dispose();
            Fault(m.Name, "loading", e);
        }
    }

    private void Unload(Loaded l, bool graceful)
    {
        _loaded.Remove(l.Manifest.Name);
        l.Shutdown(graceful);
    }

    /// <summary>A plugin failed: unload it, disable it, keep the reason, tell the activity feed. Safe to call from any thread.</summary>
    internal void Fault(string name, string what, Exception e)
    {
        var msg = $"{what}: {(e is TargetInvocationException { InnerException: { } inner } ? inner : e).Message}";
        Loaded? l;
        lock (_gate)
        {
            _loaded.TryGetValue(name, out l);
            if (l != null) _loaded.Remove(name);
            if (!_state.Plugins.TryGetValue(name, out var st)) _state.Plugins[name] = st = new PluginState();
            st.Enabled = false; st.LastError = msg; st.LastErrorAt = DateTimeOffset.Now;
            Save();
        }
        Console.WriteLine($"Plugin {name} failed and was disabled: {msg}");
        _activity?.Invoke("plugin", $"The plugin {name} failed and was disabled: {msg}");
        l?.Shutdown(graceful: false);
    }

    // ------------------------------------------------------------------ requests

    /// <summary>Runs a plugin's route. Null when the plugin isn't running or has no such route. Never throws: a failing handler faults the plugin and answers 500.</summary>
    public PluginResponse? Handle(string name, PluginRequest req)
    {
        Loaded? l;
        lock (_gate) { if (!_active() || !_loaded.TryGetValue(name, out l)) return null; }
        var handler = l.FindRoute(req.Method, req.Path);
        if (handler == null) return null;
        return l.Call("route " + req.Method + " " + req.Path, () => handler(req), PluginResponse.Json(new { error = "The plugin failed and was disabled. See the Plugins page." }, 500));
    }

    /// <summary>A file of a plugin's <c>ui</c> folder, or null. Only for a running plugin that declares a ui.</summary>
    public (byte[] Data, string ContentType)? UiFile(string name, string path)
    {
        PluginManifest? m;
        lock (_gate)
        {
            if (!_active() || !_loaded.TryGetValue(name, out var l) || l.Manifest.Ui == null) return null;
            m = l.Manifest;
        }
        path = string.IsNullOrEmpty(path) ? m.Ui!.Page : path;
        var uiRoot = Path.GetFullPath(Path.Combine(FolderOf(name), "ui")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(uiRoot, path));
        if (!full.StartsWith(uiRoot, StringComparison.Ordinal) || !File.Exists(full)) return null;
        if (new FileInfo(full).LinkTarget != null) return null; // no symbolic links out of the folder
        var ct = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8", ".js" or ".mjs" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8", ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".txt" => "text/plain; charset=utf-8", _ => "application/octet-stream"
        };
        return (File.ReadAllBytes(full), ct);
    }

    /// <summary>Console commands the running plugins added (name, help, plugin).</summary>
    public IReadOnlyList<(string Name, string Help, string Plugin)> Commands()
    {
        lock (_gate) return _loaded.Values.SelectMany(l => l.CommandHelp().Select(c => (Name: c.Name, Help: c.Help, Plugin: l.Manifest.Name))).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Dispose()
    {
        lock (_gate) { foreach (var l in _loaded.Values.ToList()) Unload(l, graceful: true); _started = false; }
    }

    // ------------------------------------------------------------------ one loaded plugin

    private sealed class PluginLoadContext(string path) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(path), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name != null && name.Name.StartsWith("OpenVP.", StringComparison.Ordinal)) return null; // the host's own copy, so the interface types match
            var p = _resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
            return File.Exists(p) ? LoadFromAssemblyPath(p) : null;
        }
    }

    private sealed class Loaded : IDisposable
    {
        private readonly PluginHost _host;
        private readonly Channel<(string What, Action Work)> _queue = Channel.CreateBounded<(string, Action)>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private readonly ConcurrentDictionary<string, ConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Func<string, string>> _handlers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Method, string Path, Func<PluginRequest, PluginResponse> Handler)> _routes = new();
        private readonly Dictionary<int, Action<TraceEvent>> _subs = new();
        private readonly Queue<string> _log = new();
        private readonly CancellationTokenSource _cts = new();
        private PluginLoadContext? _alc;
        private Thread? _thread;
        private long _busySince;       // Environment.TickCount64 while a call runs, 0 otherwise
        private string _busyWhat = "";
        private Timer? _watchdog;
        private int _shut;

        public PluginManifest Manifest { get; }
        public IOpenVpPlugin? Instance { get; private set; }
        public Ctx Context { get; }
        public bool Faulted => _shut != 0;

        public Loaded(PluginHost host, PluginManifest m) { _host = host; Manifest = m; Context = new Ctx(this); }

        public void Load()
        {
            Directory.CreateDirectory(Path.Combine(_host._dataRoot, Manifest.Name));
            if (Manifest.Assembly != null)
            {
                var dll = Path.GetFullPath(Path.Combine(_host.FolderOf(Manifest.Name), Manifest.Assembly));
                if (!File.Exists(dll)) throw new FileNotFoundException($"{Manifest.Assembly} isn't in the plugin's folder.");
                _alc = new PluginLoadContext(dll);
                var asm = _alc.LoadFromAssemblyPath(dll);
                Type? type = Manifest.Entry != null ? asm.GetType(Manifest.Entry, throwOnError: false)
                    : asm.GetExportedTypes().SingleOrDefault(t => typeof(IOpenVpPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (type == null || !typeof(IOpenVpPlugin).IsAssignableFrom(type)) throw new InvalidOperationException("No public class implementing IOpenVpPlugin was found" + (Manifest.Entry != null ? $" with the name {Manifest.Entry}" : " (or there is more than one: name it in plugin.json as entry)") + ".");
                Instance = (IOpenVpPlugin)Activator.CreateInstance(type)!;
            }
            else if (_host._inProcess.TryGetValue(Manifest.Name, out var f)) Instance = f();
            else Instance = new UiOnlyPlugin();
            _thread = new Thread(Pump) { IsBackground = true, Name = "plugin " + Manifest.Name };
            _thread.Start();
            _watchdog = new Timer(_ => Watch(), null, 1000, 1000);
        }

        private sealed class UiOnlyPlugin : IOpenVpPlugin { public void Initialize(IPluginContext context) { } }

        private void Pump()
        {
            try
            {
                while (_queue.Reader.WaitToReadAsync(_cts.Token).AsTask().GetAwaiter().GetResult())
                    while (_queue.Reader.TryRead(out var item))
                    {
                        if (_shut != 0) return;
                        Run(item.What, item.Work);
                    }
            }
            catch (OperationCanceledException) { }
        }

        private void Run(string what, Action work)
        {
            _busyWhat = what; Volatile.Write(ref _busySince, Environment.TickCount64);
            try { work(); }
            catch (Exception e) { _host.Fault(Manifest.Name, what, e); }
            finally { Volatile.Write(ref _busySince, 0); }
        }

        private void Watch()
        {
            var since = Volatile.Read(ref _busySince);
            if (since != 0 && Environment.TickCount64 - since > CallTimeout.TotalMilliseconds && _shut == 0)
                _host.Fault(Manifest.Name, _busyWhat, new TimeoutException($"didn't return in {CallTimeout.TotalSeconds:0} seconds"));
        }

        /// <summary>Queue work for the plugin's thread. Never blocks the caller.</summary>
        public void Post(string what, Action work) { if (_shut == 0) _queue.Writer.TryWrite((what, work)); }

        /// <summary>Runs work on the plugin's thread and waits for the answer (at most the call timeout). A failure faults the plugin and returns <paramref name="onFail"/>.</summary>
        public T Call<T>(string what, Func<T> work, T onFail)
        {
            if (_shut != 0) return onFail;
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Writer.TryWrite((what, () =>
            {
                try { tcs.TrySetResult(work()); }
                catch { tcs.TrySetResult(onFail); throw; }
            }));
            try { if (tcs.Task.Wait(CallTimeout)) return tcs.Task.Result; }
            catch (AggregateException) { }
            if (_shut == 0) _host.Fault(Manifest.Name, what, new TimeoutException($"didn't answer in {CallTimeout.TotalSeconds:0} seconds"));
            return onFail;
        }

        public string RunCommand(string name, string args)
        {
            if (!_handlers.TryGetValue(name, out var h)) return "";
            return Call("console command " + name, () => h(args) ?? "", $"The plugin {Manifest.Name} failed and was disabled.");
        }

        public string[] CommandNames() => _commands.Keys.Order().ToArray();
        public IEnumerable<(string Name, string Help)> CommandHelp() => _commands.Values.Select(c => (c.Name, c.Help));
        public string[] RouteNames() { lock (_routes) return _routes.Select(r => $"{r.Method} {r.Path}").ToArray(); }
        public int[] Slots() { lock (_subs) return _subs.Keys.Order().ToArray(); }
        public string[] LogTail() { lock (_log) return _log.ToArray(); }

        public Func<PluginRequest, PluginResponse>? FindRoute(string method, string path)
        {
            lock (_routes) return _routes.FirstOrDefault(r => r.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && r.Path.Equals(path.Trim('/'), StringComparison.OrdinalIgnoreCase)).Handler;
        }

        public void AddLog(string level, string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {level} {message}";
            lock (_log) { _log.Enqueue(line.Length > 400 ? line[..400] : line); while (_log.Count > 30) _log.Dequeue(); }
            Console.WriteLine($"[plugin {Manifest.Name}] {level}: {message}");
        }

        public void Shutdown(bool graceful)
        {
            if (Interlocked.Exchange(ref _shut, 1) != 0) return;
            foreach (var (slot, h) in _subs.ToList()) { try { _host._processor.Slot(slot).Traced -= h; } catch { } }
            lock (_subs) _subs.Clear();
            foreach (var n in _commands.Keys.ToList()) _host._processor.ExtraConsoleCommands.TryRemove(n, out _);
            _commands.Clear(); _handlers.Clear();
            lock (_routes) _routes.Clear();
            _watchdog?.Dispose();
            if (graceful && Instance != null)
            {
                // Give OnStopped a short turn on its own thread; a plugin that hangs here is abandoned.
                var done = new ManualResetEventSlim();
                var t = new Thread(() => { try { Instance.OnStopped(); } catch (Exception e) { AddLog("Error", "OnStopped failed: " + e.Message); } finally { done.Set(); } }) { IsBackground = true, Name = "plugin stop " + Manifest.Name };
                t.Start();
                done.Wait(TimeSpan.FromSeconds(3));
            }
            _cts.Cancel();
            _queue.Writer.TryComplete();
            Instance = null;
            if (_alc != null) { try { _alc.Unload(); } catch { } _alc = null; }
        }

        public void Dispose() => Shutdown(graceful: false);

        // ---- the context a plugin sees

        public sealed class Ctx(Loaded l) : IPluginContext
        {
            private void Need(string perm) { if (!l.Manifest.Permissions.Contains(perm)) throw new PluginPermissionException(perm); }
            public string Name => l.Manifest.Name;
            public string DataDirectory => Path.Combine(l._host._dataRoot, l.Manifest.Name);
            public int SlotCount => l._host._processor.Slots.Count;
            public void Log(string level, string message) => l.AddLog(level, message);

            public void SubscribeSlot(int slot)
            {
                Need(PluginPermissions.SignalsRead);
                var s = l._host._processor.Slot(slot);
                lock (l._subs)
                {
                    if (l._subs.ContainsKey(slot)) return;
                    Action<TraceEvent> h = e => l.Post("OnSignalChanged", () => l.Instance?.OnSignalChanged(new PluginSignalChange(e.Slot, e.Signal, e.Kind.ToString().ToLowerInvariant(), e.Value, e.Time)));
                    l._subs[slot] = h;
                    s.Traced += h;
                }
            }

            public IReadOnlyList<PluginSignal> GetSignals(int slot)
            {
                Need(PluginPermissions.SignalsRead);
                return (l._host._processor.Slot(slot).Runtime?.GetSignals() ?? Array.Empty<SignalInfo>()).Select(s => new PluginSignal(s.Name, s.Kind.ToString().ToLowerInvariant(), s.Value)).ToList();
            }

            public bool SetSignal(int slot, string name, object value)
            {
                Need(PluginPermissions.SignalsWrite);
                return l._host._processor.Slot(slot).Runtime?.TrySetSignal(name, value) ?? false;
            }

            public void AddConsoleCommand(string name, string help, Func<string, string> handler)
            {
                Need(PluginPermissions.Console);
                if (!Regex.IsMatch(name ?? "", "^[A-Za-z][A-Za-z0-9_-]{0,31}$")) throw new ArgumentException("A console command name is letters, digits, - or _.");
                var cmd = new ConsoleCommand(name!, help ?? "", (args, reply) => reply(l.RunCommand(name!, args))); // runs on the plugin's thread, with the call timeout
                if (!l._host._processor.ExtraConsoleCommands.TryAdd(name!, cmd)) throw new InvalidOperationException($"The console command {name} already exists.");
                l._handlers[name!] = handler;
                l._commands[name!] = cmd;
            }

            public void AddRoute(string method, string path, Func<PluginRequest, PluginResponse> handler)
            {
                Need(PluginPermissions.Routes);
                method = (method ?? "").ToUpperInvariant();
                if (method is not ("GET" or "POST" or "PUT" or "DELETE")) throw new ArgumentException("A route method is GET, POST, PUT or DELETE.");
                path = (path ?? "").Trim('/');
                if (path.Split('/').Any(seg => seg is ".." or "." || seg.Length == 0) && path.Length > 0) throw new ArgumentException("Bad route path.");
                if (path == "ui" || path.StartsWith("ui/")) throw new ArgumentException("ui/ is the plugin's page.");
                lock (l._routes) l._routes.Add((method, path, handler));
            }
        }
    }
}
