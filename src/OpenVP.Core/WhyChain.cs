using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>One link in the chain that explains a signal's value.</summary>
/// <param name="Kind"><c>change</c> (logic drove it), <c>panel</c>, <c>force</c>, <c>start</c>, <c>program</c> (a SIMPL# program wrote it), <c>external</c>, <c>none</c> (no change was seen) or <c>unknown</c>.</param>
public sealed record WhyStep(string Kind, string Signal, string? Value, DateTimeOffset? Time, string Text, string? Symbol = null, string? Cause = null);

/// <summary>The answer to "why is this signal this value?".</summary>
/// <param name="Complete">True when the chain reaches an input, a force, a timer or the start-up; false when it stops short (no trace, or a SIMPL# program's own code).</param>
public sealed record WhyResult(string Signal, string? Value, string Summary, IReadOnlyList<WhyStep> Steps, bool Complete, string? Note);

/// <summary>What <see cref="WhyChain"/> needs to know about the slot.</summary>
public sealed class WhyContext
{
    /// <summary>The program's wiring, or null for a program that can't describe it (SIMPL#).</summary>
    public ProgramGraph? Graph { get; init; }
    /// <summary>Recent changes, oldest first.</summary>
    public IReadOnlyList<TraceRow> Trace { get; init; } = Array.Empty<TraceRow>();
    public Func<string, object?> CurrentValue { get; init; } = _ => null;
    /// <summary>Addresses of the clients connected to an IP ID (for "from panel 192.168.1.20").</summary>
    public Func<string, IReadOnlyList<string>> ClientsOf { get; init; } = _ => Array.Empty<string>();
    public string RuntimeKind { get; init; } = "";
    public bool TraceAvailable { get; init; } = true;
}

/// <summary>
/// Walks back from a signal to what set it. Each traced change records the symbol whose output produced it and the
/// signal that made that symbol act (see docs/debugger-paths.md), so the walk follows <c>cause</c> from change to change
/// until it reaches a change from outside the logic: a panel press, a force, a timer or the start-up. Without a trace it
/// falls back to the wiring and says what drives the signal now.
/// </summary>
public static partial class WhyChain
{
    private const int MaxSteps = 40;

    [GeneratedRegex(@"^IPID([0-9A-Fa-f]{2,})$")] private static partial Regex IpSource();
    [GeneratedRegex(@"^IPID([0-9A-Fa-f]{2,})\.(in|out)\.([dasDAS])(\d+)$")] private static partial Regex JoinName();

    public static WhyResult Build(string signal, WhyContext ctx)
    {
        var graph = ctx.Graph;
        var symbols = graph?.Symbols.ToDictionary(s => s.Id, StringComparer.Ordinal) ?? new Dictionary<string, GraphSymbol>();
        var cur = ctx.CurrentValue(signal);
        var curText = cur == null ? null : Show(KindOfValue(cur), cur);
        var trace = ctx.Trace;
        // Index of each signal's changes, so a step back is a lookup.
        var byGraphSig = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < trace.Count; i++)
        {
            if (!byGraphSig.TryGetValue(trace[i].Signal, out var l)) byGraphSig[trace[i].Signal] = l = new List<int>();
            l.Add(i);
        }
        int Latest(string sig, int before)
        {
            if (!byGraphSig.TryGetValue(sig, out var l)) return -1;
            for (var k = l.Count - 1; k >= 0; k--) if (l[k] < before) return l[k];
            return -1;
        }

        var steps = new List<WhyStep>();
        var name = signal; var at = trace.Count;
        var complete = false; string? note = null;
        var seen = new HashSet<int>();
        for (var hop = 0; hop < MaxSteps; hop++)
        {
            var idx = Latest(name, at);
            if (idx < 0 || !seen.Add(idx))
            {
                if (hop == 0) return NoChange(signal, curText, ctx, symbols, trace.Count == 0 && !ctx.TraceAvailable);
                steps.Add(new WhyStep("unknown", name, null, null, $"{name} has no recorded change before that (its value was already set when the trace started)"));
                note = "The trace doesn't go back far enough to see what set it first.";
                break;
            }
            var ev = trace[idx];
            var kind = Enum.TryParse<SigKind>(ev.Kind, out var k) ? k : SigKind.Digital;
            var went = Went(kind, ev.Value);
            GraphSymbol? sym = ev.Symbol != null && symbols.TryGetValue(ev.Symbol, out var gs) ? gs : null;
            var jn = JoinText(name, kind, symbols);

            if (ev.Symbol != null)
            {
                var symText = sym != null ? $"{sym.Name} {sym.Path}" : ev.Symbol;
                steps.Add(new WhyStep("change", name, ev.ValueText, ev.Time, $"{name}{jn} went {went} at {Clock(ev.Time)}, driven by {symText}", ev.Symbol, ev.Cause));
                if (ev.Cause != null) { name = ev.Cause; at = idx; continue; }
                // The symbol acted on something we weren't told: the last of its inputs to change.
                var guess = sym == null ? null : LastInputChange(sym, idx, trace, Latest);
                if (guess != null) { steps[^1] = steps[^1] with { Text = steps[^1].Text + $" (its input {guess.Value.Name} changed last)", Cause = guess.Value.Name }; name = guess.Value.Name; at = guess.Value.Index + 1; continue; }
                complete = true; note = "The symbol acted after a delay or on a timer, so what started it isn't recorded.";
                break;
            }

            // A change from outside the logic.
            var src = ev.Source ?? "";
            var m = IpSource().Match(src);
            if (m.Success)
            {
                var id = m.Groups[1].Value.ToUpperInvariant();
                var clients = ctx.ClientsOf(id);
                var from = clients.Count > 0 ? " from panel " + string.Join(", ", clients.Take(3)) : "";
                steps.Add(new WhyStep("panel", name, ev.ValueText, ev.Time, $"{name}{jn} {PressText(kind, ev.Value)} at {Clock(ev.Time)} on IP ID {id}{from}"));
                complete = true;
            }
            else if (src == "debugger")
            {
                steps.Add(new WhyStep("force", name, ev.ValueText, ev.Time, $"{name} was forced {went} from the debugger at {Clock(ev.Time)}"));
                complete = true;
            }
            else if (src == "start")
            {
                steps.Add(new WhyStep("start", name, ev.ValueText, ev.Time, $"{name} went {went} when the program started ({Clock(ev.Time)})"));
                complete = true;
            }
            else if (graph == null)
            {
                var jm = JoinName().Match(name);
                if (jm.Success && jm.Groups[2].Value == "out")
                {
                    steps.Add(new WhyStep("panel", name, ev.ValueText, ev.Time, $"{name} went {went} at {Clock(ev.Time)}: the device at IP ID {jm.Groups[1].Value.ToUpperInvariant()} sent it{FromClients(ctx, jm.Groups[1].Value)}"));
                    complete = true;
                }
                else
                {
                    steps.Add(new WhyStep("program", name, ev.ValueText, ev.Time, $"{name} was set to {Show(kind, ev.Value)} by the program at {Clock(ev.Time)}"));
                    note = $"{ctx.RuntimeKind} programs don't expose their logic, so OpenVP can say what the join is and when and from where it was set, but not which part of the code set it.";
                }
            }
            else
            {
                // Timers and delayed outputs arrive with the symbol's path as their source.
                var tsym = symbols.Values.FirstOrDefault(s => src.Length > 0 && (s.Id == src || src.StartsWith(s.Path + " ", StringComparison.Ordinal)));
                if (tsym != null)
                {
                    steps.Add(new WhyStep("change", name, ev.ValueText, ev.Time, $"{name}{jn} went {went} at {Clock(ev.Time)}, driven by {tsym.Name} {tsym.Path} (after a delay or on a timer)", tsym.Id));
                    var guess = LastInputChange(tsym, idx, trace, Latest);
                    if (guess != null) { name = guess.Value.Name; at = guess.Value.Index + 1; continue; }
                    complete = true;
                }
                else
                {
                    steps.Add(new WhyStep("external", name, ev.ValueText, ev.Time, $"{name}{jn} went {went} at {Clock(ev.Time)}, set from outside the logic{(src.Length > 0 ? $" ({src})" : "")}"));
                    complete = true;
                }
            }
            break;
        }

        var head = curText == null ? $"{signal}" : $"{signal} is {curText}";
        var summary = $"{head} because {string.Join(", because ", steps.Select(s => s.Text))}.";
        return new WhyResult(signal, curText, summary, steps, complete, note);
    }

    private static WhyResult NoChange(string signal, string? curText, WhyContext ctx, Dictionary<string, GraphSymbol> symbols, bool noTrace)
    {
        var steps = new List<WhyStep>();
        var head = curText == null ? signal : $"{signal} is {curText}";
        steps.Add(new WhyStep("none", signal, curText, null, noTrace
            ? "The debugger hasn't recorded any changes yet, so what set it can't be traced"
            : $"No change to {signal} has been seen since the debugger connected"));
        string? note = null;
        if (ctx.Graph != null)
        {
            var drivers = ctx.Graph.Symbols.Where(s => s.Outputs.Contains(signal)).Take(5).ToList();
            foreach (var d in drivers)
            {
                var ins = d.Inputs.Where(i => i.Length > 0 && i != "0" && i != "1").Take(8)
                    .Select(i => $"{i}={ShowAny(ctx.CurrentValue(i))}");
                steps.Add(new WhyStep("unknown", signal, curText, null, $"{signal} is driven by {d.Name} {d.Path} (inputs: {string.Join(", ", ins)})", d.Id));
            }
            if (drivers.Count == 0) note = "Nothing in the program drives this signal: it is an input, a constant or set from outside.";
            else note = "Repeat the action with the debugger open to see the change that led here.";
        }
        else note = $"{ctx.RuntimeKind} programs don't expose their logic. Open the debugger and repeat the action to see when this join changes and from where.";
        return new WhyResult(signal, curText, $"{head}. {steps[0].Text}.", steps, false, note);
    }

    private static (string Name, int Index)? LastInputChange(GraphSymbol sym, int before, IReadOnlyList<TraceRow> trace, Func<string, int, int> latest)
    {
        (string, int)? best = null;
        foreach (var i in sym.Inputs)
        {
            if (i.Length == 0 || i == "0" || i == "1") continue;
            var idx = latest(i, before);
            if (idx >= 0 && (best == null || idx > best.Value.Item2)) best = (i, idx);
        }
        return best;
    }

    // " (IPID03 d1)" for a digital signal a device symbol outputs: digital pins come first, so the position is the join.
    private static string JoinText(string signal, SigKind kind, Dictionary<string, GraphSymbol> symbols)
    {
        var jm = JoinName().Match(signal);
        if (jm.Success) return $" (IPID{jm.Groups[1].Value.ToUpperInvariant()} {char.ToLowerInvariant(jm.Groups[3].Value[0])}{jm.Groups[4].Value})";
        if (kind != SigKind.Digital) return "";
        foreach (var s in symbols.Values)
        {
            if (!s.Device) continue;
            var i = s.Outputs.ToList().IndexOf(signal);
            if (i >= 0) return $" ({s.Name} d{i + 1})";
        }
        return "";
    }

    private static string FromClients(WhyContext ctx, string ipId)
    {
        var c = ctx.ClientsOf(ipId.ToUpperInvariant());
        return c.Count > 0 ? " from " + string.Join(", ", c.Take(3)) : "";
    }

    private static string PressText(SigKind kind, object? v) => kind == SigKind.Digital
        ? (v is true ? "was pressed" : "was released")
        : $"was set to {Show(kind, v)}";

    private static string Went(SigKind kind, object? v) => kind == SigKind.Digital ? (v is true ? "high" : "low") : "to " + Show(kind, v);

    private static string Clock(DateTimeOffset t) => t.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static SigKind KindOfValue(object v) => v switch { bool => SigKind.Digital, string => SigKind.Serial, _ => SigKind.Analog };

    private static string Show(SigKind kind, object? v) => kind switch
    {
        SigKind.Digital => v is true ? "1" : "0",
        SigKind.Analog => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "0",
        _ => "\"" + v + "\""
    };

    private static string ShowAny(object? v) => v == null ? "?" : Show(KindOfValue(v), v);
}
