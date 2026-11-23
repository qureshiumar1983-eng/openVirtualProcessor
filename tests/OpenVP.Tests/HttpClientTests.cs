using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Crestron.SimplSharp.Net;
using AuthMethod = Crestron.SimplSharp.Net.AuthMethod;
using Crestron.SimplSharp.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Https = Crestron.SimplSharp.Net.Https;

namespace OpenVP.Tests;

/// <summary>Crestron.SimplSharp.Net.Http.HttpClient and Net.Https.HttpsClient against a real loopback web server.</summary>
public static class HttpClientTests
{
    /// <summary>A throwaway Kestrel web server on a free loopback port that runs the given handler.</summary>
    private sealed class Web : IDisposable
    {
        public readonly WebApplication App;
        public readonly int Port;
        public readonly bool Tls;
        public Web(RequestDelegate handler, X509Certificate2? cert = null)
        {
            Port = IntegrationServiceTests.FreePort(); Tls = cert != null;
            var b = WebApplication.CreateBuilder();
            b.Logging.ClearProviders();
            b.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, Port, o => { if (cert != null) o.UseHttps(cert); }));
            App = b.Build();
            App.Run(handler);
            App.StartAsync().GetAwaiter().GetResult();
        }
        public string Url(string path = "/") => $"{(Tls ? "https" : "http")}://127.0.0.1:{Port}{path}";
        public void Dispose() { try { App.StopAsync().GetAwaiter().GetResult(); } catch { } }
    }

    private static X509Certificate2 SelfSigned(string dns = "localhost")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=" + dns, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(dns);
        req.CertificateExtensions.Add(san.Build());
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return new X509Certificate2(c.Export(X509ContentType.Pfx));
    }

    private static HttpClientRequest Req(string url, RequestType t = RequestType.Get) => new() { Url = new UrlParser(url), RequestType = t };

    [Test]
    public static void Get_returns_status_headers_and_body_and_error_statuses_are_not_exceptions()
    {
        using var web = new Web(async ctx =>
        {
            if (ctx.Request.Path == "/missing") { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsync("nope"); return; }
            if (ctx.Request.Path == "/boom") { ctx.Response.StatusCode = 500; return; }
            ctx.Response.Headers["X-Reply"] = "hello";
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync($"{ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString} ua={ctx.Request.Headers.UserAgent} x={ctx.Request.Headers["X-One"]}");
        });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient { UserAgent = "OpenVP-Test" };
        var req = Req(web.Url("/thing?a=1&b=two"));
        req.Header.AddHeader("X-One", "1");
        using var r = client.Dispatch(req);
        Assert.Equal(200, r.Code, "status");
        Assert.Equal("GET /thing?a=1&b=two ua=OpenVP-Test x=1", r.ContentString, "body shows the request");
        Assert.Equal("hello", r.Header["X-Reply"].Value, "response header");
        Assert.True(r.Header["x-reply"] != null, "header names are case-insensitive");
        Assert.True(r.Header.ContentType.StartsWith("text/plain"), "content type");
        Assert.Equal(r.ContentBytes.Length, r.ContentLength, "content length");
        Assert.Equal(web.Url("/thing?a=1&b=two"), r.ResponseUrl, "response url");

        Assert.Equal(404, client.Dispatch(Req(web.Url("/missing"))).Code, "404 comes back as a response");
        Assert.Equal("nope", client.Dispatch(Req(web.Url("/missing"))).ContentString, "with its body");
        Assert.Equal(500, client.Dispatch(Req(web.Url("/boom"))).Code, "500 too");
        Assert.True(client.Get(web.Url("/x")).StartsWith("GET /x"), "the Get(url) shortcut");
    }

    [Test]
    public static void Post_put_delete_patch_head_send_bodies_and_headers()
    {
        using var web = new Web(async ctx =>
        {
            using var ms = new MemoryStream(); await ctx.Request.Body.CopyToAsync(ms);
            ctx.Response.Headers["X-Method"] = ctx.Request.Method;
            ctx.Response.Headers["X-Ct"] = ctx.Request.ContentType ?? "";
            ctx.Response.Headers["X-Len"] = ms.Length.ToString();
            await ctx.Response.Body.WriteAsync(ms.ToArray());
        });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient();
        foreach (var t in new[] { RequestType.Post, RequestType.Put, RequestType.Patch, RequestType.Delete })
        {
            var req = Req(web.Url("/echo"), t);
            req.Header.ContentType = "application/json";
            req.ContentString = "{\"k\":\"v\"}";
            using var r = client.Dispatch(req);
            Assert.Equal(t.ToString().ToUpperInvariant(), r.Header["X-Method"].Value, "method " + t);
            Assert.Equal("{\"k\":\"v\"}", r.ContentString, "body echoed for " + t);
            Assert.Equal("application/json", r.Header["X-Ct"].Value, "content type for " + t);
        }
        var bytes = Req(web.Url("/echo"), RequestType.Post);
        bytes.ContentBytes = new byte[] { 0, 1, 2, 250, 255 }; bytes.ContentSource = ContentSource.ContentBytes;
        Assert.True(client.Dispatch(bytes).ContentBytes.SequenceEqual(new byte[] { 0, 1, 2, 250, 255 }), "binary body survives");
        var stream = Req(web.Url("/echo"), RequestType.Post);
        stream.ContentStream = new Crestron.SimplSharp.CrestronIO.MemoryStream(Encoding.ASCII.GetBytes("from a stream")); stream.ContentSource = ContentSource.ContentStream;
        Assert.Equal("from a stream", client.Dispatch(stream).ContentString, "a stream body");
        var head = client.Dispatch(Req(web.Url("/echo"), RequestType.Head));
        Assert.Equal("HEAD", head.Header["X-Method"].Value, "HEAD");
        Assert.Equal(0, head.ContentLength, "HEAD has no body");
        Assert.Equal("{\"a\":1}", client.Post(web.Url("/echo"), "{\"a\":1}"), "the Post(url, body) shortcut");
    }

    [Test]
    public static void Chunked_responses_arrive_whole_and_the_charset_decides_the_text()
    {
        using var web = new Web(async ctx =>
        {
            if (ctx.Request.Path == "/latin")
            {
                ctx.Response.ContentType = "text/plain; charset=iso-8859-1";
                await ctx.Response.Body.WriteAsync(new byte[] { 0x63, 0x61, 0x66, 0xE9 }); // "café" in Latin-1
                return;
            }
            for (var i = 0; i < 5; i++) { await ctx.Response.WriteAsync($"chunk{i};"); await ctx.Response.Body.FlushAsync(); await Task.Delay(20); }
        });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient();
        using var r = client.Dispatch(Req(web.Url("/stream")));
        Assert.Equal("chunk0;chunk1;chunk2;chunk3;chunk4;", r.ContentString, "all the chunks");
        Assert.Equal("chunked", r.Header["Transfer-Encoding"].Value, "it really was chunked");
        Assert.Equal("café", client.Dispatch(Req(web.Url("/latin"))).ContentString, "the response charset is used");
    }

    [Test]
    public static void Timeouts_connection_failures_and_abort_throw_http_exceptions()
    {
        using var web = new Web(async ctx => { await Task.Delay(3000, ctx.RequestAborted).ContinueWith(_ => { }); await ctx.Response.WriteAsync("late"); });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient { Timeout = 1 };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var e = Assert.Throws<HttpException>(() => client.Dispatch(Req(web.Url("/slow"))));
        Assert.True(sw.ElapsedMilliseconds is > 800 and < 2900, "gave up after about a second: " + sw.ElapsedMilliseconds + " ms (" + e.Message + ")");
        Assert.True(e.Message.Contains("timed out"), "says so: " + e.Message);

        client.TimeoutEnabled = false; client.Timeout = 0;
        var slow = Task.Run(() => client.Dispatch(Req(web.Url("/slow"))).ContentString);
        Thread.Sleep(300);
        client.Abort();
        Assert.True(Assert.Throws<AggregateException>(() => slow.Wait(5000)).InnerException is HttpException, "Abort cancels a running request");

        using var c2 = new Crestron.SimplSharp.Net.Http.HttpClient();
        Assert.True(Assert.Throws<HttpException>(() => c2.Dispatch(Req($"http://127.0.0.1:{IntegrationServiceTests.FreePort()}/"))).Message.Length > 0, "connection refused");
        Assert.True(Assert.Throws<HttpException>(() => c2.Dispatch(Req("/no/host"))).Message.Contains("complete"), "a URL with no host");
    }

    [Test]
    public static void Keep_alive_reuses_the_connection_and_off_opens_a_new_one()
    {
        var ids = new ConcurrentQueue<string>();
        using var web = new Web(async ctx => { ids.Enqueue(ctx.Connection.Id); await ctx.Response.WriteAsync("k"); });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient();
        for (var i = 0; i < 3; i++) client.Dispatch(Req(web.Url("/")));
        Assert.Equal(1, ids.Distinct().Count(), "three requests, one connection with KeepAlive");
        ids.Clear();
        client.KeepAlive = false;
        for (var i = 0; i < 3; i++) client.Dispatch(Req(web.Url("/")));
        Assert.Equal(3, ids.Distinct().Count(), "three requests, three connections without it");
        ids.Clear();
        client.KeepAlive = true;
        var closing = Req(web.Url("/")); closing.KeepAlive = false;
        client.Dispatch(closing); client.Dispatch(closing);
        Assert.Equal(2, ids.Distinct().Count(), "a request that says KeepAlive=false closes its connection");
    }

    [Test]
    public static void Basic_authentication_is_sent_up_front_or_on_challenge_and_wrong_passwords_get_a_401()
    {
        var seen = new ConcurrentQueue<string>();
        using var web = new Web(async ctx =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            seen.Enqueue(auth);
            var good = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:s3cret"));
            if (auth != good) { ctx.Response.StatusCode = 401; ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"dev\""; return; }
            await ctx.Response.WriteAsync("welcome");
        });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient { UserName = "admin", Password = "s3cret", AuthenticationMethod = AuthMethod.BASIC };
        Assert.Equal("welcome", client.Dispatch(Req(web.Url("/"))).ContentString, "basic");
        Assert.Equal(1, seen.Count, "BASIC sends the header on the first request: one round trip");

        seen.Clear();
        client.AuthenticationMethod = AuthMethod.ANY;
        Assert.Equal(200, client.Dispatch(Req(web.Url("/"))).Code, "ANY answers the challenge");
        Assert.Equal(2, seen.Count, "challenge then credentials");
        Assert.Equal("", seen.First(), "the first request had no credentials");

        client.Password = "wrong";
        Assert.Equal(401, client.Dispatch(Req(web.Url("/"))).Code, "wrong password: 401 comes back as a response");
        client.AuthenticationMethod = AuthMethod.NONE; client.UserName = null;
        Assert.Equal(401, client.Dispatch(Req(web.Url("/"))).Code, "none: 401");
    }

    [Test]
    public static void Redirects_are_followed_unless_switched_off_and_async_dispatch_reports_busy_and_errors()
    {
        using var web = new Web(async ctx =>
        {
            if (ctx.Request.Path == "/old") { ctx.Response.StatusCode = 302; ctx.Response.Headers.Location = "/new"; return; }
            if (ctx.Request.Path == "/wait") await Task.Delay(400);
            await ctx.Response.WriteAsync("at " + ctx.Request.Path);
        });
        using var client = new Crestron.SimplSharp.Net.Http.HttpClient();
        var followed = client.Dispatch(Req(web.Url("/old")));
        Assert.Equal("at /new", followed.ContentString, "redirect followed");
        Assert.Equal(web.Url("/new"), followed.ResponseUrl, "ResponseUrl is where it ended up");
        client.AllowAutoRedirect = false;
        var raw = client.Dispatch(Req(web.Url("/old")));
        Assert.Equal(302, raw.Code, "redirect not followed");
        Assert.Equal("/new", raw.Header["Location"].Value, "Location header");

        var results = new ConcurrentQueue<string>();
        var rc = client.DispatchAsync(Req(web.Url("/wait")), (resp, err) => results.Enqueue(err + ":" + resp?.Code + ":" + resp?.ContentString));
        Assert.Equal(Crestron.SimplSharp.Net.Http.HttpClient.DISPATCHASYNC_ERROR.PENDING, rc, "async pending");
        Assert.True(client.ProcessBusy, "busy while it runs");
        Assert.Equal(Crestron.SimplSharp.Net.Http.HttpClient.DISPATCHASYNC_ERROR.PROCESS_BUSY, client.DispatchAsync(Req(web.Url("/")), (_, _) => { }), "a second dispatch while busy");
        Assert.Eventually(() => results.Count == 1, "callback", 5000);
        Assert.Equal("COMPLETED:200:at /wait", results.First(), "the result");
        Assert.True(!client.ProcessBusy, "free again");

        var userSeen = new ConcurrentQueue<object>();
        client.DispatchAsyncEx(Req($"http://127.0.0.1:{IntegrationServiceTests.FreePort()}/"), (resp, err, user) => { userSeen.Enqueue(err); userSeen.Enqueue(resp); userSeen.Enqueue(user); }, "tag");
        Assert.Eventually(() => userSeen.Count == 3, "error callback", 5000);
        var items = userSeen.ToArray();
        Assert.Equal(HTTP_CALLBACK_ERROR.UNKNOWN_ERROR, (HTTP_CALLBACK_ERROR)items[0], "error code on failure");
        Assert.True(items[1] == null && (string)items[2] == "tag", "no response, and the user object comes back");
        Assert.Equal(Crestron.SimplSharp.Net.Http.HttpClient.DISPATCHASYNC_ERROR.INVALID_PARAM, client.DispatchAsync(null, (_, _) => { }), "no request");
    }

    [Test]
    public static void Https_client_checks_the_certificate_only_when_told_to()
    {
        using var web = new Web(async ctx => await ctx.Response.WriteAsync("secure " + ctx.Request.Headers["X-Token"] + " " + ctx.Request.Method), SelfSigned("localhost"));
        using var client = new Https.HttpsClient();
        var req = new Https.HttpsClientRequest { Url = new Https.UrlParser(web.Url("/p")), RequestType = Https.RequestType.Post, ContentString = "x" };
        req.Header.AddHeader("X-Token", "abc");

        // Defaults: nothing is verified, so a device's self-signed certificate works.
        using (var r = client.Dispatch(req))
        {
            Assert.Equal(200, r.Code, "default accepts the self-signed certificate");
            Assert.Equal("secure abc POST", r.ContentString, "headers and body travel over TLS");
        }

        // Peer verification: a self-signed certificate is no longer trusted.
        client.PeerVerification = true;
        Assert.True(Assert.Throws<Https.HttpsException>(() => client.Dispatch(req)).Message.Length > 0, "peer verification refuses it");

        // Host verification alone: the chain is not checked, but 127.0.0.1 is not in a certificate for "localhost".
        client.PeerVerification = false; client.HostVerification = true;
        Assert.True(Assert.Throws<Https.HttpsException>(() => client.Dispatch(req)).Message.Length > 0, "host verification refuses the name mismatch");
        client.HostVerification = false;
        Assert.Equal(200, client.Dispatch(req).Code, "and with both off it works again");

        // The right name, with only the host checked, passes.
        client.HostVerification = true;
        var byName = new Https.HttpsClientRequest { Url = new Https.UrlParser($"https://localhost:{web.Port}/p") };
        Assert.Equal(200, client.Dispatch(byName).Code, "host name matches the certificate");

        var seen = new ConcurrentQueue<string>();
        client.DispatchAsync(req, (resp, err) => seen.Enqueue(err + ":" + resp?.Code));
        Assert.Eventually(() => seen.Count == 1, "async https", 5000);
        Assert.Equal("UNKNOWN_ERROR:", seen.First() + "", "host mismatch is reported as an error on the async path too") ;
    }

    [Test]
    public static void Https_client_helpers_headers_and_url_parser()
    {
        using var web = new Web(async ctx => { ctx.Response.Headers["X-A"] = "1"; ctx.Response.Headers.Append("X-B", new Microsoft.Extensions.Primitives.StringValues(new[] { "x", "y" })); await ctx.Response.WriteAsync("hi " + ctx.Request.Headers.Accept); }, SelfSigned());
        using var client = new Https.HttpsClient { Accept = "text/x-test", HostName = "127.0.0.1", Port = web.Port };
        var req = new Https.HttpsClientRequest { Url = new Https.UrlParser("/relative/path") };
        using var r = client.Dispatch(req);
        Assert.Equal("hi text/x-test", r.ContentString, "HostName/Port complete a relative URL, Accept is sent");
        Assert.Equal("1", r.Header["X-A"].Value, "response header");
        Assert.Equal("x,y", r.Header["X-B"].Value, "a repeated header is joined");
        Assert.Equal(2, r.Header.Cast<Https.HttpsHeader>().Count(x => x.Name.StartsWith("X-")), "enumerating the headers");

        var u = new Https.UrlParser("https://example.com:8443/a/b?c=d");
        Assert.Equal("example.com", u.Hostname, "host"); Assert.Equal(8443, u.Port, "port"); Assert.Equal("/a/b", u.Path, "path"); Assert.Equal("c=d", u.Params, "params");
        Assert.Equal(443, new Https.UrlParser("https://example.com/").Port, "default https port");
        var h = new Https.HttpsHeaders(); h.AddHeader("A", "1"); h.SetHeaderValue("a", "2");
        Assert.Equal(1, h.Cast<Https.HttpsHeader>().Count(), "SetHeaderValue replaces, ignoring case"); Assert.Equal("2", h["A"].Value, "value");
    }
}
