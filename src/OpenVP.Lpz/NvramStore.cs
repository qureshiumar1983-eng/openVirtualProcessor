using System.Text.Json;

namespace OpenVP.Lpz;

/// <summary>Values kept across restarts (Make String Permanent, Analog RAM…), saved as JSON in the slot folder.</summary>
public sealed class NvramStore
{
    private readonly string _path;
    private readonly Dictionary<string, JsonElement> _data;
    private Timer? _saveTimer;
    private readonly object _gate = new();      // guards _data, the timer and _version (never held during disk I/O)
    private readonly object _writeGate = new();  // one writer at a time
    private long _version, _written;

    public NvramStore(string path)
    {
        _path = path;
        try { _data = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path)) ?? new() : new(); }
        catch (JsonException) { _data = new(); }
    }

    public object? Get(string key)
    {
        lock (_gate)
        {
            if (!_data.TryGetValue(key, out var e)) return null;
            return e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Number => (object)(ushort)Math.Clamp(e.GetInt32(), 0, 65535),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
    }

    public void Set(string key, object value)
    {
        lock (_gate)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            _version++;
            _saveTimer ??= new Timer(_ => Flush(), null, 1000, Timeout.Infinite); // batch writes
        }
    }

    public void Flush()
    {
        // Snapshot under the lock the slot thread needs; do the disk work outside it.
        string json;
        long version;
        lock (_gate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
            json = JsonSerializer.Serialize(_data);
            version = _version;
        }
        lock (_writeGate)
        {
            if (version <= _written) return; // a newer snapshot already reached the disk
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write beside the target and swap it in, so a crash mid-write can't leave invalid JSON
            // (which the next load reads as empty, losing every saved value).
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
            _written = version;
        }
    }
}
