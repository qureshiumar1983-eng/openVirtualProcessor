using System.Net;
using System.Reflection;
using System.Text;
using FxSsh;
using FxSsh.Services;
using OpenVP.Core;

namespace OpenVP.Server;

/// <summary>
/// The SSH side of the processor-style console (FxSsh, MIT). Password logins only: the password is the OpenVP access key, checked
/// by the same <see cref="IConsoleAuth"/> and wrong-password limiter as telnet. A shell request opens an interactive console; an
/// <c>exec</c> request (<c>ssh host ver</c>) runs one command. Public-key logins, port forwarding and subsystems are refused.
/// </summary>
public sealed class SshConsoleServer : IDisposable
{
    private readonly ConsoleShell _shell;
    private readonly IConsoleAuth _auth;
    private readonly ConsoleGate _gate;
    private readonly string _hostKeyFile;
    private readonly Action<string> _log;
    private SshServer? _server;

    public SshConsoleServer(ConsoleShell shell, IConsoleAuth auth, ConsoleGate gate, string hostKeyFile, Action<string> log)
    {
        _shell = shell; _auth = auth; _gate = gate; _hostKeyFile = hostKeyFile; _log = log;
    }

    public int Port { get; private set; }
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public void Start(IPAddress bind, int port)
    {
        var pem = LoadOrCreateHostKey();
        var server = new SshServer(new StartingInfo(bind, port, "SSH-2.0-OpenVP"));
        server.AddHostKey("rsa-sha2-256", pem);
        server.AddHostKey("rsa-sha2-512", pem);
        server.AddHostKey("ssh-rsa", pem);
        server.ConnectionAccepted += (_, session) => Accepted(session);
        server.ExceptionRaised += (_, e) => { };
        server.Start();
        _server = server;
        Port = port;
        _log(bind.Equals(IPAddress.Any) || bind.Equals(IPAddress.IPv6Any)
            ? $"SSH console listening on all interfaces, port {port}. Logins need the access key as the password."
            : $"SSH console listening on {bind}:{port}. Logins need the access key as the password.");
    }

    private string LoadOrCreateHostKey()
    {
        if (File.Exists(_hostKeyFile))
        {
            var existing = File.ReadAllText(_hostKeyFile);
            if (existing.Contains("PRIVATE KEY")) return existing;
        }
        var pem = KeyGenerator.GenerateRsaKeyPem(2048);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_hostKeyFile))!);
        File.WriteAllText(_hostKeyFile, pem);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_hostKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _log($"SSH console: made a new host key ({_hostKeyFile}). Keep it to avoid host key warnings.");
        return pem;
    }

    private static string RemoteOf(Session s)
    {
        try
        {
            var sock = typeof(Session).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(s) as System.Net.Sockets.Socket;
            if (sock?.RemoteEndPoint is IPEndPoint ep) return (ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address).ToString();
        }
        catch (Exception) { }
        return "ssh-client";
    }

    private void Accepted(Session session)
    {
        var remote = RemoteOf(session);
        if (!_gate.TryEnter(remote, out var lease))
        {
            _log($"SSH console: refused a connection from {remote} (too many sessions open).");
            try { session.Disconnect(DisconnectReason.TooManyConnections, "Too many console sessions are open."); } catch { }
            return;
        }
        string? user = null; IConsoleAccess? access = null;
        ConsoleConnection? conn = null;
        Timer? idle = null;
        var closed = 0;
        void Cleanup()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            idle?.Dispose();
            lease.Dispose();
        }
        session.Disconnected += (_, _) => Cleanup();
        session.ServiceRegistered += (_, service) =>
        {
            if (service is UserAuthService ua)
            {
                ua.UserAuth += (_, a) =>
                {
                    if (a.AuthMethod != "password") { a.Result = false; return; }
                    var r = _auth.Authenticate(a.Username, a.Password ?? "", remote);
                    a.Result = r.Ok;
                    if (r.Ok) { user = r.User; access = r.Access; _log($"SSH console: {r.User} logged in from {remote}"); }
                    else { _log($"SSH console: wrong password from {remote}"); Thread.Sleep(1000); }
                };
            }
            else if (service is ConnectionService cs)
            {
                cs.CommandOpened += (_, a) =>
                {
                    if (a.ShellType is not ("shell" or "exec")) { a.Agreed = false; return; }
                    a.Agreed = true;
                    var ch = a.Channel;
                    var info = new ConsoleSession { Remote = remote, Transport = "ssh", User = user, Access = access };
                    void Send(string text) { try { ch.SendData(Encoding.UTF8.GetBytes(text)); } catch { } }
                    if (a.ShellType == "exec")
                    {
                        var cmd = a.CommandText ?? "";
                        _ = Task.Run(() =>
                        {
                            var c = new ConsoleConnection(_shell, null, info, Send, () => { });
                            Send(c.RunOnce(cmd));
                            try { ch.SendEof(); ch.SendClose(0); } catch { }
                        });
                        return;
                    }
                    conn = new ConsoleConnection(_shell, null, info, Send, () => { try { ch.SendEof(); ch.SendClose(0); } catch { } }) { Log = _log };
                    ch.DataReceived += (_, data) => { var b = data.ToArray(); conn.Feed(b, b.Length); };
                    idle = new Timer(_ =>
                    {
                        if (DateTimeOffset.UtcNow - conn.LastActivity > IdleTimeout) { Send("\r\nSession closed (idle).\r\n"); conn.Close(); }
                    }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                    conn.Start(authenticated: true);
                };
            }
        };
    }

    public void Dispose()
    {
        try { _server?.Stop(); _server?.Dispose(); } catch { }
        _server = null;
    }
}
