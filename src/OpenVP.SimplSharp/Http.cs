using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Crestron.SimplSharp.CrestronIO;
using OpenVP.SimplSharp;
using NetHttp = System.Net.Http;

namespace Crestron.SimplSharp.Net
{
    public enum RequestType { Get = 0, Post = 1, Put = 2, Delete = 3, Head = 4, Patch = 5, Options = 6 }
    public enum AuthMethod { NONE = 0, BASIC = 1, DIGEST = 2, NTLM = 3, ANY = 4 }

    /// <summary>Base of the SIMPL# HTTP clients.</summary>
    public abstract class Client : IDisposable
    {
        public bool ProcessBusy { get; protected set; }
        public virtual void Dispose() { }
    }
}

namespace Crestron.SimplSharp.Net.Http
{
    public enum RequestType { Get = 0, Post = 1, Put = 2, Delete = 3, Head = 4, Patch = 5, Options = 6 }
    public enum ContentSource { ContentNone = 0, ContentString = 1, ContentStream = 2, ContentBytes = 3 }

    public class UrlParser
    {
        public UrlParser() { }
        public UrlParser(string url) { Parse(url); }
        public void Parse(string url) { Url = url; try { Uri = new Uri(url); } catch (UriFormatException) { Uri = null; } }
        public string PathAndParams => Uri?.PathAndQuery ?? "";
        public UrlParser(string host, string path) : this($"http://{host}{(path.StartsWith("/") ? "" : "/")}{path}") { }
        public string Url { get; set; }
        internal Uri Uri { get; private set; }
        public string Hostname => Uri?.Host ?? "";
        public int Port => Uri?.Port ?? 80;
        public string Path => Uri?.AbsolutePath ?? "";
        public string Params => Uri?.Query.TrimStart('?') ?? "";
        public override string ToString() => Url;
        public static implicit operator UrlParser(string url) => new UrlParser(url);
    }

    public class HttpHeader
    {
        public HttpHeader(string name, string value) { Name = name; Value = value; }
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class HttpHeaders
    {
        internal readonly List<HttpHeader> List = new List<HttpHeader>();
        public void AddHeader(HttpHeader h) { if (h != null) List.Add(h); }
        public void AddHeader(string name, string value) => List.Add(new HttpHeader(name, value));
        public void SetHeaderValue(string name, string value) { List.RemoveAll(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); AddHeader(name, value); }
        public void RemoveHeader(string name) => List.RemoveAll(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public HttpHeader this[string name] => List.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public string ContentType { get => this["Content-Type"]?.Value; set => SetHeaderValue("Content-Type", value); }
        public string ContentLength => this["Content-Length"]?.Value;
        public int Count => List.Count;
        public System.Collections.IEnumerator GetEnumerator() => List.ToList().GetEnumerator();
    }

    public abstract class HttpRequestResponse
    {
        public HttpHeaders Header { get; set; } = new HttpHeaders();
        public Encoding Encoding { get; set; } = Encoding.UTF8;
    }

    public abstract class HttpOutgoingRequestResponse : HttpRequestResponse
    {
        public bool KeepAlive { get; set; } = true;
        public Stream ContentStream { get; set; }
        public string ContentString { get; set; }
        public byte[] ContentBytes { get; set; }
        public ContentSource ContentSource { get; set; }
        public void FinalizeHeader() { }
        internal byte[] Body()
        {
            if (ContentSource == ContentSource.ContentStream && ContentStream != null) { var m = new System.IO.MemoryStream(); ContentStream.Inner.CopyTo(m); return m.ToArray(); }
            if (ContentBytes != null) return ContentBytes;
            if (ContentString != null) return Encoding.GetBytes(ContentString);
            if (ContentStream != null) { var ms = new System.IO.MemoryStream(); ContentStream.Inner.CopyTo(ms); return ms.ToArray(); }
            return null;
        }
    }

    public abstract class HttpIncomingRequestResponse : HttpRequestResponse
    {
        internal byte[] Data = Array.Empty<byte>();
        public string ContentString => Encoding.GetString(Data);
        public byte[] ContentBytes => Data;
        public Stream ContentStream => new CrestronIO.MemoryStream(Data);
        public int ContentLength => Data.Length;
    }

    public class HttpClientRequest : HttpOutgoingRequestResponse
    {
        public UrlParser Url { get; set; }
        public RequestType RequestType { get; set; }
    }

    public class HttpClientResponse : HttpIncomingRequestResponse, IDisposable
    {
        public void Dispose() { }
        public int Code { get; internal set; }
        public string ResponseUrl { get; internal set; }
    }

    public class HttpException : Exception
    {
        public HttpException(string message, Exception inner) : base(message, inner) { }
        public HttpClientResponse Response { get; internal set; }
    }

    public enum HTTP_CALLBACK_ERROR { COMPLETED = 0, UNKNOWN_ERROR = 1, INVALID_PARAM = 2 }
    public delegate void HTTPClientResponseCallback(HttpClientResponse response, HTTP_CALLBACK_ERROR error);
    public delegate void HTTPClientResponseCallbackEx(HttpClientResponse response, HTTP_CALLBACK_ERROR error, object userObject);
    public delegate void HTTPStreamRequestCallbackEx(HttpClientRequest request, HTTP_CALLBACK_ERROR error, object userObject);
    public delegate void HTTPStreamResponseCallbackEx(HttpClientRequest request, HTTP_CALLBACK_ERROR error, object userObject);

    /// <summary>HTTP client with the SIMPL# shape. Dispatch blocks; DispatchAsync calls back on a worker thread.</summary>
    public class HttpClient : Crestron.SimplSharp.Net.Client
    {
        public enum DISPATCHASYNC_ERROR { PENDING = 0, PROCESS_BUSY = 1, INVALID_PARAM = 2 }
        public Crestron.SimplSharp.Net.AuthMethod AuthenticationMethod { get; set; }
        public bool IncludeHeaders { get; set; }
        public bool Verbose { get; set; }
        public int Timeout { get; set; } = 30;
        public bool TimeoutEnabled { get; set; } = true;
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool KeepAlive { get; set; } = true;
        public int Port { get; set; }
        public string Url { get; set; }
        public bool AllowAutoRedirect { get; set; } = true;

        internal static HttpClientResponse Send(string url, RequestType type, HttpHeaders headers, byte[] body, string user, string pass,
                                                bool timeoutEnabled, int timeoutSeconds, bool verifyCertificates)
        {
            var handler = new NetHttp.HttpClientHandler();
            if (!verifyCertificates) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            if (!string.IsNullOrEmpty(user)) handler.Credentials = new NetworkCredential(user, pass);
            using var client = new NetHttp.HttpClient(handler) { Timeout = timeoutEnabled && timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : System.Threading.Timeout.InfiniteTimeSpan };
            var msg = new NetHttp.HttpRequestMessage(new NetHttp.HttpMethod(type.ToString().ToUpperInvariant()), url);
            string contentType = null;
            foreach (var h in headers?.List ?? new List<HttpHeader>())
            {
                if (h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) { contentType = h.Value; continue; }
                if (h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                msg.Headers.TryAddWithoutValidation(h.Name, h.Value);
            }
            if (body != null)
            {
                msg.Content = new NetHttp.ByteArrayContent(body);
                if (contentType != null) msg.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
            try
            {
                using var resp = client.Send(msg);
                var r = new HttpClientResponse { Code = (int)resp.StatusCode, ResponseUrl = resp.RequestMessage?.RequestUri?.ToString() };
                r.Data = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                foreach (var h in resp.Headers.Concat(resp.Content.Headers)) r.Header.AddHeader(h.Key, string.Join(",", h.Value));
                return r;
            }
            catch (Exception e) when (e is NetHttp.HttpRequestException or TaskCanceledException)
            {
                throw new HttpException(e.Message, e);
            }
        }

        public HttpClientResponse Dispatch(HttpClientRequest request) =>
            Send(request.Url?.Url ?? Url, request.RequestType, request.Header, request.Body(), UserName, Password, TimeoutEnabled, Timeout, true);

        public DISPATCHASYNC_ERROR DispatchAsync(HttpClientRequest request, HTTPClientResponseCallback callback)
        {
            ProcessBusy = true;
            Task.Run(() =>
            {
                try { callback?.Invoke(Dispatch(request), HTTP_CALLBACK_ERROR.COMPLETED); }
                catch (Exception) { callback?.Invoke(null, HTTP_CALLBACK_ERROR.UNKNOWN_ERROR); }
                finally { ProcessBusy = false; }
            });
            return DISPATCHASYNC_ERROR.PENDING;
        }

        public DISPATCHASYNC_ERROR DispatchAsyncEx(HttpClientRequest request, HTTPClientResponseCallbackEx callback, object userObject)
            => DispatchAsync(request, (r, e) => callback?.Invoke(r, e, userObject));

        // Streamed request/response: the request body is collected in memory, then sent on EndGetResponseStream.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<HttpClientRequest, CrestronIO.MemoryStream> _streams = new();
        public DISPATCHASYNC_ERROR BeginGetRequestStream(HTTPStreamRequestCallbackEx callback, HttpClientRequest request, object userObject)
        {
            _streams[request] = new CrestronIO.MemoryStream();
            Task.Run(() => callback?.Invoke(request, HTTP_CALLBACK_ERROR.COMPLETED, userObject));
            return DISPATCHASYNC_ERROR.PENDING;
        }
        public CrestronIO.Stream EndGetRequestStream(HttpClientRequest request, object userObject) => _streams.GetOrAdd(request, _ => new CrestronIO.MemoryStream());
        public HttpClientRequest BeginGetResponseStream(HTTPStreamResponseCallbackEx callback, HttpClientRequest request, object userObject)
        {
            Task.Run(() => callback?.Invoke(request, HTTP_CALLBACK_ERROR.COMPLETED, userObject));
            return request;
        }
        public HttpClientResponse EndGetResponseStream(HttpClientRequest request)
        {
            if (_streams.TryRemove(request, out var ms)) request.ContentBytes = ms.ToArray();
            return Dispatch(request);
        }

        public string Get(string url) => Send(url, RequestType.Get, null, null, UserName, Password, TimeoutEnabled, Timeout, true).ContentString;
        public string Post(string url, string body) => Send(url, RequestType.Post, null, Encoding.UTF8.GetBytes(body ?? ""), UserName, Password, TimeoutEnabled, Timeout, true).ContentString;
        public void Abort() { }
    }
}

namespace Crestron.SimplSharp.Net.Https
{
    using Crestron.SimplSharp.Net.Http;

    public enum RequestType { Get = 0, Post = 1, Put = 2, Delete = 3, Head = 4, Patch = 5, Options = 6 }
    public enum ContentSource { ContentNone = 0, ContentString = 1, ContentStream = 2, ContentBytes = 3 }

    public class HttpsHeader : HttpHeader { public HttpsHeader(string name, string value) : base(name, value) { } }

    public class HttpsHeaders
    {
        internal readonly HttpHeaders Inner = new HttpHeaders();
        public void AddHeader(HttpsHeader h) => Inner.AddHeader(h);
        public void AddHeader(string name, string value) => Inner.AddHeader(name, value);
        public void SetHeaderValue(string name, string value) => Inner.SetHeaderValue(name, value);
        public void RemoveHeader(string name) => Inner.RemoveHeader(name);
        public HttpsHeader this[string name] => Inner[name] is HttpHeader h ? new HttpsHeader(h.Name, h.Value) : null;
        public string ContentType { get => Inner.ContentType; set => Inner.ContentType = value; }
        public int Count => Inner.Count;
        public System.Collections.IEnumerator GetEnumerator() => Inner.List.Select(h => new HttpsHeader(h.Name, h.Value)).ToList().GetEnumerator();
    }

    public abstract class HttpsRequestResponse
    {
        public HttpsHeaders Header { get; } = new HttpsHeaders();
        public Encoding Encoding { get; set; } = Encoding.UTF8;
    }

    public abstract class HttpsOutgoingRequestResponse : HttpsRequestResponse
    {
        public bool KeepAlive { get; set; } = true;
        public Stream ContentStream { get; set; }
        public string ContentString { get; set; }
        public byte[] ContentBytes { get; set; }
        public ContentSource ContentSource { get; set; }
        public void FinalizeHeader() { }
        internal byte[] Body()
        {
            if (ContentSource == ContentSource.ContentStream && ContentStream != null) { var m = new System.IO.MemoryStream(); ContentStream.Inner.CopyTo(m); return m.ToArray(); }
            if (ContentBytes != null) return ContentBytes;
            if (ContentString != null) return Encoding.GetBytes(ContentString);
            if (ContentStream != null) { var ms = new System.IO.MemoryStream(); ContentStream.Inner.CopyTo(ms); return ms.ToArray(); }
            return null;
        }
    }

    public abstract class HttpsIncomingRequestResponse : HttpsRequestResponse
    {
        internal byte[] Data = Array.Empty<byte>();
        public string ContentString => Encoding.GetString(Data);
        public byte[] ContentBytes => Data;
        public Stream ContentStream => new CrestronIO.MemoryStream(Data);
        public int ContentLength => Data.Length;
    }

    public class HttpsClientRequest : HttpsOutgoingRequestResponse
    {
        public UrlParser Url { get; set; }
        public RequestType RequestType { get; set; }
    }

    public class HttpsClientResponse : HttpsIncomingRequestResponse, IDisposable
    {
        public void Dispose() { }
        public int Code { get; internal set; }
        public string ResponseUrl { get; internal set; }
    }

    public class HttpsException : Exception
    {
        public HttpsException(string message, Exception inner) : base(message, inner) { }
        public HttpsClientResponse Response { get; internal set; }
    }

    public enum HTTPS_CALLBACK_ERROR { COMPLETED = 0, UNKNOWN_ERROR = 1, INVALID_PARAM = 2 }
    public delegate void HTTPSClientResponseCallback(HttpsClientResponse response, HTTPS_CALLBACK_ERROR error);
    public delegate void HTTPSClientResponseCallbackEx(HttpsClientResponse response, HTTPS_CALLBACK_ERROR error, object userObject);
    public delegate void HttpsStreamRequestCallbackEx(HttpsClientRequest request, HTTPS_CALLBACK_ERROR error, object userObject);
    public delegate void HttpsStreamResponseCallbackEx(HttpsClientRequest request, HTTPS_CALLBACK_ERROR error, object userObject);

    public class HttpsClient : Crestron.SimplSharp.Net.Client
    {
        public enum DISPATCHASYNC_ERROR { PENDING = 0, PROCESS_BUSY = 1, INVALID_PARAM = 2 }
        public Crestron.SimplSharp.Net.AuthMethod AuthenticationMethod { get; set; }
        public bool IncludeHeaders { get; set; }
        public bool Verbose { get; set; }
        public int Timeout { get; set; } = 30;
        public bool TimeoutEnabled { get; set; } = true;
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool HostVerification { get; set; }
        public bool PeerVerification { get; set; }
        public bool KeepAlive { get; set; } = true;
        public bool AllowAutoRedirect { get; set; } = true;
        public UrlParser Url { get; set; }

        public HttpsClientResponse Dispatch(HttpsClientRequest request)
        {
            try
            {
                var r = HttpClient.Send(request.Url?.Url ?? Url?.Url, (Http.RequestType)(int)request.RequestType, request.Header.Inner, request.Body(),
                                        UserName, Password, TimeoutEnabled, Timeout, HostVerification || PeerVerification);
                var o = new HttpsClientResponse { Code = r.Code, ResponseUrl = r.ResponseUrl, Data = r.ContentBytes };
                foreach (var h in r.Header.List) o.Header.AddHeader(h.Name, h.Value);
                return o;
            }
            catch (HttpException e) { throw new HttpsException(e.Message, e.InnerException); }
        }

        public DISPATCHASYNC_ERROR DispatchAsync(HttpsClientRequest request, HTTPSClientResponseCallback callback)
        {
            ProcessBusy = true;
            Task.Run(() =>
            {
                try { callback?.Invoke(Dispatch(request), HTTPS_CALLBACK_ERROR.COMPLETED); }
                catch (Exception) { callback?.Invoke(null, HTTPS_CALLBACK_ERROR.UNKNOWN_ERROR); }
                finally { ProcessBusy = false; }
            });
            return DISPATCHASYNC_ERROR.PENDING;
        }

        public DISPATCHASYNC_ERROR DispatchAsyncEx(HttpsClientRequest request, HTTPSClientResponseCallbackEx callback, object userObject)
            => DispatchAsync(request, (r, e) => callback?.Invoke(r, e, userObject));

        private readonly System.Collections.Concurrent.ConcurrentDictionary<HttpsClientRequest, CrestronIO.MemoryStream> _streams = new();
        public DISPATCHASYNC_ERROR BeginGetRequestStream(HttpsStreamRequestCallbackEx callback, HttpsClientRequest request, object userObject)
        {
            _streams[request] = new CrestronIO.MemoryStream();
            Task.Run(() => callback?.Invoke(request, HTTPS_CALLBACK_ERROR.COMPLETED, userObject));
            return DISPATCHASYNC_ERROR.PENDING;
        }
        public CrestronIO.Stream EndGetRequestStream(HttpsClientRequest request) => _streams.GetOrAdd(request, _ => new CrestronIO.MemoryStream());
        public HttpsClientRequest BeginGetResponseStream(HttpsStreamResponseCallbackEx callback, HttpsClientRequest request, object userObject)
        {
            Task.Run(() => callback?.Invoke(request, HTTPS_CALLBACK_ERROR.COMPLETED, userObject));
            return request;
        }
        public HttpsClientResponse EndGetResponseStream(HttpsClientRequest request)
        {
            if (_streams.TryRemove(request, out var ms)) request.ContentBytes = ms.ToArray();
            return Dispatch(request);
        }
        public void SetClientCertificate(byte[] cert) { }
        public void SetClientPrivateKey(byte[] key) { }

        public string Get(string url) => Dispatch(new HttpsClientRequest { Url = new UrlParser(url) }).ContentString;
        public void Abort() { }
    }
}

namespace Crestron.SimplSharp.WebScripting
{
    public interface IHttpCwsHandler
    {
        void ProcessRequest(HttpCwsContext context);
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

    public class HttpCwsRoute
    {
        public HttpCwsRoute(string url) { Url = url?.Trim('/') ?? ""; }
        public string Url { get; }
        public string Name { get; set; }
        public IHttpCwsHandler RouteHandler { get; set; }
        public object DataTokens { get; set; }

        internal bool Match(string[] parts, HttpCwsRouteData data)
        {
            var tpl = Url.Length == 0 ? Array.Empty<string>() : Url.Split('/');
            if (tpl.Length != parts.Length) return false;
            for (int i = 0; i < tpl.Length; i++)
            {
                if (tpl[i].StartsWith("{") && tpl[i].EndsWith("}")) data.Values[tpl[i].Trim('{', '}')] = Uri.UnescapeDataString(parts[i]);
                else if (!tpl[i].Equals(parts[i], StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }

    public class HttpCwsRouteCollection : List<HttpCwsRoute> { }

    public class HttpCwsRequest
    {
        internal HttpListenerRequest Inner;
        public string HttpMethod => Inner.HttpMethod;
        public HttpCwsRouteData RouteData { get; internal set; }
        public Uri Url => Inner.Url;
        public string RawUrl => Inner.RawUrl;
        public string ContentType => Inner.ContentType;
        public long ContentLength => Inner.ContentLength64;
        public System.Collections.Specialized.NameValueCollection QueryString => Inner.QueryString;
        public System.Collections.Specialized.NameValueCollection Header => Inner.Headers;
        public string UserHostAddress => Inner.RemoteEndPoint?.Address.ToString();
        public Crestron.SimplSharp.CrestronIO.Stream InputStream => new Crestron.SimplSharp.CrestronIO.MemoryStream(ReadAll());
        internal byte[] ReadAll() { var ms = new System.IO.MemoryStream(); Inner.InputStream.CopyTo(ms); return ms.ToArray(); }
    }

    public class HttpCwsResponse
    {
        internal HttpListenerResponse Inner;
        private readonly StringBuilder _body = new StringBuilder();
        internal bool Ended;
        public int StatusCode { get => Inner.StatusCode; set => Inner.StatusCode = value; }
        public string StatusDescription { get => Inner.StatusDescription; set => Inner.StatusDescription = value; }
        public string ContentType { get => Inner.ContentType; set => Inner.ContentType = value; }
        public void AppendHeader(string name, string value) => Inner.AppendHeader(name, value);
        public void Write(string text, bool endResponse)
        {
            _body.Append(text);
            if (endResponse) End();
        }
        public void End()
        {
            if (Ended) return;
            Ended = true;
            var bytes = Encoding.UTF8.GetBytes(_body.ToString());
            Inner.ContentLength64 = bytes.Length;
            Inner.OutputStream.Write(bytes, 0, bytes.Length);
            Inner.OutputStream.Close();
        }
    }

    public class HttpCwsContext
    {
        public HttpCwsRequest Request { get; internal set; }
        public HttpCwsResponse Response { get; internal set; }
    }

    /// <summary>
    /// Crestron Web Scripting server. Real processors serve these under https://host/cws/&lt;path&gt;; OpenVP serves
    /// them at http://host:&lt;OPENVP_CWS_PORT, default 8082&gt;/cws/&lt;path&gt;.
    /// </summary>
    public class HttpCwsServer : IDisposable
    {
        private static readonly object Gate = new object();
        private static readonly List<HttpCwsServer> Servers = new List<HttpCwsServer>();
        private static HttpListener _listener;

        public HttpCwsServer(string path) { Path = (path ?? "").Trim('/'); OpenVPBridge.Track(this); }
        public string Path { get; }
        public HttpCwsRouteCollection Routes { get; } = new HttpCwsRouteCollection();
        public IHttpCwsHandler HttpRequestHandler { get; set; }
        public event EventHandler<OnHttpRequestArgs> ReceivedRequestEvent;

        public void AddRoute(HttpCwsRoute route) { if (route != null) Routes.Add(route); }
        public void RemoveRoute(HttpCwsRoute route) => Routes.Remove(route);

        public bool Register()
        {
            lock (Gate)
            {
                if (!Servers.Contains(this)) Servers.Add(this);
                if (_listener == null)
                {
                    var port = int.TryParse(System.Environment.GetEnvironmentVariable("OPENVP_CWS_PORT"), out var p) ? p : 8082;
                    try
                    {
                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://+:{port}/cws/");
                        _listener.Start();
                        _ = Loop(_listener);
                        OpenVPBridge.Log("Notice", $"CWS server listening on port {port} (/cws/{Path})");
                    }
                    catch (Exception e)
                    {
                        OpenVPBridge.Log("Warning", $"CWS server couldn't start on port {port}: {e.Message}");
                        _listener = null;
                        return false;
                    }
                }
            }
            return true;
        }

        public bool Unregister()
        {
            lock (Gate)
            {
                Servers.Remove(this);
                if (Servers.Count == 0 && _listener != null) { try { _listener.Stop(); } catch { } _listener = null; }
            }
            return true;
        }

        public void Dispose() { Unregister(); OpenVPBridge.Untrack(this); }

        private static async Task Loop(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath;
            var rest = path.Length > 5 ? path.Substring(5).Trim('/') : "";
            HttpCwsServer server; string sub;
            lock (Gate)
            {
                server = Servers.OrderByDescending(s => s.Path.Length)
                    .FirstOrDefault(s => s.Path.Length == 0 || rest.Equals(s.Path, StringComparison.OrdinalIgnoreCase) || rest.StartsWith(s.Path + "/", StringComparison.OrdinalIgnoreCase));
            }
            var response = new HttpCwsResponse { Inner = ctx.Response };
            try
            {
                if (server == null) { response.StatusCode = 404; response.Write("Not found", true); return; }
                sub = rest.Substring(Math.Min(rest.Length, server.Path.Length)).Trim('/');
                var parts = sub.Length == 0 ? Array.Empty<string>() : sub.Split('/');
                var context = new HttpCwsContext { Request = new HttpCwsRequest { Inner = ctx.Request }, Response = response };
                foreach (var route in server.Routes)
                {
                    var data = new HttpCwsRouteData { Route = route };
                    if (!route.Match(parts, data) || route.RouteHandler == null) continue;
                    context.Request.RouteData = data;
                    route.RouteHandler.ProcessRequest(context);
                    response.End();
                    return;
                }
                context.Request.RouteData = new HttpCwsRouteData();
                if (server.HttpRequestHandler != null) { server.HttpRequestHandler.ProcessRequest(context); response.End(); return; }
                if (server.ReceivedRequestEvent != null) { server.ReceivedRequestEvent(server, new OnHttpRequestArgs(context)); response.End(); return; }
                response.StatusCode = 404;
                response.Write("No route", true);
            }
            catch (Exception e)
            {
                OpenVPBridge.Log("Error", $"CWS {path}: {e.Message}");
                try { response.StatusCode = 500; response.Write(e.Message, true); } catch { }
            }
        }
    }

    public class OnHttpRequestArgs : EventArgs
    {
        public OnHttpRequestArgs(HttpCwsContext context) { Context = context; }
        public HttpCwsContext Context { get; }
    }
}
