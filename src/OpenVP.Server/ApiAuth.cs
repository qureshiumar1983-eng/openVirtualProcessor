using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OpenVP.Server;

/// <summary>
/// Access key for the web console, REST API and debugger stream. This class holds the key and the rules for "is this request from this machine" and "is it from another site";
/// <see cref="Authz"/> decides who is calling (the key, an API token, a signed-in user, or this computer) and what they may do, and is the middleware.
///
/// Anyone who can call the API can upload a program, and a SIMPL# program is .NET code that runs in this
/// process, so the API needs a key whenever the server is reachable from other machines. The key comes from
/// <c>OpenVP:ApiKey</c>; if that's empty, one is generated on first start and saved as
/// <c>data/apikey.txt</c>. Send it as <c>Authorization: Bearer KEY</c> or <c>X-OpenVP-Key: KEY</c>, or
/// open <c>/?key=KEY</c> once in a browser to get a login cookie.
///
/// Requests from this machine (loopback) don't need the key unless <c>OpenVP:LocalRequiresKey</c> is true,
/// and never when they carry a proxy's forwarding header, since a reverse proxy on this machine would
/// otherwise make every remote user look local. The same goes for Cloudflare's headers (<c>Cf-Connecting-IP</c>,
/// <c>Cf-Ray</c>, <c>CF-Visitor</c>), and for every request that arrives on the tunnel port (see
/// <see cref="TunnelGate"/>), whatever its address or Host header. Panels (<c>/ws/panel</c>, CIP) can only press joins, so they
/// stay open unless <c>OpenVP:PanelsRequireKey</c> is true.
/// </summary>
public sealed class ApiAuth
{
    public const string CookieName = "openvp_key";
    public const string HeaderName = "X-OpenVP-Key";

    private readonly byte[]? _key;

    public ApiAuth(string? key, bool localRequiresKey, bool panelsRequireKey, bool metricsPublic = false)
    {
        MetricsPublic = metricsPublic;
        _key = key is null ? null : Encoding.UTF8.GetBytes(key);
        LocalRequiresKey = localRequiresKey;
        PanelsRequireKey = panelsRequireKey;
    }

    /// <summary>False when auth is switched off with <c>OpenVP:ApiKey=off</c>.</summary>
    public bool Enabled => _key != null;
    public bool LocalRequiresKey { get; }
    /// <summary><c>OpenVP:MetricsPublic=true</c> lets <c>/metrics</c> (Prometheus) be read without the key. It is never served on the tunnel port either way.</summary>
    public bool MetricsPublic { get; }
    public bool PanelsRequireKey { get; }

    /// <summary>Where the key came from, for the startup message.</summary>
    public string Source { get; private init; } = "";
    public string? Key { get; private init; }

    public static ApiAuth FromConfig(IConfiguration cfg, string dataDirectory)
    {
        var configured = cfg["ApiKey"]?.Trim();
        var local = cfg.GetValue("LocalRequiresKey", false);
        var panels = cfg.GetValue("PanelsRequireKey", false);
        var metricsPublic = cfg.GetValue("MetricsPublic", false);
        if (string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase))
            return new ApiAuth(null, local, panels, metricsPublic) { Source = "off" };
        if (!string.IsNullOrEmpty(configured))
            return new ApiAuth(configured, local, panels, metricsPublic) { Source = "OpenVP:ApiKey", Key = configured };

        var file = Path.Combine(dataDirectory, "apikey.txt");
        var key = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        if (key.Length == 0)
        {
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(file, key + "\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return new ApiAuth(key, local, panels, metricsPublic) { Source = file, Key = key };
    }

    public bool Matches(string? candidate)
    {
        if (_key == null) return true;
        if (string.IsNullOrEmpty(candidate)) return false;
        return CryptographicOperations.FixedTimeEquals(_key, Encoding.UTF8.GetBytes(candidate));
    }

    /// <summary>
    /// True if the request is from this machine, addressed to a loopback name, and didn't come through a proxy.
    /// The Host check stops DNS rebinding (an attacker's domain pointed at 127.0.0.1).
    /// </summary>
    public static bool IsDirectLocal(HttpContext ctx)
    {
        // The tunnel's agent (cloudflared) connects from 127.0.0.1 and can send any Host header, so nothing
        // that arrives on the tunnel port is ever "local".
        if (ctx.Items.ContainsKey(TunnelGate.ItemKey)) return false;
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip == null || !IPAddress.IsLoopback(ip)) return false;
        var host = ctx.Request.Host.Host.Trim('[', ']');
        if (!(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var h) && IPAddress.IsLoopback(h))))
            return false;
        var hd = ctx.Request.Headers;
        return !hd.ContainsKey("X-Forwarded-For") && !hd.ContainsKey("Forwarded") && !hd.ContainsKey("X-Real-IP")
            && !hd.ContainsKey("Cf-Connecting-IP") && !hd.ContainsKey("Cf-Ray") && !hd.ContainsKey("CF-Visitor");
    }

    /// <summary>A browser request from a page on another site (blocks cross-site WebSocket hijacking).</summary>
    public static bool IsCrossOrigin(HttpContext ctx)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return false;
        return !Uri.TryCreate(origin, UriKind.Absolute, out var o)
            || !string.Equals(o.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsAuthenticated(HttpContext ctx)
    {
        if (!Enabled) return true;
        if (!LocalRequiresKey && IsDirectLocal(ctx)) return true;
        var h = ctx.Request.Headers;
        var auth = h.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && Matches(auth[7..].Trim())) return true;
        if (Matches(h[HeaderName].ToString())) return true;
        return Matches(ctx.Request.Cookies[CookieName]);
    }

    public void SetCookie(HttpContext ctx, string key) =>
        ctx.Response.Cookies.Append(CookieName, key, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = ctx.Request.IsHttps,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(365),
        });

    public void ClearCookie(HttpContext ctx) => ctx.Response.Cookies.Delete(CookieName);
}
