using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using OpenVP.Core.Integrations;
using OpenVP.Core.Security;

namespace OpenVP.Server;

/// <summary>
/// Optional single sign-on with OpenID Connect (the authorization-code flow with PKCE), using Microsoft's own handler. Off by default. The provider's
/// settings (authority, client id, scopes, claim to role mapping, allowed domains) are in the security settings and the client secret is in the secret
/// store; a change takes effect for the next sign-in with no restart. A person who signs in this way gets an ordinary OpenVP session, so the
/// access key and local users keep working beside it.
/// </summary>
public sealed class OidcSupport
{
    public const string Scheme = "openvp-oidc";
    public const string TempScheme = "openvp-oidc-cookie";
    public const string SecretName = "oidc.clientSecret";
    private readonly SecuritySettings _settings;
    private readonly Func<SecretStore> _secretsFn;
    private SecretStore _secrets => _secretsFn();
    private IOptionsMonitorCache<OpenIdConnectOptions>? _cache;

    public OidcSupport(SecuritySettings settings, Func<SecretStore> secrets) { _settings = settings; _secretsFn = secrets; }

    public OidcSettings Current => _settings.Get().Oidc;
    public bool Ready => Current is { Enabled: true } s && !string.IsNullOrWhiteSpace(s.Authority) && !string.IsNullOrWhiteSpace(s.ClientId);
    public bool AllowHttp { get; init; }

    public void AddServices(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddCookie(TempScheme, o =>
            {
                o.Cookie.Name = "openvp_oidc_tmp";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddOpenIdConnect(Scheme, o => Configure(o));
    }

    /// <summary>Call once the app is built, so <see cref="Reset"/> can reach the options cache.</summary>
    public void Attach(IServiceProvider sp) => _cache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();

    // Read the settings each time the options are (re)built. While OIDC is off the options hold placeholders: the handler never contacts them.
    private void Configure(OpenIdConnectOptions o)
    {
        var s = Current;
        o.SignInScheme = TempScheme;
        o.Authority = Ready ? s.Authority!.Trim() : "https://openvp.invalid";
        o.ClientId = Ready ? s.ClientId!.Trim() : "openvp-oidc-off";
        o.ClientSecret = _secrets.Get(SecretName);
        o.RequireHttpsMetadata = !AllowHttp;
        o.ResponseType = "code";
        o.ResponseMode = "query";
        o.UsePkce = true;
        o.SaveTokens = false;
        o.GetClaimsFromUserInfoEndpoint = false;
        o.MapInboundClaims = false;
        o.CallbackPath = "/signin-oidc";
        o.SignedOutCallbackPath = "/signout-callback-oidc";
        o.Scope.Clear();
        foreach (var sc in (Ready ? s.Scopes : "openid").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)) o.Scope.Add(sc);
        if (!o.Scope.Contains("openid")) o.Scope.Add("openid");
        // These cookies travel only on top-level GET redirects (query response mode), so Lax is enough and works on plain http too.
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SameSite = SameSiteMode.Lax;
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }

    /// <summary>Makes the next sign-in read the settings again (after they were changed).</summary>
    public void Reset() => _cache?.Clear();

    public object Public() => new { enabled = Ready, text = Current.ButtonText };

    public object Status() => new
    {
        enabled = Current.Enabled, ready = Ready, authority = Current.Authority, clientId = Current.ClientId, hasSecret = _secrets.Has(SecretName), scopes = Current.Scopes,
        roleClaim = Current.RoleClaim, mappings = Current.Mappings.Select(m => new { value = m.Value, role = Permissions.RoleName(m.Role) }), defaultRole = Current.DefaultRole == null ? null : Permissions.RoleName(Current.DefaultRole.Value),
        allowedDomains = Current.AllowedDomains, userClaim = Current.UserClaim, buttonText = Current.ButtonText, callbackPath = "/signin-oidc"
    };

    /// <summary>The claims the provider sent, as plain name and value pairs.</summary>
    public static List<KeyValuePair<string, string>> ClaimsOf(ClaimsPrincipal p) => p.Claims.Select(c => new KeyValuePair<string, string>(c.Type, c.Value)).ToList();
}
