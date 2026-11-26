using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OpenVP.Panels;

/// <summary>
/// EXPERIMENTAL. Accepts CH5 clients that speak CIP inside a WebSocket: Crestron Virtual Panel, the HTML5 Web XPanel
/// and community clients written against the same protocol. A processor listens for these on TCP 49200.
///
/// The protocol is not publicly documented. This follows what a public community client does
/// (siegeld/pycrestron, read for the wire layout; no code copied):
///   - the client opens <c>wss://host:49200/websocket</c>; a processor with SSL on uses a self-signed certificate;
///   - each binary WebSocket message is one CIP packet, [type:1][length:2 BE][payload], the same packets as on TCP 41794;
///   - the processor speaks first (0F, "program ready"), the client signs on with 0x26 (IP ID, Room ID, optional
///     token) and is answered with a connect response, exactly the exchange <see cref="CipServer"/> already has;
///   - before connecting, a client may ask for a token over HTTPS: <c>GET /cws/websocket/getWebSocketToken</c>,
///     sometimes after a login at <c>/userlogin.html</c>.
/// So each WebSocket is handed to <see cref="CipServer"/> as one more session, and this class only does the TLS,
/// the HTTP upgrade and the token answer. OpenVP does not check the token: like CIP on 41794, anyone who can reach
/// the port can drive a panel. A connection that does not start with a TLS hello is served as plain
/// <c>ws://</c> / <c>http://</c> on the same port. Not tested against Crestron's own clients: everything a client
/// asks for is logged in slot 1's log, so what it wanted can be read there.
/// </summary>
public sealed class WebXPanelServer : IDisposable
{
    private readonly CipServer _cip;
    private readonly Func<X509Certificate2> _certificate;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private int _requestsLogged;
    private const int MaxRequestsLogged = 200;
    private const int MaxHeaderBytes = 16 * 1024;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    /// <param name="cip">The CIP server whose sessions these clients become.</param>
    /// <param name="certificate">The certificate to present to a client that connects with TLS; asked for on the first one.</param>
    public WebXPanelServer(CipServer cip, int port, Func<X509Certificate2> certificate, IPAddress? bind = null)
    {
        _cip = cip;
        _certificate = certificate;
        _listener = new TcpListener(bind ?? IPAddress.Any, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            client.NoDelay = true;
            _ = Serve(client);
        }
    }

    private void Note(string message)
    {
        if (Interlocked.Increment(ref _requestsLogged) <= MaxRequestsLogged) _cip.Event(1, "WebXPanel: " + message);
    }

    private async Task Serve(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint as IPEndPoint;
        var who = remote?.ToString() ?? "?";
        var handedOver = false;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            handshake.CancelAfter(HandshakeTimeout);
            Stream stream = client.GetStream();

            // TLS or not? A TLS connection starts with a handshake record (0x16); HTTP starts with a letter.
            var first = new byte[1];
            if (await client.Client.ReceiveAsync(first, SocketFlags.Peek, handshake.Token) == 0) return;
            var tls = first[0] == 0x16;
            if (tls)
            {
                var ssl = new SslStream(stream, false);
                try
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate(),
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, handshake.Token);
                }
                catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException or CryptographicException)
                {
                    Note($"the TLS handshake with {who} on port {Port} failed ({e.Message}). If the client checks certificates, it may not trust OpenVP's self-signed one.");
                    return;
                }
                stream = ssl;
            }

            var request = await ReadRequest(stream, handshake.Token);
            if (request == null) return;
            var (method, target, headers) = request.Value;
            var scheme = tls ? "TLS" : "no TLS";

            if (headers.TryGetValue("Upgrade", out var upgrade) && upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase)
                && headers.TryGetValue("Sec-WebSocket-Key", out var key))
            {
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"), handshake.Token);
                Note($"{who} opened a WebSocket at {target} ({scheme}, port {Port})");
                var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });
                handedOver = true;
                await _cip.Run(new WebSocketCipTransport(ws, client, remote, Port, tls ? "WebSocket (TLS)" : "WebSocket"));
                return;
            }

            Note($"{who} asked for {method} {target} ({scheme}, port {Port})");
            await stream.WriteAsync(Answer(method, target, headers), handshake.Token);
            await stream.FlushAsync(handshake.Token);
            if (stream is SslStream s) { try { await s.ShutdownAsync(); } catch { } }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or WebSocketException) { }
        finally
        {
            if (!handedOver) client.Close();
        }
    }

    /// <summary>The request line and headers; whatever follows (a login form's body) is not needed and not read.</summary>
    private static async Task<(string method, string target, Dictionary<string, string> headers)?> ReadRequest(Stream stream, CancellationToken ct)
    {
        var buf = new byte[MaxHeaderBytes];
        var n = 0;
        var end = -1;
        while (end < 0)
        {
            if (n == buf.Length) return null;
            var got = await stream.ReadAsync(buf.AsMemory(n), ct);
            if (got == 0) return null;
            n += got;
            end = buf.AsSpan(0, n).IndexOf("\r\n\r\n"u8);
        }
        var lines = Encoding.Latin1.GetString(buf, 0, end).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return (first[0], first[1], headers);
    }

    /// <summary>
    /// The HTTP answers a client may want before it opens the WebSocket: the token, and the login page some clients
    /// visit first. Every login is accepted and every token is the same kind of throwaway: OpenVP does not check it.
    /// </summary>
    internal static byte[] Answer(string method, string target, Dictionary<string, string> headers)
    {
        var path = target.Split('?')[0];
        string status = "200 OK", type = "text/plain", body, extra = "";
        if (method == "OPTIONS") { status = "204 No Content"; body = ""; }
        else if (path.Contains("getWebSocketToken", StringComparison.OrdinalIgnoreCase)) body = NewToken();
        else if (path.EndsWith("/userlogin.html", StringComparison.OrdinalIgnoreCase))
        {
            type = "text/html";
            body = "<!doctype html><title>OpenVP</title><p>OpenVP needs no login here.</p>";
            extra = "Set-Cookie: TRACKID=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "; Path=/; HttpOnly\r\n";
        }
        else { status = "404 Not Found"; body = "OpenVP: this port serves CH5 panels over a WebSocket (/websocket) and their token (/cws/websocket/getWebSocketToken)."; }

        // A panel's page is loaded from somewhere else (a file, another server), so its requests here are cross-origin.
        var cors = headers.TryGetValue("Origin", out var origin) && origin.Length > 0 && !origin.Contains('\r') && !origin.Contains('\n')
            ? $"Access-Control-Allow-Origin: {origin}\r\nAccess-Control-Allow-Credentials: true\r\nVary: Origin\r\n"
            : "Access-Control-Allow-Origin: *\r\n";
        if (headers.TryGetValue("Access-Control-Request-Headers", out var asked) && !asked.Contains('\r') && !asked.Contains('\n'))
            cors += $"Access-Control-Allow-Headers: {asked}\r\n";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\n"
            + "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" + cors + extra + "Connection: close\r\n\r\n";
        return [.. Encoding.ASCII.GetBytes(head), .. bytes];
    }

    /// <summary>
    /// Shaped like the JWT a processor hands out (three base64url parts), in case a client looks at its form.
    /// It grants nothing and is never checked.
    /// </summary>
    internal static string NewToken()
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return B64("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8.ToArray()) + "."
            + B64(Encoding.ASCII.GetBytes($"{{\"iss\":\"OpenVP\",\"iat\":{now},\"exp\":{now + 86400}}}")) + "."
            + B64(RandomNumberGenerator.GetBytes(32));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}

/// <summary>CIP inside a WebSocket: one packet per binary message, in both directions.</summary>
internal sealed class WebSocketCipTransport : ICipTransport
{
    private readonly WebSocket _ws;
    private readonly TcpClient _client;
    private readonly Queue<(byte, byte[])> _pending = new();
    private readonly byte[] _chunk = new byte[16 * 1024];
    private const int MaxMessageBytes = 1024 * 1024;

    public WebSocketCipTransport(WebSocket ws, TcpClient client, IPEndPoint? remote, int localPort, string carrier)
    {
        _ws = ws;
        _client = client;
        Remote = remote;
        LocalPort = localPort;
        Carrier = carrier;
    }

    public IPEndPoint? Remote { get; }
    public int LocalPort { get; }
    public string? Carrier { get; }
    public bool PacketPerWrite => true;

    public async ValueTask<(byte type, byte[] payload)?> ReadPacketAsync(CancellationToken ct)
    {
        while (_pending.Count == 0)
        {
            using var message = new MemoryStream();
            ValueWebSocketReceiveResult r;
            do
            {
                r = await _ws.ReceiveAsync(_chunk.AsMemory(), ct);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                message.Write(_chunk, 0, r.Count);
                if (message.Length > MaxMessageBytes) return null;
            } while (!r.EndOfMessage);
            if (r.MessageType != WebSocketMessageType.Binary) continue; // CIP is binary; text is not ours
            Split(message.GetBuffer().AsSpan(0, (int)message.Length), _pending);
        }
        return _pending.Dequeue();
    }

    /// <summary>
    /// The CIP packets in one message. Normally exactly one; several in a row are taken too, and a last packet whose
    /// length field says more than the message holds is cut at the end of the message instead of eating the next one.
    /// </summary>
    internal static void Split(ReadOnlySpan<byte> message, Queue<(byte, byte[])> into)
    {
        var i = 0;
        while (i + 3 <= message.Length)
        {
            var n = Math.Min((message[i + 1] << 8) | message[i + 2], message.Length - i - 3);
            into.Enqueue((message[i], message.Slice(i + 3, n).ToArray()));
            i += 3 + n;
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => _ws.SendAsync(data, WebSocketMessageType.Binary, true, ct);

    public void Close()
    {
        try { _ws.Abort(); } catch { }
        try { _client.Close(); } catch { }
    }

    public void Dispose()
    {
        Close();
        _ws.Dispose();
    }
}
