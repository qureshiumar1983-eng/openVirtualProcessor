using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using OpenVP.Core;

namespace OpenVP.Panels;

/// <summary>
/// EXPERIMENTAL. Accepts real Crestron touch panels and XPanels over CIP (TCP 41794).
///
/// CIP is not publicly documented. This follows the framing and messages described by community
/// projects that talk CIP to processors: [type:1][length:2, big-endian][payload]. It has not been
/// tested against real panels yet. Turn on <see cref="LogPackets"/> and compare with a Wireshark capture
/// of a panel talking to a real processor to fill in the gaps; <see cref="Cip"/> holds every encoding in
/// one place so fixes are local.
/// </summary>
public sealed class CipServer : IDisposable
{
    private readonly Processor _processor;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<CipSession> _sessions = new();

    public CipServer(Processor processor, int port = 41794, IPAddress? bind = null)
    {
        _processor = processor;
        _listener = new TcpListener(bind ?? IPAddress.Any, port);
    }

    public bool LogPackets { get; set; }
    /// <summary>
    /// Also send our own heartbeat request (0x0D) to each connected panel every <see cref="ProbeInterval"/> and time the answer (0x0E),
    /// for a live round-trip time. Off by default: it is untested against real panels, so it must be asked for (OpenVP:CipHeartbeatProbe).
    /// Without it the round trip OpenVP shows for a CIP panel is the one measured at connect (WhoAreYou to connect request).
    /// </summary>
    public bool ProbeHeartbeats { get; set; }
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Connections currently open (a diagnostic).</summary>
    public int SessionCount { get { lock (_sessions) return _sessions.Count; } }
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            client.NoDelay = true; // small feedback packets should go out now, not wait for Nagle to coalesce them
            _ = Run(new TcpCipTransport(client));
        }
    }

    /// <summary>
    /// Serve one client over a transport that is already open. The TCP listener calls this for each connection;
    /// <see cref="WebXPanelServer"/> calls it for each WebSocket, so both kinds of client are the same session.
    /// </summary>
    internal Task Run(ICipTransport transport)
    {
        var session = new CipSession(transport, _processor, this);
        lock (_sessions) _sessions.Add(session);
        return session.RunAsync(_cts.Token).ContinueWith(_ => { lock (_sessions) _sessions.Remove(session); });
    }

    internal void Log(string message)
    {
        Keep("[CIP] " + message);
        if (LogPackets) _processor.Slot(1).Log("Info", "[CIP] " + message);
    }

    // The last panel events and packets, on their own: in a slot's log they scroll away under a busy program's lines.
    private readonly Queue<string> _trace = new();
    private const int MaxTraceLines = 4000;
    /// <summary>Packets of each connection that are kept for <see cref="RecentTrace"/> even with packet logging off.</summary>
    internal const int TracedPacketsPerSession = 400;

    private void Keep(string line)
    {
        lock (_trace)
        {
            _trace.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {line}");
            while (_trace.Count > MaxTraceLines) _trace.Dequeue();
        }
    }

    /// <summary>
    /// Connections, sign-ons and packets of CIP clients (TCP and WebSocket), oldest first: every event, and the first
    /// <see cref="TracedPacketsPerSession"/> packets of each connection (all of them with <see cref="LogPackets"/> on).
    /// </summary>
    public IReadOnlyList<string> RecentTrace() { lock (_trace) return _trace.ToArray(); }

    private readonly HashSet<byte> _unknownSubs = new();

    /// <summary>A data sub-type OpenVP can't decode is logged once per sub-type, with its bytes, to help compare with captures.</summary>
    internal void LogUnknownSub(byte frameType, byte sub, byte[] raw)
    {
        if (!LogPackets) return;
        lock (_unknownSubs) if (!_unknownSubs.Add(sub)) return;
        Log($"unhandled data sub-type {sub:X2} in a {frameType:X2} frame (logged once): {Convert.ToHexString(raw)}");
    }

    /// <summary>Connection events are always logged.</summary>
    internal void Event(int slot, string message)
    {
        Keep("[Panel] " + message);
        _processor.Slot(Math.Max(1, slot)).Log("Notice", "[Panel] " + message);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}

/// <summary>CIP message encodings, all in one place.</summary>
public static class Cip
{
    public const byte ConnectRequest = 0x01;   // panel -> processor, carries the IP ID
    public const byte ConnectResponse = 0x02;  // processor -> panel
    public const byte Disconnect = 0x03;
    public const byte Data = 0x05;             // join data, both directions
    public const byte ConnectRequestAlt = 0x0A;
    public const byte HeartbeatRequest = 0x0D;
    public const byte HeartbeatResponse = 0x0E;
    public const byte WhoAreYou = 0x0F;        // processor -> panel right after TCP connect
    public const byte SerialData = 0x12;
    public const byte RoomConnectRequest = 0x26;  // panel -> processor: sign-on that also names a Room ID
    public const byte RoomConnectResponse = 0x27; // processor -> panel: its answer for CH5 clients on a WebSocket; an XPanel on TCP wants 0x02

    public const byte SubDigital = 0x00;
    public const byte SubDigitalAlt = 0x27;    // digital press as some clients send it
    public const byte SubUpdateRequest = 0x03;
    public const byte SubAnalog = 0x14;
    public const byte SubAnalogShort = 0x01;   // analog as CH5 / WebXPanel clients may send it: 1- or 2-byte join, 1- or 2-byte value
    public const byte SubSerial = 0x15;        // serial as CH5 / WebXPanel clients send it, inside a 0x05 frame: [join-1:2 BE][flags][UTF-8 text]
    public const byte SubSerialMarker = 0x34;
    public const byte SubSmartObject = 0x38;   // Smart Graphics: [object id:4 BE] then nested sub-packets (digital, analog)
    public const byte SubSmartObjectExt = 0x39; // same, inside an extended (0x12) frame, where serials travel
    public const byte SubDateTime = 0x08;

    /// <summary>Longest serial string one CIP message can carry (its length byte also counts 4 header bytes).</summary>
    public const int MaxSerialBytes = 255 - 4;

    public static byte[] Frame(byte type, params byte[] payload)
    {
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentException($"CIP payload is {payload.Length} bytes; a frame holds at most {ushort.MaxValue}.", nameof(payload));
        var f = new byte[3 + payload.Length];
        f[0] = type;
        f[1] = (byte)(payload.Length >> 8);
        f[2] = (byte)payload.Length;
        payload.CopyTo(f, 3);
        return f;
    }

    public static byte[] WhoAreYouPacket() => Frame(WhoAreYou, 0x02);

    public static byte[] ConnectOk(uint ipId) => Frame(ConnectResponse, 0x00, 0x00, 0x00, 0x1F);

    public static byte[] ConnectRefused() => Frame(ConnectResponse, 0xFF, 0xFF, 0x02);

    /// <summary>
    /// The answer a CH5 client on a WebSocket (Crestron Virtual Panel, the HTML5 Web XPanel) wants to its 0x26 sign-on.
    /// Crestron Virtual Panel's own log says so: sent the ordinary 0x02 it stops with "Event CONNECT_RESPONSE has no
    /// handler!". Layout as the community client siegeld/pycrestron describes it (its mock processor):
    ///   [handle:2] [mode] [flags] [extra flags:4] [model:30, NUL-padded]
    /// The handle is 0, as in every packet OpenVP sends, and so is everything else but the model name. Crestron Virtual
    /// Panel takes that: it logs "Licensing is not supported!" (a flag that isn't set) and stays connected. What it
    /// refuses, each seen in its log: the third byte as 03, which is what that mock sends ("Connection refused!
    /// Connection timed out.", so that byte is a result code, like the 02 in a refusal), and the classic four bytes
    /// "00 00 00 1F" sent as 0x27 ("Bad packet received!").
    /// </summary>
    public static byte[] RoomConnectOk()
    {
        var p = new byte[2 + 1 + 1 + 4 + 30];
        Encoding.ASCII.GetBytes("OpenVP").CopyTo(p, 8);
        return Frame(RoomConnectResponse, p);
    }

    public const byte AuthenticateResponse = 0x0C; // processor -> panel: [handle:2][access level], 0 = refused

    /// <summary>
    /// A CH5 client that signs on with a token (extra flag 0x10 in its 0x26, "authenticate") says nothing more after
    /// the 0x27 until it is told the token was accepted: Crestron Virtual Panel sat silent for as long as it was left.
    /// Layout from community clients (siegeld/pycrestron, Antonio112009/pycrestron-cip): any level above 0 is a yes.
    /// OpenVP checks no token, so it is always a yes.
    /// </summary>
    public static byte[] Authenticated() => Frame(AuthenticateResponse, 0x00, 0x00, 0x01);

    /// <summary>True if a 0x26 sign-on asks for its token to be checked (extra flags, a 4-byte field at offset 5; bit 0x10).</summary>
    public static bool WantsAuthentication(ReadOnlySpan<byte> roomConnectPayload) => roomConnectPayload.Length > 8 && (roomConnectPayload[8] & 0x10) != 0;

    public static byte[] RoomConnectRefused() => Frame(RoomConnectResponse, 0xFF, 0xFF, 0x02);

    public static byte[] Heartbeat() => Frame(HeartbeatResponse, 0x00, 0x00);

    /// <summary>Digital: join-1 as 15 bits little-endian; bit 15 set means LOW.</summary>
    public static byte[] Digital(uint join, bool value)
    {
        var j = join - 1;
        var hi = (byte)((byte)((j >> 8) & 0x7F) | (byte)(value ? 0x00 : 0x80));
        return Frame(Data, 0x00, 0x00, 0x03, SubDigital, (byte)(j & 0xFF), hi);
    }

    public static byte[] Analog(uint join, ushort value)
    {
        var j = join - 1;
        return Frame(Data, 0x00, 0x00, 0x05, SubAnalog, (byte)(j >> 8), (byte)j, (byte)(value >> 8), (byte)value);
    }

    /// <summary>
    /// Serial join. Text longer than <see cref="MaxSerialBytes"/> is cut to fit, because the message's
    /// length field is one byte; without this, the length would wrap and the panel would misread the frame.
    /// </summary>
    public static byte[] Serial(uint join, string text)
    {
        var j = join - 1;
        var bytes = Encoding.Latin1.GetBytes(text);
        if (bytes.Length > MaxSerialBytes) bytes = bytes[..MaxSerialBytes];
        var payload = new byte[8 + bytes.Length];
        payload[3] = (byte)(4 + bytes.Length);
        payload[4] = SubSerialMarker;
        payload[5] = (byte)(j >> 8);
        payload[6] = (byte)j;
        payload[7] = 0x03;
        bytes.CopyTo(payload, 8);
        return Frame(SerialData, payload);
    }

    // ---- Smart Graphics ----
    // A smart object's joins travel in the same data packets as the panel's own joins, wrapped in a sub-packet of
    // type 0x38 (0x05 frames: digital, analog) or 0x39 (0x12 extended frames: serial). Its body is the object ID as
    // 4 bytes big-endian, followed by the ordinary sub-packets, which now belong to that object:
    //   digital  05 [00 00] [09] 38 [id:4] [03] 00|27 [join-1 lo] [join-1 hi | 0x80 if low]
    //   analog   05 [00 00] [0B] 38 [id:4] [05] 14 [join-1:2 BE] [value:2 BE]
    //   serial   12 [00 00] [len:2] 39 [id:4] [len:2] 34 [join-1:2 BE] [flags] [text]
    // Sources (public community CIP clients, read for the wire layout; no code copied):
    //   M4rKDC14/Crestron-CIP-Implementation crestron_cip.py docstring and digital()/analog()/serial(): the layout
    //     above, 4-byte big-endian IDs, "checked live, ids above 255 worked" (best-supported; the layout used here).
    //   Antonio112009/pycrestron-cip docs/protocol.md: 0x38/0x39 "00 00 00 <object id> then nested sub-packets";
    //     a 3-Series packs several joins into one sub-packet, e.g. blocks of 32 digitals for a smart object.
    //   pdehlke/homeassistant-crestron-controller issue #3: record types 0x38/0x39, 4-byte object number, nested
    //     [len][type][bytes] records.
    // Older community clients (klenae/python-cipclient) read only the last ID byte; for IDs up to 255 that is the
    // same thing. Panels send presses as 0x27 inside 0x38; the processor sends feedback as 0x00. Not tested on
    // hardware: compare against a capture, and adjust here.

    /// <summary>Encode a join for the panel, or (fromPanel) as a panel would send it. Handles smart objects.</summary>
    public static byte[] Encode(JoinKey key, object value, bool fromPanel = false) => key.Kind switch
    {
        SigKind.Digital => key.SmartObject != 0 ? SmartDigital(key.SmartObject, key.Join, (bool)value, fromPanel) : Digital(key.Join, (bool)value),
        SigKind.Analog => key.SmartObject != 0 ? SmartAnalog(key.SmartObject, key.Join, (ushort)value) : Analog(key.Join, (ushort)value),
        _ => key.SmartObject != 0 ? SmartSerial(key.SmartObject, key.Join, (string)value) : Serial(key.Join, (string)value)
    };

    private static void PutId(byte[] b, int at, uint id)
    {
        b[at] = (byte)(id >> 24); b[at + 1] = (byte)(id >> 16); b[at + 2] = (byte)(id >> 8); b[at + 3] = (byte)id;
    }

    /// <summary>Smart object digital. <paramref name="press"/> uses the button-style sub-type panels send (0x27).</summary>
    public static byte[] SmartDigital(uint so, uint join, bool value, bool press = false)
    {
        var j = join - 1;
        var p = new byte[2 + 1 + 1 + 4 + 4];
        p[2] = 9; p[3] = SubSmartObject;
        PutId(p, 4, so);
        p[8] = 3; p[9] = press ? SubDigitalAlt : SubDigital;
        p[10] = (byte)(j & 0xFF);
        p[11] = (byte)((byte)((j >> 8) & 0x7F) | (byte)(value ? 0x00 : 0x80));
        return Frame(Data, p);
    }

    public static byte[] SmartAnalog(uint so, uint join, ushort value)
    {
        var j = join - 1;
        var p = new byte[2 + 1 + 1 + 4 + 6];
        p[2] = 11; p[3] = SubSmartObject;
        PutId(p, 4, so);
        p[8] = 5; p[9] = SubAnalog;
        p[10] = (byte)(j >> 8); p[11] = (byte)j; p[12] = (byte)(value >> 8); p[13] = (byte)value;
        return Frame(Data, p);
    }

    /// <summary>Smart object serial, in an extended frame (two-byte lengths). Cut to <see cref="MaxSerialBytes"/> like <see cref="Serial"/>.</summary>
    public static byte[] SmartSerial(uint so, uint join, string text)
    {
        var j = join - 1;
        var bytes = Encoding.Latin1.GetBytes(text);
        if (bytes.Length > MaxSerialBytes) bytes = bytes[..MaxSerialBytes];
        var inner = 4 + bytes.Length;      // type, join (2), flags, text
        var outer = 1 + 4 + 2 + inner;     // type 0x39, id, inner length, inner
        var p = new byte[2 + 2 + outer];
        p[2] = (byte)(outer >> 8); p[3] = (byte)outer;
        p[4] = SubSmartObjectExt;
        PutId(p, 5, so);
        p[9] = (byte)(inner >> 8); p[10] = (byte)inner;
        p[11] = SubSerialMarker; p[12] = (byte)(j >> 8); p[13] = (byte)j; p[14] = 0x03;
        bytes.CopyTo(p, 15);
        return Frame(SerialData, p);
    }

    /// <summary>The first join in a message from a panel, or null if it holds none. See <see cref="Decode"/>.</summary>
    public static (JoinKey key, object value)? DecodeJoin(byte type, ReadOnlySpan<byte> p)
    {
        var all = Decode(type, p);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// Decode every join in one message from a panel (a sub-packet can pack several joins, and smart-object
    /// sub-packets nest the ordinary ones). Sub-types that are neither joins nor known housekeeping are added
    /// to <paramref name="unknown"/> (sub-type and the sub-packet bytes), so the caller can log them.
    /// </summary>
    public static List<(JoinKey key, object value)> Decode(byte type, ReadOnlySpan<byte> p, List<(byte sub, byte[] raw)>? unknown = null)
    {
        var joins = new List<(JoinKey, object)>();
        if ((type == Data || type == SerialData) && p.Length > 2)
            Walk(type == SerialData, p[2..], 0, joins, unknown, 0);
        return joins;
    }

    // Sub-packets: [len][type][body], len counting the type byte; one length byte in 0x05 frames, two in 0x12.
    private static void Walk(bool wide, ReadOnlySpan<byte> d, uint so, List<(JoinKey, object)> joins, List<(byte, byte[])>? unknown, int depth)
    {
        var h = wide ? 2 : 1;
        var i = 0;
        while (i + h < d.Length)
        {
            var n = wide ? (d[i] << 8) | d[i + 1] : d[i];
            if (n == 0 || i + h + n > d.Length) { unknown?.Add((0xFF, d[i..].ToArray())); return; } // truncated: not a sub-packet
            var t = d[i + h];
            var body = d.Slice(i + h + 1, n - 1);
            var whole = d.Slice(i, h + n);
            i += h + n;
            switch (t)
            {
                case SubDigital:
                case SubDigitalAlt:
                    for (int k = 0; k + 1 < body.Length; k += 2)
                        joins.Add((new JoinKey(SigKind.Digital, (uint)(((body[k + 1] & 0x7F) << 8) | body[k]) + 1, so), SigValue.Box((body[k + 1] & 0x80) == 0)));
                    break;
                case SubAnalog:
                    for (int k = 0; k + 3 < body.Length; k += 4)
                        joins.Add((new JoinKey(SigKind.Analog, (uint)((body[k] << 8) | body[k + 1]) + 1, so), (ushort)((body[k + 2] << 8) | body[k + 3])));
                    break;
                case SubAnalogShort:
                    // Layouts as community WebXPanel clients describe them (siegeld/pycrestron protocol.py): the size says which.
                    if (body.Length == 2) joins.Add((new JoinKey(SigKind.Analog, body[0] + 1u, so), (ushort)body[1]));
                    else if (body.Length == 3) joins.Add((new JoinKey(SigKind.Analog, body[0] + 1u, so), (ushort)((body[1] << 8) | body[2])));
                    else for (int k = 0; k + 3 < body.Length; k += 4)
                        joins.Add((new JoinKey(SigKind.Analog, (uint)((body[k] << 8) | body[k + 1]) + 1, so), (ushort)((body[k + 2] << 8) | body[k + 3])));
                    break;
                case SubSerial when body.Length >= 3:
                    joins.Add((new JoinKey(SigKind.Serial, (uint)((body[0] << 8) | body[1]) + 1, so), Utf8OrLatin1(body[3..])));
                    break;
                case SubSerialMarker when body.Length >= 3:
                    // Flag 0x04 is UTF-16LE. Text split over several messages (start/end flags) isn't reassembled.
                    var raw = body[3..];
                    var text = (body[2] & 0x04) != 0 ? Encoding.Unicode.GetString(raw) : Encoding.Latin1.GetString(raw);
                    joins.Add((new JoinKey(SigKind.Serial, (uint)((body[0] << 8) | body[1]) + 1, so), text));
                    break;
                case SubSmartObject:
                case SubSmartObjectExt:
                    if (body.Length >= 4 && depth == 0)
                    {
                        var id = ((uint)body[0] << 24) | ((uint)body[1] << 16) | ((uint)body[2] << 8) | body[3];
                        Walk(wide, body[4..], id, joins, unknown, depth + 1);
                    }
                    else unknown?.Add((t, whole.ToArray()));
                    break;
                case SubUpdateRequest: // update request and end-of-update markers
                case SubDateTime:
                    break;
                default:
                    unknown?.Add((t, whole.ToArray()));
                    break;
            }
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static string Utf8OrLatin1(ReadOnlySpan<byte> raw)
    {
        try { return StrictUtf8.GetString(raw); }
        catch (ArgumentException) { return Encoding.Latin1.GetString(raw); }
    }

    /// <summary>
    /// IP ID from a connect request. Two forms, both from public community clients and servers
    /// (acaprojects/ruby-cresip, moty66/node-red-contrib-cip, zhuld/CIP-Server, rdriscoll/Crestron-CIP):
    ///   0x01, the long sign-on panels send: "00 00 00 00 00 [IPID] 40 FF FF F1 01", or the older
    ///         "7F 00 00 01 00 [IPID] 40". The IP ID is the byte just before the 0x40.
    ///   0x0A, the short sign-on XPanels, apps and other processors send: "00 [IPID] A3 42 40 02 00 00 00 00".
    ///         The IP ID is the second byte.
    /// Neither carries a Room ID; a panel that has one set sends <see cref="DecodeRoomConnect"/> instead.
    /// </summary>
    public static uint? DecodeConnectIpId(byte type, ReadOnlySpan<byte> p)
    {
        if (type == ConnectRequestAlt) return p.Length > 1 ? p[1] : null;
        if (type != ConnectRequest) return null;
        if (p.Length > 6 && p[6] == 0x40) return p[5];
        if (p.Length > 5 && p[5] == 0x40) return p[4];
        if (p.Length >= 11) return p[5];
        if (p.Length > 4) return p[4];
        return null;
    }

    /// <summary>What a panel says about itself in a <see cref="RoomConnectRequest"/>.</summary>
    public sealed record RoomConnect(uint IpId, string? RoomId, string? Make, string? Model, string? Hostname, string? Mac);

    /// <summary>
    /// The sign-on a panel sends when it has a Room ID set (type 0x26). Layout read from the packet a Crestron
    /// XPanel sent to OpenVP with IP ID 03 and Room ID SLOT01 (the text fields are plain to see in it):
    ///   [IP ID:2 BE] 40 F1 01 [flags:4, 00 00 00 01 seen] [MAC:6]
    ///   [make:50 "Crestron"] [model:50 "XPanel"] [Room ID:32] [hostname:64], each text field NUL-padded.
    /// On TCP it is answered with the ordinary connect response (<see cref="ConnectOk"/>, <see cref="ConnectRefused"/>): the
    /// XPanel carries on after "02 00 00 00 1F", and hangs up on the same result sent as type 0x27. A CH5 client on a
    /// WebSocket is the other way round and gets <see cref="RoomConnectOk"/>.
    /// Returns null if it is not that packet or is too short to hold the Room ID field.
    /// </summary>
    public static RoomConnect? DecodeRoomConnect(byte type, ReadOnlySpan<byte> p)
    {
        const int text = 15, room = text + 100, host = room + 32;
        if (type != RoomConnectRequest || p.Length < host) return null;
        var mac = p.Slice(9, 6);
        return new RoomConnect((uint)((p[0] << 8) | p[1]), Text(p.Slice(room, 32)), Text(p.Slice(text, 50)), Text(p.Slice(text + 50, 50)),
            p.Length > host ? Text(p.Slice(host, Math.Min(64, p.Length - host))) : null,
            mac.IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexString(mac).Chunk(2).Select(c => new string(c)).Aggregate((a, b) => a + ":" + b));

        static string? Text(ReadOnlySpan<byte> f)
        {
            var end = f.IndexOf((byte)0);
            var t = Encoding.UTF8.GetString(end < 0 ? f : f[..end]).Trim();
            return t.Length == 0 ? null : t;
        }
    }

    /// <summary>Sent after a full update so the panel knows the snapshot is complete.</summary>
    public static byte[] UpdateComplete() => Frame(Data, 0x00, 0x00, 0x02, 0x03, 0x16);
}

internal sealed class CipSession
{
    private readonly ICipTransport _client;
    private readonly Processor _processor;
    private readonly CipServer _server;
    // Packets wait here for the sender, which writes everything pending in one go. Bounded, so a client
    // that stopped reading is cut off instead of growing memory without limit; it reconnects and gets a snapshot.
    // The limit is MaxQueuedPackets on top of whatever a snapshot just queued, so a device with more feedback
    // joins than the limit can still be sent its snapshot: only packets beyond that count as "not reading".
    private const int MaxQueuedPackets = 20_000;
    private const int MaxWriteBytes = 64 * 1024;
    private readonly Channel<byte[]> _outbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _sendCts = new(); // cancelled when the session ends, to free a write stuck on a dead peer
    private int _queued;                     // packets in _outbox
    private int _queueLimit = MaxQueuedPackets;
    private JoinDevice? _device;
    private DeviceConnection _conn = null!;
    private DateTimeOffset? _lastHeartbeat;
    private long _whoAreYouAt;   // Stopwatch timestamp of the WhoAreYou we sent, until the first connect request
    private Task? _probe;
    private long _probeAt;       // Stopwatch timestamp of the heartbeat request in flight, 0 = none
    private int _strays;         // packets logged from this client before it registered
    private int _traced;         // packets of this connection kept for the trace (a diagnostic; races only miscount it)
    private const int MaxStraysLogged = 4;

    public CipSession(ICipTransport client, Processor processor, CipServer server)
    {
        _client = client;
        _processor = processor;
        _server = server;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var _ = _client;
        var ep = _client.Remote;
        var remote = ep?.ToString() ?? "?";
        var addr = ep?.Address is { IsIPv4MappedToIPv6: true } a4 ? a4.MapToIPv4() : ep?.Address;
        _conn = new DeviceConnection("CIP", addr?.ToString(), ep?.Port ?? 0) { QueueDepth = () => Volatile.Read(ref _queued) };
        _conn.SetDetail("Local port", _client.LocalPort.ToString());
        if (_client.Carrier is { } carrier) _conn.SetDetail("Carried over", carrier);
        ClientIdentifier.Begin(_conn); // hostname, model, firmware: in the background, ready by the time anyone looks
        _server.Event(1, $"connection from {remote}" + (_client.Carrier is { } over ? $" over {over}" : ""));
        _processor.DeviceUnregistered += OnUnregistered;
        var sender = SendLoop(_sendCts.Token);
        try
        {
            _whoAreYouAt = System.Diagnostics.Stopwatch.GetTimestamp();
            Send(Cip.WhoAreYouPacket());
            if (_server.ProbeHeartbeats) _probe = ProbeLoop(_sendCts.Token);
            while (!ct.IsCancellationRequested)
            {
                if (await _client.ReadPacketAsync(ct) is not var (type, payload)) { _server.Log($"{remote}: the client closed the connection"); break; }
                if (_server.LogPackets || _traced++ < CipServer.TracedPacketsPerSession) _server.Log($"<- {type:X2} {Convert.ToHexString(payload)}"); // hex only when logging or tracing
                _conn.RecordIn(3 + payload.Length);
                Handle(type, payload);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Net.WebSockets.WebSocketException)
        {
            if (e is not OperationCanceledException) _server.Log($"{remote}: connection ended by {e.GetType().Name}: {e.Message}");
        }
        catch (Exception e)
        {
            // A packet OpenVP could not handle must not end silently: say what happened, then drop the connection as before.
            _server.Event(_device?.Slot ?? 1, $"{remote}: error while handling a packet, connection closed: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            try
            {
                _processor.DeviceUnregistered -= OnUnregistered;
                _server.Event(_device?.Slot ?? 1, $"{remote} disconnected" + (_device != null ? $" (IP ID {_device.IpId:X2})" : ""));
            }
            finally
            {
                Detach();
                _outbox.Writer.TryComplete();
                // Nothing more needs to reach a panel that is gone: abort a write stuck on a dead peer, and
                // close the socket (which also fails it), instead of waiting for it forever.
                _sendCts.Cancel();
                try { _client.Close(); } catch { }
                try { await sender.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                _sendCts.Dispose();
            }
        }
    }

    private void Handle(byte type, byte[] payload)
    {
        if (Cip.DecodeRoomConnect(type, payload) is { } rc)
        {
            if (rc.Hostname != null) _conn.Hostname ??= rc.Hostname;
            if (rc.Model != null) _conn.Model ??= rc.Model;
            if (rc.Mac != null) _conn.MacAddress ??= rc.Mac;
            if (rc.Make != null) _conn.SetDetail("Make", rc.Make);
            Register(rc.IpId, rc.RoomId, type, payload);
            return;
        }
        if (Cip.DecodeConnectIpId(type, payload) is uint ipId)
        {
            Register(ipId, null, type, payload);
            return;
        }
        // A client that says something other than a sign-on before it has an IP ID is using a handshake OpenVP
        // doesn't know. That is always logged, with the bytes, so it
        // shows in the slot log without turning packet logging on and can be reported.
        if (_device == null && type is not (Cip.HeartbeatRequest or Cip.HeartbeatResponse or Cip.Disconnect) && _strays < MaxStraysLogged)
        {
            _strays++;
            var hex = Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 400))) + (payload.Length > 400 ? "..." : "");
            _server.Event(1, $"{(_conn.RemoteAddress.Length > 0 ? _conn.RemoteAddress : "client")} sent packet type {type:X2} before signing on with an IP ID, which OpenVP doesn't understand, so it is not connected: {type:X2} {hex}"
                + (_strays == 1 ? ". Please report these bytes." : ""));
        }
        switch (type)
        {
            case Cip.HeartbeatRequest:
                var now = DateTimeOffset.Now;
                if (_lastHeartbeat is { } prev) _conn.SetDetail("Heartbeat interval", $"{(now - prev).TotalSeconds:0.0} s");
                _lastHeartbeat = now;
                Interlocked.Increment(ref _conn.Heartbeats);
                // A CH5 client on a WebSocket gets its own heartbeat bytes back, as the community mock processor does; an XPanel on TCP is content with 00 00.
                Send(_client.Carrier != null && payload.Length > 0 ? Cip.Frame(Cip.HeartbeatResponse, payload) : Cip.Heartbeat());
                return;
            case Cip.HeartbeatResponse:
                if (Interlocked.Exchange(ref _probeAt, 0) is var p0 and not 0)
                    _conn.RecordLatency(System.Diagnostics.Stopwatch.GetElapsedTime(p0).TotalMilliseconds, "CIP heartbeat");
                return;
            case Cip.Disconnect:
                _client.Close();
                return;
        }
        if (type == Cip.Data && payload.Length >= 4 && payload[3] == Cip.SubUpdateRequest)
        {
            // Panel asks for everything: send a full snapshot.
            if (_device != null)
            {
                var snapshot = _device.FeedbackSnapshot();
                // Room for the whole snapshot (plus what is already queued), then the usual margin on top.
                _queueLimit = Math.Max(_queueLimit, Volatile.Read(ref _queued) + snapshot.Count + MaxQueuedPackets);
                foreach (var (k, v) in snapshot) OnFeedback(_device, k, v);
            }
            Send(Cip.UpdateComplete());
            return;
        }
        var unknown = new List<(byte sub, byte[] raw)>();
        foreach (var (key, value) in Cip.Decode(type, payload, unknown))
        {
            if (_device == null) break;
            Interlocked.Increment(ref _conn.JoinsIn);
            _device.ReceiveInput(key, value);
        }
        foreach (var (sub, raw) in unknown) _server.LogUnknownSub(type, sub, raw);
    }

    /// <summary>Bind this connection to a device. With a Room ID the slot is the one that has it; an unknown Room ID is refused.</summary>
    private void Register(uint ipId, string? roomId, byte type, byte[] payload)
    {
        // WhoAreYou out, connect request back: a real round trip over this connection (plus the panel's own think time).
        if (Interlocked.Exchange(ref _whoAreYouAt, 0) is var t0 and not 0)
            _conn.RecordLatency(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds, "CIP handshake");
        Detach();
        // Same sign-on, two answers: an XPanel on TCP wants 0x02, a CH5 client on a WebSocket wants 0x27 (see Cip.RoomConnectOk).
        var routerAnswer = type == Cip.RoomConnectRequest && _client.Carrier != null;
        var who = _conn.Hostname ?? _conn.Model;
        var from = who != null ? $" from {who}" : "";
        var device = DeviceDirectory.Resolve(_processor, ipId, null, roomId);
        if (device == null)
        {
            var rooms = string.Join(", ", _processor.Slots.Select(x => x.RoomId));
            _server.Event(1, $"panel{from} asked for Room ID '{roomId}' (IP ID {ipId:X2}), but no slot has that Room ID, so it is not connected. Room IDs here: {rooms}");
            Send(routerAnswer ? Cip.RoomConnectRefused() : Cip.ConnectRefused());
            return;
        }
        var slot = _processor.Slot(device.Slot);
        var known = device.Type is not null && !device.Type.StartsWith("Panel (");
        _device = device;
        _server.Event(device.Slot, $"panel registered as IP ID {ipId:X2}" + (roomId != null ? $" in room {slot.RoomId}" : "") + from
            + (known ? $" (slot {device.Slot}, {device.Type})" : $" — the program in slot {device.Slot} has not registered this IP ID; check the panel's IP ID" + (roomId != null ? " and Room ID" : "")));
        // The connect request is the only thing a CIP client says about itself; keep it for identifying client types.
        _conn.SetDetail("Connect request", $"{type:X2} {Convert.ToHexString(payload)}");
        _conn.SetDetail("IP ID requested", ipId.ToString("X2"));
        if (roomId != null) _conn.SetDetail("Room ID requested", roomId);
        device.FeedbackChanged += OnFeedback;
        device.AttachConnection(_conn);
        Send(routerAnswer ? Cip.RoomConnectOk() : Cip.ConnectOk(ipId));
        if (routerAnswer && Cip.WantsAuthentication(payload)) Send(Cip.Authenticated());
    }

    private async Task ProbeLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_server.ProbeInterval, ct);
                if (_device == null) continue;   // not registered yet
                Volatile.Write(ref _probeAt, System.Diagnostics.Stopwatch.GetTimestamp());
                Send(Cip.Frame(Cip.HeartbeatRequest, 0x00, 0x00));
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnFeedback(JoinDevice dev, JoinKey key, object value)
    {
        if (key.Kind == SigKind.Serial && value is string text && text.Length > Cip.MaxSerialBytes)
            _processor.Slot(dev.Slot).Log("Warning", $"[CIP] IPID {dev.IpId:X2} serial join {key.Join} is {text.Length} chars; sent the first {Cip.MaxSerialBytes} to the panel");
        Interlocked.Increment(ref _conn.JoinsOut);
        Send(Cip.Encode(key, value));
    }

    private void OnUnregistered(JoinDevice d)
    {
        if (d == _device) _client.Close(); // panel reconnects and binds to the reloaded program
    }

    private void Detach()
    {
        if (_device == null) return;
        _device.FeedbackChanged -= OnFeedback;
        _device.DetachConnection(_conn);
        _device = null;
    }

    /// <summary>Queue a packet; never blocks the caller (often the slot thread).</summary>
    private void Send(byte[] packet)
    {
        if (Interlocked.Increment(ref _queued) <= Volatile.Read(ref _queueLimit))
        {
            if (_outbox.Writer.TryWrite(packet)) return;
            Interlocked.Decrement(ref _queued);
            return; // already shutting down
        }
        Interlocked.Decrement(ref _queued);
        if (_outbox.Reader.Completion.IsCompleted) return; // already shutting down
        _server.Event(_device?.Slot ?? 1, $"{_client.Remote} is not reading fast enough ({_queueLimit} packets queued); disconnecting it");
        _client.Close();
    }

    /// <summary>One writer per connection: whatever is queued goes out as a single write, in order.</summary>
    private async Task SendLoop(CancellationToken ct)
    {
        var buf = new ArrayBufferWriter<byte>(1024);
        try
        {
            var reader = _outbox.Reader;
            while (await reader.WaitToReadAsync(ct))
            {
                buf.Clear();
                // A WebSocket client reads one CIP packet per message, so there each packet is its own write.
                while (buf.WrittenCount < MaxWriteBytes && (buf.WrittenCount == 0 || !_client.PacketPerWrite) && reader.TryRead(out var packet))
                {
                    Interlocked.Decrement(ref _queued);
                    if (_server.LogPackets || _traced++ < CipServer.TracedPacketsPerSession) _server.Log($"-> {Convert.ToHexString(packet)}");
                    _conn.RecordOut(packet.Length);
                    buf.Write(packet);
                }
                await _client.WriteAsync(buf.WrittenMemory, ct);
            }
        }
        catch (Exception) { _client.Close(); } // dead peer: the read loop notices and cleans up
    }
}

/// <summary>
/// What carries a CIP session's packets: a TCP connection, or a WebSocket (<see cref="WebXPanelServer"/>).
/// One read and one write may be in flight at a time.
/// </summary>
internal interface ICipTransport : IDisposable
{
    IPEndPoint? Remote { get; }
    int LocalPort { get; }
    /// <summary>What CIP travels inside, for the log and the client's details; null for plain TCP.</summary>
    string? Carrier { get; }
    /// <summary>True if each CIP packet must be written on its own (a WebSocket message per packet).</summary>
    bool PacketPerWrite { get; }
    /// <summary>The next CIP packet (its type byte and its payload), or null when the client has gone.</summary>
    ValueTask<(byte type, byte[] payload)?> ReadPacketAsync(CancellationToken ct);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    /// <summary>Drop the connection now; pending reads and writes fail. Safe to call more than once and from any thread.</summary>
    void Close();
}

internal sealed class TcpCipTransport : ICipTransport
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public TcpCipTransport(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        Remote = client.Client.RemoteEndPoint as IPEndPoint;
        LocalPort = (client.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    }

    public IPEndPoint? Remote { get; }
    public int LocalPort { get; }
    public string? Carrier => null;
    public bool PacketPerWrite => false;
    private readonly byte[] _header = new byte[3];

    public async ValueTask<(byte type, byte[] payload)?> ReadPacketAsync(CancellationToken ct)
    {
        if (!await ReadExactly(_header, ct)) return null;
        var payload = new byte[(_header[1] << 8) | _header[2]];
        if (payload.Length > 0 && !await ReadExactly(payload, ct)) return null;
        return (_header[0], payload);
    }

    private async Task<bool> ReadExactly(byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await _stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => _stream.WriteAsync(data, ct);
    public void Close() { try { _client.Close(); } catch { } }
    public void Dispose() => Close();
}
