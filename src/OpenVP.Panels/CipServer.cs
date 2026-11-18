using System.Net;
using System.Net.Sockets;
using System.Text;
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
            var session = new CipSession(client, _processor, this);
            lock (_sessions) _sessions.Add(session);
            _ = session.RunAsync(_cts.Token).ContinueWith(_ => { lock (_sessions) _sessions.Remove(session); });
        }
    }

    internal void Log(string message)
    {
        if (LogPackets) _processor.Slot(1).Log("Info", "[CIP] " + message);
    }

    /// <summary>Connection events are always logged.</summary>
    internal void Event(int slot, string message) => _processor.Slot(Math.Max(1, slot)).Log("Notice", "[Panel] " + message);

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

    public const byte SubDigital = 0x00;
    public const byte SubDigitalAlt = 0x27;    // digital press as some clients send it
    public const byte SubUpdateRequest = 0x03;
    public const byte SubAnalog = 0x14;
    public const byte SubSerialMarker = 0x34;

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

    /// <summary>Decode one join message from a panel. Returns null for anything that isn't join data.</summary>
    public static (JoinKey key, object value)? DecodeJoin(byte type, ReadOnlySpan<byte> p)
    {
        if (type == Data && p.Length >= 4)
        {
            var sub = p[3];
            if ((sub == SubDigital || sub == SubDigitalAlt) && p.Length >= 6)
            {
                var join = (uint)(((p[5] & 0x7F) << 8) | p[4]) + 1;
                return (new JoinKey(SigKind.Digital, join), (p[5] & 0x80) == 0);
            }
            if (sub == SubAnalog && p.Length >= 8)
            {
                var join = (uint)((p[4] << 8) | p[5]) + 1;
                return (new JoinKey(SigKind.Analog, join), (ushort)((p[6] << 8) | p[7]));
            }
        }
        if (type == SerialData && p.Length >= 8 && p[4] == SubSerialMarker)
        {
            var join = (uint)((p[5] << 8) | p[6]) + 1;
            return (new JoinKey(SigKind.Serial, join), Encoding.Latin1.GetString(p[8..]));
        }
        return null;
    }

    /// <summary>
    /// IP ID from a connect request. Panels send "00 00 00 00 00 [IPID] 40 FF FF F1 01" (11 bytes); some
    /// clients send one leading zero fewer. The IP ID is the byte just before the 0x40.
    /// </summary>
    public static uint? DecodeConnectIpId(byte type, ReadOnlySpan<byte> p)
    {
        if (type is not (ConnectRequest or ConnectRequestAlt)) return null;
        if (p.Length > 6 && p[6] == 0x40) return p[5];
        if (p.Length > 5 && p[5] == 0x40) return p[4];
        if (p.Length >= 11) return p[5];
        if (p.Length > 4) return p[4];
        return null;
    }

    /// <summary>Sent after a full update so the panel knows the snapshot is complete.</summary>
    public static byte[] UpdateComplete() => Frame(Data, 0x00, 0x00, 0x02, 0x03, 0x16);
}

internal sealed class CipSession
{
    private readonly TcpClient _client;
    private readonly Processor _processor;
    private readonly CipServer _server;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NetworkStream? _stream;
    private JoinDevice? _device;

    public CipSession(TcpClient client, Processor processor, CipServer server)
    {
        _client = client;
        _processor = processor;
        _server = server;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var _ = _client;
        _stream = _client.GetStream();
        var remote = _client.Client.RemoteEndPoint?.ToString() ?? "?";
        _server.Event(1, $"connection from {remote}");
        _processor.DeviceUnregistered += OnUnregistered;
        try
        {
            await Send(Cip.WhoAreYouPacket());
            var header = new byte[3];
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactly(header, ct)) break;
                var len = (header[1] << 8) | header[2];
                var payload = new byte[len];
                if (len > 0 && !await ReadExactly(payload, ct)) break;
                _server.Log($"<- {header[0]:X2} {Convert.ToHexString(payload)}");
                await Handle(header[0], payload);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { }
        finally
        {
            _processor.DeviceUnregistered -= OnUnregistered;
            _server.Event(_device?.Slot ?? 1, $"{remote} disconnected" + (_device != null ? $" (IP ID {_device.IpId:X2})" : ""));
            Detach();
        }
    }

    private async Task Handle(byte type, byte[] payload)
    {
        if (Cip.DecodeConnectIpId(type, payload) is uint ipId)
        {
            Detach();
            var known = _processor.FindDevice(ipId) != null;
            _device = DeviceDirectory.Resolve(_processor, ipId, null);
            _server.Event(_device.Slot, $"panel registered as IP ID {ipId:X2}" + (known ? $" (slot {_device.Slot}, {_device.Type})" : " — no loaded program has this IP ID; check the panel's IP ID"));
            _device.FeedbackChanged += OnFeedback;
            _device.AttachConnection();
            await Send(Cip.ConnectOk(ipId));
            return;
        }
        switch (type)
        {
            case Cip.HeartbeatRequest:
                await Send(Cip.Heartbeat());
                return;
            case Cip.Disconnect:
                _client.Close();
                return;
        }
        if (type == Cip.Data && payload.Length >= 4 && payload[3] == Cip.SubUpdateRequest)
        {
            // Panel asks for everything: send a full snapshot.
            if (_device != null)
                foreach (var (k, v) in _device.FeedbackSnapshot()) OnFeedback(_device, k, v);
            await Send(Cip.UpdateComplete());
            return;
        }
        if (Cip.DecodeJoin(type, payload) is var (key, value) && _device != null)
            _device.ReceiveInput(key, value);
    }

    private void OnFeedback(JoinDevice dev, JoinKey key, object value)
    {
        if (key.SmartObject != 0) return; // Smart Graphics over CIP isn't implemented yet (web panels get them)
        if (key.Kind == SigKind.Serial && value is string text && text.Length > Cip.MaxSerialBytes)
            _processor.Slot(dev.Slot).Log("Warning", $"[CIP] IPID {dev.IpId:X2} serial join {key.Join} is {text.Length} chars; sent the first {Cip.MaxSerialBytes} to the panel");
        var packet = key.Kind switch
        {
            SigKind.Digital => Cip.Digital(key.Join, (bool)value),
            SigKind.Analog => Cip.Analog(key.Join, (ushort)value),
            _ => Cip.Serial(key.Join, (string)value)
        };
        _ = Send(packet);
    }

    private void OnUnregistered(JoinDevice d)
    {
        if (d == _device) _client.Close(); // panel reconnects and binds to the reloaded program
    }

    private void Detach()
    {
        if (_device == null) return;
        _device.FeedbackChanged -= OnFeedback;
        _device.DetachConnection();
        _device = null;
    }

    private async Task Send(byte[] packet)
    {
        if (_stream == null) return;
        await _writeLock.WaitAsync();
        try
        {
            _server.Log($"-> {Convert.ToHexString(packet)}");
            await _stream.WriteAsync(packet);
        }
        catch (Exception) { }
        finally { _writeLock.Release(); }
    }

    private async Task<bool> ReadExactly(byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await _stream!.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
