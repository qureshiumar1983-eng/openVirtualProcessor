using System.Globalization;
using System.Text;
using OpenVP.Core;

namespace OpenVP.Lpz;

public enum PortKind { Ir, Com }

/// <summary>
/// Where an IR or COM port record sits. Records point at their parent by index (field 7), so a COM port
/// reads IP-ID-11 → Slot-08 → COM-01 and an IR port reads (processor) Slot-06 → Port-01.
/// </summary>
/// <param name="Path">Qualified path, e.g. "IP-ID-11/Slot-08/COM-01". Names several ports apart where the record's own path ("COM-01") doesn't.</param>
/// <param name="IpIdText">The device's IP ID as the program shows it (hex, "11"), or null for the control processor's own ports.</param>
/// <param name="Slot">The slot on the device (or processor) that holds the port; 0 if unknown.</param>
/// <param name="Number">1-based port number.</param>
public sealed record PortInfo(PortKind Kind, string Path, string? IpIdText, int Slot, int Number)
{
    public string Owner => IpIdText != null ? $"IPID {IpIdText}" : Slot > 0 ? $"processor slot {Slot}" : "processor";
    public string Label => $"{(Kind == PortKind.Ir ? "IR" : "COM")} {Owner} port {Number}";

    internal static PortInfo From(PortKind kind, BinSymbol sym, IReadOnlyList<BinSymbol> all)
    {
        var parts = new List<string> { sym.Path };
        string? ip = null;
        int slot = 0;
        var cur = sym;
        for (int guard = 0; guard < 16; guard++)
        {
            var pi = cur.FieldInt(7);
            if (pi is not int idx || idx <= 0 || idx >= all.Count) break;
            cur = all[idx];
            parts.Add(cur.Path);
            if (slot == 0 && cur.Path.StartsWith("Slot-", StringComparison.Ordinal)) slot = cur.FieldInt(6) ?? 0;
            if (cur.Path.StartsWith("IP-ID-", StringComparison.Ordinal)) { ip = cur.Path[6..].Split('.')[0]; break; }
        }
        parts.Reverse();
        return new PortInfo(kind, string.Join('/', parts), ip, slot, (sym.FieldInt(6) ?? 0) + 1);
    }

    /// <summary>Text with control characters as \xNN, so a CR-terminated command is readable in the log.</summary>
    public static string Printable(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else if (c < 0x20 || c > 0x7E) sb.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// The seam between a program's IR and COM ports and whatever can carry them. A virtual processor has no
/// physical ports, so by default nothing does: the runtime logs every send and the bridge is a no-op.
/// A backend that can reach real hardware (for example a DM-NVX endpoint over the network) implements this
/// and is installed with <see cref="LpzRuntime.PortBridgeFactory"/> or <see cref="LpzRuntime.PortBridge"/>.
/// Calls arrive on the slot's thread; keep them quick. Data coming back goes through the callback passed
/// to <see cref="Open"/>, which may be called from any thread.
/// </summary>
public interface IPortBridge
{
    /// <summary>A COM port started. <paramref name="received"/> delivers text the device sent to the program.</summary>
    void Open(PortInfo port, Action<string> received);
    /// <summary>The program stopped.</summary>
    void Close(PortInfo port);
    /// <summary>The program sent text out of a COM port.</summary>
    void SendCom(PortInfo port, string data);
    /// <summary>The program fired an IR command. <paramref name="command"/> is the driver's command index as compiled;
    /// <paramref name="name"/> is the name of the signal that triggered it.</summary>
    void SendIr(PortInfo port, int command, string name);
}

/// <summary>The default: sends go nowhere (the runtime has already logged them).</summary>
public sealed class NullPortBridge : IPortBridge
{
    public static readonly NullPortBridge Instance = new();
    public void Open(PortInfo port, Action<string> received) { }
    public void Close(PortInfo port) { }
    public void SendCom(PortInfo port, string data) { }
    public void SendIr(PortInfo port, int command, string name) { }
}

/// <summary>
/// The bridge every runtime gets by default: the program's COM ports are listed in the slot's <see cref="PortHub"/>, so the
/// COM terminal can show their traffic and send text to the program, and a device simulator can answer them. If another bridge
/// was installed (<see cref="LpzRuntime.PortBridgeFactory"/>), it is called too.
/// </summary>
public sealed class SlotPortBridge : IPortBridge
{
    private readonly ProgramSlot _slot;
    private readonly IPortBridge? _inner;

    public SlotPortBridge(ProgramSlot slot, IPortBridge? inner = null) { _slot = slot; _inner = inner; }

    public void Open(PortInfo port, Action<string> received)
    {
        if (port.Kind == PortKind.Com) _slot.Io.Ports.Open(port.Path, port.Label, received);
        _inner?.Open(port, received);
    }

    public void Close(PortInfo port)
    {
        if (port.Kind == PortKind.Com) _slot.Io.Ports.Close(port.Path);
        _inner?.Close(port);
    }

    public void SendCom(PortInfo port, string data)
    {
        _slot.Io.Ports.Find(port.Path)?.FromProgram(data);
        _inner?.SendCom(port, data);
    }

    public void SendIr(PortInfo port, int command, string name) => _inner?.SendIr(port, command, name);
}
