using System.IO.Compression;
using System.Text;
using OpenVP.Lpz;

namespace OpenVP.Tests;

/// <summary>
/// Builds small synthetic .bin/.sig/.lpz files in the documented layout. Real customer programs are
/// never committed; check the decoder against your own with: Inspect .lpz in the web console.
/// </summary>
public static class LpzTests
{
    internal static byte[] Field(int tag, byte[] value)
    {
        var f = new byte[4 + value.Length];
        BitConverter.GetBytes((ushort)(value.Length + 2)).CopyTo(f, 0);
        BitConverter.GetBytes((ushort)tag).CopyTo(f, 2);
        value.CopyTo(f, 4);
        return f;
    }

    /// <summary>One record in the documented layout: header, fields, outputs, inputs.</summary>
    internal static byte[] Record(int type, string path, int[] ins, int[] outs, params byte[][] extraFields)
    {
        var fields = new List<byte>(Field(4, Encoding.Unicode.GetBytes(path)));
        foreach (var f in extraFields) fields.AddRange(f);
        var outsAt = 16 + fields.Count;
        var insAt = outsAt + outs.Length * 4;
        var size = insAt + ins.Length * 4;
        var r = new List<byte>();
        r.AddRange(BitConverter.GetBytes(size));
        r.AddRange(BitConverter.GetBytes(type));
        r.AddRange(BitConverter.GetBytes(insAt));
        r.AddRange(BitConverter.GetBytes(outsAt));
        r.AddRange(fields);
        foreach (var o in outs) r.AddRange(BitConverter.GetBytes(o));
        foreach (var i in ins) r.AddRange(BitConverter.GetBytes(i));
        return r.ToArray();
    }

    internal static byte[] Int(int tag, int v) => Field(tag, BitConverter.GetBytes(v));
    internal static byte[] Str(int tag, string v) => Field(tag, Encoding.Unicode.GetBytes(v));

    private static byte[] SampleBin() => Record(31, "LeadingREM", [], [], Field(15, BitConverter.GetBytes(1)), Field(3, Encoding.Unicode.GetBytes("Program File: test.smw")))
        .Concat(Record(-12, "IP-ID-03", [0, 0], [10, 11], Field(6, BitConverter.GetBytes(3))))
        .Concat(Record(29, "S-1.4", [10], [12, 0], Field(15, BitConverter.GetBytes(1)), Field(2, BitConverter.GetBytes(10))))
        .ToArray();

    [Test]
    public static void Bin_records_decode()
    {
        var p = BinProgram.Parse(SampleBin());
        Assert.Equal(3, p.Symbols.Count, "record count");
        Assert.True(p.Header.Contains("test.smw"), "header text");
        var dev = p.Symbols[1];
        Assert.Equal(-12, dev.Type, "device type");
        Assert.Equal((int?)3, dev.IpId, "IP ID from field 6");
        Assert.Equal(11, dev.Outputs[1], "device output signal");
        var delay = p.Symbols[2];
        Assert.Equal("S-1.4", delay.Path, "path");
        Assert.Equal((int?)10, delay.Params[0].Number, "time parameter");
        Assert.Equal(12, p.MaxSignal, "max signal index");
    }

    [Test]
    public static void Bin_rejects_bad_framing()
    {
        var bad = SampleBin().Take(40).ToArray();
        try { BinProgram.Parse(bad); }
        catch (InvalidDataException) { return; }
        throw new AssertException("expected InvalidDataException");
    }

    [Test]
    public static void Sig_names_decode()
    {
        var data = new List<byte>(Encoding.ASCII.GetBytes("[LOGOSSIG001.000]"));
        void Add(string name, int index, byte kind)
        {
            var text = Encoding.Unicode.GetBytes(name);
            data.AddRange(BitConverter.GetBytes((ushort)(text.Length + 8)));
            data.AddRange(text);
            data.AddRange(BitConverter.GetBytes(index));
            data.Add(kind); data.Add(1);
        }
        Add("0", 0, 128); Add("Room.Power.On", 10, 0); Add("Room.Level", 11, 1);
        var sig = SigTable.Parse(data.ToArray());
        Assert.Equal("Room.Power.On", sig.NameOf(10), "name");
        Assert.Equal(SigFileKind.Analog, sig.Entries[11].Kind, "kind");
    }

    [Test]
    public static void Lpz_package_reads_boot_iptable_and_modules()
    {
        var path = Path.Combine(Paths.TempData(), "test.lpz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
            Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=test.bin\r\nSMWFileName=test.smw\r\nNumberSymbols=3\r\nRackType=CP4\r\n"));
            Put("test.bin", SampleBin());
            Put("test.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\nid1=1A\r\naddr1=127.0.0.1\r\n"));
            Put("My_Module.dll", new byte[10]);
            Put("SplusObjects.dll", new byte[10]);
        }
        var pkg = LpzPackage.Open(path);
        Assert.Equal("CP4", pkg.Target, "target");
        Assert.Equal("test", pkg.ProgramName, "name");
        Assert.Equal(2, pkg.IpTable.Count, "IP table");
        Assert.Equal(0x1A, pkg.IpTable[1].IpId, "hex IP ID");
        Assert.Equal(1, pkg.SplusModules.Count, "SIMPL+ modules (runtime excluded)");
        var report = LpzReport.Build(pkg);
        Assert.True(report.Contains("3 compiled symbols"), "report: " + report);
    }
}
