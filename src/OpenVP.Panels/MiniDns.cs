using System.Net;
using System.Text;

namespace OpenVP.Panels;

/// <summary>One DNS resource record, with the parts mDNS needs decoded.</summary>
public sealed class DnsRecord
{
    public string Name { get; set; } = "";
    public ushort Type { get; set; }
    public ushort Class { get; set; } = 1;
    public uint Ttl { get; set; } = 120;
    /// <summary>For PTR and SRV: the target name.</summary>
    public string? Target { get; set; }
    public ushort Port { get; set; }
    public ushort Priority { get; set; }
    public ushort Weight { get; set; }
    public List<string> Text { get; set; } = new();
    public IPAddress? Address { get; set; }
    /// <summary>The cache-flush bit mDNS puts in the class.</summary>
    public bool CacheFlush { get; set; }

    public const ushort A = 1, Ptr = 12, Txt = 16, Aaaa = 28, Srv = 33, Any = 255;

    public static DnsRecord PtrRecord(string name, string target, uint ttl = 4500) => new() { Name = name, Type = Ptr, Target = target, Ttl = ttl };
    public static DnsRecord SrvRecord(string name, string target, int port, uint ttl = 120) => new() { Name = name, Type = Srv, Target = target, Port = (ushort)port, Ttl = ttl, CacheFlush = true };
    public static DnsRecord TxtRecord(string name, IEnumerable<string> text, uint ttl = 4500) => new() { Name = name, Type = Txt, Text = text.ToList(), Ttl = ttl, CacheFlush = true };
    public static DnsRecord ARecord(string name, IPAddress ip, uint ttl = 120) => new() { Name = name, Type = A, Address = ip, Ttl = ttl, CacheFlush = true };
}

public sealed record DnsQuestion(string Name, ushort Type, bool UnicastResponse);

public sealed class DnsPacket
{
    public ushort Id { get; set; }
    public bool IsResponse { get; set; }
    public List<DnsQuestion> Questions { get; } = new();
    public List<DnsRecord> Records { get; } = new(); // answers, authority and additional together

    // ---------------------------------------------------------------- build

    public byte[] ToBytes()
    {
        var w = new Writer();
        w.U16(Id);
        w.U16((ushort)(IsResponse ? 0x8400 : 0)); // response + authoritative
        w.U16((ushort)Questions.Count);
        w.U16((ushort)Records.Count);
        w.U16(0); w.U16(0);
        foreach (var q in Questions)
        {
            w.Name(q.Name);
            w.U16(q.Type);
            w.U16((ushort)(1 | (q.UnicastResponse ? 0x8000 : 0)));
        }
        foreach (var r in Records)
        {
            w.Name(r.Name);
            w.U16(r.Type);
            w.U16((ushort)(r.Class | (r.CacheFlush ? 0x8000 : 0)));
            w.U32(r.Ttl);
            var lenAt = w.Position;
            w.U16(0);
            var start = w.Position;
            switch (r.Type)
            {
                case DnsRecord.A when r.Address != null: w.Bytes(r.Address.GetAddressBytes()); break;
                case DnsRecord.Aaaa when r.Address != null: w.Bytes(r.Address.GetAddressBytes()); break;
                case DnsRecord.Ptr: w.Name(r.Target ?? ""); break;
                case DnsRecord.Srv: w.U16(r.Priority); w.U16(r.Weight); w.U16(r.Port); w.Name(r.Target ?? ""); break;
                case DnsRecord.Txt:
                    if (r.Text.Count == 0) w.Byte(0);
                    foreach (var t in r.Text) { var b = Encoding.UTF8.GetBytes(t); var n = Math.Min(b.Length, 255); w.Byte((byte)n); w.Bytes(b.AsSpan(0, n)); }
                    break;
            }
            w.SetU16(lenAt, (ushort)(w.Position - start));
        }
        return w.ToArray();
    }

    public static byte[] Query(IEnumerable<string> names, ushort type = DnsRecord.Ptr, bool unicastResponse = false)
    {
        var p = new DnsPacket();
        foreach (var n in names) p.Questions.Add(new DnsQuestion(n, type, unicastResponse));
        return p.ToBytes();
    }

    // ---------------------------------------------------------------- parse

    /// <summary>Parse a packet. Returns null if it is damaged. Record types it doesn't know are skipped.</summary>
    public static DnsPacket? Parse(byte[] data)
    {
        try { return ParseCore(data); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or FormatException or InvalidOperationException) { return null; }
    }

    private static DnsPacket? ParseCore(byte[] d)
    {
        if (d.Length < 12) return null;
        var p = new DnsPacket { Id = (ushort)(d[0] << 8 | d[1]), IsResponse = (d[2] & 0x80) != 0 };
        int qd = d[4] << 8 | d[5], an = d[6] << 8 | d[7], ns = d[8] << 8 | d[9], ar = d[10] << 8 | d[11];
        if (qd > 64 || an + ns + ar > 512) return null;
        var pos = 12;
        for (var i = 0; i < qd; i++)
        {
            var name = ReadName(d, ref pos);
            var type = (ushort)(d[pos] << 8 | d[pos + 1]);
            var cls = d[pos + 2] << 8 | d[pos + 3];
            pos += 4;
            p.Questions.Add(new DnsQuestion(name, type, (cls & 0x8000) != 0));
        }
        for (var i = 0; i < an + ns + ar; i++)
        {
            var name = ReadName(d, ref pos);
            var type = (ushort)(d[pos] << 8 | d[pos + 1]);
            var cls = d[pos + 2] << 8 | d[pos + 3];
            var ttl = (uint)(d[pos + 4] << 24 | d[pos + 5] << 16 | d[pos + 6] << 8 | d[pos + 7]);
            int len = d[pos + 8] << 8 | d[pos + 9];
            pos += 10;
            var end = pos + len;
            if (end > d.Length) return p;
            var r = new DnsRecord { Name = name, Type = type, Class = (ushort)(cls & 0x7FFF), CacheFlush = (cls & 0x8000) != 0, Ttl = ttl };
            switch (type)
            {
                case DnsRecord.A when len == 4: r.Address = new IPAddress(d.AsSpan(pos, 4)); break;
                case DnsRecord.Aaaa when len == 16: r.Address = new IPAddress(d.AsSpan(pos, 16)); break;
                case DnsRecord.Ptr: { var q = pos; r.Target = ReadName(d, ref q); break; }
                case DnsRecord.Srv:
                {
                    r.Priority = (ushort)(d[pos] << 8 | d[pos + 1]);
                    r.Weight = (ushort)(d[pos + 2] << 8 | d[pos + 3]);
                    r.Port = (ushort)(d[pos + 4] << 8 | d[pos + 5]);
                    var q = pos + 6; r.Target = ReadName(d, ref q);
                    break;
                }
                case DnsRecord.Txt:
                {
                    var q = pos;
                    while (q < end) { int n = d[q++]; if (n == 0) continue; r.Text.Add(Encoding.UTF8.GetString(d, q, Math.Min(n, end - q))); q += n; }
                    break;
                }
            }
            p.Records.Add(r);
            pos = end;
        }
        return p;
    }

    private static string ReadName(byte[] d, ref int pos)
    {
        var sb = new StringBuilder();
        var p = pos; var jumped = false; var guard = 0;
        while (true)
        {
            if (++guard > 128) throw new FormatException("name loops");
            int len = d[p];
            if (len == 0) { p++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                var ptr = (len & 0x3F) << 8 | d[p + 1];
                if (!jumped) pos = p + 2;
                jumped = true;
                p = ptr;
                continue;
            }
            if (len > 63) throw new FormatException("label too long");
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.UTF8.GetString(d, p + 1, len));
            p += 1 + len;
        }
        if (!jumped) pos = p;
        return sb.ToString();
    }

    private sealed class Writer
    {
        private readonly List<byte> _b = new();
        public int Position => _b.Count;
        public void Byte(byte v) => _b.Add(v);
        public void Bytes(ReadOnlySpan<byte> v) { foreach (var x in v) _b.Add(x); }
        public void U16(ushort v) { _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
        public void U32(uint v) { _b.Add((byte)(v >> 24)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
        public void SetU16(int at, ushort v) { _b[at] = (byte)(v >> 8); _b[at + 1] = (byte)v; }
        public void Name(string name)
        {
            foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var b = Encoding.UTF8.GetBytes(label);
                var n = Math.Min(b.Length, 63);
                _b.Add((byte)n);
                for (var i = 0; i < n; i++) _b.Add(b[i]);
            }
            _b.Add(0);
        }
        public byte[] ToArray() => _b.ToArray();
    }
}
