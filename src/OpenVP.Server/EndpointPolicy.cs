using Microsoft.AspNetCore.Routing;
using OpenVP.Core.Security;

namespace OpenVP.Server;

/// <summary>
/// What a request to one endpoint needs. <see cref="Kind"/> is <c>public</c> (no sign-in), <c>perm</c> (needs <see cref="Perm"/>), <c>panel</c> (open unless
/// <c>OpenVP:PanelsRequireKey</c>, then needs <see cref="Perm.Signal"/>), <c>metrics</c> (open with <c>OpenVP:MetricsPublic</c>, else needs <see cref="Perm.View"/>)
/// or <c>cws</c> (a program's web-scripting server: open when that slot's "CWS is public" setting is on, else needs <see cref="Perm.Signal"/>).
/// </summary>
/// <param name="Locks">A locked slot refuses it (unless the caller is an Admin or made the lock).</param>
/// <param name="AllSlots">Not for an account limited to some slots.</param>
public sealed record Policy(string Kind, Perm Perm, bool Locks = false, bool AllSlots = false)
{
    public static readonly Policy Open = new("public", Perm.View);
    public static Policy Needs(Perm p, bool locks = false, bool allSlots = false) => new("perm", p, locks, allSlots);
}

/// <summary>
/// The central permission map. Every endpoint and WebSocket the server registers has one line here: the HTTP method (or <c>*</c> for a WebSocket or a
/// catch-all), its route pattern exactly as registered, and what it needs. A request to an endpoint with no line is refused for everyone but an Admin, and
/// a test lists every registered endpoint and fails if any has no line, so a new endpoint can't be left open by accident.
/// Line format: <c>needs METHOD /pattern [+lock] [+all]</c>, where <c>needs</c> is <c>public</c>, <c>panel</c>, <c>metrics</c> or a <see cref="Perm"/> name.
/// </summary>
public static class EndpointPolicy
{
    private static readonly Dictionary<string, Policy> Map = Parse(PolicyTable.Text);

    public static IReadOnlyDictionary<string, Policy> Table => Map;

    public static string KeyOf(string method, string pattern) => method.ToUpperInvariant() + " " + pattern;

    /// <summary>The policy for a registered endpoint, or null if it has none (which the server treats as Admin only and the test treats as a bug).</summary>
    public static Policy? For(string method, string pattern)
    {
        if (Map.TryGetValue(KeyOf(method, pattern), out var p)) return p;
        return Map.TryGetValue(KeyOf("*", pattern), out p) ? p : null;
    }

    public static Policy? For(string method, RouteEndpoint e) => For(method, e.RoutePattern.RawText ?? "");

    private static Dictionary<string, Policy> Parse(string text)
    {
        var d = new Dictionary<string, Policy>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) throw new InvalidOperationException("Bad policy line: " + line);
            var flags = parts.Skip(3).ToHashSet();
            var kind = parts[0].ToLowerInvariant();
            Policy pol = kind switch
            {
                "public" => Policy.Open,
                "panel" => new Policy("panel", Perm.Signal),
                "metrics" => new Policy("metrics", Perm.View),
                "cws" => new Policy("cws", Perm.Signal),
                _ => Enum.TryParse<Perm>(parts[0], true, out var perm) ? Policy.Needs(perm, flags.Contains("+lock"), flags.Contains("+all")) : throw new InvalidOperationException("Bad permission in policy line: " + line)
            };
            var key = KeyOf(parts[1], parts[2]);
            if (!d.TryAdd(key, pol)) throw new InvalidOperationException("Duplicate policy line: " + key);
        }
        return d;
    }
}
