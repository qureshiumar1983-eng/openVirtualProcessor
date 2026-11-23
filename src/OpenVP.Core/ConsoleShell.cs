using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenVP.Core;

/// <summary>Who is typing: set by the listener, used by the shell.</summary>
public sealed class ConsoleSession
{
    public string Remote { get; init; } = "";
    public string Transport { get; init; } = "telnet";
    public string? User { get; set; }
    /// <summary>The slot that <c>use N</c> selected; commands the shell doesn't know go to it. 0 = none.</summary>
    public int CurrentSlot { get; set; }
    /// <summary>What this person may run; null means everything (the access key, or no accounts yet).</summary>
    public IConsoleAccess? Access { get; set; }
}

public sealed record ConsoleResult(string Output, bool Close = false);

/// <summary>
/// The commands of the telnet and SSH console. It speaks like a processor console: <c>ver</c>, <c>ipt</c>, <c>progreset -p:N</c>,
/// <c>stopprog -p:N</c>, <c>info</c>, <c>hostname</c>, <c>uptime</c>, <c>time</c>, <c>cpuload</c>, <c>ramfree</c>, <c>taskstat</c>, <c>help</c>,
/// and the console commands that programs register (run through <see cref="ProgramSlot.RunConsoleCommand"/>, as the web console does).
/// It does no I/O of its own, so it is tested by calling <see cref="Execute"/>.
/// </summary>
public sealed class ConsoleShell
{
    private static readonly DateTimeOffset ProcessStart = Process.GetCurrentProcess().StartTime;
    private readonly Processor _processor;

    public ConsoleShell(Processor processor, string version = "0.1.0", string? hostname = null)
    {
        _processor = processor;
        Version = version;
        Hostname = hostname ?? Environment.MachineName;
    }

    public string Version { get; }
    public string Hostname { get; }

    public string Banner =>
        $"OpenVP virtual control processor {Version} console\r\nType HELP for a list of commands, EXIT to leave.\r\n";

    public string Prompt(ConsoleSession s) => s.CurrentSlot > 0 ? $"{Hostname.ToUpperInvariant()} PROG{s.CurrentSlot:00}>" : $"{Hostname.ToUpperInvariant()}>";

    /// <summary>Pulls <c>-p:N</c> (or <c>-p N</c>) out of the arguments. Returns the slot (0 if none) and what is left.</summary>
    public static (int Slot, string Remaining, bool Bad) TakeProgramOption(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var slot = 0; var bad = false;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (!p.StartsWith("-p:", StringComparison.OrdinalIgnoreCase) && !p.Equals("-p", StringComparison.OrdinalIgnoreCase)) continue;
            string? num = p.Length > 3 ? p[3..] : null;
            var drop = 1;
            if (num == null && i + 1 < parts.Count) { num = parts[i + 1]; drop = 2; }
            if (!int.TryParse(num, out slot)) { bad = true; slot = 0; }
            parts.RemoveRange(i, drop);
            break;
        }
        return (slot, string.Join(' ', parts), bad);
    }

    public ConsoleResult Execute(ConsoleSession session, string line)
    {
        line = line.Trim();
        if (line.Length == 0) return new("");
        var space = line.IndexOf(' ');
        var name = (space < 0 ? line : line[..space]).ToLowerInvariant();
        var args = space < 0 ? "" : line[(space + 1)..].Trim();
        if (session.Access != null)
        {
            var slotArg = name == "prog" ? (int.TryParse(args.Split(' ', 2)[0], out var ps) ? ps : 0) : TakeProgramOption(args).Slot;
            if (slotArg == 0 && session.CurrentSlot > 0 && IsSlotCommand(name)) slotArg = session.CurrentSlot;
            if (session.Access.Deny(name, slotArg) is { } why) return new(why + "\r\n");
        }
        try
        {
            switch (name)
            {
                case "exit" or "bye" or "quit" or "logout": return new("Goodbye.\r\n", true);
                case "help" or "?": return new(Help(args));
                case "ver" or "version": return new(Ver());
                case "hostname": return new($"Host Name: {Hostname}\r\n");
                case "uptime": return new(Uptime());
                case "time" or "date": return new(Time());
                case "info": return new(Info());
                case "slots" or "progs": return new(Slots());
                case "ipt" or "ipinfo": return new(IpTable(args));
                case "progreset" or "restartprog": return new(ProgReset(args));
                case "progload": return new(ProgLoad(args));
                case "stopprog": return new(ProgStop(args));
                case "startprog": return new(ProgStart(args));
                case "use": return new(Use(session, args));
                case "prog": return new(Prog(args));
                case "cpuload" or "ramfree" or "taskstat":
                    return new(Metrics(name, args));
                case "progcomments" or "appstat" or "signals" or "metrics":
                    return new(SlotCommand(session, name, args));
            }
            return new(SlotCommand(session, name, args));
        }
        catch (Exception e)
        {
            return new($"Error: {e.Message}\r\n");
        }
    }

    private static bool IsSlotCommand(string name) => name is not ("exit" or "bye" or "quit" or "logout" or "help" or "?" or "ver" or "version" or "hostname" or "uptime" or "time" or "date" or "info" or "slots" or "progs" or "ipt" or "ipinfo" or "progreset" or "restartprog" or "progload" or "stopprog" or "startprog" or "cpuload" or "ramfree" or "taskstat" or "use" or "prog");

    private static string Lf(string s) => s.Replace("\r\n", "\n").Replace("\n", "\r\n") + (s.EndsWith('\n') ? "" : "\r\n");

    private string Help(string args)
    {
        var sb = new StringBuilder();
        sb.Append("Commands (add -p:N to name a program slot):\r\n");
        foreach (var (n, h) in new (string, string)[]
        {
            ("HELP", "this list; HELP -p:N also lists that program's own commands"),
            ("VER", "OpenVP version"), ("INFO", "host, operating system and runtime"), ("HOSTNAME", "this computer's name"),
            ("UPTIME", "how long OpenVP has been running"), ("TIME", "date, time and time zone"),
            ("SLOTS", "every program slot and what it runs"),
            ("IPT [-p:N]", "the IP table (every slot, or one)"),
            ("PROGRESET -p:N", "load the program in slot N again and start it"),
            ("PROGLOAD -p:N [-f:file]", "load the slot's remembered program, or a file in its upload folder, and start it"),
            ("STOPPROG -p:N", "stop the program in slot N"), ("STARTPROG -p:N", "start the program in slot N"),
            ("CPULOAD / RAMFREE / TASKSTAT [-p:N]", "process load, memory and threads"),
            ("PROGCOMMENTS / SIGNALS -p:N", "what slot N runs; its signals"),
            ("USE N", "make slot N the default for program commands (USE 0 to clear)"),
            ("PROG N command", "run one of program N's own console commands"),
            ("EXIT", "close this session"),
        })
            sb.Append($"  {n,-38} {h}\r\n");
        var (slot, _, _) = TakeProgramOption(args);
        var slots = slot > 0 && _processor.IsValidSlot(slot) ? new[] { _processor.Slot(slot) } : _processor.Slots.ToArray();
        var any = false;
        foreach (var s in slots)
            foreach (var c in s.ConsoleCommands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!any) { sb.Append("Program commands:\r\n"); any = true; }
                sb.Append($"  {c.Name,-38} (slot {s.Number}) {c.Help}\r\n");
            }
        return sb.ToString();
    }

    private string Ver() => $"OpenVP virtual control processor [v{Version}] @{Hostname}\r\n  Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}\r\n  Slots: {_processor.Slots.Count}\r\n";

    private string Uptime()
    {
        var up = DateTimeOffset.Now - ProcessStart;
        return $"OpenVP has been running for {(up.Days > 0 ? up.Days + " day(s) " : "")}{up.Hours:00}:{up.Minutes:00}:{up.Seconds:00} (since {ProcessStart:yyyy-MM-dd HH:mm:ss}).\r\n";
    }

    private static string Time()
    {
        var now = DateTimeOffset.Now;
        return $"{now:ddd MMM dd, yyyy HH:mm:ss} {TimeZoneInfo.Local.StandardName} (UTC{now:zzz})\r\n";
    }

    private string Info()
    {
        var running = _processor.Slots.Count(s => s.State == SlotState.Running);
        return $"Host Name: {Hostname}\r\nOpenVP: v{Version}\r\nOS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\r\nRuntime: {RuntimeInformation.FrameworkDescription}\r\nProcessors: {Environment.ProcessorCount}\r\nSlots: {_processor.Slots.Count}, {running} running\r\nData folder: {_processor.DataDirectory}\r\n";
    }

    private string Slots()
    {
        var sb = new StringBuilder();
        foreach (var s in _processor.Slots)
            sb.Append($"Slot {s.Number,2}  {s.State,-8} {s.Runtime?.ProgramName ?? "-",-32} {s.Runtime?.Kind ?? ""}\r\n");
        return sb.ToString();
    }

    private ProgramSlot NeedSlot(string args, out string rest)
    {
        var (n, r, bad) = TakeProgramOption(args);
        rest = r;
        if (bad) throw new ArgumentException("-p: needs a slot number, like -p:1.");
        if (n == 0) throw new ArgumentException("Which program? Add -p:N, for example -p:1.");
        if (!_processor.IsValidSlot(n)) throw new ArgumentException($"There is no slot {n}. Slots are 1-{_processor.Slots.Count}.");
        return _processor.Slot(n);
    }

    private string IpTable(string args)
    {
        var (n, _, bad) = TakeProgramOption(args);
        if (bad || (n != 0 && !_processor.IsValidSlot(n))) throw new ArgumentException($"Slots are 1-{_processor.Slots.Count}.");
        var slots = n > 0 ? new[] { _processor.Slot(n) } : _processor.Slots.Where(s => s.Devices.Count > 0).ToArray();
        if (slots.Length == 0) return "IP table is empty.\r\n";
        return string.Concat(slots.Select(s => Lf(s.RunConsoleCommand("ipt"))));
    }

    private string ProgReset(string args)
    {
        var s = NeedSlot(args, out _);
        if (s.Runtime == null && s.ProgramPath == null) return $"Slot {s.Number} has no program to reset.\r\n";
        _processor.Restart(s.Number);
        return $"Program {s.Number} reset: {s.Runtime?.ProgramName} is {s.State.ToString().ToLowerInvariant()}.\r\n";
    }

    private string? RememberedPath(ProgramSlot s)
    {
        if (s.Settings.Program is { } m)
        {
            var p = m.Source == ProgramMemory.Path ? m.FilePath : m.File is { } f ? Path.Combine(_processor.DataDirectory, "uploads", $"slot{s.Number:00}", Path.GetFileName(f)) : null;
            if (p != null && File.Exists(p)) return p;
        }
        return s.ProgramPath;
    }

    private string ProgLoad(string args)
    {
        var s = NeedSlot(args, out var rest);
        string? path = null;
        foreach (var part in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (part.StartsWith("-f:", StringComparison.OrdinalIgnoreCase))
            {
                var file = Path.GetFileName(part[3..].Trim('"'));
                path = Path.Combine(_processor.DataDirectory, "uploads", $"slot{s.Number:00}", file);
                if (!File.Exists(path)) throw new ArgumentException($"There is no {file} in slot {s.Number}'s upload folder. Upload it from the web console first.");
            }
        path ??= RememberedPath(s);
        if (path == null || !File.Exists(path)) throw new ArgumentException($"Slot {s.Number} has no program to load. Upload one in the web console, or use -f:file.");
        _processor.LoadProgram(s.Number, path, true);
        return $"Program {s.Number} loaded: {s.Runtime?.ProgramName} ({Path.GetFileName(path)}) is {s.State.ToString().ToLowerInvariant()}.\r\n";
    }

    private string ProgStop(string args)
    {
        var s = NeedSlot(args, out _);
        if (s.Runtime == null) return $"Slot {s.Number} has no program.\r\n";
        s.Stop();
        s.RememberRunning(false);
        return $"Program {s.Number} stopped.\r\n";
    }

    private string ProgStart(string args)
    {
        var s = NeedSlot(args, out _);
        if (s.Runtime == null) return $"Slot {s.Number} has no program. Use PROGLOAD -p:{s.Number}.\r\n";
        s.Start();
        s.RememberRunning(true);
        return $"Program {s.Number} started.\r\n";
    }

    private string Use(ConsoleSession session, string args)
    {
        if (!int.TryParse(args, out var n)) throw new ArgumentException("USE N, where N is a slot number (0 to clear).");
        if (n != 0 && !_processor.IsValidSlot(n)) throw new ArgumentException($"There is no slot {n}.");
        session.CurrentSlot = n;
        return n == 0 ? "No default program.\r\n" : $"Program commands now go to slot {n}.\r\n";
    }

    private string Prog(string args)
    {
        var sp = args.IndexOf(' ');
        if (sp < 0 || !int.TryParse(args[..sp], out var n) || !_processor.IsValidSlot(n)) throw new ArgumentException("PROG N command, for example: PROG 1 help");
        return Lf(_processor.Slot(n).RunConsoleCommand(args[(sp + 1)..]));
    }

    private string Metrics(string name, string args)
    {
        var (n, _, bad) = TakeProgramOption(args);
        if (bad || (n != 0 && !_processor.IsValidSlot(n))) throw new ArgumentException($"Slots are 1-{_processor.Slots.Count}.");
        var slot = _processor.Slot(n == 0 ? (_processor.Slots.FirstOrDefault(s => s.Runtime != null)?.Number ?? 1) : n);
        return Lf(MetricsReport.Console(_processor, slot, name));
    }

    /// <summary>A command the shell doesn't know: ask the slot's registered console commands (the way the web console does).</summary>
    private string SlotCommand(ConsoleSession session, string name, string args)
    {
        var (n, rest, bad) = TakeProgramOption(args);
        if (bad) throw new ArgumentException("-p: needs a slot number, like -p:1.");
        ProgramSlot? slot = null;
        if (n > 0)
        {
            if (!_processor.IsValidSlot(n)) throw new ArgumentException($"There is no slot {n}.");
            slot = _processor.Slot(n);
        }
        else if (session.CurrentSlot > 0) slot = _processor.Slot(session.CurrentSlot);
        else slot = _processor.Slots.FirstOrDefault(s => s.ConsoleCommands.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                    ?? (name is "progcomments" or "appstat" or "signals" or "metrics" ? _processor.Slots.FirstOrDefault(s => s.Runtime != null) : null);
        if (slot == null) return $"Bad or incomplete command: {name}. Type HELP for the list.\r\n";
        var text = slot.RunConsoleCommand((name + " " + rest).Trim());
        return text.Length == 0 ? "" : Lf(text);
    }
}
