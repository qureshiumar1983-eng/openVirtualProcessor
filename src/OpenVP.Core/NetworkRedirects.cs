using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// One redirect: when the program connects to <see cref="From"/>, connect to <see cref="To"/> instead.
/// <c>From</c> is <c>host:port</c> or just <c>host</c> (any port). <c>To</c> is <c>host:port</c>, or <c>sim:ID</c> for a
/// device simulator that listens on a local port.
/// </summary>
public sealed class RedirectEntry
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    /// <summary><c>tcp</c>, <c>udp</c> or <c>any</c>.</summary>
    public string Proto { get; set; } = "any";
    public bool Enabled { get; set; } = true;
    public string Note { get; set; } = "";
}

/// <summary>The result of looking an address up: where to really connect, and why.</summary>
public sealed record RedirectResult(string Host, int Port, RedirectEntry? Entry)
{
    public bool Redirected => Entry != null;
}

/// <summary>
/// Per-slot map of "network redirects", so a program that connects to a real device's address (say 192.168.1.50:23)
/// can be pointed at a simulator, without changing the program. The SIMPL# and SIMPL+ socket code asks <see cref="Resolve"/>
/// before it connects. Saved in <c>devices/redirects.json</c> in the slot's folder.
/// </summary>
public sealed class NetworkRedirects
{
    private readonly object _gate = new();
    private readonly List<RedirectEntry> _entries = new();
    private readonly string _file;

    public NetworkRedirects(string file)
    {
        _file = file;
        Load();
    }

    /// <summary>Turns <c>sim:ID</c> into a local endpoint (host, port); null if that simulator isn't listening.</summary>
    public Func<string, (string Host, int Port)?>? SimResolver { get; set; }

    public IReadOnlyList<RedirectEntry> List { get { lock (_gate) return _entries.Select(Copy).ToList(); } }

    private static RedirectEntry Copy(RedirectEntry e) => new() { Id = e.Id, From = e.From, To = e.To, Proto = e.Proto, Enabled = e.Enabled, Note = e.Note };

    public static (string Host, int? Port) ParseEndpoint(string s)
    {
        s = s.Trim();
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close > 0) return (s[1..close], close + 2 < s.Length && int.TryParse(s[(close + 2)..], out var p6) ? p6 : null);
        }
        var colon = s.LastIndexOf(':');
        if (colon > 0 && s.IndexOf(':') == colon && int.TryParse(s[(colon + 1)..], out var p)) return (s[..colon], p);
        return (s, null);
    }

    public static void Validate(RedirectEntry e)
    {
        e.From = e.From.Trim(); e.To = e.To.Trim(); e.Proto = (e.Proto ?? "any").Trim().ToLowerInvariant();
        if (e.Proto is not ("any" or "tcp" or "udp")) throw new ArgumentException("Protocol must be tcp, udp or any.");
        if (e.From.Length == 0 || e.From.Length > 200) throw new ArgumentException("\"Program connects to\" needs an address, like 192.168.1.50:23.");
        var (fh, fp) = ParseEndpoint(e.From);
        if (fh.Length == 0 || (fp is { } p && (p < 1 || p > 65535))) throw new ArgumentException("\"Program connects to\" must be host or host:port.");
        if (e.To.Length == 0 || e.To.Length > 200) throw new ArgumentException("\"Send it to\" needs a simulator or an address.");
        if (e.To.StartsWith("sim:", StringComparison.OrdinalIgnoreCase))
        {
            if (e.To.Length <= 4) throw new ArgumentException("Name the simulator after sim:.");
        }
        else
        {
            var (th, tp) = ParseEndpoint(e.To);
            if (th.Length == 0 || tp is null or < 1 or > 65535) throw new ArgumentException("\"Send it to\" must be a simulator or host:port.");
        }
        if (e.Note.Length > 200) e.Note = e.Note[..200];
    }

    public RedirectEntry Add(RedirectEntry e)
    {
        Validate(e);
        lock (_gate)
        {
            if (_entries.Count >= 100) throw new ArgumentException("At most 100 redirects per slot.");
            e.Id = Guid.NewGuid().ToString("N")[..8];
            _entries.Add(Copy(e));
            Save();
        }
        return e;
    }

    public RedirectEntry? Update(string id, RedirectEntry e)
    {
        Validate(e);
        lock (_gate)
        {
            var i = _entries.FindIndex(x => x.Id == id);
            if (i < 0) return null;
            e.Id = id;
            _entries[i] = Copy(e);
            Save();
            return e;
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(x => x.Id == id) == 0) return false;
            Save();
            return true;
        }
    }

    /// <summary>Where should a connection to host:port really go? Entries are tried most specific first (a port beats "any port").</summary>
    public RedirectResult Resolve(string host, int port, string proto = "tcp")
    {
        host = (host ?? "").Trim();
        List<RedirectEntry> candidates;
        lock (_gate) candidates = _entries.Where(e => e.Enabled).Select(Copy).ToList();
        foreach (var e in candidates
            .Where(e => e.Proto == "any" || e.Proto == proto)
            .Select(e => (e, f: ParseEndpoint(e.From)))
            .Where(x => (x.f.Host == "*" || string.Equals(x.f.Host, host, StringComparison.OrdinalIgnoreCase)) && (x.f.Port == null || x.f.Port == port))
            .OrderByDescending(x => (x.f.Port != null ? 2 : 0) + (x.f.Host != "*" ? 1 : 0))
            .Select(x => x.e))
        {
            if (e.To.StartsWith("sim:", StringComparison.OrdinalIgnoreCase))
            {
                var ep = SimResolver?.Invoke(e.To[4..].Trim());
                if (ep is { } r) return new RedirectResult(r.Host, r.Port, e);
                continue; // that simulator isn't listening: connect normally
            }
            var (th, tp) = ParseEndpoint(e.To);
            return new RedirectResult(th, tp ?? port, e);
        }
        return new RedirectResult(host, port, null);
    }

    // ---------------------------------------------------------------- saving

    private sealed class FileShape { public List<RedirectEntry> Redirects { get; set; } = new(); }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(new FileShape { Redirects = _entries }, SlotIo.Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Couldn't save the redirects: {e.Message}", e);
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            _entries.Clear();
            try
            {
                if (!File.Exists(_file)) return;
                var f = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_file), SlotIo.Json);
                foreach (var e in f?.Redirects ?? new())
                {
                    try { Validate(e); if (e.Id.Length == 0) e.Id = Guid.NewGuid().ToString("N")[..8]; _entries.Add(e); } catch (ArgumentException) { }
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        }
    }
}
