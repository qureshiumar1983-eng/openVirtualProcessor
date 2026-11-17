using System.Text;

namespace OpenVP.Lpz;

/// <summary>A parameter value on a compiled symbol: a number (tag 2) or a string (tag 3).</summary>
public readonly record struct BinParam(int Tag, int? Number, string? Text)
{
    public override string ToString() => Text != null ? $"\"{Text}\"" : Number?.ToString() ?? $"tag{Tag}";
}

/// <summary>
/// One compiled symbol (record) from a .bin. Type is the logic engine's primitive number, which is
/// not the same as the SIMPL symbol code (SmC): the compiler lowers SIMPL symbols into primitives
/// and flattens user modules. Inputs and outputs are signal indices; 0 and 1 are the constant
/// signals "0" and "1".
/// </summary>
public sealed class BinSymbol
{
    public int Offset { get; init; }
    public int Type { get; init; }
    /// <summary>Hierarchical position, e.g. "S-2.1.6.1:S-5" (module path : symbol in module) or "IP-ID-03".</summary>
    public string Path { get; internal set; } = string.Empty;
    public List<(int Tag, byte[] Value)> Fields { get; } = new();
    public List<BinParam> Params { get; } = new();
    public int[] Inputs { get; init; } = Array.Empty<int>();
    public int[] Outputs { get; init; } = Array.Empty<int>();

    /// <summary>Negative types are hardware: slots, cards, IP devices, COM ports.</summary>
    public bool IsDevice => Type < 0;

    /// <summary>For IP devices: the IP ID (field 6 on records named IP-ID-xx).</summary>
    public int? IpId => Path.StartsWith("IP-ID-", StringComparison.Ordinal) ? FieldInt(6) : null;

    public int? FieldInt(int tag)
    {
        foreach (var (t, v) in Fields)
            if (t == tag && v.Length >= 4) return BitConverter.ToInt32(v, 0);
        return null;
    }

    public override string ToString() => $"{Path} type {Type} in {Inputs.Length} out {Outputs.Length}";
}

/// <summary>
/// Decoder for the logic file inside a .lpz (the .bin named in boot.bt).
///
/// Layout, little-endian, as observed across 101 programs from 3-Series and 4-Series targets:
///   record  := u32 size, i32 type, u32 inputsOffset, u32 outputsOffset, fields, outputs, inputs
///   fields  := { u16 n, u16 tag, (n-2) bytes }   from byte 16 up to outputsOffset
///   outputs := u32 signal index × ((inputsOffset - outputsOffset) / 4)
///   inputs  := u32 signal index × ((size - inputsOffset) / 4)
/// Field tags seen: 4 = path (UTF-16), 2 = numeric parameter, 3 = string parameter (UTF-16),
/// 15 = parameter count, 6 = slot / IP ID on hardware records.
/// The first record (type 31, "LeadingREM") holds the compile header as a string parameter.
/// </summary>
public sealed class BinProgram
{
    public List<BinSymbol> Symbols { get; } = new();

    public string Header =>
        Symbols.FirstOrDefault(s => s.Type == 31)?.Params.FirstOrDefault(p => p.Text != null).Text ?? string.Empty;

    public int MaxSignal => Symbols.Count == 0 ? 0 : Symbols.Max(s => Math.Max(s.Inputs.DefaultIfEmpty(0).Max(), s.Outputs.DefaultIfEmpty(0).Max()));

    public static BinProgram Parse(byte[] data)
    {
        var prog = new BinProgram();
        var off = 0;
        while (off + 16 <= data.Length)
        {
            var size = BitConverter.ToInt32(data, off);
            var type = BitConverter.ToInt32(data, off + 4);
            var insAt = BitConverter.ToInt32(data, off + 8);
            var outsAt = BitConverter.ToInt32(data, off + 12);
            if (size < 16 || off + size > data.Length || outsAt < 16 || outsAt > insAt || insAt > size)
                throw new InvalidDataException($"Bad record at 0x{off:X}: size={size} outs@{outsAt} ins@{insAt}");

            var sym = new BinSymbol
            {
                Offset = off,
                Type = type,
                Outputs = ReadInts(data, off + outsAt, (insAt - outsAt) / 4),
                Inputs = ReadInts(data, off + insAt, (size - insAt) / 4)
            };
            var p = off + 16;
            while (p + 4 <= off + outsAt)
            {
                int n = BitConverter.ToUInt16(data, p), tag = BitConverter.ToUInt16(data, p + 2);
                if (n < 2 || p + 2 + n > off + outsAt) break;
                var value = data.AsSpan(p + 4, n - 2).ToArray();
                sym.Fields.Add((tag, value));
                switch (tag)
                {
                    case 4: sym.Path = Encoding.Unicode.GetString(value); break;
                    case 2 when value.Length == 4: sym.Params.Add(new BinParam(2, BitConverter.ToInt32(value), null)); break;
                    case 3: sym.Params.Add(new BinParam(3, null, Encoding.Unicode.GetString(value))); break;
                }
                p += 2 + n;
            }
            prog.Symbols.Add(sym);
            off += size;
        }
        if (off != data.Length) throw new InvalidDataException($"Trailing {data.Length - off} bytes after last record.");
        return prog;
    }

    private static int[] ReadInts(byte[] data, int at, int count)
    {
        var a = new int[count];
        for (int i = 0; i < count; i++) a[i] = BitConverter.ToInt32(data, at + i * 4);
        return a;
    }
}
