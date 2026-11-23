namespace OpenVP.Core.Security;

public sealed class SlotLockRecord
{
    public int Slot { get; set; }
    /// <summary>Who locked it, as <see cref="Principal.LockId"/> ("user:sam").</summary>
    public string ById { get; set; } = "";
    /// <summary>The name to show ("Sam").</summary>
    public string By { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset? Expires { get; set; }
}

public sealed class SlotLockFile { public List<SlotLockRecord> Locks { get; set; } = new(); }

/// <summary>
/// "Live event, don't touch." A locked slot refuses anything that would change it (load, unload, stop, restart, forcing signals, editing files,
/// rolling back, scheduled and pushed changes, import) unless the caller is an Admin or the one who locked it. Panels and shared panels keep working.
/// Locks are kept in <c>data/security/slotlocks.json</c> and last until someone unlocks or the optional expiry passes.
/// </summary>
public sealed class SlotLocks
{
    private readonly object _gate = new();
    private readonly string? _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly SlotLockFile _data;

    /// <summary>Raised after a slot is locked (true) or unlocked, by hand or by its expiry (false).</summary>
    public event Action<int, bool>? Changed;

    public SlotLocks(string? dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _file = dataDirectory == null ? null : Path.Combine(dataDirectory, "security", "slotlocks.json");
        _data = _file == null ? new() : SecurityFiles.Load<SlotLockFile>(_file);
    }

    private void Save() { if (_file != null) SecurityFiles.Save(_file, _data); }

    public SlotLockRecord? Get(int slot)
    {
        var expired = false;
        SlotLockRecord? r;
        lock (_gate)
        {
            r = _data.Locks.FirstOrDefault(l => l.Slot == slot);
            if (r != null && r.Expires != null && r.Expires <= _now()) { _data.Locks.Remove(r); Save(); r = null; expired = true; }
        }
        if (expired) Changed?.Invoke(slot, false);
        return r;
    }

    public List<SlotLockRecord> All()
    {
        Tick();
        lock (_gate) return _data.Locks.OrderBy(l => l.Slot).ToList();
    }

    /// <summary>Drops locks whose expiry has passed (raising <see cref="Changed"/>); called by a timer.</summary>
    public void Tick()
    {
        List<int> gone;
        lock (_gate)
        {
            var now = _now();
            gone = _data.Locks.Where(l => l.Expires != null && l.Expires <= now).Select(l => l.Slot).ToList();
            if (gone.Count > 0) { _data.Locks.RemoveAll(l => gone.Contains(l.Slot)); Save(); }
        }
        foreach (var s in gone) Changed?.Invoke(s, false);
    }

    /// <summary>Locks the slot. Throws if it is already locked by someone else (an Admin or the locker can change a lock by unlocking first).</summary>
    public SlotLockRecord Lock(int slot, Principal by, string? reason, DateTimeOffset? expires)
    {
        if (expires != null && expires <= _now()) throw new ArgumentException("The expiry has to be in the future.");
        if (reason is { Length: > 300 }) throw new ArgumentException("The reason is too long (300 characters at most).");
        SlotLockRecord rec;
        lock (_gate)
        {
            var existing = _data.Locks.FirstOrDefault(l => l.Slot == slot && (l.Expires == null || l.Expires > _now()));
            if (existing != null && !Allows(existing, by)) throw new InvalidOperationException($"Slot {slot} is already locked by {existing.By}.");
            _data.Locks.RemoveAll(l => l.Slot == slot);
            rec = new SlotLockRecord { Slot = slot, ById = by.LockId, By = by.Display, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), Since = existing?.Since ?? _now(), Expires = expires };
            _data.Locks.Add(rec);
            Save();
        }
        Changed?.Invoke(slot, true);
        return rec;
    }

    /// <summary>Removes the lock. Only an Admin or the one who locked it may; returns false if the slot wasn't locked.</summary>
    public bool Unlock(int slot, Principal by)
    {
        lock (_gate)
        {
            var existing = _data.Locks.FirstOrDefault(l => l.Slot == slot);
            if (existing == null) return false;
            if (!Allows(existing, by)) throw new UnauthorizedAccessException($"Slot {slot} was locked by {existing.By}. Only they or an Admin can unlock it.");
            _data.Locks.Remove(existing);
            Save();
        }
        Changed?.Invoke(slot, false);
        return true;
    }

    private static bool Allows(SlotLockRecord l, Principal p) => p.IsAdmin || l.ById == p.LockId;

    /// <summary>Null if <paramref name="p"/> may change the slot now; else the lock that stops them. Pass null for the scheduler, push-in from nowhere in particular, anything automatic: a lock stops it.</summary>
    public SlotLockRecord? Blocking(int slot, Principal? p)
    {
        var l = Get(slot);
        if (l == null) return null;
        return p != null && Allows(l, p) ? null : l;
    }

    public static string Message(SlotLockRecord l) =>
        $"Slot {l.Slot} is locked by {l.By}{(string.IsNullOrEmpty(l.Reason) ? "" : ": " + l.Reason)}{(l.Expires != null ? $" (until {l.Expires.Value.ToLocalTime():yyyy-MM-dd HH:mm})" : "")}. Only they or an Admin can change it.";
}
