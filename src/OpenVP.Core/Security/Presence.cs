namespace OpenVP.Core.Security;

public sealed record PresenceEntry(string Key, string Name, string Initials, string Kind, DateTimeOffset Since);

/// <summary>
/// Who has a slot open right now. Every /ws/trace connection registers here for as long as it is open: the debugger as <c>debugging</c>, the console's
/// log view as <c>viewing</c>. A person with several tabs open counts once per kind. The identity is a user name, or the IP address for the access key
/// and the localhost exemption.
/// </summary>
public sealed class PresenceTracker
{
    private sealed class Item { public int Slot; public string Key = "", Name = "", Kind = ""; public DateTimeOffset Since; }
    private readonly object _gate = new();
    private readonly List<Item> _items = new();
    public event Action<int>? Changed;

    public IDisposable Enter(int slot, string key, string name, string kind)
    {
        var it = new Item { Slot = slot, Key = key, Name = name, Kind = kind, Since = DateTimeOffset.UtcNow };
        lock (_gate) _items.Add(it);
        Changed?.Invoke(slot);
        return new Lease(this, it);
    }

    private sealed class Lease(PresenceTracker t, Item it) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            lock (t._gate) t._items.Remove(it);
            t.Changed?.Invoke(it.Slot);
        }
    }

    public List<PresenceEntry> ForSlot(int slot)
    {
        lock (_gate)
            return _items.Where(i => i.Slot == slot).GroupBy(i => (i.Key, i.Kind))
                .Select(g => new PresenceEntry(g.Key.Key, g.First().Name, Principal.Initials(g.First().Name), g.Key.Kind, g.Min(i => i.Since)))
                .OrderBy(e => e.Since).ToList();
    }

    /// <summary>The others with this slot's debugger open, for the warning before a force, reload or step. <paramref name="myKey"/> is left out.</summary>
    public List<PresenceEntry> OthersDebugging(int slot, string myKey) => ForSlot(slot).Where(e => e.Kind == "debugging" && e.Key != myKey).ToList();
}
