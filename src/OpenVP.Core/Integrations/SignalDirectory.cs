using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenVP.Core.Integrations;

/// <summary>
/// Looks up a slot's signals by name (kind, current value) for the integrations. The name table is kept per loaded
/// program and rebuilt when the program changes, or (at most twice a second) when a name isn't found, because a SIMPL#
/// program's devices register after it starts. Reading a current value asks the program, so don't call it per change.
/// </summary>
public sealed class SignalDirectory(Processor processor)
{
    private sealed class Table
    {
        public int Generation = -1;
        public long BuiltAt;
        public Dictionary<string, SignalInfo> ByName = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<int, Table> _tables = new();
    private static readonly long MinRebuild = Stopwatch.Frequency / 2;

    private Table Get(ProgramSlot slot, bool force)
    {
        var t = _tables.GetOrAdd(slot.Number, _ => new Table());
        lock (t)
        {
            var now = Stopwatch.GetTimestamp();
            var stale = t.Generation != slot.Generation || t.BuiltAt == 0;
            if (stale || (force && now - t.BuiltAt >= MinRebuild))
            {
                var map = new Dictionary<string, SignalInfo>(StringComparer.Ordinal);
                try { if (slot.Runtime is { } rt) foreach (var s in rt.GetSignals()) map[s.Name] = s; } catch { /* a runtime that can't list signals has none to offer */ }
                t.ByName = map; t.Generation = slot.Generation; t.BuiltAt = now == 0 ? 1 : now;
            }
            return t;
        }
    }

    /// <summary>The signal's kind and the value it had when the table was built (use <see cref="Current"/> for a fresh value).</summary>
    public bool TryGet(int slot, string name, out SignalInfo info)
    {
        var s = processor.Slot(slot);
        var t = Get(s, false);
        Dictionary<string, SignalInfo> map; lock (t) map = t.ByName;
        if (map.TryGetValue(name, out info!)) return true;
        t = Get(s, true);
        lock (t) map = t.ByName;
        return map.TryGetValue(name, out info!);
    }

    /// <summary>The signal with its value right now, or null if the slot has no such signal.</summary>
    public SignalInfo? Current(int slot, string name)
    {
        var s = processor.Slot(slot);
        if (s.Runtime is not { } rt) return null;
        try { return rt.GetSignals().FirstOrDefault(x => x.Name == name); }
        catch { return null; }
    }

    /// <summary>Every signal of the slot with its current value.</summary>
    public IReadOnlyList<SignalInfo> All(int slot)
    {
        try { return processor.Slot(slot).Runtime?.GetSignals() ?? Array.Empty<SignalInfo>(); }
        catch { return Array.Empty<SignalInfo>(); }
    }

    public enum SetOutcome { Ok, NoProgram, UnknownSignal, BadValue }

    /// <summary>
    /// Sets a signal from text a person or device sent (<c>1</c>, <c>50%</c>, <c>pulse</c>, serial text).
    /// <paramref name="error"/> says why not when the result isn't <see cref="SetOutcome.Ok"/>.
    /// </summary>
    public SetOutcome TrySetFromText(int slot, string name, string text, out string? error)
    {
        error = null;
        var s = processor.Slot(slot);
        if (s.Runtime == null) { error = "There is no program in that slot."; return SetOutcome.NoProgram; }
        if (!TryGet(slot, name, out var info)) { error = $"The program has no signal '{name}'."; return SetOutcome.UnknownSignal; }
        if (info.Kind == SigKind.Digital && SignalValues.IsPulse(text))
        {
            if (s.ForcePulse(name)) return SetOutcome.Ok;
            error = $"Couldn't pulse '{name}'."; return SetOutcome.UnknownSignal;
        }
        if (!SignalValues.TryParse(info.Kind, text, out var v, out error)) return SetOutcome.BadValue;
        if (s.ForceSignal(name, SignalValues.ForRuntime(v))) return SetOutcome.Ok;
        error = $"The program has no signal '{name}'."; return SetOutcome.UnknownSignal;
    }

    /// <summary>Like <see cref="TrySetFromText"/>: null on success, else the reason.</summary>
    public string? SetFromText(int slot, string name, string text)
    {
        TrySetFromText(slot, name, text, out var error);
        return error;
    }
}
