using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OpenVP.SimplSharp.SshCore
{
    /// <summary>
    /// A small SSH-2 client (RFC 4253/4252/4254), enough for control-system use: key exchange
    /// (ecdh-sha2-nistp256, diffie-hellman-group14-sha256/sha1, group1-sha1), aes128/256-ctr and -cbc,
    /// hmac-sha2-256 / hmac-sha1, password and keyboard-interactive authentication, and one interactive
    /// shell channel. Host keys are reported to the caller rather than verified against a known-hosts list.
    /// </summary>
    internal sealed class SshConnection : IDisposable
    {
        private const string ClientVersion = "SSH-2.0-OpenVP_1.0";
        private TcpClient _tcp;
        private NetworkStream _net;
        private readonly object _sendLock = new object();
        private uint _seqOut, _seqIn;
        private Cipher _encOut, _encIn;
        private HMAC _macOut, _macIn;
        private int _blockOut = 8, _blockIn = 8;
        private byte[] _sessionId;
        private Thread _reader;
        private volatile bool _closed;

        private uint _remoteChannel;
        private uint _remoteWindow;
        private uint _remoteMaxPacket = 32768;
        private uint _localWindow = 1024 * 1024;
        private readonly AutoResetEvent _channelReply = new AutoResetEvent(false);
        private bool _channelOk;

        public event Action<byte[]> DataReceived;
        public event Action<Exception> Closed;
        public Func<byte[], byte[], bool> HostKeyCheck; // (hostKey, md5 fingerprint) -> trust
        public Func<string, string, IList<(string prompt, bool echo)>, IList<string>> KeyboardInteractive;
        public bool IsConnected => !_closed && _tcp?.Connected == true;

        // ------------------------------------------------------------------ connect

        public void Connect(string host, int port, string user, string password, bool tryKeyboardInteractive, TimeSpan timeout)
        {
            _tcp = new TcpClient();
            if (!_tcp.ConnectAsync(host, port).Wait(timeout)) throw new TimeoutException($"SSH connect to {host}:{port} timed out");
            _tcp.NoDelay = true;
            _net = _tcp.GetStream();
            _net.ReadTimeout = (int)timeout.TotalMilliseconds;

            var serverVersion = ExchangeVersions();
            KeyExchange(serverVersion);
            Authenticate(user, password, tryKeyboardInteractive);
            _net.ReadTimeout = Timeout.Infinite;
        }

        private string ExchangeVersions()
        {
            var v = Encoding.ASCII.GetBytes(ClientVersion + "\r\n");
            _net.Write(v, 0, v.Length);
            while (true)
            {
                var line = ReadLine();
                if (line.StartsWith("SSH-", StringComparison.Ordinal)) return line;
            }
        }

        private string ReadLine()
        {
            var sb = new StringBuilder();
            while (true)
            {
                var b = _net.ReadByte();
                if (b < 0) throw new IOException("Connection closed during version exchange");
                if (b == '\n') return sb.ToString().TrimEnd('\r');
                sb.Append((char)b);
                if (sb.Length > 1024) throw new IOException("Bad SSH version line");
            }
        }

        // ------------------------------------------------------------------ key exchange

        private static readonly string[] KexAlgs = { "ecdh-sha2-nistp256", "diffie-hellman-group14-sha256", "diffie-hellman-group14-sha1", "diffie-hellman-group1-sha1" };
        private static readonly string[] HostKeyAlgs = { "rsa-sha2-256", "ssh-rsa", "ecdsa-sha2-nistp256", "ssh-ed25519", "ssh-dss" };
        private static readonly string[] CipherAlgs = { "aes128-ctr", "aes256-ctr", "aes192-ctr", "aes128-cbc", "aes256-cbc" };
        private static readonly string[] MacAlgs = { "hmac-sha2-256", "hmac-sha1" };

        private void KeyExchange(string serverVersion)
        {
            var cookie = RandomNumberGenerator.GetBytes(16);
            var w = new Writer();
            w.Byte(20); w.Raw(cookie);
            w.NameList(KexAlgs); w.NameList(HostKeyAlgs);
            w.NameList(CipherAlgs); w.NameList(CipherAlgs);
            w.NameList(MacAlgs); w.NameList(MacAlgs);
            w.NameList(new[] { "none" }); w.NameList(new[] { "none" });
            w.NameList(Array.Empty<string>()); w.NameList(Array.Empty<string>());
            w.Bool(false); w.UInt(0);
            var clientKexInit = w.ToArray();
            SendPacket(clientKexInit);

            byte[] serverKexInit;
            do serverKexInit = ReadPacket(); while (serverKexInit[0] != 20);
            var r = new Reader(serverKexInit); r.Byte(); r.Raw(16);
            var sKex = r.NameList(); var sHost = r.NameList();
            var sEncCS = r.NameList(); var sEncSC = r.NameList();
            var sMacCS = r.NameList(); var sMacSC = r.NameList();

            string Pick(string[] mine, string[] theirs, string what) =>
                mine.FirstOrDefault(theirs.Contains) ?? throw new IOException($"No common SSH {what} (server offers {string.Join(",", theirs)})");
            var kex = Pick(KexAlgs, sKex, "key exchange");
            Pick(HostKeyAlgs, sHost, "host key type");
            var encCS = Pick(CipherAlgs, sEncCS, "cipher"); var encSC = Pick(CipherAlgs, sEncSC, "cipher");
            var macCS = Pick(MacAlgs, sMacCS, "MAC"); var macSC = Pick(MacAlgs, sMacSC, "MAC");

            Func<byte[], byte[]> hash = kex.EndsWith("sha1") ? SHA1.HashData : SHA256.HashData;
            byte[] hostKey, k, exchangeHash;
            if (kex == "ecdh-sha2-nistp256")
            {
                using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                var p = ecdh.ExportParameters(false).Q;
                var qc = new byte[65]; qc[0] = 4; p.X.CopyTo(qc, 1); p.Y.CopyTo(qc, 33);
                var init = new Writer(); init.Byte(30); init.String(qc); SendPacket(init.ToArray());
                var reply = new Reader(ExpectPacket(31)) { Pos = 1 };
                hostKey = reply.String(); var qs = reply.String(); reply.String(); // signature not verified
                var serverKey = ECDiffieHellman.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = qs.Skip(1).Take(32).ToArray(), Y = qs.Skip(33).Take(32).ToArray() }
                });
                k = ecdh.DeriveRawSecretAgreement(serverKey.PublicKey);
                var h = new Writer();
                h.String(ClientVersion); h.String(serverVersion); h.String(clientKexInit); h.String(serverKexInit);
                h.String(hostKey); h.String(qc); h.String(qs); h.MpInt(k);
                exchangeHash = hash(h.ToArray());
            }
            else
            {
                var prime = BigInteger.Parse("0" + (kex.Contains("group14") ? Group14 : Group1), System.Globalization.NumberStyles.HexNumber);
                var x = new BigInteger(RandomNumberGenerator.GetBytes(kex.Contains("group14") ? 64 : 32), isUnsigned: true, isBigEndian: true);
                var e = BigInteger.ModPow(2, x, prime);
                var eb = e.ToByteArray(isUnsigned: true, isBigEndian: true);
                var init = new Writer(); init.Byte(30); init.MpInt(eb); SendPacket(init.ToArray());
                var reply = new Reader(ExpectPacket(31)) { Pos = 1 };
                hostKey = reply.String(); var fb = reply.MpIntBytes(); reply.String();
                var f = new BigInteger(fb, isUnsigned: true, isBigEndian: true);
                k = BigInteger.ModPow(f, x, prime).ToByteArray(isUnsigned: true, isBigEndian: true);
                var h = new Writer();
                h.String(ClientVersion); h.String(serverVersion); h.String(clientKexInit); h.String(serverKexInit);
                h.String(hostKey); h.MpInt(eb); h.MpInt(fb); h.MpInt(k);
                exchangeHash = hash(h.ToArray());
            }

            if (HostKeyCheck != null && !HostKeyCheck(hostKey, MD5.HashData(hostKey)))
                throw new IOException("Host key not trusted");

            _sessionId ??= exchangeHash;
            SendPacket(new byte[] { 21 });
            ExpectPacket(21);

            byte[] Derive(char letter, int size)
            {
                var kw = new Writer(); kw.MpInt(k);
                var kenc = kw.ToArray();
                var outp = hash(kenc.Concat(exchangeHash).Concat(new[] { (byte)letter }).Concat(_sessionId).ToArray());
                while (outp.Length < size) outp = outp.Concat(hash(kenc.Concat(exchangeHash).Concat(outp).ToArray())).ToArray();
                return outp.Take(size).ToArray();
            }

            int KeySize(string c) => c.StartsWith("aes256") ? 32 : c.StartsWith("aes192") ? 24 : 16;
            _encOut = new Cipher(encCS, Derive('C', KeySize(encCS)), Derive('A', 16), encrypt: true);
            _encIn = new Cipher(encSC, Derive('D', KeySize(encSC)), Derive('B', 16), encrypt: false);
            _macOut = macCS == "hmac-sha1" ? new HMACSHA1(Derive('E', 20)) : new HMACSHA256(Derive('E', 32));
            _macIn = macSC == "hmac-sha1" ? new HMACSHA1(Derive('F', 20)) : new HMACSHA256(Derive('F', 32));
            _blockOut = _blockIn = 16;
        }

        private const string Group14 =
            "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
            "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
            "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3DC2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
            "83655D23DCA3AD961C62F356208552BB9ED529077096966D670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
            "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
            "15728E5A8AACAA68FFFFFFFFFFFFFFFF";
        private const string Group1 =
            "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
            "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
            "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381FFFFFFFFFFFFFFFF";

        // ------------------------------------------------------------------ authentication

        private void Authenticate(string user, string password, bool tryKeyboardInteractive)
        {
            var s = new Writer(); s.Byte(5); s.String("ssh-userauth"); SendPacket(s.ToArray());
            ExpectPacket(6);

            var a = new Writer(); a.Byte(50); a.String(user); a.String("ssh-connection"); a.String("password"); a.Bool(false); a.String(password ?? "");
            SendPacket(a.ToArray());
            var triedKbd = false;
            while (true)
            {
                var p = ReadPacket();
                switch (p[0])
                {
                    case 52: return; // success
                    case 53: continue; // banner
                    case 51:
                        var methods = new Reader(p) { Pos = 1 }.NameList();
                        if (!triedKbd && methods.Contains("keyboard-interactive"))
                        {
                            triedKbd = true;
                            var k = new Writer(); k.Byte(50); k.String(user); k.String("ssh-connection"); k.String("keyboard-interactive"); k.String(""); k.String("");
                            SendPacket(k.ToArray());
                            continue;
                        }
                        throw new IOException("SSH authentication failed");
                    case 60:
                        var r = new Reader(p) { Pos = 1 };
                        var name = Encoding.UTF8.GetString(r.String()); var instruction = Encoding.UTF8.GetString(r.String()); r.String();
                        var n = (int)r.UInt();
                        var prompts = new List<(string, bool)>();
                        for (int i = 0; i < n; i++) prompts.Add((Encoding.UTF8.GetString(r.String()), r.Bool()));
                        var answers = KeyboardInteractive?.Invoke(name, instruction, prompts) ?? prompts.Select(_ => password ?? "").ToList();
                        var resp = new Writer(); resp.Byte(61); resp.UInt((uint)answers.Count);
                        foreach (var ans in answers) resp.String(ans ?? "");
                        SendPacket(resp.ToArray());
                        continue;
                    default: continue;
                }
            }
        }

        // ------------------------------------------------------------------ shell channel

        public void OpenShell(string term, uint cols, uint rows, uint width, uint height)
        {
            var o = new Writer(); o.Byte(90); o.String("session"); o.UInt(0); o.UInt(_localWindow); o.UInt(32768);
            SendPacket(o.ToArray());
            while (true)
            {
                var p = ReadPacket();
                if (p[0] == 91)
                {
                    var r = new Reader(p) { Pos = 1 }; r.UInt();
                    _remoteChannel = r.UInt(); _remoteWindow = r.UInt(); _remoteMaxPacket = r.UInt();
                    break;
                }
                if (p[0] == 92) throw new IOException("SSH server refused the session channel");
            }
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ssh-reader" };
            _reader.Start();

            var pty = new Writer(); pty.Byte(98); pty.UInt(_remoteChannel); pty.String("pty-req"); pty.Bool(true);
            pty.String(term ?? "vt100"); pty.UInt(cols); pty.UInt(rows); pty.UInt(width); pty.UInt(height); pty.String(new byte[] { 0 });
            SendPacket(pty.ToArray());
            _channelReply.WaitOne(10000);

            var sh = new Writer(); sh.Byte(98); sh.UInt(_remoteChannel); sh.String("shell"); sh.Bool(true);
            SendPacket(sh.ToArray());
            if (!_channelReply.WaitOne(10000) || !_channelOk) throw new IOException("SSH server refused the shell");
        }

        public void Send(byte[] data)
        {
            for (int off = 0; off < data.Length;)
            {
                var n = (int)Math.Min(data.Length - off, Math.Min(_remoteMaxPacket - 64, 16384));
                var w = new Writer(); w.Byte(94); w.UInt(_remoteChannel); w.String(data.Skip(off).Take(n).ToArray());
                SendPacket(w.ToArray());
                off += n;
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (!_closed)
                {
                    var p = ReadPacket();
                    var r = new Reader(p) { Pos = 1 };
                    switch (p[0])
                    {
                        case 94:
                            r.UInt();
                            var data = r.String();
                            DataReceived?.Invoke(data);
                            if ((_localWindow -= (uint)data.Length) < 512 * 1024)
                            {
                                var adj = new Writer(); adj.Byte(93); adj.UInt(_remoteChannel); adj.UInt(1024 * 1024);
                                SendPacket(adj.ToArray());
                                _localWindow += 1024 * 1024;
                            }
                            break;
                        case 95: r.UInt(); r.UInt(); DataReceived?.Invoke(r.String()); break; // extended data (stderr)
                        case 93: r.UInt(); _remoteWindow += r.UInt(); break;
                        case 99: _channelOk = true; _channelReply.Set(); break;
                        case 100: _channelOk = false; _channelReply.Set(); break;
                        case 80: // global request: refuse if a reply is wanted
                            r.String(); if (r.Bool()) SendPacket(new byte[] { 82 });
                            break;
                        case 98: // channel request from server (exit-status etc.)
                            r.UInt(); r.String(); if (r.Bool()) { var f = new Writer(); f.Byte(100); f.UInt(_remoteChannel); SendPacket(f.ToArray()); }
                            break;
                        case 96: break;
                        case 97: case 1: throw new IOException("SSH connection closed by the server");
                    }
                }
            }
            catch (Exception e)
            {
                if (!_closed) { _closed = true; Closed?.Invoke(e); }
            }
        }

        // ------------------------------------------------------------------ packets

        private byte[] ExpectPacket(byte type)
        {
            while (true)
            {
                var p = ReadPacket();
                if (p[0] == type) return p;
                if (p[0] == 1) throw new IOException("SSH server disconnected: " + Encoding.UTF8.GetString(new Reader(p) { Pos = 5 }.String()));
                if (p[0] is 2 or 4 or 53) continue;
                throw new IOException($"Unexpected SSH message {p[0]} (expected {type})");
            }
        }

        private void SendPacket(byte[] payload)
        {
            lock (_sendLock)
            {
                var block = _encOut != null ? _blockOut : 8;
                var pad = block - ((payload.Length + 5) % block);
                if (pad < 4) pad += block;
                var packet = new byte[4 + 1 + payload.Length + pad];
                WriteUInt(packet, 0, (uint)(packet.Length - 4));
                packet[4] = (byte)pad;
                payload.CopyTo(packet, 5);
                RandomNumberGenerator.Fill(packet.AsSpan(5 + payload.Length));
                byte[] mac = null;
                if (_macOut != null)
                {
                    var seq = new byte[4]; WriteUInt(seq, 0, _seqOut);
                    mac = _macOut.ComputeHash(seq.Concat(packet).ToArray());
                }
                var wire = _encOut != null ? _encOut.Process(packet) : packet;
                _net.Write(wire, 0, wire.Length);
                if (mac != null) _net.Write(mac, 0, mac.Length);
                _seqOut++;
            }
        }

        private byte[] ReadPacket()
        {
            var block = _encIn != null ? _blockIn : 8;
            var first = ReadExact(block);
            if (_encIn != null) first = _encIn.Process(first);
            var length = ReadUInt(first, 0);
            if (length > 256 * 1024 || length < 5) throw new IOException("Bad SSH packet length");
            var rest = ReadExact((int)(length + 4 - block));
            if (_encIn != null) rest = _encIn.Process(rest);
            var packet = first.Concat(rest).ToArray();
            if (_macIn != null)
            {
                var mac = ReadExact(_macIn.HashSize / 8);
                var seq = new byte[4]; WriteUInt(seq, 0, _seqIn);
                var expected = _macIn.ComputeHash(seq.Concat(packet).ToArray());
                if (!CryptographicOperations.FixedTimeEquals(mac, expected)) throw new IOException("SSH MAC mismatch");
            }
            _seqIn++;
            var padLen = packet[4];
            return packet.Skip(5).Take((int)length - padLen - 1).ToArray();
        }

        private byte[] ReadExact(int n)
        {
            var b = new byte[n];
            for (int off = 0; off < n;)
            {
                var r = _net.Read(b, off, n - off);
                if (r <= 0) throw new IOException("SSH connection closed");
                off += r;
            }
            return b;
        }

        private static void WriteUInt(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static uint ReadUInt(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

        public void Dispose()
        {
            if (!_closed)
            {
                _closed = true;
                try { var d = new Writer(); d.Byte(1); d.UInt(11); d.String("bye"); d.String(""); SendPacket(d.ToArray()); } catch { }
            }
            try { _tcp?.Dispose(); } catch { }
        }

        // ------------------------------------------------------------------ helpers

        private sealed class Cipher
        {
            private readonly Aes _aes;
            private readonly ICryptoTransform _cbc;
            private readonly ICryptoTransform _ecb;
            private readonly byte[] _counter;
            private readonly byte[] _stream = new byte[16];
            private int _used = 16;

            public Cipher(string name, byte[] key, byte[] iv, bool encrypt)
            {
                _aes = Aes.Create();
                _aes.Key = key;
                _aes.Padding = PaddingMode.None;
                if (name.EndsWith("ctr"))
                {
                    _aes.Mode = CipherMode.ECB;
                    _ecb = _aes.CreateEncryptor();
                    _counter = (byte[])iv.Clone();
                }
                else
                {
                    _aes.Mode = CipherMode.CBC;
                    _aes.IV = iv;
                    _cbc = encrypt ? _aes.CreateEncryptor() : _aes.CreateDecryptor();
                }
            }

            public byte[] Process(byte[] data)
            {
                var o = new byte[data.Length];
                if (data.Length == 0) return o;
                if (_cbc != null) { _cbc.TransformBlock(data, 0, data.Length, o, 0); return o; }
                for (int i = 0; i < data.Length; i++)
                {
                    if (_used == 16)
                    {
                        _ecb.TransformBlock(_counter, 0, 16, _stream, 0);
                        for (int j = 15; j >= 0 && ++_counter[j] == 0; j--) { }
                        _used = 0;
                    }
                    o[i] = (byte)(data[i] ^ _stream[_used++]);
                }
                return o;
            }
        }

        private sealed class Writer
        {
            private readonly MemoryStream _ms = new MemoryStream();
            public void Byte(byte b) => _ms.WriteByte(b);
            public void Bool(bool b) => _ms.WriteByte((byte)(b ? 1 : 0));
            public void Raw(byte[] b) => _ms.Write(b, 0, b.Length);
            public void UInt(uint v) { var b = new byte[4]; WriteUInt(b, 0, v); Raw(b); }
            public void String(byte[] b) { UInt((uint)b.Length); Raw(b); }
            public void String(string s) => String(Encoding.UTF8.GetBytes(s ?? ""));
            public void NameList(string[] names) => String(string.Join(",", names));
            public void MpInt(byte[] unsignedBigEndian)
            {
                var i = 0;
                while (i < unsignedBigEndian.Length && unsignedBigEndian[i] == 0) i++;
                var v = unsignedBigEndian.Skip(i).ToArray();
                if (v.Length > 0 && (v[0] & 0x80) != 0) v = new byte[] { 0 }.Concat(v).ToArray();
                String(v);
            }
            public byte[] ToArray() => _ms.ToArray();
        }

        private sealed class Reader
        {
            private readonly byte[] _b;
            public int Pos;
            public Reader(byte[] b) { _b = b; }
            public byte Byte() => _b[Pos++];
            public bool Bool() => _b[Pos++] != 0;
            public byte[] Raw(int n) { var r = _b.Skip(Pos).Take(n).ToArray(); Pos += n; return r; }
            public uint UInt() { var v = ReadUInt(_b, Pos); Pos += 4; return v; }
            public byte[] String() => Raw((int)UInt());
            public string[] NameList() { var s = Encoding.ASCII.GetString(String()); return s.Length == 0 ? Array.Empty<string>() : s.Split(','); }
            public byte[] MpIntBytes() { var v = String(); return v.SkipWhile(x => x == 0).ToArray(); }
        }
    }
}
