using System.Text.Json;

namespace OpenVP.Core;

/// <summary>A saved trace, without its rows.</summary>
public sealed record TraceInfo(string Id, string Name, DateTimeOffset Saved, string Program, int Slot, int Count);

/// <summary>
/// Named traces saved per slot in <c>data/traces/slotNN/</c>, for comparing before and after a program edit.
/// One <c>.json</c> file per trace (the same format the trace export writes) and an <c>index.json</c> with the names.
/// </summary>
public sealed class TraceStore
{
    public const int MaxPerSlot = 50;
    private readonly string _root;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TraceStore(string dataDirectory) => _root = Path.Combine(dataDirectory, "traces");

    private string Dir(int slot) => Path.Combine(_root, $"slot{slot:00}");
    private string IndexPath(int slot) => Path.Combine(Dir(slot), "index.json");

    private List<TraceInfo> ReadIndex(int slot)
    {
        try { return File.Exists(IndexPath(slot)) ? JsonSerializer.Deserialize<List<TraceInfo>>(File.ReadAllText(IndexPath(slot)), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    private void WriteIndex(int slot, List<TraceInfo> list)
    {
        Directory.CreateDirectory(Dir(slot));
        File.WriteAllText(IndexPath(slot), JsonSerializer.Serialize(list, Json));
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<TraceInfo> List(int slot) { lock (_gate) return ReadIndex(slot).OrderByDescending(t => t.Saved).ToList(); }

    public TraceInfo Save(int slot, string? name, string program, IReadOnlyList<TraceRow> rows)
    {
        name = (name ?? "").Trim();
        if (name.Length > 80) name = name[..80];
        lock (_gate)
        {
            var list = ReadIndex(slot);
            if (list.Count >= MaxPerSlot) throw new InvalidOperationException($"A slot keeps at most {MaxPerSlot} saved traces. Delete one first.");
            var now = DateTimeOffset.Now;
            if (name.Length == 0) name = $"Trace {now:yyyy-MM-dd HH:mm:ss}";
            var id = Guid.NewGuid().ToString("N")[..10];
            Directory.CreateDirectory(Dir(slot));
            File.WriteAllText(Path.Combine(Dir(slot), id + ".json"), TraceIo.ToJson(new TraceFile(slot, program, now, rows)));
            var info = new TraceInfo(id, name, now, program, slot, rows.Count);
            list.Add(info);
            WriteIndex(slot, list);
            return info;
        }
    }

    public (TraceInfo Info, TraceFile File)? Get(int slot, string id)
    {
        if (!IsId(id)) return null;
        lock (_gate)
        {
            var info = ReadIndex(slot).FirstOrDefault(t => t.Id == id);
            var path = Path.Combine(Dir(slot), id + ".json");
            if (info == null || !File.Exists(path)) return null;
            return (info, TraceIo.FromJson(File.ReadAllText(path)));
        }
    }

    public bool Rename(int slot, string id, string name)
    {
        if (!IsId(id)) return false;
        lock (_gate)
        {
            var list = ReadIndex(slot);
            var i = list.FindIndex(t => t.Id == id);
            if (i < 0) return false;
            list[i] = list[i] with { Name = name.Trim().Length == 0 ? list[i].Name : name.Trim()[..Math.Min(80, name.Trim().Length)] };
            WriteIndex(slot, list);
            return true;
        }
    }

    public bool Delete(int slot, string id)
    {
        if (!IsId(id)) return false;
        lock (_gate)
        {
            var list = ReadIndex(slot);
            if (list.RemoveAll(t => t.Id == id) == 0) return false;
            WriteIndex(slot, list);
            try { File.Delete(Path.Combine(Dir(slot), id + ".json")); } catch (IOException) { }
            return true;
        }
    }

    private static bool IsId(string id) => id.Length is > 0 and <= 32 && id.All(c => char.IsAsciiLetterOrDigit(c));
}

/// <summary>The last changes of a slot, kept while a debugger (or anything else) is attached to it, for "why is this signal this value?".</summary>
public sealed class TraceBuffer
{
    private readonly TraceEvent?[] _ring;
    private readonly object _gate = new();
    private int _head, _count;

    public TraceBuffer(int capacity = 20_000) => _ring = new TraceEvent?[capacity];

    public void Add(TraceEvent e)
    {
        lock (_gate)
        {
            _ring[_head] = e;
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }
    }

    public void Clear() { lock (_gate) { Array.Clear(_ring); _head = _count = 0; } }
    public int Count { get { lock (_gate) return _count; } }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<TraceEvent> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<TraceEvent>(_count);
            for (var i = 0; i < _count; i++) list.Add(_ring[(_head - _count + i + _ring.Length * 2) % _ring.Length]!);
            return list;
        }
    }
}
