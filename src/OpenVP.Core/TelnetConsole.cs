using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenVP.Core;

/// <summary>
/// One console session, independent of how bytes arrive: telnet feeds it socket data, SSH feeds it channel data.
/// It handles the login (a user name if the authenticator wants one, then the password), line editing with the server doing the
/// echo, and runs each line through the <see cref="ConsoleShell"/>.
/// </summary>
public sealed class ConsoleConnection
{
    private enum Stage { User, Password, Shell, Closed }

    private readonly ConsoleShell _shell;
    private readonly IConsoleAuth? _auth;
    private readonly Action<string> _write;
    private readonly Action _close;
    private readonly StringBuilder _line = new();
    private readonly object _gate = new();
    private Stage _stage = Stage.Shell;
    private string? _user;
    private int _wrong;
    private bool _sawCr, _delaying;
    private int _iac; // 0 none, 1 saw IAC, 2 expecting option byte, 3 in SB
    private Task _work = Task.CompletedTask;

    public ConsoleConnection(ConsoleShell shell, IConsoleAuth? auth, ConsoleSession session, Action<string> write, Action close)
    {
        _shell = shell;
        _auth = auth;
        Session = session;
        _write = write;
        _close = close;
    }

    public ConsoleSession Session { get; }
    /// <summary>True for telnet: bytes may carry IAC option negotiation, which is skipped.</summary>
    public bool Telnet { get; init; }
    /// <summary>How long to wait after a wrong password before asking again.</summary>
    public TimeSpan FailDelay { get; set; } = TimeSpan.FromMilliseconds(1500);
    public int MaxWrongPerSession { get; init; } = 3;
    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;
    public bool Closed => _stage == Stage.Closed;
    /// <summary>Log lines (login, wrong password, commands).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Begin the session. With <paramref name="authenticated"/> (SSH already checked the password) it goes straight to the prompt.</summary>
    public void Start(bool authenticated = false)
    {
        if (authenticated || _auth == null)
        {
            _user = Session.User;
            _stage = Stage.Shell;
            _write(_shell.Banner + _shell.Prompt(Session) + " ");
            return;
        }
        _write(_shell.Banner);
        if (_auth.RequiresUsername) { _stage = Stage.User; _write("Login: "); }
        else { _stage = Stage.Password; _write("Password: "); }
    }

    /// <summary>Run one command without a login (SSH <c>exec</c> requests): returns the text.</summary>
    public string RunOnce(string command) => _shell.Execute(Session, command).Output;

    public void Close()
    {
        if (_stage == Stage.Closed) return;
        _stage = Stage.Closed;
        try { _close(); } catch { }
    }

    /// <summary>Bytes from the user.</summary>
    public void Feed(byte[] data, int count)
    {
        LastActivity = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            for (var i = 0; i < count && _stage != Stage.Closed; i++)
            {
                var b = data[i];
                if (Telnet && SkipTelnet(b)) continue;
                if (_delaying) continue;
                Input(b);
            }
        }
    }

    private bool SkipTelnet(byte b)
    {
        switch (_iac)
        {
            case 0:
                if (b == 255) { _iac = 1; return true; }
                return false;
            case 1:
                if (b is >= 251 and <= 254) { _iac = 2; return true; }
                if (b == 250) { _iac = 3; return true; }
                _iac = 0;
                return b != 255; // IAC IAC is a literal 255; any other command is dropped
            case 2: _iac = 0; return true;
            default: // inside an SB ... IAC SE block
                if (b == 240) _iac = 0;
                return true;
        }
    }

    private bool _escape; private int _escapeLen;

    private void Input(byte b)
    {
        if (_escape) // ESC [ ... final letter: arrow keys and the like are ignored
        {
            _escapeLen++;
            if (b is >= 0x40 and <= 0x7E && _escapeLen > 1 || _escapeLen > 6) _escape = false;
            return;
        }
        switch (b)
        {
            case 27: _escape = true; _escapeLen = 0; return;
            case 13: _sawCr = true; EndLine(); return;
            case 10: if (_sawCr) { _sawCr = false; return; } EndLine(); return;
            case 0: if (_sawCr) { _sawCr = false; } return;
            case 8 or 127:
                _sawCr = false;
                if (_line.Length > 0) { _line.Length--; if (_stage != Stage.Password) _write("\b \b"); }
                return;
            case 3: // Ctrl-C
                _sawCr = false; _line.Clear(); _write("^C\r\n" + PromptText()); return;
            case 4: // Ctrl-D on an empty line ends the session
                if (_line.Length == 0 && _stage == Stage.Shell) { _write("\r\n"); Close(); }
                return;
        }
        _sawCr = false;
        if (b < 32) return;
        if (_line.Length >= 1024) return;
        _line.Append((char)b);
        if (_stage != Stage.Password) _write(((char)b).ToString());
    }

    private string PromptText() => _stage switch
    {
        Stage.User => "Login: ",
        Stage.Password => "Password: ",
        _ => _shell.Prompt(Session) + " ",
    };

    private void EndLine()
    {
        var text = _line.ToString();
        _line.Clear();
        _write("\r\n");
        switch (_stage)
        {
            case Stage.User:
                _user = text.Trim();
                _stage = Stage.Password;
                _write("Password: ");
                break;
            case Stage.Password:
                Login(text);
                break;
            case Stage.Shell:
                RunLine(text);
                break;
        }
    }

    private void Login(string password)
    {
        var result = _auth!.Authenticate(_user, password, Session.Remote);
        if (result.Ok)
        {
            Session.User = result.User;
            Session.Access = result.Access;
            _stage = Stage.Shell;
            Log?.Invoke($"{Session.Transport} console: {result.User ?? "user"} logged in from {Session.Remote}");
            _write(_shell.Prompt(Session) + " ");
            return;
        }
        _wrong++;
        Log?.Invoke($"{Session.Transport} console: wrong password from {Session.Remote}");
        if (_wrong >= MaxWrongPerSession || (result.Message?.StartsWith("Too many", StringComparison.Ordinal) ?? false) || (result.Message?.Contains("switched off") ?? false))
        {
            _write((result.Message ?? "Login failed.") + "\r\n");
            Close();
            return;
        }
        _delaying = true;
        _ = Task.Delay(FailDelay).ContinueWith(_ =>
        {
            lock (_gate)
            {
                _delaying = false;
                if (_stage == Stage.Closed) return;
                _write("Login incorrect.\r\n");
                if (_auth.RequiresUsername) { _stage = Stage.User; _write("Login: "); } else _write("Password: ");
            }
        });
    }

    private void RunLine(string text)
    {
        if (text.Trim().Length == 0) { _write(_shell.Prompt(Session) + " "); return; }
        // Commands can take a while (a program reload). Run them one at a time, off the reading thread.
        var prev = _work;
        _work = prev.ContinueWith(_ =>
        {
            ConsoleResult r;
            try { r = _shell.Execute(Session, text); }
            catch (Exception e) { r = new($"Error: {e.Message}\r\n"); }
            if (_stage == Stage.Closed) return;
            _write(r.Output);
            if (r.Close) { Close(); return; }
            _write(_shell.Prompt(Session) + " ");
        });
    }
}

/// <summary>
/// The telnet console (default port 41795, like a processor's console). It is off unless enabled, then listens on every
/// interface; logins need the access key. Connections are capped (<see cref="ConsoleGate"/>) and idle ones are closed.
/// </summary>
public sealed class TelnetServer : IDisposable
{
    private readonly ConsoleShell _shell;
    private readonly IConsoleAuth _auth;
    private readonly ConsoleGate _gate;
    private readonly Action<string> _log;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public TelnetServer(ConsoleShell shell, IConsoleAuth auth, ConsoleGate gate, Action<string> log)
    {
        _shell = shell; _auth = auth; _gate = gate; _log = log;
    }

    public int Port { get; private set; }
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan FailDelay { get; set; } = TimeSpan.FromMilliseconds(1500);

    public void Start(IPAddress bind, int port)
    {
        _listener = new TcpListener(bind, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_listener, _cts.Token);
        _log(bind.Equals(IPAddress.Any) || bind.Equals(IPAddress.IPv6Any)
            ? $"Telnet console listening on all interfaces, port {Port}. Logins need the access key."
            : $"Telnet console listening on {bind}:{Port}. Logins need the access key.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Serve(c, ct);
        }
    }

    private async Task Serve(TcpClient c, CancellationToken ct)
    {
        var remote = (c.Client.RemoteEndPoint as IPEndPoint)?.Address is { } a ? (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString() : "unknown";
        using var _ = c;
        IDisposable lease;
        var stream = c.GetStream();
        if (!_gate.TryEnter(remote, out lease))
        {
            _log($"Telnet console: refused a connection from {remote} (too many sessions open).");
            try { await stream.WriteAsync(Encoding.ASCII.GetBytes("Too many console sessions are open. Try again later.\r\n"), ct); } catch { }
            return;
        }
        using var leaseGuard = lease;
        c.NoDelay = true;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var session = new ConsoleSession { Remote = remote, Transport = "telnet" };
        var conn = new ConsoleConnection(_shell, _auth, session,
            text => { try { var b = Encoding.UTF8.GetBytes(text); lock (stream) stream.Write(b, 0, b.Length); } catch { } },
            () => { try { cts.Cancel(); } catch { } })
        { Telnet = true, FailDelay = FailDelay, Log = _log };
        try
        {
            // IAC WILL ECHO, IAC WILL SUPPRESS-GO-AHEAD: character at a time, the server echoes (so the password stays hidden).
            await stream.WriteAsync(new byte[] { 255, 251, 1, 255, 251, 3 }, cts.Token);
            conn.Start();
            var buf = new byte[1024];
            while (!cts.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                idle.CancelAfter(IdleTimeout);
                int n;
                try { n = await stream.ReadAsync(buf, idle.Token); }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    try { await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\nSession closed (idle).\r\n"), CancellationToken.None); } catch { }
                    break;
                }
                if (n == 0) break;
                conn.Feed(buf, n);
            }
        }
        catch { }
        finally { conn.Close(); }
    }

    public void Dispose() => Stop();
}
