using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OpenVP.Core;
using OpenVP.Core.Security;

namespace OpenVP.Server.Mcp;

/// <summary>
/// The tools OpenVP offers over MCP (<c>/mcp</c>), so an AI assistant can test a control program the way a person would: load it, press
/// each button on its panel, set sliders and text, and check what the program did (feedback, log lines, exceptions), or replay a saved
/// recording as a regression test. Each tool checks the same permission, slot access and slot lock as the REST endpoint that does the same job.
/// </summary>
[McpServerToolType]
public sealed class OpenVpTools(McpBridge bridge, IHttpContextAccessor http)
{
    private const int MaxChanges = 200;
    private Processor P => bridge.Processor;

    // ------------------------------------------------------------------ slots and programs

    [McpServerTool(Name = "list_slots", Title = "List slots", ReadOnly = true, Idempotent = true)]
    [Description("Lists OpenVP's program slots: number, state (Empty, Loaded, Running, Stopped, Error), the program in it and its kind (SIMPL#, SIMPL Windows), the last error, and the panels and other devices (IP IDs) it has. Start here.")]
    public object ListSlots()
    {
        var p = bridge.Require(Perm.View);
        return P.Slots.Where(s => p.CanSlot(s.Number)).Select(s => new
        {
            slot = s.Number, name = s.Settings.Name, state = s.State.ToString(), kind = s.Runtime?.Kind, program = s.Runtime?.ProgramName,
            file = s.ProgramPath is null ? null : Path.GetFileName(s.ProgramPath), error = s.LastError, exceptions = s.Exceptions.Count,
            devices = s.Devices.OrderBy(d => d.IpId).Select(d => new { ipId = $"{d.IpId:X2}", type = d.Type, description = d.Description, online = d.Online })
        }).ToList();
    }

    [McpServerTool(Name = "get_slot", Title = "Get slot", ReadOnly = true, Idempotent = true)]
    [Description("Everything about one slot as the web console shows it: state, program, summary (panels, EISCs, ports, S+ modules), crash settings and devices.")]
    public object GetSlot([Description("Slot number, 1-based.")] int slot)
    {
        bridge.Require(Perm.View, slot);
        var s = P.Slot(slot);
        return bridge.SlotDto?.Invoke(s) ?? new { slot, state = s.State.ToString(), program = s.Runtime?.ProgramName };
    }

    [McpServerTool(Name = "load_program", Title = "Load program", Destructive = true)]
    [Description("Loads a compiled program (.cpz, .dll, .lpz) or a SIMPL Windows source file (.smw) into a slot from a file on the computer OpenVP runs on, with its .sig and sidecar files, replacing what's there (a snapshot of the old program is kept, so the console's Undo works). Only works when the MCP client runs on that same computer. Use it after building a program, then press its buttons to test it.")]
    public async Task<object> LoadProgram(
        [Description("Slot number, 1-based.")] int slot,
        [Description("Full path of the program file on the OpenVP computer, e.g. C:\\\\Projects\\\\Room\\\\Room.lpz or /home/me/room/bin/Room.cpz.")] string path,
        [Description("Start the program once loaded (default true).")] bool start = true)
    {
        bridge.Require(Perm.LoadProgram, slot, locks: true);
        var ctx = http.HttpContext;
        if (ctx == null || !ApiAuth.IsDirectLocal(ctx))
            throw new McpException("Loading from a path only works when the MCP client runs on the computer OpenVP runs on. Upload the program in the web console instead.");
        path = (path ?? "").Trim().Trim('"');
        if (path.Length == 0 || !Path.IsPathFullyQualified(path)) throw new McpException("Give the program's full path.");
        if (!File.Exists(path)) throw new McpException($"There's no file at {path}.");
        var load = bridge.LoadFromPath ?? throw new McpException("Loading isn't available.");
        var error = await load(slot, path, start, bridge.Who(ctx));
        if (error != null) throw new McpException($"Couldn't load {Path.GetFileName(path)} into slot {slot}: {error}");
        return Summary(P.Slot(slot));
    }

    [McpServerTool(Name = "control_slot", Title = "Start, stop or restart a slot")]
    [Description("Starts, stops or restarts the program in a slot. Restart reloads it from its file, which also resets its state: use it between test runs for a clean start.")]
    public async Task<object> ControlSlot(
        [Description("Slot number, 1-based.")] int slot,
        [Description("start, stop or restart.")] string action)
    {
        var a = (action ?? "").Trim().ToLowerInvariant();
        bridge.Require(Perm.Operate, slot, locks: a != "start");
        var s = P.Slot(slot);
        if (s.Runtime == null) throw new McpException($"Slot {slot} has no program. Load one first.");
        var name = s.Runtime.ProgramName;
        try
        {
            switch (a)
            {
                case "start": await (bridge.Start ?? throw new McpException("Not available."))(slot); break;
                case "stop": (bridge.Stop ?? throw new McpException("Not available."))(slot); break;
                case "restart": await (bridge.Restart ?? throw new McpException("Not available."))(slot); break;
                default: throw new McpException("action must be start, stop or restart.");
            }
        }
        catch (McpException) { throw; }
        catch (Exception e) { throw new McpException($"Couldn't {a} slot {slot}: {e.Message}"); }
        bridge.Note?.Invoke(a, $"{char.ToUpperInvariant(a[0])}{a[1..]}{(a == "stop" ? "ped" : "ed")} {name} in slot {slot} (MCP)", slot);
        return Summary(s);
    }

    // ------------------------------------------------------------------ the panel: find controls, press, set, read

    [McpServerTool(Name = "list_controls", Title = "List panel controls", ReadOnly = true, Idempotent = true)]
    [Description("Lists every control on the slot's generated test panel: one per button (digital), slider (analog) and text field (serial) the program has, grouped by panel (IP ID) or name prefix, with the signal a press drives, the signal its feedback is read from and the current value. Pass a control's id, label or signal name to press_button or set_value.")]
    public object ListControls(
        [Description("Slot number, 1-based.")] int slot,
        [Description("Only controls whose id, label, group or signal names contain this text (case-insensitive).")] string? search = null,
        [Description("Only controls of this kind: digital, analog or serial.")] string? kind = null,
        [Description("At most this many controls (default 300).")] int max = 300)
    {
        bridge.Require(Perm.View, slot);
        var model = Model(P.Slot(slot));
        var all = Controls(model).Where(c => Matches(c.Control, c.Group, search) && (kind == null || c.Control.Kind.Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        return new
        {
            slot, program = model.Program, total = all.Count, truncated = model.Truncated || all.Count > max,
            controls = all.Take(Math.Clamp(max, 1, 4000)).Select(c => new
            {
                id = c.Control.Id, label = c.Control.Label, kind = c.Control.Kind, group = c.Group, device = c.Control.Device,
                drives = c.Control.DriveName, feedback = c.Control.FeedbackName, value = c.Control.Value
            })
        };
    }

    [McpServerTool(Name = "press_button", Title = "Press a button")]
    [Description("Presses a button on the slot's panel like a finger would: drives its digital signal high, holds it, releases it, waits for the program to settle, then reports the button's feedback before and after, every signal that changed in that time (in order, including brief pulses), log lines and any exceptions the program threw. Use list_controls to find buttons.")]
    public async Task<object> PressButton(
        [Description("Slot number, 1-based.")] int slot,
        [Description("The button: a control id from list_controls (e.g. IPID03.d10), its label, or a digital signal name.")] string control,
        [Description("How long to hold it down, in ms (default 100).")] int holdMs = 100,
        [Description("How long to wait after release for the program to react, in ms (default 300).")] int settleMs = 300)
    {
        bridge.Require(Perm.Signal, slot, locks: true);
        var s = Running(slot);
        var c = Resolve(s, control);
        if (c.Kind != "digital") throw new McpException($"{c.Label} is {Article(c.Kind)} {c.Kind} control, not a button. Use set_value.");
        var r = await Exercise(s, c, async () =>
        {
            Force(s, c.Drive, true);
            await Task.Delay(Math.Clamp(holdMs, 0, 10_000));
            Force(s, c.Drive, false);
        }, settleMs);
        bridge.Note?.Invoke("force", $"Pressed {c.Label} ({c.Drive}) in slot {slot} (MCP)", slot);
        return r;
    }

    [McpServerTool(Name = "set_value", Title = "Set a slider, text or signal")]
    [Description("Sets a control to a value and leaves it there: a slider (analog, 0-65535), a text field (serial), or a digital signal held high (true) or low (false). Reports the same as press_button: feedback before and after, every signal that changed, log lines and exceptions.")]
    public async Task<object> SetValue(
        [Description("Slot number, 1-based.")] int slot,
        [Description("A control id from list_controls, its label, or a signal name.")] string control,
        [Description("The value: a number for analog, text for serial, true/false (or 1/0) for digital.")] string value,
        [Description("How long to wait for the program to react, in ms (default 300).")] int settleMs = 300)
    {
        bridge.Require(Perm.Signal, slot, locks: true);
        var s = Running(slot);
        var c = Resolve(s, control);
        var v = Parse(c.SigKind, value);
        var r = await Exercise(s, c, () => { Force(s, c.Drive, v); return Task.CompletedTask; }, settleMs);
        bridge.Note?.Invoke("force", $"Set {c.Label} ({c.Drive}) to {v} in slot {slot} (MCP)", slot);
        return r;
    }

    [McpServerTool(Name = "read_signals", Title = "Read signals", ReadOnly = true, Idempotent = true)]
    [Description("Reads the program's signals and their current values: panel joins (IPIDxx.in/out.d/a/s<join>) and, for SIMPL Windows programs, every named signal. Use it to check state after pressing buttons.")]
    public object ReadSignals(
        [Description("Slot number, 1-based.")] int slot,
        [Description("Only signals whose name contains this text (case-insensitive).")] string? search = null,
        [Description("Only signals that aren't at their default (high digitals, non-zero analogs, non-empty serials).")] bool activeOnly = false,
        [Description("At most this many (default 300).")] int max = 300)
    {
        bridge.Require(Perm.View, slot);
        var rt = P.Slot(slot).Runtime ?? throw new McpException($"Slot {slot} has no program.");
        var list = rt.GetSignals()
            .Where(x => search == null || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(x => !activeOnly || !SigValue.AreEqual(SigValue.Coerce(x.Kind, x.Value), SigValue.Default(x.Kind)))
            .ToList();
        return new { slot, total = list.Count, truncated = list.Count > max, signals = list.Take(Math.Clamp(max, 1, 10_000)).Select(x => new { name = x.Name, kind = AutoPanel.KindName(x.Kind), value = x.Value }) };
    }

    [McpServerTool(Name = "wait_for_signal", Title = "Wait for a signal (assert)", ReadOnly = true)]
    [Description("Waits until a signal has the expected value, or the time runs out. Returns passed true/false with the actual value: use it as a test assertion after pressing a button (e.g. a feedback that should come on after a ramp or a delay).")]
    public async Task<object> WaitForSignal(
        [Description("Slot number, 1-based.")] int slot,
        [Description("Signal name, or a control id/label (its feedback signal is checked).")] string signal,
        [Description("The expected value: true/false or 1/0 for digital, a number for analog, text for serial.")] string expected,
        [Description("How long to wait, in ms (default 2000, at most 60000).")] int timeoutMs = 2000)
    {
        bridge.Require(Perm.View, slot);
        var s = P.Slot(slot);
        if (s.Runtime == null) throw new McpException($"Slot {slot} has no program.");
        var c = Resolve(s, signal);
        var want = Parse(c.SigKind, expected);
        var sw = Stopwatch.StartNew();
        object? actual;
        while (true)
        {
            actual = ValueOf(s, c.Feedback);
            if (actual != null && SigValue.AreEqual(SigValue.Coerce(c.SigKind, actual), want)) break;
            if (sw.ElapsedMilliseconds >= Math.Clamp(timeoutMs, 0, 60_000)) break;
            await Task.Delay(20);
        }
        var passed = actual != null && SigValue.AreEqual(SigValue.Coerce(c.SigKind, actual), want);
        return new { passed, signal = c.Feedback, expected = want, actual, waitedMs = sw.ElapsedMilliseconds };
    }

    [McpServerTool(Name = "test_all_buttons", Title = "Press every button")]
    [Description("Smoke test: presses every button (digital control) on the slot's panel, or those matching a search, one after another, and reports for each what changed, with a list of buttons that did nothing visible, buttons that logged errors and buttons that threw exceptions. Presses are real: anything the program drives (relays, serial ports, IR, other slots over EISC) reacts too.")]
    public async Task<object> TestAllButtons(
        [Description("Slot number, 1-based.")] int slot,
        [Description("Only buttons whose id, label, group or signal names contain this text, e.g. IPID03 for one panel.")] string? search = null,
        [Description("How long to hold each button, in ms (default 100).")] int holdMs = 100,
        [Description("How long to wait after each release, in ms (default 250).")] int settleMs = 250,
        [Description("At most this many buttons (default 100, at most 1000).")] int max = 100)
    {
        bridge.Require(Perm.Signal, slot, locks: true);
        var s = Running(slot);
        var buttons = Controls(Model(s)).Where(c => c.Control.Kind == "digital" && Matches(c.Control, c.Group, search)).Select(c => c.Control).ToList();
        if (buttons.Count == 0) throw new McpException(search == null ? "The program has no buttons (digital controls)." : $"No button matches \"{search}\".");
        var results = new List<ButtonResult>();
        foreach (var b in buttons.Take(Math.Clamp(max, 1, 1000)))
        {
            if (s.Runtime == null || s.State != SlotState.Running) break;   // the program stopped or crashed: say so below instead of pressing into nothing
            var c = new Ctl(b.Id, b.Label, b.Kind, SigKind.Digital, b.DriveName, b.FeedbackName, b.Device);
            var r = await Exercise(s, c, async () =>
            {
                Force(s, c.Drive, true);
                await Task.Delay(Math.Clamp(holdMs, 0, 10_000));
                Force(s, c.Drive, false);
            }, settleMs, maxChanges: 30);
            results.Add(r);
        }
        bridge.Note?.Invoke("force", $"Pressed {results.Count} button{(results.Count == 1 ? "" : "s")} in slot {slot} (MCP test_all_buttons)", slot);
        return new
        {
            slot, program = s.Runtime?.ProgramName, state = s.State.ToString(), pressed = results.Count, buttons = buttons.Count,
            stoppedEarly = results.Count < Math.Min(buttons.Count, Math.Clamp(max, 1, 1000)) ? $"The program is {s.State} after {results.Count} presses{(s.LastError is { } e ? ": " + e : "")}." : null,
            noReaction = results.Where(r => r.Changes.Count == 0).Select(r => r.Control).ToList(),
            withErrors = results.Where(r => r.Log.Any(l => l.Level == "Error")).Select(r => r.Control).ToList(),
            withExceptions = results.Where(r => r.Exceptions.Count > 0).Select(r => r.Control).ToList(),
            results
        };
    }

    // ------------------------------------------------------------------ what the program said

    [McpServerTool(Name = "read_log", Title = "Read the log", ReadOnly = true, Idempotent = true)]
    [Description("The latest log lines (what the program printed, errors, OpenVP's own notices), newest last. Without a slot: every slot you may see.")]
    public object ReadLog(
        [Description("Slot number, 1-based; leave out for all slots.")] int? slot = null,
        [Description("How many lines (default 100, at most 500).")] int max = 100,
        [Description("Only this level or worse: Info, Notice, Warning or Error.")] string? level = null)
    {
        var p = bridge.Require(Perm.View, slot);
        var min = Rank(level);
        return P.RecentLog(slot, 500).Where(l => p.CanSlot(l.Slot) && Rank(l.Level) >= min).TakeLast(Math.Clamp(max, 1, 500))
            .Select(l => new { time = l.Time, slot = l.Slot, level = l.Level, message = l.Message, detail = l.Detail }).ToList();
    }

    [McpServerTool(Name = "get_exceptions", Title = "Get exceptions", ReadOnly = true, Idempotent = true)]
    [Description("Exceptions the slot's program threw and nothing caught (event handlers, timers, threads), newest first, with stack traces and source lines when the program's .pdb was loaded.")]
    public object GetExceptions([Description("Slot number, 1-based.")] int slot)
    {
        bridge.Require(Perm.View, slot);
        return P.Slot(slot).Exceptions.Recent().Select(e => new { time = e.Time, source = e.Source, type = e.Type, message = e.Message, stack = e.Stack }).ToList();
    }

    [McpServerTool(Name = "run_console_command", Title = "Run a console command")]
    [Description("Runs a command on the slot's processor-style console, as typed at the slot's console in the web debugger (e.g. HELP, INFO, or a command the program registered).")]
    public string RunConsoleCommand([Description("Slot number, 1-based.")] int slot, [Description("The command line.")] string command)
    {
        bridge.Require(Perm.Console, slot);
        try { return P.Slot(slot).RunConsoleCommand(command ?? ""); }
        catch (Exception e) { throw new McpException("The command failed: " + e.Message); }
    }

    // ------------------------------------------------------------------ recordings as regression tests

    [McpServerTool(Name = "list_recordings", Title = "List recordings", ReadOnly = true, Idempotent = true)]
    [Description("Saved recordings for a slot. A recording is a set of panel presses and the feedback they caused, made in the web console (Record); replay_recording runs one again and checks the program still answers the same way.")]
    public object ListRecordings([Description("Slot number, 1-based.")] int slot)
    {
        bridge.Require(Perm.View, slot);
        var rec = bridge.Recordings ?? throw new McpException("Recordings aren't available.");
        return rec.Store.List(slot);
    }

    [McpServerTool(Name = "replay_recording", Title = "Replay a recording (regression test)")]
    [Description("Replays a saved recording into the slot's running program and waits for the result: PASS or FAIL, how many feedback changes matched, and each difference. Use after changing a program to check nothing that used to work broke.")]
    public async Task<object> ReplayRecording(
        [Description("Slot number, 1-based.")] int slot,
        [Description("The recording's id from list_recordings; leave out for the newest.")] string? id = null,
        [Description("Replay as fast as the program allows instead of in real time (default true).")] bool fast = true,
        [Description("Give up waiting after this many seconds (default 120, at most 600). The replay is cancelled then.")] int timeoutSeconds = 120)
    {
        bridge.Require(Perm.Recording, slot, locks: true);
        var rec = bridge.Recordings ?? throw new McpException("Recordings aren't available.");
        try { rec.StartReplay(slot, string.IsNullOrWhiteSpace(id) ? null : id, new ReplayOptions { Fast = fast }); }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or ArgumentException) { throw new McpException(e.Message); }
        bridge.Note?.Invoke("replay", $"Replaying a recording in slot {slot} (MCP)", slot);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var st = rec.Status(slot).Replay;
            if (st != null && st.State != "running") return new { state = st.State, passed = st.Report?.Passed, message = st.Message, report = st.Report };
            if (sw.Elapsed.TotalSeconds > Math.Clamp(timeoutSeconds, 1, 600))
            {
                rec.CancelReplay(slot);
                throw new McpException($"The replay didn't finish within {timeoutSeconds} s and was cancelled.");
            }
            await Task.Delay(100);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A control resolved to the signals behind it.</summary>
    private sealed record Ctl(string Id, string Label, string Kind, SigKind SigKind, string Drive, string Feedback, string? Device);

    public sealed record SignalChange(long AtMs, string Signal, object? Value);
    public sealed record NetChange(string Signal, object? Before, object? After);
    public sealed record LogLine(string Level, string Message);
    public sealed record ButtonResult(string Control, string Label, string Drives, string FeedbackSignal, object? FeedbackBefore, object? FeedbackAfter,
        IReadOnlyList<NetChange> Changes, IReadOnlyList<SignalChange> Timeline, bool TimelineTruncated, IReadOnlyList<LogLine> Log, IReadOnlyList<string> Exceptions, string State);

    private object Summary(ProgramSlot s) => new { slot = s.Number, state = s.State.ToString(), kind = s.Runtime?.Kind, program = s.Runtime?.ProgramName, error = s.LastError,
        devices = s.Devices.OrderBy(d => d.IpId).Select(d => new { ipId = $"{d.IpId:X2}", type = d.Type, description = d.Description }) };

    private ProgramSlot Running(int slot)
    {
        var s = P.Slot(slot);
        if (s.Runtime == null) throw new McpException($"Slot {slot} has no program. Load one with load_program.");
        if (s.State != SlotState.Running) throw new McpException($"The program in slot {slot} is {s.State}{(s.LastError is { } e ? " (" + e + ")" : "")}. Start it with control_slot.");
        return s;
    }

    private static AutoPanelModel Model(ProgramSlot s) => PanelEndpoints.AutoPanelFor(s) ?? throw new McpException($"Slot {s.Number} has no program.");

    private static IEnumerable<(AutoControl Control, string Group)> Controls(AutoPanelModel m) =>
        m.Groups.SelectMany(g => g.Controls.Select(c => (c, g.Subtitle is { Length: > 0 } sub ? $"{g.Title} ({sub})" : g.Title)));

    private static bool Matches(AutoControl c, string group, string? q) => string.IsNullOrWhiteSpace(q)
        || new[] { c.Id, c.Label, group, c.DriveName, c.FeedbackName, c.Device ?? "" }.Any(x => x.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A control by id, signal name or label (exact first, then case-insensitive); else a plain signal name the program has.</summary>
    private static Ctl Resolve(ProgramSlot s, string control)
    {
        var q = (control ?? "").Trim();
        if (q.Length == 0) throw new McpException("Say which control: an id or label from list_controls, or a signal name.");
        var all = Controls(Model(s)).Select(x => x.Control).ToList();
        var hit = all.FirstOrDefault(c => c.Id == q || c.DriveName == q || c.FeedbackName == q)
            ?? all.FirstOrDefault(c => c.Id.Equals(q, StringComparison.OrdinalIgnoreCase) || c.DriveName.Equals(q, StringComparison.OrdinalIgnoreCase) || c.FeedbackName.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (hit == null)
        {
            var byLabel = all.Where(c => c.Label.Equals(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byLabel.Count > 1) throw new McpException($"{byLabel.Count} controls are labelled \"{q}\": {string.Join(", ", byLabel.Take(10).Select(c => c.Id))}. Use the id.");
            hit = byLabel.FirstOrDefault();
        }
        if (hit != null) return new Ctl(hit.Id, hit.Label, hit.Kind, KindOf(hit.Kind), hit.DriveName, hit.FeedbackName, hit.Device);
        var sig = s.Runtime!.GetSignals().FirstOrDefault(x => x.Name.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (sig != null) return new Ctl(sig.Name, sig.Name, AutoPanel.KindName(sig.Kind), sig.Kind, sig.Name, sig.Name, null);
        var near = all.Where(c => c.Id.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Label.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(8).Select(c => c.Id).ToList();
        throw new McpException($"Slot {s.Number} has no control or signal \"{q}\".{(near.Count > 0 ? " Did you mean: " + string.Join(", ", near) + "?" : " See list_controls.")}");
    }

    private static SigKind KindOf(string kind) => kind switch { "digital" => SigKind.Digital, "analog" => SigKind.Analog, _ => SigKind.Serial };

    private static object Parse(SigKind kind, string value)
    {
        var v = (value ?? "").Trim();
        switch (kind)
        {
            case SigKind.Digital:
                if (v is "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase) || v.Equals("high", StringComparison.OrdinalIgnoreCase)) return SigValue.True;
                if (v is "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase) || v.Equals("low", StringComparison.OrdinalIgnoreCase)) return SigValue.False;
                throw new McpException($"A digital value is true or false (1 or 0), not \"{value}\".");
            case SigKind.Analog:
                if (ushort.TryParse(v, out var u)) return u;
                if (v.EndsWith('%') && double.TryParse(v[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct is >= 0 and <= 100)
                    return (ushort)Math.Round(pct / 100 * 65535);
                throw new McpException($"An analog value is a whole number from 0 to 65535 (or a percentage like 50%), not \"{value}\".");
            default:
                return value ?? "";
        }
    }

    private static void Force(ProgramSlot s, string signal, object value)
    {
        if (!s.ForceSignal(signal, value)) throw new McpException($"Slot {s.Number} refused to set {signal}: the program has no such signal (it may have been reloaded).");
    }

    private static object? ValueOf(ProgramSlot s, string signal) => s.Runtime?.GetSignals().FirstOrDefault(x => x.Name == signal)?.Value;

    private static Dictionary<string, object> Snapshot(ProgramSlot s)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var x in s.Runtime?.GetSignals() ?? Array.Empty<SignalInfo>()) d[x.Name] = x.Value;
        return d;
    }

    /// <summary>
    /// Do something to a control and report what it caused: the control's feedback before and after, the net change of every signal, the order
    /// signals changed in while it happened (from the slot's trace, so a pulse that came and went shows too), log lines and exceptions.
    /// </summary>
    private async Task<ButtonResult> Exercise(ProgramSlot s, Ctl c, Func<Task> act, int settleMs, int maxChanges = MaxChanges)
    {
        var before = Snapshot(s);
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        var timeline = new List<SignalChange>();
        var truncated = false;
        void OnTrace(TraceEvent e)
        {
            lock (timeline)
            {
                if (timeline.Count >= maxChanges) { truncated = true; return; }
                timeline.Add(new SignalChange(sw.ElapsedMilliseconds, e.Signal, e.Value));
            }
        }
        s.Traced += OnTrace;
        try
        {
            await act();
            await Task.Delay(Math.Clamp(settleMs, 0, 30_000));
        }
        finally { s.Traced -= OnTrace; }

        var after = Snapshot(s);
        var changes = after.Where(kv => !before.TryGetValue(kv.Key, out var old) || !SigValue.AreEqual(old, kv.Value))
            .Select(kv => new NetChange(kv.Key, before.GetValueOrDefault(kv.Key), kv.Value))
            .Concat(before.Keys.Where(k => !after.ContainsKey(k)).Select(k => new NetChange(k, before[k], null)))
            .Take(maxChanges).ToList();
        var log = P.RecentLog(s.Number, 200).Where(l => l.Time >= started).Select(l => new LogLine(l.Level, l.Message)).TakeLast(50).ToList();
        var exceptions = s.Exceptions.Recent().Where(e => e.Time >= started).Select(e => $"{e.Type}: {e.Message} ({e.Source})").ToList();
        List<SignalChange> tl;
        lock (timeline) tl = timeline.ToList();
        return new ButtonResult(c.Id, c.Label, c.Drive, c.Feedback, before.GetValueOrDefault(c.Feedback), after.GetValueOrDefault(c.Feedback),
            changes, tl, truncated, log, exceptions, s.State.ToString());
    }

    private static int Rank(string? level) => (level ?? "").Trim().ToLowerInvariant() switch
    {
        "error" => 3, "warning" or "warn" => 2, "notice" => 1, _ => 0
    };

    private static string Article(string word) => word.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an" : "a";
}
