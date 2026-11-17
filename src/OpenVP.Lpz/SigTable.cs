using System.Text;

namespace OpenVP.Lpz;

public enum SigFileKind { Digital = 0, Analog = 1, Serial = 2, Constant = 128 }

public sealed record SigEntry(int Index, string Name, SigFileKind Kind, byte Flags);

/// <summary>
/// Signal names from the .sig file SIMPL Windows writes next to the .smw at compile time (it is not
/// inside the .lpz). Optional: without it the debugger shows signal numbers instead of names.
///
/// Layout: ASCII header "[LOGOSSIG001.000]", then records of
///   u16 recordLength, UTF-16 name (recordLength - 8 bytes), u32 signal index, u8 kind, u8 flags.
/// Names of signals inside user modules end with ":S-path.XXXXXXXX" (the index in hex).
/// </summary>
public sealed class SigTable
{
    public Dictionary<int, SigEntry> Entries { get; } = new();

    public string? NameOf(int index) => Entries.TryGetValue(index, out var e) ? e.Name : null;

    public static SigTable Parse(byte[] data)
    {
        var table = new SigTable();
        var p = Array.IndexOf(data, (byte)']') + 1;
        if (p <= 0) throw new InvalidDataException("Not a .sig file (no header).");
        while (p + 8 <= data.Length)
        {
            int len = BitConverter.ToUInt16(data, p);
            if (len < 8 || p + len > data.Length) break;
            var name = Encoding.Unicode.GetString(data, p + 2, len - 8);
            var index = BitConverter.ToInt32(data, p + len - 6);
            table.Entries[index] = new SigEntry(index, name, (SigFileKind)data[p + len - 2], data[p + len - 1]);
            p += len;
        }
        return table;
    }
}
