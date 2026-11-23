using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Crestron.SimplSharp.Ssh;
using Crestron.SimplSharp.Ssh.Common;
using Crestron.SimplSharp.Ssh.Messages.Transport;
using FxSsh;
using FxSsh.Services;
using SshConnectionException = Crestron.SimplSharp.Ssh.Common.SshConnectionException;
using DisconnectReason = Crestron.SimplSharp.Ssh.Messages.Transport.DisconnectReason;

namespace OpenVP.Tests;

/// <summary>Crestron.SimplSharp.Ssh (SshClient, ShellStream, SshCommand, the authentication methods) against an in-process SSH server.</summary>
public static class SshClientTests
{
    /// <summary>A small SSH server on loopback: user "admin" with password "pw" or an allowed public key; a shell that echoes lines; a few commands.</summary>
    private sealed class FakeSsh : IDisposable
    {
        private readonly SshServer _server;
        public readonly int Port = IntegrationServiceTests.FreePort();
        public byte[]? AllowedKey;
        public readonly ConcurrentQueue<string> Attempts = new();
        public readonly ConcurrentQueue<string> Pty = new();

        public FakeSsh()
        {
            var pem = KeyGenerator.GenerateRsaKeyPem(2048);
            _server = new SshServer(new StartingInfo(IPAddress.Loopback, Port, "SSH-2.0-TestServer"));
            _server.AddHostKey("rsa-sha2-256", pem);
            _server.AddHostKey("ssh-rsa", pem);
            _server.ConnectionAccepted += (_, session) =>
            {
                session.ServiceRegistered += (_, service) =>
                {
                    if (service is UserAuthService ua)
                        ua.UserAuth += (_, a) =>
                        {
                            Attempts.Enqueue(a.AuthMethod + ":" + a.Username);
                            a.Result = a.Username == "admin" && ((a.AuthMethod == "password" && a.Password == "pw") || (a.AuthMethod == "publickey" && AllowedKey != null && a.Key.SequenceEqual(AllowedKey)));
                        };
                    else if (service is ConnectionService cs)
                    {
                        cs.PtyReceived += (_, p) => Pty.Enqueue($"{p.Terminal} {p.WidthChars}x{p.HeightRows}");
                        cs.CommandOpened += (_, c) => Command(c);
                    }
                };
            };
            _server.ExceptionRaised += (_, _) => { };
            _server.Start();
        }

        private static readonly object SendGate = new();

        private static void Command(CommandRequestedArgs c)
        {
            c.Agreed = true;
            var ch = c.Channel;
            void Say(string s) { lock (SendGate) { try { ch.SendData(Encoding.UTF8.GetBytes(s)); } catch { } } }
            if (c.ShellType == "exec")
            {
                _ = Task.Run(() =>
                {
                    Thread.Sleep(40); // let the server library send its "yes" before any output, as a real server does
                    var cmd = c.CommandText ?? "";
                    var code = 0u;
                    if (cmd.StartsWith("echo ")) Say(cmd[5..] + "\n");
                    else if (cmd == "fail") { Say("boom\n"); code = 3; }
                    else if (cmd == "slow") Thread.Sleep(2500);
                    else if (cmd == "big") Say(new string('x', 200_000));
                    else { Say("unknown\n"); code = 127; }
                    lock (SendGate) { try { ch.SendEof(); ch.SendClose(code); } catch { } }
                });
                return;
            }
            var line = new StringBuilder();
            Say("Welcome\r\nlogin> ");
            ch.DataReceived += (_, data) =>
            {
                foreach (var b in data.ToArray())
                {
                    if (b is (byte)'\r' or (byte)'\n')
                    {
                        if (line.Length == 0) continue;
                        var text = line.ToString(); line.Clear();
                        if (text == "exit") { Say("bye\r\n"); try { ch.SendEof(); ch.SendClose(0); } catch { } return; }
                        Say($"echo:{text}\r\nlogin> ");
                    }
                    else line.Append((char)b);
                }
            };
        }

        public void Dispose() { try { _server.Stop(); } catch { } try { _server.Dispose(); } catch { } }
    }

    private static SshClient Client(FakeSsh s, string user = "admin", string pw = "pw") => new("127.0.0.1", s.Port, user, pw);

    [Test]
    public static void Password_login_runs_commands_with_output_and_exit_status_on_one_connection()
    {
        using var s = new FakeSsh();
        using var c = Client(s);
        string? seenFingerprint = null;
        c.HostKeyReceived += (_, e) => seenFingerprint = Convert.ToHexString(e.FingerPrint);
        Assert.True(!c.IsConnected, "not connected yet");
        c.Connect();
        Assert.True(c.IsConnected, "connected");
        Assert.True(seenFingerprint is { Length: 32 }, "the host key's MD5 fingerprint is reported: " + seenFingerprint);
        Assert.True(c.ConnectionInfo.ServerVersion == "SSH-2.0-TestServer", "server version: " + c.ConnectionInfo.ServerVersion);

        var echo = c.RunCommand("echo hello world");
        Assert.Equal("hello world\n", echo.Result, "stdout");
        Assert.Equal(0, echo.ExitStatus, "exit status 0");
        var fail = c.RunCommand("fail");
        Assert.Equal("boom\n", fail.Result, "output of a failing command");
        Assert.Equal(3, fail.ExitStatus, "exit status 3");
        Assert.Equal(127, c.RunCommand("nope").ExitStatus, "exit status of an unknown command");

        // A command made first and run later, twice; and several at once on the same connection.
        var cmd = c.CreateCommand("echo a");
        Assert.Equal("a\n", cmd.Execute(), "Execute()");
        Assert.Equal("b\n", cmd.Execute("echo b"), "Execute(text) again");
        var many = Enumerable.Range(0, 3).Select(i => Task.Run(() => c.RunCommand("echo n" + i).Result)).ToArray();
        Task.WaitAll(many.Cast<Task>().ToArray(), 10000);
        Assert.True(many.Select(t => t.Result).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 3).Select(i => $"n{i}\n")), "three commands in parallel each got their own output");

        Assert.Equal(200_000, c.RunCommand("big").Result.Length, "a large result needs the receive window to be topped up");
        var slow = c.CreateCommand("slow"); slow.CommandTimeout = TimeSpan.FromMilliseconds(500);
        Assert.True(Assert.Throws<SshOperationTimeoutException>(() => slow.Execute()).Message.Contains("timed out"), "CommandTimeout");

        c.SendKeepAlive();
        Assert.True(c.IsConnected, "still connected after a keep-alive");
        c.Disconnect();
        Assert.True(!c.IsConnected, "disconnected");
        Assert.True(Assert.Throws<SshConnectionException>(() => c.RunCommand("echo x")) != null, "no commands once disconnected");
    }

    [Test]
    public static void Shell_stream_expects_prompts_reads_lines_and_reports_close()
    {
        using var s = new FakeSsh();
        using var c = Client(s);
        c.Connect();
        using var shell = c.CreateShellStream("vt100", 100, 30, 800, 600, 4096);
        var received = new ConcurrentQueue<string>();
        shell.DataReceived += (_, e) => received.Enqueue(e.Line);
        var closed = new ManualResetEventSlim();
        shell.Closed += (_, _) => closed.Set();
        Assert.Equal("Welcome\r\nlogin> ", shell.Expect("login> ", TimeSpan.FromSeconds(5)), "banner up to and including the prompt");
        Assert.True(s.Pty.Contains("vt100 100x30"), "terminal request: " + string.Join(";", s.Pty));
        Assert.True(!shell.DataAvailable, "buffer empty after Expect took it");

        shell.WriteLine("status");
        var reply = shell.Expect("login> ", TimeSpan.FromSeconds(5));
        Assert.Equal("echo:status\r\nlogin> ", reply, "the reply to one line");
        shell.WriteLine("one"); Assert.True(shell.Expect(new System.Text.RegularExpressions.Regex(@"echo:\w+\r\n"), TimeSpan.FromSeconds(5)) == "echo:one\r\n", "regex expect leaves the rest");
        Assert.Equal("login> ", shell.Read(), "Read takes what is left");
        Assert.True(received.Count > 0, "DataReceived events fired");

        shell.Write("two\r");
        Assert.Equal("echo:two", shell.ReadLine(TimeSpan.FromSeconds(5)), "ReadLine waits for a whole line");
        Assert.True(shell.ReadLine() == null, "ReadLine() with nothing complete returns null");
        Assert.True(shell.Expect("never appears", TimeSpan.FromMilliseconds(300)) == null, "Expect times out with null");

        // ExpectAction: run the action of the first match.
        shell.WriteLine("three");
        string? acted = null;
        var text = shell.Expect(TimeSpan.FromSeconds(5), new ExpectAction("nomatch", _ => acted = "wrong"), new ExpectAction("echo:three", t => acted = "right"));
        Assert.True(text != null && text.EndsWith("echo:three") && acted == "right", "ExpectAction");

        shell.Read();
        shell.WriteLine("exit");
        Assert.True(closed.Wait(5000), "Closed fires when the server ends the shell");
        Assert.True(!shell.CanWrite, "no writing after close");
        Assert.True(Assert.Throws<SshConnectionException>(() => shell.WriteLine("x")) != null, "writing to a closed shell throws");
    }

    [Test]
    public static void Wrong_passwords_and_untrusted_hosts_fail_with_the_right_exception()
    {
        using var s = new FakeSsh();
        using var bad = Client(s, pw: "wrong");
        var e = Assert.Throws<SshAuthenticationException>(() => bad.Connect());
        Assert.True(e.Message.Contains("password"), "names the method: " + e.Message);
        Assert.True(!bad.IsConnected, "not connected");

        // Methods are tried in order: the first is refused, the second works.
        var info = new ConnectionInfo("127.0.0.1", s.Port, "admin", new PasswordAuthenticationMethod("admin", "nope"), new PasswordAuthenticationMethod("admin", "pw"));
        using var two = new SshClient(info);
        two.Connect();
        Assert.True(two.IsConnected, "the second method got in");
        Assert.True(s.Attempts.Count(a => a == "password:admin") >= 3, "the server saw each attempt: " + string.Join(",", s.Attempts));

        using var distrust = Client(s);
        distrust.HostKeyReceived += (_, ev) => ev.CanTrust = false;
        var ce = Assert.Throws<SshConnectionException>(() => distrust.Connect());
        Assert.Equal(DisconnectReason.HostKeyNotVerifiable, ce.DisconnectReason, "refusing the host key");

        using var nobody = new SshClient("127.0.0.1", IntegrationServiceTests.FreePort(), "admin", "pw");
        Assert.True(Assert.Throws<SshConnectionException>(() => nobody.Connect()) != null, "nothing listening");
        using var noMethods = new SshClient(new ConnectionInfo("127.0.0.1", s.Port, "admin"));
        Assert.True(Assert.Throws<SshAuthenticationException>(() => noMethods.Connect()).Message.Contains("no authentication method"), "no methods given");
    }

    [Test]
    public static void Private_key_login_works_with_rsa_and_ecdsa_keys_and_passphrases()
    {
        using var s = new FakeSsh();
        // A key file is found the way a program's files are: under the program's own folder, here slot 1's.
        using var proc = new OpenVP.Core.Processor(Paths.TempData());
        OpenVP.SimplSharp.OpenVPBridge.Slot = proc.Slot(1);
        var nv = Path.Combine(proc.Slot(1).Directory, "nvram"); Directory.CreateDirectory(nv);
        var dir = nv;
        string Mapped(string full) => "\\NVRAM\\" + Path.GetRelativePath(nv, full).Replace('/', '\\');
        try
        {
            using var rsa = RSA.Create(2048);
            var p = rsa.ExportParameters(false);
            // The public key as the SSH wire blob: string "ssh-rsa", mpint e, mpint n.
            byte[] Str(byte[] v) => BitConverter.GetBytes(v.Length).Reverse().Concat(v).ToArray();
            byte[] Mp(byte[] v) { var l = v.SkipWhile(b => b == 0).ToArray(); if (l.Length > 0 && (l[0] & 0x80) != 0) l = new byte[] { 0 }.Concat(l).ToArray(); return Str(l); }
            s.AllowedKey = Str(Encoding.ASCII.GetBytes("ssh-rsa")).Concat(Mp(p.Exponent!)).Concat(Mp(p.Modulus!)).ToArray();

            // PKCS#1, PKCS#8 and passphrase-protected PKCS#8 all work.
            foreach (var (name, pem, pass) in new[]
            {
                ("pkcs1", rsa.ExportRSAPrivateKeyPem(), (string?)null),
                ("pkcs8", rsa.ExportPkcs8PrivateKeyPem(), null),
                ("enc", rsa.ExportEncryptedPkcs8PrivateKeyPem("hunter2", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2000)), "hunter2")
            })
            {
                var file = Path.Combine(dir, name + ".pem"); File.WriteAllText(file, pem);
                using var key = pass == null ? new PrivateKeyFile(Mapped(file)) : new PrivateKeyFile(Mapped(file), pass);
                Assert.Equal("ssh-rsa", key.HostKeyAlgorithm, "key type");
                using var c = new SshClient(new ConnectionInfo("127.0.0.1", s.Port, "admin", new PrivateKeyAuthenticationMethod("admin", key)));
                c.Connect();
                Assert.Equal("ok\n", c.RunCommand("echo ok").Result, "logged in with the " + name + " key");
            }
            Assert.True(s.Attempts.Contains("publickey:admin"), "the server saw public-key attempts");

            // An unencrypted OpenSSH-format file (what ssh-keygen writes) with the same RSA key.
            {
                var full = rsa.ExportParameters(true);
                byte[] U32(int v) => BitConverter.GetBytes(v).Reverse().ToArray();
                byte[] S(byte[] v) => U32(v.Length).Concat(v).ToArray();
                byte[] S1(string v) => S(Encoding.ASCII.GetBytes(v));
                var priv = new List<byte>();
                priv.AddRange(U32(0x1234abcd)); priv.AddRange(U32(0x1234abcd));
                priv.AddRange(S1("ssh-rsa")); priv.AddRange(Mp(full.Modulus!)); priv.AddRange(Mp(full.Exponent!)); priv.AddRange(Mp(full.D!)); priv.AddRange(Mp(full.InverseQ!)); priv.AddRange(Mp(full.P!)); priv.AddRange(Mp(full.Q!));
                priv.AddRange(S1("comment"));
                for (byte i = 1; priv.Count % 8 != 0; i++) priv.Add(i);
                var file = new List<byte>(Encoding.ASCII.GetBytes("openssh-key-v1\0"));
                file.AddRange(S1("none")); file.AddRange(S1("none")); file.AddRange(S(Array.Empty<byte>())); file.AddRange(U32(1)); file.AddRange(S(s.AllowedKey!)); file.AddRange(S(priv.ToArray()));
                var path = Path.Combine(dir, "id_rsa");
                File.WriteAllText(path, "-----BEGIN OPENSSH PRIVATE KEY-----\n" + Convert.ToBase64String(file.ToArray(), Base64FormattingOptions.InsertLineBreaks) + "\n-----END OPENSSH PRIVATE KEY-----\n");
                using var key = new PrivateKeyFile(Mapped(path));
                using var c = new SshClient("127.0.0.1", s.Port, "admin", key);
                c.Connect();
                Assert.Equal("openssh\n", c.RunCommand("echo openssh").Result, "logged in with an OpenSSH-format key");
            }

            // An ECDSA P-256 key (SEC1 PEM): parsed and used to sign, whether or not this test server verifies that kind of signature.
            {
                using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var path = Path.Combine(dir, "ec.pem"); File.WriteAllText(path, ec.ExportECPrivateKeyPem());
                using var key = new PrivateKeyFile(Mapped(path));
                Assert.Equal("ecdsa-sha2-nistp256", key.HostKeyAlgorithm, "EC key type");
                var q = ec.ExportParameters(false).Q;
                var saved = s.AllowedKey;
                s.AllowedKey = Str(Encoding.ASCII.GetBytes("ecdsa-sha2-nistp256")).Concat(Str(Encoding.ASCII.GetBytes("nistp256"))).Concat(Str(new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray())).ToArray();
                using var c = new SshClient("127.0.0.1", s.Port, "admin", key);
                c.Connect();
                Assert.Equal("ec\n", c.RunCommand("echo ec").Result, "logged in with an ECDSA key");
                s.AllowedKey = saved;
            }

            // A different key is refused.
            using var other = RSA.Create(2048);
            var otherFile = Path.Combine(dir, "other.pem"); File.WriteAllText(otherFile, other.ExportRSAPrivateKeyPem());
            using var okey = new PrivateKeyFile(Mapped(otherFile));
            using var denied = new SshClient(new ConnectionInfo("127.0.0.1", s.Port, "admin", new PrivateKeyAuthenticationMethod("admin", okey)));
            Assert.True(Assert.Throws<SshAuthenticationException>(() => denied.Connect()).Message.Contains("publickey"), "an unknown key is refused");

            // The key files that can't be used say why.
            var enc = Path.Combine(dir, "enc.pem");
            Assert.True(Assert.Throws<SshException>(() => new PrivateKeyFile(Mapped(enc), "wrong")).Message.Contains("passphrase"), "wrong passphrase");
            Assert.True(Assert.Throws<SshException>(() => new PrivateKeyFile(Mapped(enc))).Message.Contains("needs a passphrase"), "missing passphrase");
            var junk = Path.Combine(dir, "junk.pem"); File.WriteAllText(junk, "not a key");
            Assert.True(Assert.Throws<SshException>(() => new PrivateKeyFile(Mapped(junk))).Message.Contains("PEM"), "not a key");
            var ed = Path.Combine(dir, "ed.pem");
            File.WriteAllText(ed, "-----BEGIN OPENSSH PRIVATE KEY-----\n" + Convert.ToBase64String(Encoding.ASCII.GetBytes("openssh-key-v1\0").Concat(new byte[] { 0, 0, 0, 4 }).Concat(Encoding.ASCII.GetBytes("none")).Concat(new byte[] { 0, 0, 0, 4 }).Concat(Encoding.ASCII.GetBytes("none")).Concat(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 }).ToArray()) + "\n-----END OPENSSH PRIVATE KEY-----\n");
            Assert.True(Assert.Throws<SshException>(() => new PrivateKeyFile(Mapped(ed))).Message.Length > 0, "a truncated OpenSSH file is refused cleanly");
        }
        finally { OpenVP.SimplSharp.OpenVPBridge.Slot = null; }
    }

    [Test]
    public static void Server_closing_the_connection_raises_the_error_event_and_ends_open_shells()
    {
        var s = new FakeSsh();
        using var c = Client(s);
        var errors = new ConcurrentQueue<Exception>();
        c.ErrorOccurred += (_, e) => errors.Enqueue(e.Exception);
        c.Connect();
        using var shell = c.CreateShellStream("vt100", 80, 24, 0, 0, 1024);
        shell.Expect("login> ", TimeSpan.FromSeconds(5));
        var closed = new ManualResetEventSlim(); shell.Closed += (_, _) => closed.Set();
        s.Dispose(); // the server goes away
        Assert.True(closed.Wait(8000), "the shell reports it closed");
        Assert.Eventually(() => !c.IsConnected, "client sees the loss", 8000);
    }
}
