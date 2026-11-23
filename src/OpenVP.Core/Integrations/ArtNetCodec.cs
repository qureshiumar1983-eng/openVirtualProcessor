namespace OpenVP.Core.Integrations;

/// <summary>An ArtDMX packet: 15-bit port address (net, sub-net, universe) and up to 512 channel values.</summary>
public sealed record ArtDmx(byte Sequence, byte Physical, int Universe, byte[] Data);

/// <summary>
/// ArtDMX packets, from the public Art-Net 4 specification (art-net.org.uk): the ID "Art-Net" and a zero, OpCode 0x5000 sent low
/// byte first, protocol version 14 (high byte first), sequence, physical port, the universe's low byte (SubUni) and high
/// 7 bits (Net), then the data length (high byte first, even, 2 to 512) and the data. Port 6454.
/// </summary>
public static class ArtNetCodec
{
    public const int Port = 6454;
    public const int HeaderLength = 18;
    private static readonly byte[] Id = "Art-Net\0"u8.ToArray();

    /// <summary>Builds an ArtDMX packet. Data shorter than 2 is padded and an odd length gets a zero, as the length must be even.</summary>
    public static byte[] EncodeDmx(int universe, byte sequence, ReadOnlySpan<byte> data, byte physical = 0)
    {
        if (universe is < 0 or > 32767) throw new ArgumentOutOfRangeException(nameof(universe), "An Art-Net universe is 0 to 32767.");
        if (data.Length > 512) throw new ArgumentOutOfRangeException(nameof(data), "DMX has at most 512 channels.");
        var len = Math.Max(2, (data.Length + 1) & ~1);
        var p = new byte[HeaderLength + len];
        Id.CopyTo(p, 0);
        p[8] = 0x00; p[9] = 0x50;       // OpOutput / OpDmx = 0x5000, low byte first
        p[10] = 0; p[11] = 14;          // protocol version 14, high byte first
        p[12] = sequence;
        p[13] = physical;
        p[14] = (byte)(universe & 0xFF);        // SubUni
        p[15] = (byte)((universe >> 8) & 0x7F); // Net
        p[16] = (byte)(len >> 8); p[17] = (byte)(len & 0xFF);
        data.CopyTo(p.AsSpan(HeaderLength));
        return p;
    }

    /// <summary>Reads an ArtDMX packet; false for anything else (other Art-Net opcodes, short or damaged packets).</summary>
    public static bool TryDecodeDmx(ReadOnlySpan<byte> p, out ArtDmx? dmx)
    {
        dmx = null;
        if (p.Length < HeaderLength + 2 || !p[..8].SequenceEqual(Id)) return false;
        if (p[8] != 0x00 || p[9] != 0x50) return false;
        if (p[10] != 0 || p[11] < 14) return false;
        var len = (p[16] << 8) | p[17];
        if (len < 2 || len > 512 || len > p.Length - HeaderLength) return false;
        dmx = new ArtDmx(p[12], p[13], p[14] | ((p[15] & 0x7F) << 8), p.Slice(HeaderLength, len).ToArray());
        return true;
    }

    /// <summary>0-65535 to a DMX byte, rounding (65535 gives 255).</summary>
    public static byte ToByte(int v) => (byte)((Math.Clamp(v, 0, 65535) * 255 + 32767) / 65535);

    /// <summary>A DMX byte to 0-65535 (255 gives 65535).</summary>
    public static ushort FromByte(byte b) => (ushort)(b * 257);
}
