using Microsoft.Net.Http.Headers;
using OpenVP.Core;

namespace OpenVP.Server;

/// <summary>Bytes that are already JSON, with a strong ETag and a 304 for a client that has them.</summary>
public sealed class ValidatedJson(GraphJson.Form form) : IResult
{
    public async Task ExecuteAsync(HttpContext ctx)
    {
        var res = ctx.Response;
        res.Headers[HeaderNames.ETag] = form.ETag;
        // no-cache = the browser may keep it but must ask first; the ETag turns that ask into a tiny 304.
        res.Headers[HeaderNames.CacheControl] = "no-cache";
        if (GraphJson.NotModified(ctx.Request.Headers[HeaderNames.IfNoneMatch].ToString(), form.ETag)) { res.StatusCode = StatusCodes.Status304NotModified; return; }
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength = null; // the compression middleware changes the length; it sets it itself when it doesn't compress
        await res.Body.WriteAsync(form.Bytes);
    }
}

/// <summary>Bytes that are already JSON, no caching.</summary>
public sealed class RawJson(byte[] bytes) : IResult
{
    public async Task ExecuteAsync(HttpContext ctx)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.Body.WriteAsync(bytes);
    }
}
