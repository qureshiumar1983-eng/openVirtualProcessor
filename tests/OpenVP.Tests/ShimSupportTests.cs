using System.Reflection;
using Crestron.SimplSharp.CrestronSockets;
using OpenVP.Core;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

/// <summary>The compatibility scanner's member check (overloads by argument count) and the unsupported log's pruning of SIMPL# rows.</summary>
public static class ShimSupportTests
{
    private static Assembly Shim => typeof(TCPServer).Assembly;

    [Test]
    public static void Members_are_matched_by_name_and_argument_count()
    {
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), ".ctor", 5), "the five-argument constructor");
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), ".ctor", 4), "the four-argument constructor");
        Assert.True(!CompatScanner.IsMemberSupported(typeof(TCPServer), ".ctor", 9), "no nine-argument constructor");
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), "SendData", 3), "SendData(index, data, length)");
        Assert.True(!CompatScanner.IsMemberSupported(typeof(TCPServer), "SendData", 9), "a missing overload is reported even though the name exists");
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), "get_NumberOfClientsConnected", 0), "a property getter");
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), "ToString", 0), "a method of object");
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), "Dispose", 0), "an interface method");
        Assert.True(!CompatScanner.IsMemberSupported(typeof(TCPServer), "NoSuchThing", 0), "an unknown name");
        Assert.True(CompatScanner.IsMemberSupported(typeof(TCPServer), "SendData", null), "with no count known, the name is enough");
    }

    [Test]
    public static void Rows_for_sharp_types_and_members_that_exist_now_count_as_supported()
    {
        Assert.True(ShimSupport.IsSupported(Shim, "SIMPL# type", "Crestron.SimplSharp.CrestronSockets.TCPServer"), "a type the shim has");
        Assert.True(!ShimSupport.IsSupported(Shim, "SIMPL# type", "Crestron.SimplSharp.Nope.Nothing"), "a type it hasn't");
        Assert.True(ShimSupport.IsSupported(Shim, "SIMPL# member", "Crestron.SimplSharp.CrestronSockets.TCPServer.WaitForConnectionsAlways"), "a member");
        Assert.True(ShimSupport.IsSupported(Shim, "SIMPL# member", "Crestron.SimplSharp.CrestronSockets.TCPServer..ctor(5 arguments)"), "a constructor with that many arguments");
        Assert.True(!ShimSupport.IsSupported(Shim, "SIMPL# member", "Crestron.SimplSharp.CrestronSockets.TCPServer.SendData(9 arguments)"), "an overload it hasn't");
        Assert.True(!ShimSupport.IsSupported(Shim, "SIMPL# member", "Crestron.SimplSharp.CrestronSockets.TCPServer.NoSuchThing"), "a member it hasn't");
        Assert.True(!ShimSupport.IsSupported(Shim, "SIMPL symbol", "Crestron.SimplSharp.CrestronSockets.TCPServer"), "other kinds are not its business");
        Assert.True(!ShimSupport.IsSupported(null, "SIMPL# type", "Crestron.SimplSharp.CrestronSockets.TCPServer"), "no shim, nothing is supported");
    }

    [Test]
    public static void The_log_drops_sharp_rows_once_the_shim_supports_them()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("openvp-unsupported").FullName, "unsupported.md");
        File.WriteAllText(path, string.Join('\n',
            "| Kind | Symbol / API | Details | Programs | First seen | Last seen |",
            "|---|---|---|---:|---|---|",
            "| SIMPL# type | Crestron.SimplSharp.CrestronSockets.TCPServer |  | 3 | 2026-11-01 | 2026-11-02 |",
            "| SIMPL# type | Crestron.SimplSharp.Imaginary.Widget |  | 2 | 2026-11-01 | 2026-11-02 |",
            "| SIMPL# member | Crestron.SimplSharp.CrestronSockets.TCPServer.WaitForConnectionsAlways |  | 1 | 2026-11-01 | 2026-11-02 |",
            "| SIMPL# member | Crestron.SimplSharp.CrestronSockets.TCPServer.SendData(9 arguments) |  | 1 | 2026-11-01 | 2026-11-02 |",
            "| SIMPL symbol | Something (SmC 123456) |  | 1 | 2026-11-01 | 2026-11-02 |", ""));
        using var log = new UnsupportedLog(path);
        log.SetSupportCheck((k, n) => ShimSupport.IsSupported(Shim, k, n));
        var left = log.Items.Select(i => i.Name).OrderBy(x => x).ToList();
        Assert.True(left.SequenceEqual(new[] { "Crestron.SimplSharp.CrestronSockets.TCPServer.SendData(9 arguments)", "Crestron.SimplSharp.Imaginary.Widget", "Something (SmC 123456)" }.OrderBy(x => x)), "only what is still missing stays: " + string.Join("; ", left));
        log.Report("SIMPL# type", "Crestron.SimplSharp.CrestronSockets.UDPServer", null, "P");
        Assert.True(log.Items.All(i => i.Name != "Crestron.SimplSharp.CrestronSockets.UDPServer"), "and a supported type is not reported again");
    }
}
