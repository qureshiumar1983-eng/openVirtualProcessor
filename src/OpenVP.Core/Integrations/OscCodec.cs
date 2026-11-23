using System.Buffers.Binary;
using System.Text;

namespace OpenVP.Core.Integrations;

/// <summary>One OSC message. Arguments are <c>int</c> (i), <c>long</c> (h), <c>float</c> (f), <c>double</c> (d), <c>string</c> (s), <c>bool</c> (T/F), <c>byte[]</c> (b) or null (N).</summary>
public sealed record OscMessage(string Address, IReadOnlyList<object?> Args);

/// <summary>
/// Open Sound Control 1.0 packets, written from the public specification (opensoundcontrol.stanford.edu/spec-1_0.html):
/// big-endian numbers, strings and blobs padded with zero bytes to a multiple of four, a type tag string that starts with a comma.
/// Reads messages and bundles; anything malformed is skipped, never thrown.
/// </summary>
public static class OscCodec
{
    public static byte[] Encode(OscMessage m)
    {
        var ms = new MemoryStream();
        WriteString(ms, m.Address);
        var tags = new StringBuilder(",");
        foreach (var a in m.Args)
            tags.Append(a switch { int => 'i', long => 'h', float => 'f', double => 'd', string => 's', bool b => b ? 'T' : 'F', byte[] => 'b', null => 'N', _ => throw new ArgumentException($"OSC can't carry a {a.GetType().Name}.") });
        WriteString(ms, tags.ToString());
        Span<byte> buf = stackalloc byte[8];
        foreach (var a in m.Args)
        {
            switch (a)
            {
                case int i: BinaryPrimitives.WriteInt32BigEndian(buf, i); ms.Write(buf[..4]); break;
                case long l: BinaryPrimitives.WriteInt64BigEndian(buf, l); ms.Write(buf); break;
                case float f: BinaryPrimitives.WriteInt32BigEndian(buf, BitConverter.SingleToInt32Bits(f)); ms.Write(buf[..4]); break;
                case double d: BinaryPrimitives.WriteInt64BigEndian(buf, BitConverter.DoubleToInt64Bits(d)); ms.Write(buf); break;
                case string s: WriteString(ms, s); break;
                case byte[] blob:
                    BinaryPrimitives.WriteInt32BigEndian(buf, blob.Length); ms.Write(buf[..4]);
                    ms.Write(blob); Pad(ms, blob.Length);
                    break;
            }
        }
        return ms.ToArray();
    }

    private static void WriteString(MemoryStream ms, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        ms.Write(b);
        ms.WriteByte(0);
        Pad(ms, b.Length + 1);
    }

    private static void Pad(MemoryStream ms, int written) { for (var i = written; i % 4 != 0; i++) ms.WriteByte(0); }

    /// <summary>Reads a datagram: one message, or every message of a bundle (nested bundles too).</summary>
    public static List<OscMessage> DecodePacket(ReadOnlySpan<byte> data)
    {
        var list = new List<OscMessage>();
        Packet(data, list, 0);
        return list;
    }

    private static void Packet(ReadOnlySpan<byte> data, List<OscMessage> into, int depth)
    {
        if (data.Length < 4 || depth > 8) return;
        if (data[0] == '#')
        {
            // "#bundle\0", an 8-byte time tag, then elements each led by a 4-byte size.
            if (data.Length < 16 || !data[..8].SequenceEqual("#bundle\0"u8)) return;
            var p = 16;
            while (p + 4 <= data.Length)
            {
                var size = BinaryPrimitives.ReadInt32BigEndian(data[p..]);
                p += 4;
                if (size < 0 || size > data.Length - p) return;
                Packet(data.Slice(p, size), into, depth + 1);
                p += size;
            }
            return;
        }
        if (TryDecodeMessage(data, out var m)) into.Add(m!);
    }

    /// <summary>Reads one message. Returns false for anything that isn't a well-formed message.</summary>
    public static bool TryDecodeMessage(ReadOnlySpan<byte> data, out OscMessage? message)
    {
        message = null;
        var p = 0;
        if (!ReadString(data, ref p, out var address) || address.Length == 0 || address[0] != '/') return false;
        var args = new List<object?>();
        if (p < data.Length)
        {
            if (!ReadString(data, ref p, out var tags)) return false;
            if (tags.Length == 0 || tags[0] != ',') return false;
            foreach (var t in tags.AsSpan(1))
            {
                switch (t)
                {
                    case 'i': if (!Have(data, p, 4)) return false; args.Add(BinaryPrimitives.ReadInt32BigEndian(data[p..])); p += 4; break;
                    case 'f': if (!Have(data, p, 4)) return false; args.Add(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data[p..]))); p += 4; break;
                    case 'h': if (!Have(data, p, 8)) return false; args.Add(BinaryPrimitives.ReadInt64BigEndian(data[p..])); p += 8; break;
                    case 'd': if (!Have(data, p, 8)) return false; args.Add(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data[p..]))); p += 8; break;
                    case 't': if (!Have(data, p, 8)) return false; args.Add(BinaryPrimitives.ReadInt64BigEndian(data[p..])); p += 8; break;
                    case 'c': if (!Have(data, p, 4)) return false; args.Add((int)BinaryPrimitives.ReadInt32BigEndian(data[p..])); p += 4; break;
                    case 'r' or 'm': if (!Have(data, p, 4)) return false; args.Add(BinaryPrimitives.ReadInt32BigEndian(data[p..])); p += 4; break;
                    case 's' or 'S': if (!ReadString(data, ref p, out var s)) return false; args.Add(s); break;
                    case 'b':
                        if (!Have(data, p, 4)) return false;
                        var n = BinaryPrimitives.ReadInt32BigEndian(data[p..]); p += 4;
                        if (n < 0 || !Have(data, p, n)) return false;
                        args.Add(data.Slice(p, n).ToArray()); p += (n + 3) & ~3;
                        break;
                    case 'T': args.Add(true); break;
                    case 'F': args.Add(false); break;
                    case 'N' or 'I': args.Add(null); break;
                    default: return false; // a type we can't size, so the rest can't be read
                }
            }
        }
        message = new OscMessage(address, args);
        return true;
    }

    private static bool Have(ReadOnlySpan<byte> d, int p, int n) => n >= 0 && p >= 0 && p <= d.Length && d.Length - p >= n;

    private static bool ReadString(ReadOnlySpan<byte> d, ref int p, out string s)
    {
        s = "";
        if (p >= d.Length) return false;
        var end = d[p..].IndexOf((byte)0);
        if (end < 0) return false;
        s = Encoding.UTF8.GetString(d.Slice(p, end));
        var next = p + ((end + 4) & ~3);
        if (next > d.Length) return false;
        p = next;
        return true;
    }
}
