using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>
/// One reply rule of a device simulator: "when the program sends something that matches <see cref="Match"/>, wait
/// <see cref="DelayMs"/>, send <see cref="Reply"/> and set these state values".
/// Text in <see cref="Match"/> (literal mode) and <see cref="Reply"/> may use \r \n \t \0 \\ and \xNN.
/// In a reply, <c>{1}</c> is a capture group of a regex match and <c>{name}</c> (or <c>{name:X2}</c>) is a state value.
/// </summary>
public sealed class SimRule
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public bool Regex { get; set; }
    public bool IgnoreCase { get; set; }
    public string Reply { get; set; } = "";
    public int DelayMs { get; set; }
    /// <summary>State changes, applied when the rule fires. Keys and values may use <c>{1}</c> captures and <c>{state}</c> values;
    /// a value like <c>{vol}+5</c> is worked out.</summary>
    public Dictionary<string, string> Set { get; set; } = new();
}

/// <summary>A message the simulator sends on its own, every <see cref="EveryMs"/> milliseconds while it runs.</summary>
public sealed class SimTimer
{
    public string Name { get; set; } = "";
    public int EveryMs { get; set; } = 1000;
    public string Text { get; set; } = "";
}

/// <summary>A device simulator: what it listens on, its state and its rules. Saved per slot (see <see cref="SlotSimulators"/>).</summary>
public sealed class SimDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The generic profile this started from (<c>projector</c>, <c>display</c>, <c>dsp</c>, <c>switcher</c>), or empty.</summary>
    public string Profile { get; set; } = "";
    /// <summary><c>com</c> (a COM port of the program), <c>tcp</c> or <c>udp</c> (listens on a local port the program connects to).</summary>
    public string Kind { get; set; } = "com";
    /// <summary>For <c>com</c>: the port's path as in comports.json, e.g. <c>IP-ID-11/Slot-08/COM-01</c> (or <c>COM-01</c>).</summary>
    public string Com { get; set; } = "";
    /// <summary>For <c>tcp</c>/<c>udp</c>: the local port (127.0.0.1) to listen on; 0 picks a free one.</summary>
    public int Port { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> State { get; set; } = new();
    public List<SimRule> Rules { get; set; } = new();
    public List<SimTimer> Unsolicited { get; set; } = new();
    /// <summary>Sent when a TCP client connects, or when the program opens the COM port.</summary>
    public List<string> OnConnect { get; set; } = new();

    public SimDef Clone() => System.Text.Json.JsonSerializer.Deserialize<SimDef>(System.Text.Json.JsonSerializer.Serialize(this))!;
}

/// <summary>Text with escapes: <c>\r \n \t \0 \\ \xNN</c>.</summary>
public static class SimText
{
    public static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            var n = s[++i];
            switch (n)
            {
                case 'r': sb.Append('\r'); break;
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case '0': sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                case 'x' when IsHex(s, i + 1) && IsHex(s, i + 2):
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 2), 16)); i += 2; break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }

    private static bool IsHex(string s, int i) => i < s.Length && Uri.IsHexDigit(s[i]);

    /// <summary>Control characters as \r, \n and \xNN, so a CR-terminated command is readable.</summary>
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (c < 0x20 || c > 0x7E) sb.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Bytes as hex, 16 to a group of two digits: <c>50 57 52 0D</c>.</summary>
    public static string Hex(string s) => string.Join(' ', s.Select(c => ((int)c & 0xFF).ToString("X2", CultureInfo.InvariantCulture)));
}

/// <summary>Timers for the simulator engine, replaceable in tests.</summary>
public interface ISimClock
{
    IDisposable After(int milliseconds, Action action);
    IDisposable Every(int milliseconds, Action action);
}

public sealed class RealSimClock : ISimClock
{
    public static readonly RealSimClock Instance = new();

    public IDisposable After(int milliseconds, Action action)
    {
        Timer? t = null;
        t = new Timer(_ => { try { action(); } catch { } finally { t?.Dispose(); } }, null, Math.Max(0, milliseconds), Timeout.Infinite);
        return t;
    }

    public IDisposable Every(int milliseconds, Action action) =>
        new Timer(_ => { try { action(); } catch { } }, null, Math.Max(10, milliseconds), Math.Max(10, milliseconds));
}

/// <summary>A clock that runs delayed actions at once and remembers the delay: for trying a message and for tests.</summary>
public sealed class ImmediateSimClock : ISimClock
{
    public int LastDelay { get; private set; }
    public IDisposable After(int milliseconds, Action action) { LastDelay = milliseconds; action(); return Noop.Instance; }
    public IDisposable Every(int milliseconds, Action action) => Noop.Instance;
    private sealed class Noop : IDisposable { public static readonly Noop Instance = new(); public void Dispose() { } }
}

/// <summary>One line in a simulator's activity list.</summary>
public sealed record SimLogEntry(DateTimeOffset Time, string Kind, string Text);

/// <summary>
/// The reply engine behind a device simulator. Bytes (as text) from the program go in with <see cref="Receive"/>; replies,
/// timer messages and connect messages come out through the send callback. It holds the device's state. It touches no
/// sockets or ports, so it is tested on its own; <see cref="SimHost"/> attaches it to a COM port or a socket.
/// </summary>
public sealed class SimEngine : IDisposable
{
    private const int MaxBuffer = 4096;
    private readonly object _gate = new();
    private readonly ISimClock _clock;
    private readonly Action<int, string> _send;
    private readonly Dictionary<string, string> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, StringBuilder> _buffers = new();
    private readonly List<IDisposable> _timers = new();
    private readonly HashSet<IDisposable> _pending = new();
    private bool _running;
    private readonly List<(SimRule Rule, Regex? Rx, string Literal)> _rules = new();
    private readonly Queue<SimLogEntry> _log = new();
    private SimDef _def;
    private bool _disposed;

    /// <param name="send">Called with (connection, text). Connection 0 means "everyone / the COM port".</param>
    public SimEngine(SimDef def, Action<int, string> send, ISimClock? clock = null)
    {
        _def = def;
        _send = send;
        _clock = clock ?? RealSimClock.Instance;
        foreach (var kv in def.State) _state[kv.Key] = kv.Value;
        CompileRules(def);
    }

    public SimDef Definition => _def;
    public long Matched { get; private set; }
    /// <summary>The delay of the rule that fired last (what a reply sent right now was waiting for).</summary>
    public int LastReplyDelayMs { get; private set; }
    public long Unmatched { get; private set; }

    /// <summary>Raised (outside the lock) whenever a state value changes.</summary>
    public event Action<string, string>? StateChanged;

    private void CompileRules(SimDef def)
    {
        _rules.Clear();
        foreach (var r in def.Rules)
        {
            if (r.Match.Length == 0) continue;
            if (r.Regex)
                _rules.Add((r, new Regex(r.Match, RegexOptions.CultureInvariant | (r.IgnoreCase ? RegexOptions.IgnoreCase : 0), TimeSpan.FromMilliseconds(250)), ""));
            else
                _rules.Add((r, null, SimText.Unescape(r.Match)));
        }
    }

    /// <summary>Check a definition: every regex compiles, sizes are sane. Throws ArgumentException with the problem.</summary>
    public static void Validate(SimDef def)
    {
        if (def.Name.Length > 80) throw new ArgumentException("The name can be at most 80 characters.");
        if (def.Kind is not ("com" or "tcp" or "udp")) throw new ArgumentException("Kind must be com, tcp or udp.");
        if (def.Port is < 0 or > 65535) throw new ArgumentException("Port must be 0-65535.");
        if (def.Kind == "com" && def.Com.Length > 200) throw new ArgumentException("The COM port name is too long.");
        if (def.Rules.Count > 300) throw new ArgumentException("At most 300 rules per simulator.");
        if (def.Unsolicited.Count > 20) throw new ArgumentException("At most 20 timed messages per simulator.");
        if (def.OnConnect.Count > 20) throw new ArgumentException("At most 20 connect messages per simulator.");
        if (def.State.Count > 500) throw new ArgumentException("At most 500 state values per simulator.");
        var i = 0;
        foreach (var r in def.Rules)
        {
            i++;
            var label = string.IsNullOrWhiteSpace(r.Name) ? $"rule {i}" : $"rule {i} ({r.Name})";
            if (r.Match.Length == 0) throw new ArgumentException($"{label}: \"when received\" is empty.");
            if (r.Match.Length > 500 || r.Reply.Length > 2000) throw new ArgumentException($"{label}: text is too long.");
            if (r.DelayMs is < 0 or > 60000) throw new ArgumentException($"{label}: delay must be 0-60000 ms.");
            if (r.Regex)
            {
                try { _ = new Regex(r.Match, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
                catch (ArgumentException e) { throw new ArgumentException($"{label}: the pattern isn't a valid regular expression ({e.Message})"); }
            }
        }
        foreach (var t in def.Unsolicited)
            if (t.EveryMs is < 50 or > 3_600_000) throw new ArgumentException("A timed message must repeat every 50 ms to 1 hour.");
    }

    /// <summary>Replace the rules and timers (state is kept) and restart the timers if running.</summary>
    public void Update(SimDef def)
    {
        lock (_gate)
        {
            _def = def;
            CompileRules(def);
            foreach (var kv in def.State) if (!_state.ContainsKey(kv.Key)) _state[kv.Key] = kv.Value;
        }
        bool running; lock (_gate) running = _running;
        if (running) { Stop(); Start(); }
    }

    // ---------------------------------------------------------------- state

    public IReadOnlyDictionary<string, string> State
    {
        get { lock (_gate) return new Dictionary<string, string>(_state, StringComparer.OrdinalIgnoreCase); }
    }

    public string? Get(string key) { lock (_gate) return _state.TryGetValue(key, out var v) ? v : null; }

    public void SetState(string key, string value)
    {
        lock (_gate) _state[key] = value;
        Note("state", $"{key} = {value}");
        StateChanged?.Invoke(key, value);
    }

    /// <summary>Put the state back to the definition's starting values.</summary>
    public void ResetState()
    {
        List<KeyValuePair<string, string>> now;
        lock (_gate) { _state.Clear(); foreach (var kv in _def.State) _state[kv.Key] = kv.Value; now = _state.ToList(); }
        Note("state", "state reset");
        foreach (var kv in now) StateChanged?.Invoke(kv.Key, kv.Value);
    }

    public IReadOnlyList<SimLogEntry> Recent() { lock (_gate) return _log.ToList(); }

    private void Note(string kind, string text)
    {
        lock (_gate)
        {
            _log.Enqueue(new SimLogEntry(DateTimeOffset.Now, kind, text));
            while (_log.Count > 100) _log.Dequeue();
        }
    }

    // ---------------------------------------------------------------- run

    /// <summary>Start the timed (unsolicited) messages.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _running) return;
            _running = true;
            foreach (var t in _def.Unsolicited.ToList())
            {
                if (t.Text.Length == 0) continue;
                var text = t.Text;
                _timers.Add(_clock.Every(Math.Max(50, t.EveryMs), () =>
                {
                    var rendered = Render(text, null);
                    Note("timer", $"sent {SimText.Escape(rendered)}");
                    _send(0, rendered);
                }));
            }
        }
    }

    public void Stop()
    {
        List<IDisposable> timers;
        lock (_gate) { timers = _timers.Concat(_pending).ToList(); _timers.Clear(); _pending.Clear(); _running = false; }
        foreach (var t in timers) t.Dispose();
    }

    /// <summary>A client connected (or the program opened the COM port): send the connect messages.</summary>
    public void Connected(int connection)
    {
        string[] msgs;
        lock (_gate) { msgs = _def.OnConnect.ToArray(); _buffers[connection] = new StringBuilder(); }
        foreach (var m in msgs)
        {
            if (m.Length == 0) continue;
            var text = Render(m, null);
            Note("connect", $"sent {SimText.Escape(text)}");
            _send(connection, text);
        }
    }

    public void Disconnected(int connection) { lock (_gate) _buffers.Remove(connection); }

    /// <summary>Text arrived from the program. Rules are tried against what has arrived so far; a match uses it up.</summary>
    public void Receive(int connection, string text)
    {
        if (text.Length == 0) return;
        Note("rx", SimText.Escape(text));
        var fired = new List<(SimRule rule, Match? m)>();
        lock (_gate)
        {
            if (!_buffers.TryGetValue(connection, out var buf)) _buffers[connection] = buf = new StringBuilder();
            buf.Append(text);
            for (var guard = 0; guard < 64 && buf.Length > 0; guard++)
            {
                var s = buf.ToString();
                var bestIndex = int.MaxValue; var bestEnd = 0; SimRule? best = null; Match? bestMatch = null;
                foreach (var (rule, rx, literal) in _rules)
                {
                    int idx, end; Match? m = null;
                    if (rx != null)
                    {
                        try { m = rx.Match(s); } catch (RegexMatchTimeoutException) { continue; }
                        if (!m.Success) continue;
                        idx = m.Index; end = m.Index + m.Length;
                        if (m.Length == 0) continue;
                    }
                    else
                    {
                        idx = s.IndexOf(literal, rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                        if (idx < 0) continue;
                        end = idx + literal.Length;
                    }
                    if (idx < bestIndex) { bestIndex = idx; bestEnd = end; best = rule; bestMatch = m; }
                }
                if (best == null) break;
                buf.Remove(0, bestEnd);
                fired.Add((best, bestMatch));
                Matched++;
            }
            if (buf.Length > MaxBuffer) buf.Remove(0, buf.Length - MaxBuffer);
            if (fired.Count == 0 && buf.Length > 0) Unmatched++;
        }
        foreach (var (rule, m) in fired) Fire(connection, rule, m);
    }

    private void Fire(int connection, SimRule rule, Match? m)
    {
        var label = string.IsNullOrWhiteSpace(rule.Name) ? SimText.Escape(rule.Match) : rule.Name;
        LastReplyDelayMs = rule.DelayMs;
        var changed = new List<(string, string)>();
        lock (_gate)
        {
            foreach (var (k, v) in rule.Set)
            {
                var key = ExpandCaptures(k, m);
                var value = Arithmetic(Render(v, m));
                if (key.Length == 0) continue;
                _state[key] = value;
                changed.Add((key, value));
            }
        }
        Note("rule", $"matched {label}" + (changed.Count > 0 ? "; set " + string.Join(", ", changed.Select(c => $"{c.Item1}={c.Item2}")) : ""));
        foreach (var (k, v) in changed) StateChanged?.Invoke(k, v);
        if (rule.Reply.Length == 0) return;
        void Send()
        {
            if (_disposed) return;
            var text = Render(rule.Reply, m);
            Note("tx", SimText.Escape(text));
            _send(connection, text);
        }
        if (rule.DelayMs <= 0) Send();
        else
        {
            IDisposable? handle = null;
            handle = _clock.After(rule.DelayMs, () => { lock (_gate) { if (handle != null) _pending.Remove(handle); } Send(); });
            lock (_gate) _pending.Add(handle);
        }
    }

    // ---------------------------------------------------------------- templates

    private static readonly Regex CaptureRef = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex StateRef = new(@"\{([A-Za-z_][\w.\-]*)(?::([^{}]*))?\}", RegexOptions.Compiled);

    private static string ExpandCaptures(string s, Match? m)
    {
        s = SimText.Unescape(s);
        if (m == null) return s;
        s = CaptureRef.Replace(s, x =>
        {
            var g = m.Groups[int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)];
            return g.Success ? g.Value : x.Value;
        });
        return s;
    }

    /// <summary>Fill in escapes, <c>{1}</c> captures and <c>{state}</c> values. Anything it can't fill is left as written.</summary>
    public string Render(string template, Match? m)
    {
        var s = ExpandCaptures(template, m);
        lock (_gate)
        {
            return StateRef.Replace(s, x =>
            {
                var name = x.Groups[1].Value;
                if (m != null && m.Groups[name] is { Success: true } g && !char.IsDigit(name[0])) return Format(g.Value, x.Groups[2].Value);
                return _state.TryGetValue(name, out var v) ? Format(v, x.Groups[2].Value) : x.Value;
            });
        }
    }

    private static string Format(string value, string format)
    {
        if (format.Length == 0) return value;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n.ToString(format, CultureInfo.InvariantCulture) : value;
    }

    private static readonly Regex Sum = new(@"^\s*(-?\d+)\s*([+\-*])\s*(-?\d+)\s*$", RegexOptions.Compiled);

    private static string Arithmetic(string v)
    {
        var m = Sum.Match(v);
        if (!m.Success) return v;
        var a = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var b = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return (m.Groups[2].Value switch { "+" => a + b, "-" => a - b, _ => a * b }).ToString(CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
