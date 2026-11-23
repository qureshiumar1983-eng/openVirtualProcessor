using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crestron.SimplSharp.CrestronIO;
using OpenVP.SimplSharp;
using NetHttp = System.Net.Http;

namespace Crestron.SimplSharp.Net
{
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
        public void Parse(string url) { Url = url; try { Uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null; } catch (UriFormatException) { Uri = null; } }
        public string PathAndParams => Uri?.PathAndQuery ?? Url ?? "";
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

    public class HttpHeaders : System.Collections.IEnumerable
    {
        internal readonly List<HttpHeader> List = new List<HttpHeader>();
        public void AddHeader(HttpHeader h) { if (h != null) List.Add(h); }
        public void AddHeader(string name, string value) => List.Add(new HttpHeader(name, value));
        public void SetHeaderValue(string name, string value) { List.RemoveAll(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); AddHeader(name, value); }
        public void RemoveHeader(string name) => List.RemoveAll(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public bool ContainsHeader(string name) => List.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public void Clear() => List.Clear();
        public HttpHeader this[string name] => List.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        public HttpHeader this[int index] => index >= 0 && index < List.Count ? List[index] : null;
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
        public HttpException(string message) : base(message) { }
        public HttpClientResponse Response { get; internal set; }
    }

    public enum HTTP_CALLBACK_ERROR { COMPLETED = 0, UNKNOWN_ERROR = 1, INVALID_PARAM = 2 }
    public delegate void HTTPClientResponseCallback(HttpClientResponse response, HTTP_CALLBACK_ERROR error);
    public delegate void HTTPClientResponseCallbackEx(HttpClientResponse response, HTTP_CALLBACK_ERROR error, object userObject);
    public delegate void HTTPStreamRequestCallbackEx(HttpClientRequest request, HTTP_CALLBACK_ERROR error, object userObject);
    public delegate void HTTPStreamResponseCallbackEx(HttpClientRequest request, HTTP_CALLBACK_ERROR error, object userObject);

    /// <summary>Everything one request needs, whichever client sends it.</summary>
    internal sealed class HttpJob
    {
        public string Url;
        public string Method;
        public IEnumerable<HttpHeader> Headers = Array.Empty<HttpHeader>();
        public byte[] Body;
        public string User, Password;
        public Crestron.SimplSharp.Net.AuthMethod Auth;
        public bool TimeoutEnabled = true;
        public int TimeoutSeconds = 30;
        public bool KeepAlive = true;
        public bool AllowRedirect = true;
        public string UserAgent, Accept;
        public bool Secure, VerifyPeer, VerifyHost;
        public bool ApplyRedirects;
    }

    /// <summary>
    /// The engine under HttpClient and HttpsClient, backed by System.Net.Http. One engine per client object, so a client with KeepAlive
    /// reuses its connection across requests; it is rebuilt when a setting that needs a new handler (credentials, certificate checks) changes.
    /// </summary>
    internal sealed class HttpEngine : IDisposable
    {
        private NetHttp.HttpClient _http;
        private string _signature;
        private CancellationTokenSource _inflight;
        private readonly object _gate = new object();

        public void Abort() { lock (_gate) { try { _inflight?.Cancel(); } catch (ObjectDisposedException) { } } }

        public void Dispose() { lock (_gate) { _http?.Dispose(); _http = null; } }

        private NetHttp.HttpClient Client(HttpJob j, Uri uri)
        {
            var sig = $"{j.User}\u0001{j.Password}\u0001{j.Auth}\u0001{j.AllowRedirect}\u0001{j.Secure}\u0001{j.VerifyPeer}\u0001{j.VerifyHost}\u0001{(j.KeepAlive ? 1 : 0)}";
            lock (_gate)
            {
                if (_http != null && sig == _signature && j.KeepAlive) return _http;
                _http?.Dispose();
                var h = new NetHttp.SocketsHttpHandler { AllowAutoRedirect = j.AllowRedirect, MaxAutomaticRedirections = 10, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
                if (!j.KeepAlive) h.PooledConnectionIdleTimeout = TimeSpan.Zero;
                if (!string.IsNullOrEmpty(j.User) && j.Auth != Crestron.SimplSharp.Net.AuthMethod.BASIC)
                    h.Credentials = new NetworkCredential(j.User, j.Password ?? "");
                if (j.Secure)
                {
                    var peer = j.VerifyPeer; var host = j.VerifyHost;
                    h.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
                    {
                        if (errors == SslPolicyErrors.None) return true;
                        if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0) return false;
                        if (!peer) errors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
                        if (!host) errors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
                        return errors == SslPolicyErrors.None;
                    };
                }
                _http = new NetHttp.HttpClient(h) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                _signature = sig;
                return _http;
            }
        }

        public HttpClientResponse Send(HttpJob j)
        {
            if (string.IsNullOrWhiteSpace(j.Url) || !Uri.TryCreate(j.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new HttpException("The URL isn't a complete http or https address: " + j.Url);
            string hostHeader = null;
            if (j.ApplyRedirects && uri.Scheme == "http")
            {
                var go = OpenVPBridge.Redirect(uri.Host, uri.Port);
                if (go.Redirected) { hostHeader = uri.Authority; uri = new UriBuilder(uri) { Host = go.Host, Port = go.Port }.Uri; }
            }
            var client = Client(j, uri);
            var msg = new NetHttp.HttpRequestMessage(new NetHttp.HttpMethod(j.Method), uri);
            if (hostHeader != null) msg.Headers.Host = hostHeader;
            var headers = (j.Headers ?? Array.Empty<HttpHeader>()).ToList();
            if (j.Body != null || j.Method is "POST" or "PUT" or "PATCH")
                msg.Content = new NetHttp.ByteArrayContent(j.Body ?? Array.Empty<byte>());
            foreach (var h in headers)
            {
                if (h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Name.Equals("Host", StringComparison.OrdinalIgnoreCase)) { msg.Headers.Host = h.Value; continue; }
                if (msg.Headers.TryAddWithoutValidation(h.Name, h.Value)) continue;
                msg.Content ??= new NetHttp.ByteArrayContent(Array.Empty<byte>());
                msg.Content.Headers.TryAddWithoutValidation(h.Name, h.Value);
            }
            if (!string.IsNullOrEmpty(j.UserAgent) && !msg.Headers.Contains("User-Agent")) msg.Headers.TryAddWithoutValidation("User-Agent", j.UserAgent);
            if (!string.IsNullOrEmpty(j.Accept) && !msg.Headers.Contains("Accept")) msg.Headers.TryAddWithoutValidation("Accept", j.Accept);
            if (j.Auth == Crestron.SimplSharp.Net.AuthMethod.BASIC && !string.IsNullOrEmpty(j.User) && !msg.Headers.Contains("Authorization"))
                msg.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(j.User + ":" + (j.Password ?? ""))));
            if (!j.KeepAlive) msg.Headers.ConnectionClose = true;

            using var cts = new CancellationTokenSource();
            lock (_gate) _inflight = cts;
            if (j.TimeoutEnabled && j.TimeoutSeconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(j.TimeoutSeconds));
            try
            {
                using var resp = client.Send(msg, NetHttp.HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var r = new HttpClientResponse { Code = (int)resp.StatusCode, ResponseUrl = resp.RequestMessage?.RequestUri?.ToString() };
                using (var body = resp.Content.ReadAsStream(cts.Token))
                using (var ms = new System.IO.MemoryStream())
                {
                    body.CopyTo(ms);
                    r.Data = ms.ToArray();
                }
                foreach (var h in resp.Headers.Concat(resp.Content.Headers)) r.Header.AddHeader(h.Key, string.Join(",", h.Value));
                var charset = resp.Content.Headers.ContentType?.CharSet;
                if (!string.IsNullOrEmpty(charset)) { try { r.Encoding = Encoding.GetEncoding(charset.Trim('"')); } catch (ArgumentException) { } }
                return r;
            }
            catch (OperationCanceledException e)
            {
                throw new HttpException(cts.IsCancellationRequested && j.TimeoutEnabled ? "The request timed out." : "The request was aborted.", e);
            }
            catch (NetHttp.HttpRequestException e)
            {
                throw new HttpException(e.InnerException?.Message ?? e.Message, e);
            }
            catch (System.IO.IOException e)
            {
                throw new HttpException(e.Message, e);
            }
            finally { lock (_gate) if (ReferenceEquals(_inflight, cts)) _inflight = null; }
        }
    }

    /// <summary>HTTP client with the SIMPL# shape. Dispatch blocks; DispatchAsync answers on the slot's thread.</summary>
    public class HttpClient : Crestron.SimplSharp.Net.Client
    {
        public enum DISPATCHASYNC_ERROR { PENDING = 0, PROCESS_BUSY = 1, INVALID_PARAM = 2 }
        private readonly HttpEngine _engine = new HttpEngine();

        public HttpClient() { OpenVPBridge.Track(this); }

        public Crestron.SimplSharp.Net.AuthMethod AuthenticationMethod { get; set; }
        public bool IncludeHeaders { get; set; }
        public bool Verbose { get; set; }
        /// <summary>Seconds a request may take (connecting, sending and reading the whole answer).</summary>
        public int Timeout { get; set; } = 30;
        public bool TimeoutEnabled { get; set; } = true;
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool KeepAlive { get; set; } = true;
        public int Port { get; set; }
        public string HostName { get; set; }
        public string Url { get; set; }
        public string UserAgent { get; set; }
        public string Accept { get; set; }
        public int BufferSize { get; set; } = 4096;
        public bool AllowAutoRedirect { get; set; } = true;

        private HttpJob Job(HttpClientRequest request)
        {
            var url = request.Url?.Url ?? Url;
            if (!string.IsNullOrEmpty(url) && !url.Contains("://") && !string.IsNullOrEmpty(HostName))
                url = $"http://{HostName}{(Port > 0 && Port != 80 ? ":" + Port : "")}{(url.StartsWith("/") ? "" : "/")}{url}";
            return new HttpJob
            {
                Url = url, Method = request.RequestType.ToString().ToUpperInvariant(), Headers = request.Header.List, Body = request.Body(),
                User = UserName, Password = Password, Auth = AuthenticationMethod, TimeoutEnabled = TimeoutEnabled, TimeoutSeconds = Timeout,
                KeepAlive = KeepAlive && request.KeepAlive, AllowRedirect = AllowAutoRedirect, UserAgent = UserAgent, Accept = Accept, ApplyRedirects = true
            };
        }

        internal static HttpClientResponse Send(string url, RequestType type, HttpHeaders headers, byte[] body, string user, string pass, bool timeoutEnabled, int timeoutSeconds)
        {
            using var e = new HttpEngine();
            return e.Send(new HttpJob
            {
                Url = url, Method = type.ToString().ToUpperInvariant(), Headers = headers?.List ?? new List<HttpHeader>(), Body = body, User = user, Password = pass,
                Auth = string.IsNullOrEmpty(user) ? Crestron.SimplSharp.Net.AuthMethod.NONE : Crestron.SimplSharp.Net.AuthMethod.ANY,
                TimeoutEnabled = timeoutEnabled, TimeoutSeconds = timeoutSeconds, ApplyRedirects = true
            });
        }

        public HttpClientResponse Dispatch(HttpClientRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return _engine.Send(Job(request));
        }

        public DISPATCHASYNC_ERROR DispatchAsync(HttpClientRequest request, HTTPClientResponseCallback callback)
        {
            if (request == null) return DISPATCHASYNC_ERROR.INVALID_PARAM;
            if (ProcessBusy) return DISPATCHASYNC_ERROR.PROCESS_BUSY;
            ProcessBusy = true;
            Task.Run(() =>
            {
                HttpClientResponse r = null; var err = HTTP_CALLBACK_ERROR.COMPLETED;
                try { r = Dispatch(request); }
                catch (Exception e) { err = HTTP_CALLBACK_ERROR.UNKNOWN_ERROR; OpenVPBridge.Log("Warning", "HTTP request failed: " + e.Message); }
                ProcessBusy = false;
                OpenVPBridge.Post(() => callback?.Invoke(r, err));
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
            Task.Run(() => OpenVPBridge.Post(() => callback?.Invoke(request, HTTP_CALLBACK_ERROR.COMPLETED, userObject)));
            return DISPATCHASYNC_ERROR.PENDING;
        }
        public CrestronIO.Stream EndGetRequestStream(HttpClientRequest request, object userObject) => _streams.GetOrAdd(request, _ => new CrestronIO.MemoryStream());
        public HttpClientRequest BeginGetResponseStream(HTTPStreamResponseCallbackEx callback, HttpClientRequest request, object userObject)
        {
            Task.Run(() => OpenVPBridge.Post(() => callback?.Invoke(request, HTTP_CALLBACK_ERROR.COMPLETED, userObject)));
            return request;
        }
        public HttpClientResponse EndGetResponseStream(HttpClientRequest request)
        {
            if (_streams.TryRemove(request, out var ms)) { request.ContentBytes = ms.ToArray(); request.ContentSource = ContentSource.ContentBytes; }
            return Dispatch(request);
        }

        public string Get(string url) => Dispatch(new HttpClientRequest { Url = new UrlParser(url), RequestType = RequestType.Get }).ContentString;
        public string Post(string url, string body) => Dispatch(new HttpClientRequest { Url = new UrlParser(url), RequestType = RequestType.Post, ContentString = body ?? "" }).ContentString;
        public void Abort() => _engine.Abort();
        public override void Dispose() { _engine.Dispose(); OpenVPBridge.Untrack(this); }
    }
}

namespace Crestron.SimplSharp.Net.Https
{
    using Crestron.SimplSharp.Net.Http;

    public enum RequestType { Get = 0, Post = 1, Put = 2, Delete = 3, Head = 4, Patch = 5, Options = 6 }
    public enum ContentSource { ContentNone = 0, ContentString = 1, ContentStream = 2, ContentBytes = 3 }

    public class UrlParser : Http.UrlParser
    {
        public UrlParser() { }
        public UrlParser(string url) : base(url) { }
        public static implicit operator UrlParser(string url) => new UrlParser(url);
        public new int Port => Uri?.Port ?? 443;
    }

    public class HttpsHeader : HttpHeader { public HttpsHeader(string name, string value) : base(name, value) { } }

    public class HttpsHeaders : System.Collections.IEnumerable
    {
        internal readonly HttpHeaders Inner = new HttpHeaders();
        public void AddHeader(HttpsHeader h) => Inner.AddHeader(h);
        public void AddHeader(string name, string value) => Inner.AddHeader(name, value);
        public void SetHeaderValue(string name, string value) => Inner.SetHeaderValue(name, value);
        public void RemoveHeader(string name) => Inner.RemoveHeader(name);
        public bool ContainsHeader(string name) => Inner.ContainsHeader(name);
        public void Clear() => Inner.Clear();
        public HttpsHeader this[string name] => Inner[name] is HttpHeader h ? new HttpsHeader(h.Name, h.Value) : null;
        public HttpsHeader this[int index] => Inner[index] is HttpHeader h ? new HttpsHeader(h.Name, h.Value) : null;
        public string ContentType { get => Inner.ContentType; set => Inner.ContentType = value; }
        public string ContentLength => Inner.ContentLength;
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
        public HttpsException(string message) : base(message) { }
        public HttpsClientResponse Response { get; internal set; }
    }

    public enum HTTPS_CALLBACK_ERROR { COMPLETED = 0, UNKNOWN_ERROR = 1, INVALID_PARAM = 2 }
    public delegate void HTTPSClientResponseCallback(HttpsClientResponse response, HTTPS_CALLBACK_ERROR error);
    public delegate void HTTPSClientResponseCallbackEx(HttpsClientResponse response, HTTPS_CALLBACK_ERROR error, object userObject);
    public delegate void HttpsStreamRequestCallbackEx(HttpsClientRequest request, HTTPS_CALLBACK_ERROR error, object userObject);
    public delegate void HttpsStreamResponseCallbackEx(HttpsClientRequest request, HTTPS_CALLBACK_ERROR error, object userObject);

    /// <summary>
    /// HTTPS client. <see cref="PeerVerification"/> (the certificate chains to a trusted authority) and <see cref="HostVerification"/> (its name
    /// matches the host) are both off until a program turns them on, so a device's self-signed certificate works as it does on a processor
    /// whose program leaves them off.
    /// </summary>
    public class HttpsClient : Crestron.SimplSharp.Net.Client
    {
        public enum DISPATCHASYNC_ERROR { PENDING = 0, PROCESS_BUSY = 1, INVALID_PARAM = 2 }
        private readonly HttpEngine _engine = new HttpEngine();

        public HttpsClient() { OpenVPBridge.Track(this); }

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
        public string UserAgent { get; set; }
        public string Accept { get; set; }
        public string HostName { get; set; }
        public int Port { get; set; }
        public int BufferSize { get; set; } = 4096;
        public UrlParser Url { get; set; }

        public HttpsClientResponse Dispatch(HttpsClientRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var url = request.Url?.Url ?? Url?.Url;
            if (!string.IsNullOrEmpty(url) && !url.Contains("://") && !string.IsNullOrEmpty(HostName))
                url = $"https://{HostName}{(Port > 0 && Port != 443 ? ":" + Port : "")}{(url.StartsWith("/") ? "" : "/")}{url}";
            try
            {
                var r = _engine.Send(new HttpJob
                {
                    Url = url, Method = request.RequestType.ToString().ToUpperInvariant(), Headers = request.Header.Inner.List, Body = request.Body(),
                    User = UserName, Password = Password, Auth = AuthenticationMethod, TimeoutEnabled = TimeoutEnabled, TimeoutSeconds = Timeout,
                    KeepAlive = KeepAlive && request.KeepAlive, AllowRedirect = AllowAutoRedirect, UserAgent = UserAgent, Accept = Accept,
                    Secure = true, VerifyPeer = PeerVerification, VerifyHost = HostVerification
                });
                var o = new HttpsClientResponse { Code = r.Code, ResponseUrl = r.ResponseUrl, Data = r.ContentBytes, Encoding = r.Encoding };
                foreach (var h in r.Header.List) o.Header.AddHeader(h.Name, h.Value);
                return o;
            }
            catch (HttpException e) { throw new HttpsException(e.Message, e.InnerException); }
        }

        public DISPATCHASYNC_ERROR DispatchAsync(HttpsClientRequest request, HTTPSClientResponseCallback callback)
        {
            if (request == null) return DISPATCHASYNC_ERROR.INVALID_PARAM;
            if (ProcessBusy) return DISPATCHASYNC_ERROR.PROCESS_BUSY;
            ProcessBusy = true;
            Task.Run(() =>
            {
                HttpsClientResponse r = null; var err = HTTPS_CALLBACK_ERROR.COMPLETED;
                try { r = Dispatch(request); }
                catch (Exception e) { err = HTTPS_CALLBACK_ERROR.UNKNOWN_ERROR; OpenVPBridge.Log("Warning", "HTTPS request failed: " + e.Message); }
                ProcessBusy = false;
                OpenVPBridge.Post(() => callback?.Invoke(r, err));
            });
            return DISPATCHASYNC_ERROR.PENDING;
        }

        public DISPATCHASYNC_ERROR DispatchAsyncEx(HttpsClientRequest request, HTTPSClientResponseCallbackEx callback, object userObject)
            => DispatchAsync(request, (r, e) => callback?.Invoke(r, e, userObject));

        private readonly System.Collections.Concurrent.ConcurrentDictionary<HttpsClientRequest, CrestronIO.MemoryStream> _streams = new();
        public DISPATCHASYNC_ERROR BeginGetRequestStream(HttpsStreamRequestCallbackEx callback, HttpsClientRequest request, object userObject)
        {
            _streams[request] = new CrestronIO.MemoryStream();
            Task.Run(() => OpenVPBridge.Post(() => callback?.Invoke(request, HTTPS_CALLBACK_ERROR.COMPLETED, userObject)));
            return DISPATCHASYNC_ERROR.PENDING;
        }
        public CrestronIO.Stream EndGetRequestStream(HttpsClientRequest request) => _streams.GetOrAdd(request, _ => new CrestronIO.MemoryStream());
        public HttpsClientRequest BeginGetResponseStream(HttpsStreamResponseCallbackEx callback, HttpsClientRequest request, object userObject)
        {
            Task.Run(() => OpenVPBridge.Post(() => callback?.Invoke(request, HTTPS_CALLBACK_ERROR.COMPLETED, userObject)));
            return request;
        }
        public HttpsClientResponse EndGetResponseStream(HttpsClientRequest request)
        {
            if (_streams.TryRemove(request, out var ms)) { request.ContentBytes = ms.ToArray(); request.ContentSource = ContentSource.ContentBytes; }
            return Dispatch(request);
        }
        public void SetClientCertificate(byte[] cert) { }
        public void SetClientPrivateKey(byte[] key) { }

        public string Get(string url) => Dispatch(new HttpsClientRequest { Url = new UrlParser(url) }).ContentString;
        public void Abort() => _engine.Abort();
        public override void Dispose() { _engine.Dispose(); OpenVPBridge.Untrack(this); }
    }
}
