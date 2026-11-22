namespace OpenVP.Core;

/// <summary>One slot to bring back when OpenVP starts.</summary>
/// <param name="Source"><c>config</c> (an <c>OpenVP:Autoload</c> entry, which wins) or <c>remembered</c> (the slot's own memory).</param>
public sealed record RestoreEntry(int Slot, int Order, int DelaySeconds, string Source, string? ConfigPath, ProgramMemory? Memory);

/// <summary>
/// Brings programs back after OpenVP restarts. The plan lists the slots in start order (lowest first, then slot number);
/// each waits its own delay after the one before it. A slot that fails to come back goes to Error and the rest carry on.
/// </summary>
public sealed class StartupRestorer
{
    private readonly Processor _processor;
    private readonly Action<TimeSpan> _sleep;

    /// <summary>Loads a program remembered from a file on this computer (so the server can also watch it). Return false to fall back to the uploaded copy.</summary>
    public Func<RestoreEntry, bool>? PathLoader { get; set; }

    /// <summary>Wraps loading one slot (the server holds the slot's upload lock here).</summary>
    public Func<int, Func<bool>, bool>? Guard { get; set; }

    public StartupRestorer(Processor processor, Action<TimeSpan>? sleep = null)
    {
        _processor = processor;
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>The slots to restore and in what order. <paramref name="autoload"/> maps slot numbers to program files from the config.</summary>
    public static List<RestoreEntry> Plan(IReadOnlyList<ProgramSlot> slots, IReadOnlyDictionary<int, string>? autoload)
    {
        var list = new List<RestoreEntry>();
        foreach (var s in slots)
        {
            var st = s.Settings;
            var order = st.StartOrder ?? s.Number;
            if (autoload != null && autoload.TryGetValue(s.Number, out var cfg) && !string.IsNullOrWhiteSpace(cfg))
                list.Add(new RestoreEntry(s.Number, order, st.StartDelaySeconds, "config", cfg, null));
            else if (st.Autostart && st.Program != null)
                list.Add(new RestoreEntry(s.Number, order, st.StartDelaySeconds, "remembered", null, st.Program));
        }
        return list.OrderBy(e => e.Order).ThenBy(e => e.Slot).ToList();
    }

    /// <summary>The program file in the slot's upload folder that a remembered program uses.</summary>
    public string UploadedCopy(int slot, ProgramMemory m) =>
        Path.Combine(_processor.DataDirectory, "uploads", $"slot{slot:00}", Path.GetFileName(m.File ?? ""));

    /// <summary>Restores each entry in order, waiting out the delays. Returns how many slots came back without an error.</summary>
    public int Run(IReadOnlyList<RestoreEntry> plan)
    {
        var ok = 0;
        foreach (var e in plan)
        {
            var slot = _processor.Slot(e.Slot);
            if (e.DelaySeconds > 0)
            {
                slot.Log("Notice", $"Waiting {e.DelaySeconds} s before restoring this slot (start order {e.Order}).");
                _sleep(TimeSpan.FromSeconds(e.DelaySeconds));
            }
            try
            {
                var did = Guard != null ? Guard(e.Slot, () => RestoreOne(e)) : RestoreOne(e);
                if (did) ok++;
            }
            catch (Exception ex)
            {
                if (slot.State != SlotState.Error) slot.Fail(ex);
                slot.Log("Error", $"Couldn't restore the program at start-up: {ex.Message}");
            }
        }
        return ok;
    }

    /// <summary>Restores one entry. Throws if it can't; true if the slot was loaded.</summary>
    public bool RestoreOne(RestoreEntry e)
    {
        var slot = _processor.Slot(e.Slot);
        if (e.Source == "config")
        {
            _processor.LoadProgram(e.Slot, e.ConfigPath!);
            slot.Log("Notice", $"Restored from the Autoload setting: {Path.GetFileName(e.ConfigPath)} (started).");
            return true;
        }
        var m = e.Memory!;
        if (m.Source == ProgramMemory.Path && !string.IsNullOrEmpty(m.FilePath))
        {
            if (File.Exists(m.FilePath))
            {
                if (PathLoader != null && PathLoader(e)) return true;
            }
            else slot.Log("Warning", $"{m.FilePath} isn't there any more; using the copy OpenVP kept.");
        }
        var path = UploadedCopy(e.Slot, m);
        if (!File.Exists(path)) throw new FileNotFoundException($"The remembered program {m.File ?? m.FilePath} is no longer in the data folder.");
        _processor.LoadProgram(e.Slot, path, m.Running);
        slot.Log("Notice", $"Restored {Path.GetFileName(path)} from its saved copy ({(m.Running ? "started" : "left stopped, as it was")}).");
        return true;
    }
}
