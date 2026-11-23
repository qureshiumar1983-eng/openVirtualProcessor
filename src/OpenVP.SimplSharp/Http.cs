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
