using System.Globalization;
using System.Text;

namespace OpenVP.Simpl;

/// <summary>One bracketed block from a .smw file: ObjTp plus its key=value lines.</summary>
public sealed class SmwBlock
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);
    public string ObjTp => Get("ObjTp") ?? string.Empty;
    public string? Get(string key) => Fields.TryGetValue(key, out var v) ? v : null;
    public int GetInt(string key, int fallback = 0) => int.TryParse(Get(key), out var v) ? v : fallback;
}

/// <summary>
/// A symbol: a logic block or a device. Inputs and outputs are lists of signal handles, indexed by pin
/// (pin 1 = I1 / O1). Pins are grouped digital, then analog, then serial.
/// </summary>
public sealed class SmwSymbol
{
    public int Handle { get; init; }
    public int Code { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Parent { get; init; }
    public int DeviceHandle { get; init; }
    public List<int> Children { get; } = new();

    /// <summary>Signal handle per input pin. Index 0 = pin 1. 0 means unconnected.</summary>
    public List<int> Inputs { get; } = new();
    public List<int> Outputs { get; } = new();
    public List<string> Parameters { get; } = new();

    /// <summary>Digital / analog / serial pin counts from n1I..n3I and n1O..n3O, if present.</summary>
    public int[] InputGroups { get; } = new int[3];
    public int[] OutputGroups { get; } = new int[3];
    public bool HasGroups => InputGroups.Sum() + OutputGroups.Sum() > 0;

    public SmwBlock Raw { get; init; } = new();

    public override string ToString() => $"#{Handle} {Name} (SmC={Code})";
}

/// <summary>Where a device symbol sits in the hardware tree (see <see cref="SmwFile.RoleOf"/>).</summary>
public enum DeviceRole
{
    /// <summary>On the processor's Ethernet slot: has its own IP ID (panel, EISC, DM-NVX...).</summary>
    IpId,
    /// <summary>Inside an IP-ID device: a slot, port or extender. Shares its parent's IP ID, has no ID of its own.</summary>
    SubDevice,
    /// <summary>Other processor hardware (Cresnet, COM, IR, relays...). No IP ID.</summary>
    Other
}

public sealed class SmwSignal
{
    public int Handle { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Reader for the SIMPL Windows .smw text format. The format is undocumented; this follows what has
/// been observed in real files: a flat list of blocks, each with ObjTp=, where Sm blocks are symbols,
/// Sg blocks are signals, and symbols point at signals by handle (I1=4 means input pin 1 is signal 4).
/// </summary>
public sealed class SmwFile
{
    public List<SmwBlock> Blocks { get; } = new();
    public Dictionary<int, SmwSymbol> Symbols { get; } = new();
    public Dictionary<int, SmwSignal> Signals { get; } = new();
    /// <summary>Dv blocks (hardware/devices). Ad= holds the IP ID or Cresnet ID in hex.</summary>
    public Dictionary<int, SmwBlock> Devices { get; } = new();
    public SmwBlock? Header { get; private set; }
    public string ProgramName => Header?.Get("PgmNm") ?? Header?.Get("Nm") ?? "SIMPL Windows program";

    public static SmwFile Load(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // .smw files are Windows-1252 text.
        var text = File.ReadAllText(path, Encoding.GetEncoding(1252));
        return Parse(text);
    }

    public static SmwFile Parse(string text)
    {
        var file = new SmwFile();
        SmwBlock? current = null;
        var bracketed = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line == "[") { current = new SmwBlock(); bracketed = true; continue; }
            if (line == "]") { Finish(); continue; }
            if (line.Length == 0)
            {
                if (!bracketed) Finish();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq];
            var value = line[(eq + 1)..];
            if (key == "ObjTp" && current != null && current.Fields.ContainsKey("ObjTp")) Finish();
            current ??= new SmwBlock();
            current.Fields[key] = value;
        }
        Finish();

        foreach (var b in file.Blocks) file.Index(b);
        return file;

        void Finish()
        {
            if (current != null && current.Fields.Count > 0) file.Blocks.Add(current);
            current = null;
            bracketed = false;
        }
    }

    private void Index(SmwBlock b)
    {
        switch (b.ObjTp)
        {
            case "Hd":
                Header = b;
                break;
            case "Dv":
                // Runs of unused slots are written as ranges ("H=261.262", "Ad=01,02"); nothing refers to those.
                if (b.GetInt("H") is > 0 and var dh) Devices[dh] = b;
                break;
            case "Sg":
                var sg = new SmwSignal { Handle = b.GetInt("H"), Name = b.Get("Nm") ?? string.Empty };
                Signals[sg.Handle] = sg;
                break;
            case "Sm":
                var sm = new SmwSymbol
                {
                    Handle = b.GetInt("H"),
                    Code = b.GetInt("SmC"),
                    Name = b.Get("Nm") ?? string.Empty,
                    Parent = b.GetInt("PrH"),
                    DeviceHandle = b.GetInt("DvH"),
                    Raw = b
                };
                for (int g = 0; g < 3; g++)
                {
                    sm.InputGroups[g] = b.GetInt($"n{g + 1}I");
                    sm.OutputGroups[g] = b.GetInt($"n{g + 1}O");
                }
                Fill(sm.Inputs, b, "I", b.GetInt("mI"));
                Fill(sm.Outputs, b, "O", b.GetInt("mO"));
                for (int i = 1, n = Math.Max(b.GetInt("mP"), MaxIndex(b, "P")); i <= n; i++)
                    sm.Parameters.Add(b.Get($"P{i}") ?? string.Empty);
                for (int i = 1, n = Math.Max(b.GetInt("mC"), MaxIndex(b, "C")); i <= n; i++)
                    if (b.GetInt($"C{i}") is var c && c > 0) sm.Children.Add(c);
                FillMissingGroups(sm);
                Symbols[sm.Handle] = sm;
                break;
        }
    }

    private static void Fill(List<int> pins, SmwBlock b, string prefix, int declared)
    {
        var n = Math.Max(declared, MaxIndex(b, prefix));
        for (int i = 1; i <= n; i++) pins.Add(b.GetInt($"{prefix}{i}"));
    }

    private static int MaxIndex(SmwBlock b, string prefix)
    {
        var max = 0;
        foreach (var k in b.Fields.Keys)
            if (k.Length > prefix.Length && k.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(k.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var i))
                max = Math.Max(max, i);
        return max;
    }

    /// <summary>
    /// Files often list only some group counts (e.g. n1I and n2I with mI=96). Whatever pins are left
    /// over belong to the next group, which is serial.
    /// </summary>
    private static void FillMissingGroups(SmwSymbol sm)
    {
        if (!sm.HasGroups) return;
        Fix(sm.InputGroups, sm.Inputs.Count);
        Fix(sm.OutputGroups, sm.Outputs.Count);

        static void Fix(int[] groups, int total)
        {
            var listed = groups.Sum();
            if (listed >= total) return;
            var lastSet = groups[2] > 0 ? 2 : groups[1] > 0 ? 1 : groups[0] > 0 ? 0 : -1;
            if (lastSet < 2) groups[lastSet + 1] += total - listed;
        }
    }

    /// <summary>
    /// What a device symbol is, going by where its Dv block sits in the hardware tree:
    /// processor -> card (EthernetCard) -> device list (Ethernet Devices) -> slot (P4Ethernet, Ad = IP ID) ->
    /// IP-ID device (Ad = IP ID in hex) -> its own slots, ports and extenders (Ad = slot number in the device).
    /// </summary>
    public DeviceRole RoleOf(int dvHandle)
    {
        // No Dv block, or one that hangs off nothing we know: a hand-written or trimmed file. Treat as an IP-ID device.
        if (!Devices.TryGetValue(dvHandle, out var dv) || !Devices.ContainsKey(dv.GetInt("PrH"))) return DeviceRole.IpId;
        if (IsOnIpIdSlot(dv)) return DeviceRole.IpId;
        for (var a = ParentOf(dv); a != null; a = ParentOf(a))
            if (IsOnIpIdSlot(a)) return DeviceRole.SubDevice;
        return DeviceRole.Other;
    }

    private SmwBlock? ParentOf(SmwBlock dv) => Devices.GetValueOrDefault(dv.GetInt("PrH"));

    /// <summary>A device (it has a symbol) that sits directly on an Ethernet slot of the processor.</summary>
    private bool IsOnIpIdSlot(SmwBlock dv)
    {
        if (dv.GetInt("SmH") <= 0) return false;
        var slot = ParentOf(dv);
        if (slot == null) return false;
        var list = ParentOf(slot);
        return slot.Get("Nm")?.EndsWith("Ethernet", StringComparison.OrdinalIgnoreCase) == true ||
               list?.Get("Nm")?.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) == true;
    }

    public string SignalName(int handle) =>
        Signals.TryGetValue(handle, out var s) && s.Name.Length > 0 ? s.Name : $"#{handle}";

    /// <summary>
    /// Parses a SIMPL parameter: "255d" decimal, "1Fh" hex, "50%" percent of 65535, "1.5s" / "500ms" / "25t"
    /// (ticks of 10 ms) as milliseconds, or a quoted string.
    /// </summary>
    public static bool TryParseNumber(string? raw, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        if (s.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) return Try(s[..^2], 1, out value);
        var last = char.ToLowerInvariant(s[^1]);
        switch (last)
        {
            case 'd': return long.TryParse(s[..^1], out value);
            case 'h': return long.TryParse(s[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            case 's': return Try(s[..^1], 1000, out value);
            case 'm': return Try(s[..^1], 60_000, out value);
            case 't': return Try(s[..^1], 10, out value);
            case '%':
                if (!double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return false;
                value = (long)Math.Round(pct / 100.0 * 65535);
                return true;
            default: return long.TryParse(s, out value);
        }

        static bool Try(string num, double scale, out long v)
        {
            v = 0;
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
            v = (long)Math.Round(d * scale);
            return true;
        }
    }

    public static string ParseString(string? raw)
    {
        if (raw == null) return string.Empty;
        var s = raw.Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }
}
