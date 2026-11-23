using OpenVP.Core;

namespace OpenVP.Server;

/// <summary>
/// <c>/cws/{slot}/...</c>: the web-scripting (CWS) servers a slot's SIMPL# program registered with <c>HttpCwsServer</c>. A real processor serves them at
/// <c>/cws/...</c>; OpenVP runs ten programs, so the slot number comes first. Who may call is the permission map's <c>cws</c> rule (Signal, or nobody in
/// particular when the slot's "CWS is public" setting is on). Never on the tunnel port: the gate answers 404 for anything but shared panels.
/// </summary>
public static class CwsEndpoints
{
    public const int MaxBody = 8 * 1024 * 1024;

    public static void Map(WebApplication app, Processor processor)
    {
        app.Map("/cws/{n:int}", (int n, HttpContext ctx) => Serve(processor, n, "", ctx));
        app.Map("/cws/{n:int}/{**path}", (int n, string? path, HttpContext ctx) => Serve(processor, n, path ?? "", ctx));
    }

    private static async Task<IResult> Serve(Processor processor, int n, string path, HttpContext ctx)
    {
        if (ctx.Items.ContainsKey(TunnelGate.ItemKey)) return Results.NotFound();
        if (!processor.IsValidSlot(n)) return Results.NotFound(new { error = $"Slot must be 1–{processor.Slots.Count}." });
        var slot = processor.Slot(n);
        if (slot.State != SlotState.Running) return Results.Json(new { error = $"Slot {n} isn't running." }, statusCode: 503);
        if (!slot.Io.Cws.Any) return Results.NotFound(new { error = $"The program in slot {n} has no web-scripting (CWS) server registered." });

        var ms = new MemoryStream();
        var buf = new byte[16 * 1024];
        int r;
        while ((r = await ctx.Request.Body.ReadAsync(buf, ctx.RequestAborted)) > 0)
        {
            ms.Write(buf, 0, r);
            if (ms.Length > MaxBody) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        var headers = ctx.Request.Headers.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v ?? ""))).ToList();
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is { IsIPv4MappedToIPv6: true }) ip = ip.MapToIPv4();
        var req = new CwsRequest(ctx.Request.Method, path, ctx.Request.QueryString.Value ?? "", headers, ms.ToArray(), ip?.ToString() ?? "", ctx.Request.Scheme, ctx.Request.Host.Value ?? "");

        // The program's handler runs on a worker thread (as on a processor), never on the request thread, and may take its time.
        CwsReply? reply;
        try { reply = await Task.Run(() => slot.Io.Cws.Handle(req), ctx.RequestAborted); }
        catch (OperationCanceledException) { return Results.Empty; }
        catch (Exception e)
        {
            slot.Log("Error", $"CWS {req.Method} /{path}: {e.Message}");
            return Results.Json(new { error = "The program's web handler failed: " + e.Message }, statusCode: 500);
        }
        if (reply == null) return Results.NotFound(new { error = "No web-scripting server of this program serves that path." });

        ctx.Response.StatusCode = reply.Status;
        if (reply.StatusDescription != null) ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>()!.ReasonPhrase = reply.StatusDescription;
        foreach (var h in reply.Headers)
        {
            if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            try { ctx.Response.Headers.Append(h.Key, h.Value); } catch (ArgumentException) { }
        }
        if (reply.ContentType != null) ctx.Response.ContentType = reply.ContentType;
        if (ctx.Request.Method != "HEAD" && reply.Status is not (204 or 304) && reply.Body.Length > 0)
        {
            ctx.Response.ContentLength = reply.Body.Length;
            await ctx.Response.Body.WriteAsync(reply.Body, ctx.RequestAborted);
        }
        return Results.Empty;
    }
}
