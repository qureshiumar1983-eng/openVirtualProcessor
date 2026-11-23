using System.Globalization;
using System.Text;

namespace OpenVP.Core.Integrations;

/// <summary>
/// Lets integrations (MQTT, webhooks, OSC, Art-Net, the REST stream) hear about changes to chosen signals of one slot.
/// The hub attaches to the slot's trace stream only while someone is subscribed, and looks each change up by name, so a
/// slot with no integrations pays nothing and a slot with a few signals chosen pays one dictionary lookup per change.
/// Handlers run on the slot thread: they must only copy the change somewhere (a bounded queue) and return.
/// </summary>
public sealed class SignalHub : IDisposable
{
    private sealed class Sub(string[]? names, Action<TraceEvent> handler)
    {
        public readonly string[]? Names = names;
        public readonly Action<TraceEvent> Handler = handler;
    }

    private sealed class Routes(Dictionary<string, Action<TraceEvent>[]> byName, Action<TraceEvent>[] all)
    {
        public readonly Dictionary<string, Action<TraceEvent>[]> ByName = byName;
        public readonly Action<TraceEvent>[] All = all;
        public static readonly Routes Empty = new(new Dictionary<string, Action<TraceEvent>[]>(), Array.Empty<Action<TraceEvent>>());
    }

    private readonly ProgramSlot _slot;
    private readonly object _gate = new();
    private readonly List<Sub> _subs = new();
    private volatile Routes _routes = Routes.Empty;
    private readonly Action<TraceEvent> _onTrace;
    private bool _attached;
    private long _errors;

    public SignalHub(ProgramSlot slot) { _slot = slot; _onTrace = OnTrace; }

    /// <summary>How many subscriptions are active (for tests and status).</summary>
    public int SubscriberCount { get { lock (_gate) return _subs.Count; } }

    /// <summary>True while the hub is listening to the slot's trace stream.</summary>
    public bool Attached => _attached;

    /// <summary>Handlers that threw (they are caught so a bad integration can't hurt the program).</summary>
    public long HandlerErrors => Interlocked.Read(ref _errors);

    /// <summary>
    /// Call <paramref name="handler"/> for every change of one of <paramref name="names"/> (or of every signal when null).
    /// Dispose the result to stop. To change the names, dispose and subscribe again.
    /// </summary>
    public IDisposable Subscribe(IEnumerable<string>? names, Action<TraceEvent> handler)
    {
        var sub = new Sub(names?.Distinct(StringComparer.Ordinal).ToArray(), handler);
        lock (_gate)
        {
            _subs.Add(sub);
            Rebuild();
        }
        return new Handle(this, sub);
    }

    private sealed class Handle(SignalHub hub, Sub sub) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            lock (hub._gate) { hub._subs.Remove(sub); hub.Rebuild(); }
        }
    }

    // Called with the lock held.
    private void Rebuild()
    {
        var all = _subs.Where(s => s.Names == null).Select(s => s.Handler).ToArray();
        var by = new Dictionary<string, List<Action<TraceEvent>>>(StringComparer.Ordinal);
        foreach (var s in _subs)
            if (s.Names != null)
                foreach (var n in s.Names)
                {
                    if (!by.TryGetValue(n, out var l)) by[n] = l = new();
                    l.Add(s.Handler);
                }
        _routes = _subs.Count == 0 ? Routes.Empty : new Routes(by.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.Ordinal), all);
        var want = _subs.Count > 0;
        if (want && !_attached) { _attached = true; _slot.Traced += _onTrace; }
        else if (!want && _attached) { _attached = false; _slot.Traced -= _onTrace; }
    }

    private void OnTrace(TraceEvent e)
    {
        var r = _routes;
        foreach (var h in r.All) Call(h, e);
        if (r.ByName.TryGetValue(e.Signal, out var hs))
            foreach (var h in hs) Call(h, e);
    }

    private void Call(Action<TraceEvent> h, TraceEvent e)
    {
        try { h(e); }
        catch { Interlocked.Increment(ref _errors); }
    }

    public void Dispose()
    {
        lock (_gate) { _subs.Clear(); Rebuild(); }
    }
}

/// <summary>How signal values are written and read by the integrations: digital as 1 or 0, analog as 0-65535, serial as text.</summary>
public static class SignalValues
{
    public static string KindName(SigKind k) => k switch { SigKind.Digital => "digital", SigKind.Analog => "analog", _ => "serial" };

    public static bool TryKind(string? name, out SigKind kind)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "digital": kind = SigKind.Digital; return true;
            case "analog": kind = SigKind.Analog; return true;
            case "serial": kind = SigKind.Serial; return true;
        }
        kind = default; return false;
    }

    /// <summary>The text form: digital <c>1</c>/<c>0</c>, analog a number, serial as is.</summary>
    public static string Format(object? v) => v switch
    {
        null => "",
        bool b => b ? "1" : "0",
        string s => s,
        IConvertible c => c.ToString(CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    /// <summary>A value as it goes into JSON: true/false, a whole number, or text.</summary>
    public static object ForJson(SigKind kind, object? v) => kind switch
    {
        SigKind.Digital => v is bool b ? b : Format(v) is "1" or "true" or "True",
        SigKind.Analog => v is bool bb ? (bb ? 1L : 0L) : v is IConvertible c && v is not string ? Convert.ToInt64(c, CultureInfo.InvariantCulture) : long.TryParse(Format(v), out var l) ? l : 0L,
        _ => Format(v)
    };

    /// <summary>True for the word that asks for a pulse (high, then low).</summary>
    public static bool IsPulse(string? text) => string.Equals(text?.Trim(), "pulse", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a value sent by a person or a device for a signal of this kind. Digital: <c>1 0 true false on off</c>.
    /// Analog: a number 0-65535 (anything else is clamped) or a percentage such as <c>50%</c>. Serial: the text as it is.
    /// </summary>
    public static bool TryParse(SigKind kind, string? text, out object value, out string? error)
    {
        error = null;
        text ??= "";
        switch (kind)
        {
            case SigKind.Digital:
                switch (text.Trim().ToLowerInvariant())
                {
                    case "1" or "true" or "on" or "high": value = true; return true;
                    case "0" or "false" or "off" or "low": value = false; return true;
                }
                value = false; error = "A digital signal takes 1, 0, true, false or pulse."; return false;
            case SigKind.Analog:
                var t = text.Trim();
                if (t.EndsWith('%'))
                {
                    if (double.TryParse(t[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) && double.IsFinite(pct))
                    { value = (ushort)Math.Round(Math.Clamp(pct, 0, 100) / 100.0 * 65535); return true; }
                }
                else if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
                { value = (ushort)Math.Round(Math.Clamp(d, 0, 65535)); return true; }
                value = (ushort)0; error = "An analog signal takes a number from 0 to 65535, or a percentage such as 50%."; return false;
            default:
                value = text; return true;
        }
    }

    /// <summary>What <see cref="IProgramRuntime.TrySetSignal"/> is given for a parsed value: a bool, an int, or a string.</summary>
    public static object ForRuntime(object parsed) => parsed is ushort u ? (int)u : parsed;

    /// <summary>
    /// A name that is safe inside an MQTT topic or an OSC address: letters, digits, <c>_ . -</c>; anything else becomes <c>_</c>.
    /// (Topics can't contain <c>+ #</c> and <c>/</c> separates levels; OSC reserves <c># * , ? [ ] { }</c> and spaces.)
    /// </summary>
    public static string Segment(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-' ? c : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    /// <summary>
    /// Assigns each signal name its <see cref="Segment"/>, adding <c>_2</c>, <c>_3</c> to a segment that would clash with an
    /// earlier one, so two signals never share a topic.
    /// </summary>
    public static Dictionary<string, string> Segments(IEnumerable<string> names)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in names)
        {
            if (map.ContainsKey(n)) continue;
            var seg = Segment(n);
            var s = seg; var i = 2;
            while (!used.Add(s)) s = $"{seg}_{i++}";
            map[n] = s;
        }
        return map;
    }
}
