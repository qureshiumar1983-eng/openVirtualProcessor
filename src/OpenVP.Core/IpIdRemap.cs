using System.Globalization;
using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// Per-slot IP ID remap: "the program's IP ID 03 answers at 0A". A panel connects to the <i>public</i> ID (0A) and drives the
/// program's own ID (03); the program never sees a difference. It is applied where a panel finds its device (browser panels,
/// CIP, share links), so it works for every runtime. Once an ID is remapped, panels no longer reach it at its old number.
/// Saved in <c>devices/ipremap.json</c> in the slot's folder.
/// </summary>
public sealed class IpIdRemap
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, uint> _toPublic = new();   // program id -> public id
    private readonly Dictionary<uint, uint> _toProgram = new();  // public id -> program id
    private readonly string _file;

    public IpIdRemap(string file)
    {
        _file = file;
        Load();
    }

    /// <summary>Program ID → public ID, as pairs.</summary>
    public IReadOnlyList<(uint Program, uint Public)> Entries
    {
        get { lock (_gate) return _toPublic.OrderBy(k => k.Key).Select(k => (k.Key, k.Value)).ToList(); }
    }

    /// <summary>The ID panels use for this program ID (the same ID if it isn't remapped).</summary>
    public uint PublicOf(uint programId) { lock (_gate) return _toPublic.TryGetValue(programId, out var p) ? p : programId; }

    public bool IsRemapped(uint programId) { lock (_gate) return _toPublic.ContainsKey(programId); }

    /// <summary>The program ID a panel connecting to <paramref name="publicId"/> drives: the mapped one, the same ID if
    /// nothing is mapped, or null if that number was remapped away and nothing answers there any more.</summary>
    public uint? ToProgram(uint publicId)
    {
        lock (_gate)
        {
            if (_toProgram.TryGetValue(publicId, out var p)) return p;
            return _toPublic.ContainsKey(publicId) ? null : publicId;
        }
    }

    /// <summary>True if a panel connecting to <paramref name="publicId"/> would be routed by a mapping (to a different ID).</summary>
    public bool Maps(uint publicId) { lock (_gate) return _toProgram.ContainsKey(publicId); }

    /// <param name="inUse">IDs the program itself has registered that aren't remapped: a public ID can't land on one of those.</param>
    public void Set(uint programId, uint publicId, Func<uint, bool>? inUse = null)
    {
        if (programId is 0 or > 0xFE || publicId is 0 or > 0xFE) throw new ArgumentException("IP IDs go from 01 to FE.");
        if (programId == publicId) { Remove(programId); return; }
        lock (_gate)
        {
            if (_toProgram.TryGetValue(publicId, out var other) && other != programId)
                throw new ArgumentException($"{publicId:X2} is already used for the program's IP ID {other:X2}.");
            if (!_toPublic.ContainsKey(publicId) && inUse?.Invoke(publicId) == true)
                throw new ArgumentException($"The program has its own IP ID {publicId:X2}; remap that one first, or pick another number.");
            if (_toPublic.TryGetValue(programId, out var old)) _toProgram.Remove(old);
            _toPublic[programId] = publicId;
            _toProgram[publicId] = programId;
            Save();
        }
    }

    public bool Remove(uint programId)
    {
        lock (_gate)
        {
            if (!_toPublic.Remove(programId, out var p)) return false;
            _toProgram.Remove(p);
            Save();
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) { _toPublic.Clear(); _toProgram.Clear(); Save(); }
    }

    private void Save()
    {
        try
        {
            var map = _toPublic.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString("X2"), k => k.Value.ToString("X2"));
            if (map.Count == 0) { if (File.Exists(_file)) File.Delete(_file); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(map, SlotIo.Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Couldn't save the IP ID remap: {e.Message}", e);
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            _toPublic.Clear(); _toProgram.Clear();
            try
            {
                if (!File.Exists(_file)) return;
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file), SlotIo.Json);
                foreach (var (k, v) in map ?? new())
                {
                    if (uint.TryParse(k, NumberStyles.HexNumber, null, out var a) && uint.TryParse(v, NumberStyles.HexNumber, null, out var b)
                        && a is > 0 and <= 0xFE && b is > 0 and <= 0xFE && a != b && !_toProgram.ContainsKey(b))
                    {
                        _toPublic[a] = b; _toProgram[b] = a;
                    }
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        }
    }
}
