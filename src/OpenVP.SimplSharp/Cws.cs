using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using OpenVP.Core;
using OpenVP.SimplSharp;
using CIO = Crestron.SimplSharp.CrestronIO;

namespace Crestron.SimplSharp.WebScripting
{
    public interface IHttpCwsHandler
    {
        void ProcessRequest(HttpCwsContext context);
    }

    /// <summary>Base class for route handlers written as classes.</summary>
    public abstract class HttpCwsHandler : IHttpCwsHandler
    {
        public abstract void ProcessRequest(HttpCwsContext context);
    }

    public class CwsRouteValueDictionary : Dictionary<string, object>
    {
        public CwsRouteValueDictionary() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    public class HttpCwsRouteData
    {
        public CwsRouteValueDictionary Values { get; } = new CwsRouteValueDictionary();
        public HttpCwsRoute Route { get; internal set; }
    }

    /// <summary>
    /// A route: a URL template such as <c>devices/{id}</c>, <c>status</c> or <c>files/{*rest}</c>. <c>{name}</c> matches one segment,
    /// <c>{name?}</c> an optional last segment and <c>{*name}</c> the rest of the path. Matching ignores case.
    /// </summary>
    public class HttpCwsRoute
    {
        public HttpCwsRoute(string name) { Name = name?.Trim('/') ?? ""; }
        public string Name { get; set; }
        public string Url => Name;
        public IHttpCwsHandler RouteHandler { get; set; }
        public object DataTokens { get; set; }

        internal bool Match(string[] parts, HttpCwsRouteData data)
        {
            var tpl = (Name ?? "").Trim('/').Length == 0 ? Array.Empty<string>() : Name.Trim('/').Split('/');
            for (int i = 0; i < tpl.Length; i++)
            {
                var t = tpl[i];
                if (t.StartsWith("{*") && t.EndsWith("}"))
                {
                    data.Values[t.Substring(2, t.Length - 3)] = string.Join("/", parts.Skip(i).Select(Uri.UnescapeDataString));
                    return true;
                }
                if (t.StartsWith("{") && t.EndsWith("}"))
                {
                    var optional = t.EndsWith("?}");
                    var key = t.Trim('{', '}').TrimEnd('?');
                    if (i >= parts.Length)
                    {
                        if (optional && i == tpl.Length - 1) return true;
                        return false;
                    }
                    var colon = key.IndexOf(':'); // {id:int} style constraints are accepted and ignored
                    if (colon > 0) key = key.Substring(0, colon);
                    data.Values[key] = Uri.UnescapeDataString(parts[i]);
                    continue;
                }
                if (i >= parts.Length || !t.Equals(parts[i], StringComparison.OrdinalIgnoreCase)) return false;
            }
            return tpl.Length == parts.Length;
        }
    }

    public class HttpCwsRouteCollection : List<HttpCwsRoute>
    {
        public bool Remove(string name) => RemoveAll(r => string.Equals(r.Name, name?.Trim('/'), StringComparison.OrdinalIgnoreCase)) > 0;
        public bool Contains(string name) => this.Any(r => string.Equals(r.Name, name?.Trim('/'), StringComparison.OrdinalIgnoreCase));
        public HttpCwsRoute this[string name] => this.FirstOrDefault(r => string.Equals(r.Name, name?.Trim('/'), StringComparison.OrdinalIgnoreCase));
    }

    public class HttpCwsRequest
    {
        internal CwsRequest Inner;
        internal int Slot;
        private NameValueCollection _headers;
        private NameValueCollection _query;
        public HttpCwsRouteData RouteData { get; internal set; }
        public string HttpMethod => Inner.Method;
        /// <summary>The path as a processor shows it: <c>/cws/...</c> (OpenVP serves it at <c>/cws/{slot}/...</c>).</summary>
        public string Path => "/cws/" + Inner.Path.Trim('/');
        public string RawUrl => Path + Inner.QueryString;
        public Uri Url => new Uri($"{Inner.Scheme}://{Inner.Host}/cws/{Slot}/{Inner.Path.Trim('/')}{Inner.QueryString}");
        public NameValueCollection QueryString => _query ??= System.Web.HttpUtility.ParseQueryString(Inner.QueryString);
        public NameValueCollection Headers
        {
            get
            {
                if (_headers != null) return _headers;
                _headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
                foreach (var h in Inner.Headers) _headers.Add(h.Key, h.Value);
                return _headers;
            }
        }
        public NameValueCollection Header => Headers;
        public string ContentType => Headers["Content-Type"];
        public long ContentLength => Inner.Body.Length;
        public string UserAgent => Headers["User-Agent"];
        public string UserHostAddress => Inner.RemoteAddress;
        public bool IsLocal => System.Net.IPAddress.TryParse(Inner.RemoteAddress, out var ip) && System.Net.IPAddress.IsLoopback(ip);
        public Encoding ContentEncoding
        {
            get
            {
                var ct = ContentType;
                var i = ct?.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) ?? -1;
                if (i >= 0) { try { return Encoding.GetEncoding(ct.Substring(i + 8).Trim('"', ' ', ';')); } catch (ArgumentException) { } }
                return Encoding.UTF8;
            }
        }
        public CIO.Stream InputStream => new CIO.MemoryStream(Inner.Body);
        /// <summary>The body as text.</summary>
        public string ContentString => ContentEncoding.GetString(Inner.Body);
    }

    public class HttpCwsResponse
    {
        private readonly System.IO.MemoryStream _body = new System.IO.MemoryStream();
        internal bool Ended;
        public int StatusCode { get; set; } = 200;
        public string StatusDescription { get; set; }
        public string ContentType { get; set; }
        public Encoding ContentEncoding { get; set; } = Encoding.UTF8;
        public NameValueCollection Headers { get; } = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        public void AppendHeader(string name, string value) => Headers.Add(name, value);
        public CIO.Stream OutputStream => new BodyStream(_body, this);
        public void Write(string text, bool endResponse)
        {
            if (text != null) { var b = ContentEncoding.GetBytes(text); _body.Write(b, 0, b.Length); }
            if (endResponse) End();
        }
        public void Write(byte[] data, int offset, int count, bool endResponse)
        {
            if (data != null) _body.Write(data, offset, count);
            if (endResponse) End();
        }
        public void Redirect(string url) { StatusCode = 302; Headers["Location"] = url; End(); }
        public void End() { Ended = true; }
        public void Close() => End();

        internal CwsReply ToReply()
        {
            var r = new CwsReply { Status = StatusCode, StatusDescription = StatusDescription, ContentType = ContentType ?? (_body.Length > 0 ? "text/html; charset=utf-8" : null), Body = _body.ToArray() };
            foreach (string k in Headers) foreach (var v in Headers.GetValues(k) ?? Array.Empty<string>()) r.Headers.Add(new KeyValuePair<string, string>(k, v));
            return r;
        }

        private sealed class BodyStream : CIO.Stream
        {
            public BodyStream(System.IO.MemoryStream inner, HttpCwsResponse owner) : base(inner) { }
        }
    }

    public class HttpCwsContext
    {
        public HttpCwsRequest Request { get; internal set; }
        public HttpCwsResponse Response { get; internal set; }
    }

    /// <summary>
    /// Crestron Web Scripting server. A processor serves a registered server at <c>https://host/cws/&lt;path&gt;</c>; OpenVP serves it at
    /// <c>/cws/&lt;slot&gt;/&lt;path&gt;</c> on the console's own port, behind the console's permissions (Signal, or open when the slot's
    /// "CWS is public" setting is on). It is never served on the internet tunnel port.
    /// </summary>
    public class HttpCwsServer : IDisposable
    {
        private IDisposable _registration;

        public HttpCwsServer(string path) { Path = (path ?? "").Trim('/'); OpenVPBridge.Track(this); }
        public string Path { get; }
        public HttpCwsRouteCollection Routes { get; } = new HttpCwsRouteCollection();
        public IHttpCwsHandler HttpRequestHandler { get; set; }
        public event EventHandler<OnHttpRequestArgs> ReceivedRequestEvent;
        public bool Registered => _registration != null;

        public void AddRoute(HttpCwsRoute route) { if (route != null) Routes.Add(route); }
        public void RemoveRoute(HttpCwsRoute route) => Routes.Remove(route);

        public bool Register()
        {
            var slot = OpenVPBridge.Slot;
            if (slot == null) return false;
            _registration?.Dispose();
            _registration = slot.Io.Cws.Register(Path, (req, sub) => Handle(slot.Number, req, sub));
            OpenVPBridge.Log("Notice", $"CWS server registered: /cws/{slot.Number}/{Path}".TrimEnd('/'));
            return true;
        }

        public bool Unregister()
        {
            _registration?.Dispose();
            _registration = null;
            return true;
        }

        public void Dispose() { Unregister(); OpenVPBridge.Untrack(this); }

        private CwsReply Handle(int slot, CwsRequest req, string sub)
        {
            var response = new HttpCwsResponse();
            try
            {
                var parts = sub.Length == 0 ? Array.Empty<string>() : sub.Split('/');
                var context = new HttpCwsContext { Request = new HttpCwsRequest { Inner = req, Slot = slot }, Response = response };
                foreach (var route in Routes.ToList())
                {
                    var data = new HttpCwsRouteData { Route = route };
                    if (route.RouteHandler == null || !route.Match(parts, data)) continue;
                    context.Request.RouteData = data;
                    route.RouteHandler.ProcessRequest(context);
                    return response.ToReply();
                }
                context.Request.RouteData = new HttpCwsRouteData();
                if (HttpRequestHandler != null) { HttpRequestHandler.ProcessRequest(context); return response.ToReply(); }
                var ev = ReceivedRequestEvent;
                if (ev != null) { ev(this, new OnHttpRequestArgs(context)); return response.ToReply(); }
                return CwsReply.Of(404, "No route");
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException { InnerException: { } ie } ? ie : e;
                OpenVPBridge.Log("Error", $"CWS {req.Method} /{req.Path}: {inner.Message}");
                return CwsReply.Of(500, inner.Message);
            }
        }
    }

    public class OnHttpRequestArgs : EventArgs
    {
        public OnHttpRequestArgs(HttpCwsContext context) { Context = context; }
        public HttpCwsContext Context { get; }
    }
}
