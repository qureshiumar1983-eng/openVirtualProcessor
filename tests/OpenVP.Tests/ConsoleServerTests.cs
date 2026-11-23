using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenVP.Core;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

/// <summary>The processor-style console: commands, login, wrong-password limits, connection caps, and the telnet listener.</summary>
public static class ConsoleServerTests
{
    private static Processor WithSample()
    {
        var p = new Processor(Paths.TempData(), 3);
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        return p;
    }

    private static string Run(ConsoleShell shell, ConsoleSession s, string line) => shell.Execute(s, line).Output;

    [Test]
    public static void Shell_answers_the_processor_style_commands()
    {
        using var p = WithSample();
        var shell = new ConsoleShell(p, "9.9.9", "TESTBOX");
        var s = new ConsoleSession { Remote = "127.0.0.1" };
        Assert.True(Run(shell, s, "ver").Contains("v9.9.9") && Run(shell, s, "VER").Contains("TESTBOX"), "ver");
        Assert.Equal("Host Name: TESTBOX\r\n", Run(shell, s, "hostname"), "hostname");
        Assert.True(Run(shell, s, "uptime").Contains("running for"), "uptime");
        Assert.True(Run(shell, s, "time").Contains(DateTime.Now.Year.ToString()), "time");
        Assert.True(Run(shell, s, "info").Contains("Slots: 3, 1 running"), "info");
        Assert.True(Run(shell, s, "slots").Contains("Slot  1") && Run(shell, s, "slots").Contains("Running"), "slots");
        Assert.True(Run(shell, s, "help").Contains("PROGRESET") && Run(shell, s, "help").Contains("sample"), "help lists the program's own command too");
        var ipt = Run(shell, s, "ipt");
        Assert.True(ipt.Contains("IPID 03") && ipt.Contains("IPID 10") && !ipt.Contains("\n") == false && ipt.Contains("\r\n"), "ipt shows the IP table with CRLF: " + ipt);
        Assert.True(Run(shell, s, "ipt -p:2").Contains("empty"), "ipt for an empty slot");
        Assert.True(Run(shell, s, "cpuload").Length > 0 && Run(shell, s, "ramfree -p:1").Length > 0 && Run(shell, s, "taskstat -p:1").Length > 0, "metrics commands");
        Assert.True(Run(shell, s, "nonsense").Contains("Bad or incomplete command"), "unknown command");
        Assert.True(shell.Execute(s, "exit").Close, "exit closes");
    }

    [Test]
    public static void Shell_controls_programs_with_the_p_option()
    {
        using var p = WithSample();
        var shell = new ConsoleShell(p);
        var s = new ConsoleSession();
        Assert.True(Run(shell, s, "stopprog -p:1").Contains("stopped") && p.Slot(1).State == SlotState.Stopped, "stopprog");
        Assert.True(Run(shell, s, "startprog -p:1").Contains("started") && p.Slot(1).State == SlotState.Running, "startprog");
        var gen = p.Slot(1).Generation;
        var reset = Run(shell, s, "progreset -p:1");
        Assert.True(reset.Contains("reset") && p.Slot(1).State == SlotState.Running && p.Slot(1).Generation != gen, "progreset loads it again: " + reset);
        Assert.True(Run(shell, s, "progreset").Contains("Which program"), "needs -p");
        Assert.True(Run(shell, s, "progreset -p:99").Contains("no slot 99"), "bad slot");
        Assert.True(Run(shell, s, "progreset -p:x").Contains("-p: needs"), "bad number");
        Assert.True(Run(shell, s, "progreset -p:2").Contains("no program"), "empty slot");
        Assert.True(Run(shell, s, "progload -p:2").Contains("no program to load"), "progload of an empty slot says so");
        Assert.True(Run(shell, s, "progload -p:1 -f:../../etc/passwd").Contains("upload folder"), "progload -f stays in the upload folder");
        var up = Path.Combine(p.DataDirectory, "uploads", "slot02");
        Directory.CreateDirectory(up);
        File.Copy(Paths.SampleDll, Path.Combine(up, "SampleProgram.dll"));
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(Paths.SampleDll)!)) File.Copy(f, Path.Combine(up, Path.GetFileName(f)), true);
        Assert.True(Run(shell, s, "progload -p:2 -f:SampleProgram.dll").Contains("loaded") && p.Slot(2).State == SlotState.Running, "progload -f from the upload folder");
    }

    [Test]
    public static void Program_commands_run_in_the_slot_by_option_use_or_name()
    {
        using var p = WithSample();
        var shell = new ConsoleShell(p);
        var s = new ConsoleSession();
        Assert.True(Run(shell, s, "sample hello").Contains("sample ok"), "found by name in the slot that registered it");
        Assert.True(Run(shell, s, "sample -p:1 x").Contains("args=x"), "with -p");
        Assert.True(Run(shell, s, "prog 1 sample y").Contains("args=y"), "prog N");
        Assert.True(Run(shell, s, "use 1").Contains("slot 1") && shell.Prompt(s).Contains("PROG01"), "use selects the slot");
        Assert.True(Run(shell, s, "sample z").Contains("args=z"), "bare command goes to it");
        Run(shell, s, "use 0");
        Assert.True(Run(shell, s, "progcomments -p:1").Contains("SampleProgram"), "progcomments");
    }

    // ------------------------------------------------------------------ limits

    [Test]
    public static void Wrong_passwords_lock_an_address_out_and_a_good_one_clears_the_count()
    {
        var now = DateTimeOffset.UtcNow;
        var t = new AuthThrottle(clock: () => now);
        for (var i = 0; i < 4; i++) t.Failed("1.2.3.4");
        Assert.True(t.LockedFor("1.2.3.4") == null, "4 wrong is still fine");
        t.Failed("1.2.3.4");
        Assert.True(t.LockedFor("1.2.3.4") is { } left && left > TimeSpan.FromMinutes(4), "5 wrong locks it for about 5 minutes");
        Assert.True(t.LockedFor("5.6.7.8") == null, "another address is not locked");
        now += TimeSpan.FromMinutes(5.1);
        Assert.True(t.LockedFor("1.2.3.4") == null, "lockout ends");
        t.Failed("9.9.9.9"); t.Failed("9.9.9.9"); t.Succeeded("9.9.9.9");
        for (var i = 0; i < 4; i++) t.Failed("9.9.9.9");
        Assert.True(t.LockedFor("9.9.9.9") == null, "a good password cleared the earlier failures");
    }

    [Test]
    public static void Too_many_wrong_passwords_from_everywhere_lock_everyone()
    {
        var now = DateTimeOffset.UtcNow;
        var t = new AuthThrottle(perAddress: 100, global: 10, clock: () => now);
        for (var i = 0; i < 10; i++) t.Failed("10.0.0." + i);
        Assert.True(t.LockedFor("10.0.0.99") != null, "global limit");
        now += TimeSpan.FromMinutes(6);
        Assert.True(t.LockedFor("10.0.0.99") == null, "and it ends");
    }

    [Test]
    public static void Access_key_auth_checks_the_key_and_refuses_when_it_is_off()
    {
        string? key = "s3cret-key";
        var a = new AccessKeyAuth(() => key, new AuthThrottle());
        Assert.True(a.Authenticate("anyone", "s3cret-key", "1.1.1.1").Ok, "right key");
        Assert.True(!a.Authenticate(null, "s3cret-keyX", "1.1.1.1").Ok && !a.Authenticate(null, "", "1.1.1.1").Ok, "wrong or empty");
        Assert.Equal("admin", a.Authenticate(null, "s3cret-key", "1.1.1.1").User!, "default user name");
        key = null;
        var r = a.Authenticate(null, "anything", "2.2.2.2");
        Assert.True(!r.Ok && r.Message!.Contains("switched off"), "no key, no console");
        key = "k";
        for (var i = 0; i < 5; i++) a.Authenticate(null, "bad", "3.3.3.3");
        Assert.True(a.Authenticate(null, "k", "3.3.3.3") is { Ok: false } l && l.Message!.StartsWith("Too many"), "locked out even with the right key");
    }

    [Test]
    public static void The_gate_caps_total_and_per_address_sessions()
    {
        var g = new ConsoleGate { MaxTotal = 3, MaxPerAddress = 2 };
        Assert.True(g.TryEnter("a", out var l1), "first from one address");
        Assert.True(g.TryEnter("a", out var l2), "second from one address");
        Assert.True(!g.TryEnter("a", out _), "third from the same address refused");
        Assert.True(g.TryEnter("b", out var l3), "another address");
        Assert.True(!g.TryEnter("c", out _), "total of three reached");
        l1.Dispose(); l1.Dispose();
        Assert.Equal(2, g.Open, "a lease frees its slot once");
        Assert.True(g.TryEnter("c", out _), "room again");
        _ = l2; _ = l3;
    }

    // ------------------------------------------------------------------ a login on the line

    [Test]
    public static void A_connection_asks_for_the_password_then_runs_commands()
    {
        using var p = WithSample();
        var shell = new ConsoleShell(p, hostname: "BOX");
        var auth = new AccessKeyAuth(() => "good", new AuthThrottle());
        var output = new StringBuilder();
        var closed = false;
        var c = new ConsoleConnection(shell, auth, new ConsoleSession { Remote = "9.9.9.9" }, t => { lock (output) output.Append(t); }, () => closed = true) { FailDelay = TimeSpan.Zero };
        c.Start();
        void Type(string s) { var b = Encoding.ASCII.GetBytes(s); c.Feed(b, b.Length); }
        string Out() { lock (output) return output.ToString(); }
        Assert.True(Out().Contains("Password:"), "asks for a password");
        Type("nope\r\n");
        Assert.Eventually(() => Out().Contains("Login incorrect"), "wrong password is refused");
        Assert.True(!Out().Contains("nope"), "the password is never echoed");
        Type("hostname\r");
        Thread.Sleep(50);
        Assert.True(!Out().Contains("Host Name"), "no commands before login");
        Type("good\r\n");
        Assert.Eventually(() => Out().Contains("BOX>"), "prompt after the right password");
        Type("hostname\r\n");
        Assert.Eventually(() => Out().Contains("Host Name: BOX"), "a command runs");
        Type("vee\x7fr\r\n"); // backspace edits the line
        Assert.Eventually(() => Out().Contains("OpenVP virtual control processor [v"), "backspace");
        Type("exit\r\n");
        Assert.Eventually(() => closed, "exit closes the session");
    }

    [Test]
    public static void Three_wrong_passwords_end_the_session_and_telnet_negotiation_is_skipped()
    {
        using var p = new Processor(Paths.TempData(), 1);
        var shell = new ConsoleShell(p);
        var auth = new AccessKeyAuth(() => "good", new AuthThrottle());
        var output = new StringBuilder();
        var closed = false;
        var c = new ConsoleConnection(shell, auth, new ConsoleSession { Remote = "8.8.8.8" }, t => { lock (output) output.Append(t); }, () => closed = true) { Telnet = true, FailDelay = TimeSpan.Zero };
        c.Start();
        var neg = new byte[] { 255, 253, 1, 255, 250, 31, 0, 80, 0, 24, 255, 240 }; // DO ECHO, SB window size SE
        c.Feed(neg, neg.Length);
        for (var i = 0; i < 3; i++)
        {
            var b = Encoding.ASCII.GetBytes("bad" + i + "\r\n");
            c.Feed(b, b.Length);
            Thread.Sleep(30);
        }
        Assert.Eventually(() => closed, "closed after 3 wrong passwords");
    }

    // ------------------------------------------------------------------ the telnet listener, for real

    private static string ReadUntil(NetworkStream s, string text, int ms = 4000)
    {
        var sb = new StringBuilder();
        var buf = new byte[512];
        var end = Environment.TickCount64 + ms;
        s.ReadTimeout = 300;
        while (Environment.TickCount64 < end && !sb.ToString().Contains(text))
        {
            try { var n = s.Read(buf, 0, buf.Length); if (n == 0) break; sb.Append(Encoding.Latin1.GetString(buf, 0, n)); }
            catch (IOException) { }
        }
        return sb.ToString();
    }

    [Test]
    public static void Telnet_listener_logs_in_runs_commands_and_locks_out_guessers()
    {
        using var p = WithSample();
        var shell = new ConsoleShell(p, hostname: "TELBOX");
        var throttle = new AuthThrottle();
        var auth = new AccessKeyAuth(() => "open-sesame", throttle);
        var log = new List<string>();
        using var server = new TelnetServer(shell, auth, new ConsoleGate { MaxTotal = 4, MaxPerAddress = 4 }, l => { lock (log) log.Add(l); }) { FailDelay = TimeSpan.Zero };
        server.Start(IPAddress.Loopback, 0);
        Assert.True(server.Port > 0, "port assigned");
        Assert.True(log.Any(l => l.Contains("Telnet console listening")), "says what it listens on");

        using (var c = new TcpClient("127.0.0.1", server.Port))
        {
            var s = c.GetStream();
            Assert.True(ReadUntil(s, "Password:").Contains("Password:"), "password prompt");
            s.Write(Encoding.ASCII.GetBytes("open-sesame\r\n"));
            Assert.True(ReadUntil(s, "TELBOX>").Contains("TELBOX>"), "logged in");
            s.Write(Encoding.ASCII.GetBytes("ver\r\n"));
            Assert.True(ReadUntil(s, "Slots:").Contains("OpenVP virtual control processor"), "ver");
            s.Write(Encoding.ASCII.GetBytes("ipt\r\n"));
            Assert.True(ReadUntil(s, "IPID 10").Contains("IPID 03"), "ipt");
            s.Write(Encoding.ASCII.GetBytes("progreset -p:1\r\n"));
            Assert.True(ReadUntil(s, "is running").Contains("Program 1 reset"), "progreset");
            s.Write(Encoding.ASCII.GetBytes("exit\r\n"));
            Assert.True(ReadUntil(s, "Goodbye").Contains("Goodbye"), "exit");
        }

        // Guess wrongly from the same address until it is locked out.
        for (var i = 0; i < 2; i++)
        {
            using var g = new TcpClient("127.0.0.1", server.Port);
            var s = g.GetStream();
            ReadUntil(s, "Password:");
            for (var k = 0; k < 3; k++) { s.Write(Encoding.ASCII.GetBytes("guess" + k + "\r\n")); ReadUntil(s, "Password:", 1500); }
        }
        Assert.True(throttle.LockedFor("127.0.0.1") != null, "address locked after 5 wrong");
        using (var c = new TcpClient("127.0.0.1", server.Port))
        {
            var s = c.GetStream();
            ReadUntil(s, "Password:");
            s.Write(Encoding.ASCII.GetBytes("open-sesame\r\n"));
            Assert.True(ReadUntil(s, "Too many wrong passwords").Contains("Too many wrong passwords"), "even the right password is refused during the lockout");
        }
    }

    [Test]
    public static void Telnet_listener_refuses_connections_over_the_cap()
    {
        using var p = new Processor(Paths.TempData(), 1);
        var shell = new ConsoleShell(p);
        using var server = new TelnetServer(shell, new AccessKeyAuth(() => "k"), new ConsoleGate { MaxTotal = 1, MaxPerAddress = 1 }, _ => { });
        server.Start(IPAddress.Loopback, 0);
        using var first = new TcpClient("127.0.0.1", server.Port);
        ReadUntil(first.GetStream(), "Password:");
        using var second = new TcpClient("127.0.0.1", server.Port);
        Assert.True(ReadUntil(second.GetStream(), "Too many console sessions").Contains("Too many console sessions"), "second session refused");
    }
}
