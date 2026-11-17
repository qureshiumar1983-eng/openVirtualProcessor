using System.Text.Json;

namespace OpenVP.Lpz;

/// <summary>Values kept across restarts (Make String Permanent, Analog RAM…), saved as JSON in the slot folder.</summary>
public sealed class NvramStore
{
    private readonly string _path;
    private readonly Dictionary<string, JsonElement> _data;
    private Timer? _saveTimer;
    private readonly object _gate = new();

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
            _saveTimer ??= new Timer(_ => Flush(), null, 1000, Timeout.Infinite); // batch writes
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
    }
}
