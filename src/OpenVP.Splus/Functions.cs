using System;
using System.Globalization;
using System.Text;
using System.Threading;
using Crestron.Logos.SplusObjects;

namespace OpenVP.Splus
{
    /// <summary>SIMPL+ MakeString/Print formatting: %d %u %ld %lu %x %X %lx %c %s %%, with width and 0-padding.</summary>
    public static class Printf
    {
        public static string Format(string format, object[] args)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            args ??= Array.Empty<object>();
            // The SIMPL+ compiler turns MakeString/Print formats into .NET composite formats ("{0:d2}").
            if (format.IndexOf('{') >= 0 && format.IndexOf('}') > format.IndexOf('{'))
            {
                try
                {
                    var converted = new object[args.Length];
                    for (int k = 0; k < args.Length; k++) converted[k] = args[k] is CrestronString cs ? cs.Str : args[k];
                    return string.Format(CultureInfo.InvariantCulture, format, converted);
                }
                catch (FormatException) { /* fall through to printf-style */ }
            }
            var sb = new StringBuilder();
            int ai = 0;
            for (int i = 0; i < format.Length; i++)
            {
                var ch = format[i];
                if (ch != '%') { sb.Append(ch); continue; }
                if (i + 1 < format.Length && format[i + 1] == '%') { sb.Append('%'); i++; continue; }
                int j = i + 1;
                var flags = "";
                while (j < format.Length && "-0+ ".IndexOf(format[j]) >= 0) flags += format[j++];
                int width = 0;
                while (j < format.Length && char.IsDigit(format[j])) width = width * 10 + (format[j++] - '0');
                int precision = -1;
                if (j < format.Length && format[j] == '.') { j++; precision = 0; while (j < format.Length && char.IsDigit(format[j])) precision = precision * 10 + (format[j++] - '0'); }
                while (j < format.Length && (format[j] == 'l' || format[j] == 'h')) j++;
                if (j >= format.Length) { sb.Append(format, i, format.Length - i); break; }
                var conv = format[j];
                var arg = ai < args.Length ? args[ai++] : null;
                string text = conv switch
                {
                    'd' or 'i' => ToLong(arg, true).ToString(CultureInfo.InvariantCulture),
                    'u' => ToULong(arg).ToString(CultureInfo.InvariantCulture),
                    'x' => ToULong(arg).ToString("x"),
                    'X' => ToULong(arg).ToString("X"),
                    'o' => Convert.ToString((long)ToULong(arg), 8),
                    'c' => ((char)(ToULong(arg) & 0xFF)).ToString(),
                    's' => arg?.ToString() ?? "",
                    _ => ""
                };
                if (conv == 's' && precision >= 0 && text.Length > precision) text = text.Substring(0, precision);
                if (text.Length < width)
                    text = flags.Contains('-') ? text.PadRight(width) : flags.Contains('0') && conv != 's' ? text.PadLeft(width, '0') : text.PadLeft(width);
                sb.Append(text);
                i = j;
            }
            return sb.ToString();
        }

        private static long ToLong(object o, bool signed) => o switch
        {
            null => 0,
            CrestronString s => long.TryParse(s.Str, out var v) ? v : 0,
            ushort u => u,
            short s16 => s16,
            uint u32 => u32,
            int i32 => i32,
            bool b => b ? 1 : 0,
            _ => Convert.ToInt64(o, CultureInfo.InvariantCulture)
        };

        private static ulong ToULong(object o) => o switch
        {
            null => 0,
            short s => (ushort)s,
            int i => (uint)i,
            long l => (ulong)l,
            _ => (ulong)ToLong(o, false)
        };
    }
}

namespace Crestron.Logos.SplusLibrary
{
    /// <summary>SIMPL+ built-in functions. Strings are 1-based, as in SIMPL+.</summary>
    public static partial class Functions
    {
        private static readonly Random Rng = new Random();

        private static string S(CrestronString s) => s?.Str ?? string.Empty;

        // ---------------- truth and integers

        public static bool TestForTrue(int v) => v != 0;
        public static bool TestForTrue(ushort v) => v != 0;
        public static bool TestForTrue(short v) => v != 0;
        public static bool TestForTrue(uint v) => v != 0;
        public static bool TestForTrue(bool v) => v;
        public static int BoolToInt(bool v) => v ? 1 : 0;
        public static int Not(int v) => v == 0 ? 1 : 0;
        public static ushort Not(ushort v) => (ushort)(v == 0 ? 1 : 0);
        public static int Abs(int v) => Math.Abs(v);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static uint SMax(int a, int b) => (uint)Math.Max(a, b);
        public static uint SMin(int a, int b) => (uint)Math.Min(a, b);
        public static ushort High(ushort v) => (ushort)(v >> 8);
        public static ushort Low(ushort v) => (ushort)(v & 0xFF);
        public static ushort HighWord(uint v) => (ushort)(v >> 16);
        public static ushort LowWord(uint v) => (ushort)(v & 0xFFFF);
        public static uint RotateLeftLong(uint v, uint n) => (v << (int)(n & 31)) | (v >> (int)((32 - n) & 31));
        public static uint RotateRightLong(uint v, uint n) => (v >> (int)(n & 31)) | (v << (int)((32 - n) & 31));
        public static ushort RotateLeft(ushort v, ushort n) => (ushort)((v << (n & 15)) | (v >> ((16 - n) & 15)));
        public static ushort RotateRight(ushort v, ushort n) => (ushort)((v >> (n & 15)) | (v << ((16 - n) & 15)));
        public static uint ToLongInteger(int v) => unchecked((uint)v);
        public static short ToSignedInteger(int v) => unchecked((short)v);
        public static int ToSignedLongInteger(int v) => v;
        public static ushort ToInteger(int v) => unchecked((ushort)v);
        public static ushort Random(ushort lo, ushort hi) => (ushort)Rng.Next(lo, Math.Max(lo, hi) + 1);
        public static ushort Rnd() => (ushort)Rng.Next(0, 65536);

        // ---------------- strings

        public static int Length(CrestronString s) => S(s).Length;
        public static int Len(CrestronString s) => S(s).Length;
        public static CrestronString Chr(int c) => new CrestronString(((char)(c & 0xFF)).ToString());
        public static ushort Byte(CrestronString s, int pos) { var v = S(s); return pos >= 1 && pos <= v.Length ? (ushort)(v[pos - 1] & 0xFF) : (ushort)0; }

        public static CrestronString Left(CrestronString s, int n) { var v = S(s); return new CrestronString(v.Substring(0, Math.Clamp(n, 0, v.Length))); }
        public static CrestronString Right(CrestronString s, int n) { var v = S(s); n = Math.Clamp(n, 0, v.Length); return new CrestronString(v.Substring(v.Length - n)); }
        public static CrestronString Mid(CrestronString s, int start, int n)
        {
            var v = S(s);
            if (start < 1) start = 1;
            if (start > v.Length || n <= 0) return new CrestronString("");
            return new CrestronString(v.Substring(start - 1, Math.Min(n, v.Length - start + 1)));
        }

        public static CrestronString Upper(CrestronString s) => new CrestronString(S(s).ToUpperInvariant());
        public static CrestronString Lower(CrestronString s) => new CrestronString(S(s).ToLowerInvariant());

        public static int Find(string match, CrestronString source) => Find(match, source, 1);
        public static int Find(string match, CrestronString source, int start)
        {
            var v = S(source);
            if (string.IsNullOrEmpty(match) || start < 1 || start > v.Length) return 0;
            var i = v.IndexOf(match, start - 1, StringComparison.Ordinal);
            return i < 0 ? 0 : i + 1;
        }
        public static int Find(CrestronString match, CrestronString source) => Find(S(match), source, 1);
        public static int Find(CrestronString match, CrestronString source, int start) => Find(S(match), source, start);
        public static int FindNoCase(string match, CrestronString source) => FindNoCase(match, source, 1);
        public static int FindNoCase(string match, CrestronString source, int start)
        {
            var v = S(source);
            if (string.IsNullOrEmpty(match) || start < 1 || start > v.Length) return 0;
            var i = v.IndexOf(match, start - 1, StringComparison.OrdinalIgnoreCase);
            return i < 0 ? 0 : i + 1;
        }
        public static int ReverseFind(string match, CrestronString source)
        {
            var i = S(source).LastIndexOf(match ?? "", StringComparison.Ordinal);
            return i < 0 ? 0 : i + 1;
        }

        /// <summary>Remove: returns everything up to and including the match, and takes it out of the source.</summary>
        public static CrestronString Remove(string match, CrestronString source) => Remove(match, source, 1);
        public static CrestronString Remove(string match, CrestronString source, int start)
        {
            var pos = Find(match, source, start);
            if (pos == 0) return new CrestronString("");
            var v = S(source);
            var end = pos - 1 + match.Length;
            var result = v.Substring(0, end);
            source.UpdateValue(v.Substring(end));
            return new CrestronString(result);
        }
        public static CrestronString Remove(CrestronString match, CrestronString source) => Remove(S(match), source, 1);
        public static CrestronString Remove(int count, CrestronString source)
        {
            var v = S(source);
            count = Math.Clamp(count, 0, v.Length);
            source.UpdateValue(v.Substring(count));
            return new CrestronString(v.Substring(0, count));
        }

        public static CrestronString RemoveByLength(int count, CrestronString source) => Remove(count, source);

        public static void ClearBuffer(CrestronString s) => s?.UpdateValue("");

        public static CrestronString Gather(string delimiter, StringInput buffer) => Gather(delimiter, buffer, -1);
        public static CrestronString Gather(string delimiter, StringInput buffer, int timeoutTicks)
        {
            var owner = buffer?.Owner;
            if (owner == null) return new CrestronString("");
            owner.WaitForBuffer(() => Find(delimiter, buffer) > 0, timeoutTicks < 0 ? -1 : timeoutTicks * 10);
            return Remove(delimiter, buffer);
        }
        public static CrestronString Gather(int length, StringInput buffer) => Gather(length, buffer, -1);
        public static CrestronString Gather(int length, StringInput buffer, int timeoutTicks)
        {
            var owner = buffer?.Owner;
            if (owner == null) return new CrestronString("");
            owner.WaitForBuffer(() => S(buffer).Length >= length, timeoutTicks < 0 ? -1 : timeoutTicks * 10);
            return Remove(Math.Min(length, S(buffer).Length), buffer);
        }

        public static CrestronString ItoA(int v) => new CrestronString(v.ToString(CultureInfo.InvariantCulture));
        public static CrestronString LtoA(int v) => ItoA(v);
        public static CrestronString ItoHex(int v) => new CrestronString(((uint)v & 0xFFFF).ToString("X"));
        public static CrestronString LtoHex(int v) => new CrestronString(((uint)v).ToString("X"));

        /// <summary>Atoi: the first run of digits in the string (sign ignored), 0 if none.</summary>
        public static ushort Atoi(CrestronString s) => (ushort)(ParseDigits(S(s), false) & 0xFFFF);
        public static uint Atol(CrestronString s) => (uint)ParseDigits(S(s), false);
        public static short Atosi(CrestronString s) => (short)ParseDigits(S(s), true);
        public static int Atosl(CrestronString s) => (int)ParseDigits(S(s), true);

        private static long ParseDigits(string v, bool signed)
        {
            int i = 0;
            while (i < v.Length && !char.IsDigit(v[i])) i++;
            var negative = signed && i > 0 && v[i - 1] == '-';
            long n = 0;
            while (i < v.Length && char.IsDigit(v[i])) n = n * 10 + (v[i++] - '0');
            return negative ? -n : n;
        }

        public static ushort HextoI(CrestronString s)
        {
            var v = S(s).Trim();
            int i = 0; uint n = 0;
            while (i < v.Length && Uri.IsHexDigit(v[i])) n = (n << 4) | (uint)Convert.ToInt32(v[i++].ToString(), 16);
            return (ushort)n;
        }

        public static CrestronString Trim(CrestronString s) => new CrestronString(S(s).Trim());

        // ---------------- time

        public static int GetHourNum() => DateTime.Now.Hour;
        public static int GetMinutesNum() => DateTime.Now.Minute;
        public static int GetSecondsNum() => DateTime.Now.Second;
        public static int GetDateNum() => DateTime.Now.Day;
        public static int GetMonthNum() => DateTime.Now.Month;
        public static int GetYearNum() => DateTime.Now.Year;
        public static int GetDayOfWeekNum() => (int)DateTime.Now.DayOfWeek;
        public static int GetTicks() => (int)(Environment.TickCount64 / 10);
        public static CrestronString Time() => new CrestronString(DateTime.Now.ToString("HH:mm:ss"));
        public static CrestronString Date(int format) => new CrestronString(format switch
        {
            1 => DateTime.Now.ToString("dd/MM/yyyy"),
            2 => DateTime.Now.ToString("yyyy/MM/dd"),
            3 => DateTime.Now.ToString("dd.MM.yyyy"),
            4 => DateTime.Now.ToString("yyyy-MM-dd"),
            _ => DateTime.Now.ToString("MM/dd/yyyy")
        });
        public static CrestronString Day() => new CrestronString(DateTime.Now.DayOfWeek.ToString());

        // ---------------- flow control (hand the baton over while waiting)

        public static void Delay(int ticks)
        {
            var m = SplusObject.Current;
            if (m == null) { Thread.Sleep(Math.Max(0, ticks) * 10); return; }
            m.Yield(() => Thread.Sleep(Math.Max(0, ticks) * 10));
        }

        public static void ProcessLogic()
        {
            var m = SplusObject.Current;
            if (m == null) { Thread.Yield(); return; }
            m.Yield(() => Thread.Sleep(1));
        }

        public static void Pulse(int ticks, DigitalOutput output)
        {
            if (output == null) return;
            output.Value = 1;
            var t = default(Timer);
            t = new Timer(_ => { output.Value = 0; t?.Dispose(); }, null, Math.Max(1, ticks) * 10, Timeout.Infinite);
        }

        public static void SetArray(InOutArray<DigitalOutput> array, int value)
        {
            for (int i = 1; i <= array.Count; i++) if (array[i] != null) array[i].Value = value;
        }
        public static void SetArray(InOutArray<AnalogOutput> array, int value)
        {
            for (int i = 1; i <= array.Count; i++) if (array[i] != null) array[i].Value = (ushort)value;
        }
        public static void SetArray(InOutArray<StringOutput> array, string value)
        {
            for (int i = 1; i <= array.Count; i++) array[i]?.UpdateValue(value);
        }

        public static ushort GetLastModifiedArrayIndex(SignalEventArgs args) => args?.ArrayIndex ?? 0;

        // ---------------- sockets

        public static short SocketUDP_Enable(SplusUdpSocket socket, string address, ushort port)
        {
            try
            {
                OpenVP.Splus.UdpPorts.Leave(socket);
                socket.Remote = new System.Net.IPEndPoint(System.Net.IPAddress.TryParse(address, out var ip) ? ip : System.Net.Dns.GetHostAddresses(address)[0], port);
                OpenVP.Splus.UdpPorts.Join(socket, port);
                socket.PortNumber = port;
                socket.SocketStatus = 2;
                return 0;
            }
            catch (Exception e) { socket.Owner?.Log("Warning", $"SIMPL+ UDP enable {address}:{port}: {e.Message}"); return -1; }
        }

        public static short SocketUDP_Disable(SplusUdpSocket socket)
        {
            OpenVP.Splus.UdpPorts.Leave(socket); socket.SocketStatus = 0; return 0;
        }

        /// <summary>A datagram for this socket (from <see cref="OpenVP.Splus.UdpPorts"/>).</summary>
        internal static void DeliverUdp(SplusUdpSocket socket, System.Net.IPEndPoint sender, byte[] data)
        {
            if (socket.Owner?.Stopped == true) return;
            socket.LastSender = sender;
            socket.SocketRxBuf.UpdateValue(S(socket.SocketRxBuf) + Encoding.Latin1.GetString(data));
            socket.Raise(socket.OnSocketReceive);
        }

        public static short SocketSend(SocketInfo socket, CrestronString data)
        {
            var bytes = Encoding.Latin1.GetBytes(S(data));
            try
            {
                switch (socket)
                {
                    case SplusUdpSocket u when u.Udp != null && u.Remote != null: u.Udp.Send(bytes, bytes.Length, u.Remote); return (short)bytes.Length;
                    case SplusTcpClient c when c.Client?.Connected == true: c.Client.GetStream().Write(bytes); return (short)bytes.Length;
                    case SplusTcpServer s when s.Client?.Connected == true: s.Client.GetStream().Write(bytes); return (short)bytes.Length;
                }
            }
            catch (Exception e) { socket.Owner?.Log("Warning", $"SIMPL+ SocketSend: {e.Message}"); }
            return -1;
        }

        public static short SocketConnectClient(SplusTcpClient socket, string address, ushort port, ushort reconnect)
        {
            try
            {
                socket.Client?.Dispose();
                socket.Client = new System.Net.Sockets.TcpClient();
                var c = socket.Client;
                c.ConnectAsync(address, port).ContinueWith(t =>
                {
                    if (t.IsFaulted) { socket.SocketStatus = 4; socket.Raise(socket.OnSocketStatus); return; }
                    socket.SocketStatus = 2;
                    socket.Raise(socket.OnSocketConnect);
                    socket.Raise(socket.OnSocketStatus);
                    _ = ReceiveTcp(socket, c);
                });
                return 0;
            }
            catch { return -1; }
        }

        public static short SocketDisconnectClient(SplusTcpClient socket)
        {
            socket.Client?.Dispose(); socket.Client = null;
            socket.SocketStatus = 0;
            socket.Raise(socket.OnSocketDisconnect);
            return 0;
        }

        private static async System.Threading.Tasks.Task ReceiveTcp(SocketInfo socket, System.Net.Sockets.TcpClient client)
        {
            var buf = new byte[4096];
            try
            {
                var stream = client.GetStream();
                while (socket.Owner?.Stopped != true)
                {
                    var n = await stream.ReadAsync(buf, 0, buf.Length);
                    if (n == 0) break;
                    socket.SocketRxBuf.UpdateValue(S(socket.SocketRxBuf) + Encoding.Latin1.GetString(buf, 0, n));
                    socket.Raise(socket.OnSocketReceive);
                }
            }
            catch { }
            socket.SocketStatus = 0;
            socket.Raise(socket.OnSocketDisconnect);
            socket.Raise(socket.OnSocketStatus);
        }

        public static short SocketGetStatus(SocketInfo socket) => socket?.SocketStatus ?? 0;
    }
}
