namespace OpenVP.Server;

/// <summary>
/// Locks down the tunnel port. The internet tunnel (cloudflared) points at a second listener on 127.0.0.1,
/// not at the console's port, and everything that arrives there goes through this gate first:
///   - the request is marked as a tunnel request, so <see cref="ApiAuth.IsDirectLocal"/> is false for it
///     whatever its remote address (cloudflared connects from 127.0.0.1) or Host header;
///   - only shared-panel paths (<c>/s/...</c>) are let through; everything else is a plain 404, as if the
///     console, the API, the debugger streams, help and metrics did not exist.
/// Requests are recognised by the local port they arrived on, which no client can influence.
/// </summary>
public sealed class TunnelGate(int port)
{
    public const string ItemKey = "openvp.tunnel";

    public int Port => port;

    public bool IsTunnelRequest(HttpContext ctx) => port > 0 && ctx.Connection.LocalPort == port;

    /// <summary>The only paths served on the tunnel port: <c>/s/{token}...</c>.</summary>
    public static bool PathAllowed(PathString path) =>
        path.StartsWithSegments("/s", out var rest) && rest.HasValue && rest.Value!.Length > 1;

    public Task Handle(HttpContext ctx, RequestDelegate next)
    {
        if (!IsTunnelRequest(ctx)) return next(ctx);
        ctx.Items[ItemKey] = true;
        if (!PathAllowed(ctx.Request.Path))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(ctx);
    }
}
