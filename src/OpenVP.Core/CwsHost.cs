namespace OpenVP.Core;

/// <summary>One HTTP request for a program's web-scripting (CWS) server, as the console received it.</summary>
/// <param name="Path">The path after <c>/cws/{slot}/</c>, no leading slash and no query.</param>
public sealed record CwsRequest(string Method, string Path, string QueryString, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body, string RemoteAddress, string Scheme, string Host);

/// <summary>What a CWS server answers with.</summary>
public sealed class CwsReply
{
    public int Status { get; set; } = 200;
    public string? StatusDescription { get; set; }
    public string? ContentType { get; set; }
    public List<KeyValuePair<string, string>> Headers { get; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();

    public static CwsReply Of(int status, string text) => new() { Status = status, ContentType = "text/plain; charset=utf-8", Body = System.Text.Encoding.UTF8.GetBytes(text) };
}

/// <summary>
/// The web-scripting servers (<c>HttpCwsServer</c>) a slot's SIMPL# program has registered. The console's <c>/cws/{slot}/...</c>
/// endpoint hands each request to the one whose path is the longest prefix of the request path. The program's API shim
/// registers here; nothing in this class knows about the program.
/// </summary>
public sealed class CwsHost
{
    private readonly object _gate = new();
    private readonly List<Entry> _servers = new();

    private sealed class Entry(string path, Func<CwsRequest, string, CwsReply> handler) { public string Path = path; public Func<CwsRequest, string, CwsReply> Handler = handler; }

    /// <summary>Paths of the registered servers (what is served under <c>/cws/{slot}/</c>), shortest first.</summary>
    public IReadOnlyList<string> Paths { get { lock (_gate) return _servers.Select(s => s.Path).Order().ToList(); } }

    public bool Any { get { lock (_gate) return _servers.Count > 0; } }

    /// <summary>Serve <paramref name="path"/> (no slashes at the ends; empty for the root). The handler gets the request and the part of its path after <paramref name="path"/>.
    /// Dispose the result to stop. A second registration of the same path replaces the first.</summary>
    public IDisposable Register(string path, Func<CwsRequest, string, CwsReply> handler)
    {
        path = (path ?? "").Trim('/');
        var e = new Entry(path, handler);
        lock (_gate)
        {
            _servers.RemoveAll(s => s.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            _servers.Add(e);
        }
        return new Registration(this, e);
    }

    private sealed class Registration(CwsHost host, Entry e) : IDisposable
    {
        public void Dispose() { lock (host._gate) host._servers.Remove(e); }
    }

    public void Clear() { lock (_gate) _servers.Clear(); }

    /// <summary>Runs the request through the matching server. Null when no server matches (the caller answers 404).</summary>
    public CwsReply? Handle(CwsRequest req)
    {
        Entry? hit;
        string sub;
        var path = req.Path.Trim('/');
        lock (_gate)
        {
            hit = _servers.OrderByDescending(s => s.Path.Length)
                .FirstOrDefault(s => s.Path.Length == 0 || path.Equals(s.Path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(s.Path + "/", StringComparison.OrdinalIgnoreCase));
            sub = hit == null ? "" : path[Math.Min(path.Length, hit.Path.Length)..].Trim('/');
        }
        return hit?.Handler(req, sub);
    }
}
