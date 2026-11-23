using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Amib.Threading.Internal;
using Crestron.Logos.SplusObjects;

namespace Crestron.Logos.SplusLibrary
{
    // The rest of the SIMPL+ built-ins (see the SIMPL+ language reference for each function's meaning).
    public static partial class Functions
    {
        // ---------------- integers

        public static ushort Abs(ushort v) => v;
        public static short Abs(short v) => (short)Math.Abs((int)v);
        public static uint Abs(uint v) => v;
        public static uint Max(uint a, uint b) => Math.Max(a, b);
        public static uint Max(uint a, int b) => Math.Max(a, (uint)b);
        public static uint Max(int a, uint b) => Math.Max((uint)a, b);
        public static uint Min(uint a, uint b) => Math.Min(a, b);
        public static uint Min(uint a, int b) => Math.Min(a, (uint)b);
        public static uint Min(int a, uint b) => Math.Min((uint)a, b);
        public static uint SMax(uint a, uint b) => (uint)Math.Max((int)a, (int)b);
        public static uint SMax(uint a, int b) => (uint)Math.Max((int)a, b);
        public static uint SMax(int a, uint b) => (uint)Math.Max(a, (int)b);
        public static uint SMin(uint a, uint b) => (uint)Math.Min((int)a, (int)b);
        public static uint SMin(uint a, int b) => (uint)Math.Min((int)a, b);
        public static uint SMin(int a, uint b) => (uint)Math.Min(a, (int)b);

        private static uint UMulDiv(ulong a, ulong b, ulong c) => c == 0 ? 0 : (uint)(a * b / c);
        private static long SMulDivL(long a, long b, long c) => c == 0 ? 0 : a * b / c;
        public static uint MulDiv(uint a, uint b, uint c) => UMulDiv(a, b, c);
        public static uint MulDiv(uint a, uint b, int c) => UMulDiv(a, b, (uint)c);
        public static uint MulDiv(uint a, int b, uint c) => UMulDiv(a, (uint)b, c);
        public static uint MulDiv(uint a, int b, int c) => UMulDiv(a, (uint)b, (uint)c);
        public static uint MulDiv(int a, uint b, uint c) => UMulDiv((uint)a, b, c);
        public static uint MulDiv(int a, uint b, int c) => UMulDiv((uint)a, b, (uint)c);
        public static uint MulDiv(int a, int b, uint c) => UMulDiv((uint)a, (uint)b, c);
        public static int MulDiv(int a, int b, int c) => (int)UMulDiv((uint)a, (uint)b, (uint)c);
        public static uint SMulDiv(uint a, uint b, uint c) => (uint)SMulDivL((int)a, (int)b, (int)c);
        public static uint SMulDiv(uint a, uint b, int c) => (uint)SMulDivL((int)a, (int)b, c);
        public static uint SMulDiv(uint a, int b, uint c) => (uint)SMulDivL((int)a, b, (int)c);
        public static uint SMulDiv(uint a, int b, int c) => (uint)SMulDivL((int)a, b, c);
        public static uint SMulDiv(int a, uint b, uint c) => (uint)SMulDivL(a, (int)b, (int)c);
        public static uint SMulDiv(int a, uint b, int c) => (uint)SMulDivL(a, (int)b, c);
        public static uint SMulDiv(int a, int b, uint c) => (uint)SMulDivL(a, b, (int)c);
        public static int SMulDiv(int a, int b, int c) => (int)SMulDivL(a, b, c);

        public static void Seed(ushort seed) { }
        public static uint RotateLeft(uint v, uint n) => RotateLeftLong(v, n);
        public static uint RotateRight(uint v, uint n) => RotateRightLong(v, n);

        public static ushort ToInteger(ushort v) => v;
        public static ushort ToInteger(short v) => unchecked((ushort)v);
        public static ushort ToInteger(uint v) => unchecked((ushort)v);
        public static ushort ToInteger(long v) => unchecked((ushort)v);
        public static short ToSignedInteger(ushort v) => unchecked((short)v);
        public static short ToSignedInteger(short v) => v;
        public static short ToSignedInteger(uint v) => unchecked((short)v);
        public static short ToSignedInteger(long v) => unchecked((short)v);
        public static uint ToLongInteger(ushort v) => v;
        public static uint ToLongInteger(short v) => unchecked((uint)v);
        public static uint ToLongInteger(uint v) => v;
        public static uint ToLongInteger(long v) => unchecked((uint)v);
        public static int ToSignedLongInteger(ushort v) => v;
        public static int ToSignedLongInteger(short v) => v;
        public static int ToSignedLongInteger(uint v) => unchecked((int)v);
        public static int ToSignedLongInteger(long v) => unchecked((int)v);

        public static short Not(short v) => (short)(v == 0 ? 1 : 0);
        public static uint Not(uint v) => v == 0 ? 1u : 0u;
        public static ushort OnesComplement(ushort v) => (ushort)~v;
        public static short OnesComplement(short v) => (short)~v;
        public static uint OnesComplement(uint v) => ~v;
        public static int OnesComplement(int v) => ~v;

        public static uint HextoL(CrestronString s) => (uint)Hex(S(s));
        public static short HextoSI(CrestronString s) => unchecked((short)Hex(S(s)));
        public static int HextoSL(CrestronString s) => unchecked((int)Hex(S(s)));
        private static ulong Hex(string v)
        {
            v = v.Trim();
            int i = 0; ulong n = 0;
            while (i < v.Length && Uri.IsHexDigit(v[i])) n = (n << 4) | (uint)Convert.ToInt32(v[i++].ToString(), 16);
            return n;
        }

        public static ushort LowerChar(uint c) => (ushort)char.ToLowerInvariant((char)(c & 0xFF));
        public static ushort UpperChar(uint c) => (ushort)char.ToUpperInvariant((char)(c & 0xFF));
        public static int GetSeries() => 4;
        public static void TerminateEvent() => throw new OperationCanceledException();

        public static string GetExceptionMessage(SimplPlusException e) => e?.Message ?? "";
        public static int GetExceptionCode(SimplPlusException e) => e?.HResult ?? 0;

        // ---------------- strings

        public static int FindNoCase(CrestronString match, CrestronString source) => FindNoCase(S(match), source, 1);
        public static int FindNoCase(CrestronString match, CrestronString source, int start) => FindNoCase(S(match), source, start);
        public static CrestronString Remove(CrestronString match, CrestronString source, int start) => Remove(S(match), source, start);

        /// <summary>ReverseFind: position of the last match at or before start (0 = from the end).</summary>
        private static int RFind(string match, CrestronString source, int start, StringComparison cmp)
        {
            var v = S(source);
            if (string.IsNullOrEmpty(match) || v.Length == 0) return 0;
            var from = start <= 0 || start > v.Length ? v.Length - 1 : start - 1;
            var i = v.LastIndexOf(match, from, cmp);
            return i < 0 ? 0 : i + 1;
        }
        public static int ReverseFind(CrestronString m, CrestronString s) => RFind(S(m), s, 0, StringComparison.Ordinal);
        public static int ReverseFind(CrestronString m, CrestronString s, int start) => RFind(S(m), s, start, StringComparison.Ordinal);
        public static int ReverseFind(string m, CrestronString s, int start) => RFind(m, s, start, StringComparison.Ordinal);
        public static int ReverseFindNoCase(string m, CrestronString s) => RFind(m, s, 0, StringComparison.OrdinalIgnoreCase);
        public static int ReverseFindNoCase(string m, CrestronString s, int start) => RFind(m, s, start, StringComparison.OrdinalIgnoreCase);
        public static int ReverseFindNoCase(CrestronString m, CrestronString s) => RFind(S(m), s, 0, StringComparison.OrdinalIgnoreCase);
        public static int ReverseFindNoCase(CrestronString m, CrestronString s, int start) => RFind(S(m), s, start, StringComparison.OrdinalIgnoreCase);

        private static int Cmp(string a, string b, StringComparison c) => Math.Sign(string.Compare(a ?? "", b ?? "", c));
        public static int CompareStrings(CrestronString a, CrestronString b) => Cmp(S(a), S(b), StringComparison.Ordinal);
        public static int CompareStrings(string a, CrestronString b) => Cmp(a, S(b), StringComparison.Ordinal);
        public static int CompareStrings(CrestronString a, string b) => Cmp(S(a), b, StringComparison.Ordinal);
        public static int CompareStrings(string a, string b) => Cmp(a, b, StringComparison.Ordinal);
        public static int CompareStringsNoCase(CrestronString a, CrestronString b) => Cmp(S(a), S(b), StringComparison.OrdinalIgnoreCase);
        public static int CompareStringsNoCase(string a, CrestronString b) => Cmp(a, S(b), StringComparison.OrdinalIgnoreCase);
        public static int CompareStringsNoCase(CrestronString a, string b) => Cmp(S(a), b, StringComparison.OrdinalIgnoreCase);
        public static int CompareStringsNoCase(string a, string b) => Cmp(a, b, StringComparison.OrdinalIgnoreCase);

        public static CrestronString ToASCII(string s) => new CrestronString(s);
        public static CrestronString ToASCII(CrestronString s) => new CrestronString(S(s));
        public static CrestronString ToUTF16(string s) => new CrestronString(s);
        public static CrestronString ToUTF16(CrestronString s) => new CrestronString(S(s));
        public static CrestronStringEncoding GetEncoding(CrestronString s) => s?.Encoding ?? CrestronStringEncoding.eEncodingASCII;
        public static void SetEncoding(CrestronString s, CrestronStringEncoding e) { if (s is not null) s.Encoding = e; }

        /// <summary>GetC: removes and returns the first character (0 if empty).</summary>
        public static ushort GetC(CrestronString s)
        {
            var v = S(s);
            if (v.Length == 0) return 0;
            s.UpdateValue(v.Substring(1));
            return (ushort)(v[0] & 0xFF);
        }

        // Gather with a destination string: returns the length gathered (or -1 on timeout).
        public static CrestronString Gather(CrestronString delimiter, StringInput buffer) => Gather(S(delimiter), buffer, -1);
        public static CrestronString Gather(CrestronString delimiter, StringInput buffer, int timeout) => Gather(S(delimiter), buffer, timeout);
        public static int Gather(CrestronString delimiter, StringInput buffer, CrestronString dest) => Gather(S(delimiter), buffer, dest, -1);
        public static int Gather(CrestronString delimiter, StringInput buffer, CrestronString dest, int timeout) => Gather(S(delimiter), buffer, dest, timeout);
        public static int Gather(string delimiter, StringInput buffer, CrestronString dest) => Gather(delimiter, buffer, dest, -1);
        public static int Gather(string delimiter, StringInput buffer, CrestronString dest, int timeout)
        {
            if (buffer?.Owner == null) return -1;
            buffer.Owner.WaitForBuffer(() => Find(delimiter, buffer) > 0, timeout < 0 ? -1 : timeout * 10);
            if (Find(delimiter, buffer) == 0) return -1;
            var r = Remove(delimiter, buffer);
            dest?.UpdateValue(r);
            return r.Len();
        }
        public static int Gather(int length, StringInput buffer, CrestronString dest) => Gather(length, buffer, dest, -1);
        public static int Gather(int length, StringInput buffer, CrestronString dest, int timeout)
        {
            if (buffer?.Owner == null) return -1;
            buffer.Owner.WaitForBuffer(() => S(buffer).Length >= length, timeout < 0 ? -1 : timeout * 10);
            if (S(buffer).Length < length) return -1;
            var r = Remove(length, buffer);
            dest?.UpdateValue(r);
            return r.Len();
        }

        public static int GatherAsync(CrestronString d, BufferInput b, SmartThreadSPLUSGLobalEventHandler h) => b?.GatherAsync(S(d), h, -1) ?? -1;
        public static int GatherAsync(CrestronString d, BufferInput b, SmartThreadSPLUSGLobalEventHandler h, int t) => b?.GatherAsync(S(d), h, t) ?? -1;
        public static int GatherAsync(string d, BufferInput b, SmartThreadSPLUSGLobalEventHandler h) => b?.GatherAsync(d, h, -1) ?? -1;
        public static int GatherAsync(string d, BufferInput b, SmartThreadSPLUSGLobalEventHandler h, int t) => b?.GatherAsync(d, h, t) ?? -1;
        public static int GatherAsync(int n, BufferInput b, SmartThreadSPLUSGLobalEventHandler h) => b?.GatherAsync(n, h, -1) ?? -1;
        public static int GatherAsync(int n, BufferInput b, SmartThreadSPLUSGLobalEventHandler h, int t) => b?.GatherAsync(n, h, t) ?? -1;
        public static int RemoveGatherAsync(BufferInput b) => b?.RemoveGather() ?? -1;
        public static int RearmGatherAsync(BufferInput b) => b?.RearmGather() ?? -1;

        // ---------------- arrays

        public static ushort GetLastModifiedArrayIndex(SplusObject owner) => (ushort)(owner?.LastModifiedArrayIndex ?? 0);
        public static uint GetNumArrayCols<T>(T[,] a) => a == null ? 0 : (uint)Math.Max(0, a.GetLength(1) - 1);
        public static uint GetNumArrayRows<T>(T[,] a) => a == null ? 0 : (uint)Math.Max(0, a.GetLength(0) - 1);
        public static uint GetNumArrayCols<T>(T[] a) => a == null ? 0 : (uint)Math.Max(0, a.Length - 1);
        public static uint GetNumArrayRows<T>(T[] a) => a == null ? 0 : (uint)Math.Max(0, a.Length - 1);
        public static uint GetNumArrayCols(CrestronString[] a) => a == null || a.Length == 0 || a[0] is null ? 0 : (uint)a[0].MaxLength;
        public static uint GetNumArrayRows(CrestronString[] a) => a == null ? 0 : (uint)Math.Max(0, a.Length - 1);
        public static uint GetNumArrayCols(CrestronString s) => (uint)(s?.MaxLength ?? 0);
        public static uint GetNumArrayCols<T>(InOutArray<T> a) => (uint)(a?.Count ?? 0);
        public static uint GetNumArrayRows<T>(InOutArray<T> a) => (uint)(a?.Count ?? 0);

        public static int ResizeArray<T>(ref T[] a, int size, SplusStructureBase s) { Array.Resize(ref a, size + 1); return 0; }
        public static int ResizeArray<T>(ref T[,] a, int rows, int cols, SplusStructureBase s)
        {
            var n = new T[rows + 1, cols + 1];
            if (a != null)
                for (int i = 0; i < Math.Min(a.GetLength(0), rows + 1); i++)
                    for (int j = 0; j < Math.Min(a.GetLength(1), cols + 1); j++) n[i, j] = a[i, j];
            a = n;
            return 0;
        }
        public static int ResizeArray(ref CrestronString[] a, int size, int length, SplusStructureBase s)
        {
            var old = a ?? Array.Empty<CrestronString>();
            var n = new CrestronString[size + 1];
            for (int i = 0; i < n.Length; i++)
                n[i] = new CrestronString(i < old.Length ? old[i]?.Str : "", length, old.FirstOrDefault(x => x is not null)?.Owner);
            a = n;
            return 0;
        }
        public static int ResizeString(ref CrestronString s, int length, SplusStructureBase owner)
        {
            s = new CrestronString(s?.Str, length, s?.Owner);
            return 0;
        }

        public static void SetArray<T>(ref T[] a, T value) { if (a != null) for (int i = 0; i < a.Length; i++) a[i] = value; }
        public static void SetArray<T>(ref T[,] a, T value) { if (a != null) for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) a[i, j] = value; }
        public static void SetArray(CrestronString[] a, string value) { foreach (var s in a ?? Array.Empty<CrestronString>()) s?.UpdateValue(value); }
        public static void SetArray(InOutArray<StringInput> a, string value) { for (int i = 1; i <= (a?.Count ?? 0); i++) a[i]?.UpdateValue(value); }
        public static void SetArray(InOutArray<BufferInput> a, string value) { for (int i = 1; i <= (a?.Count ?? 0); i++) a[i]?.UpdateValue(value); }
        public static void SetArray(InOutArray<AnalogOutput> a, ushort value) { for (int i = 1; i <= (a?.Count ?? 0); i++) if (a[i] != null) a[i].Value = value; }

        // ---------------- time

        public static int GetDst() => TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now) ? 1 : 0;
        public static int GetGmtOffset() => (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
        public static int SetGmtOffset(int minutes) => 0;
        public static int GetHSeconds() => DateTime.Now.Millisecond / 10;
        public static CrestronString Month() => new CrestronString(CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(DateTime.Now.Month));
        public static void SetClock(int h, int m, int s) { }
        public static void SetDate(int month, int day, int year) { }

        // ---------------- file info

        public static DateTime ConvertFileInfoToDateTime(FILE_INFO f) => f.Modified;
        public static string FileDate(FILE_INFO f) => FileDate(f, 0);
        public static string FileDate(FILE_INFO f, ushort format) => Date(format).Str.Length > 0 ? f.Modified.ToString(format switch
        {
            1 => "dd/MM/yyyy", 2 => "yyyy/MM/dd", 3 => "dd.MM.yyyy", 4 => "yyyy-MM-dd", _ => "MM/dd/yyyy"
        }, CultureInfo.InvariantCulture) : "";
        public static string FileDay(FILE_INFO f) => f.Modified.DayOfWeek.ToString();
        public static string FileMonth(FILE_INFO f) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(f.Modified.Month);
        public static string FileTime(FILE_INFO f) => f.Modified.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        public static short FileGetDateNum(FILE_INFO f) => (short)f.Modified.Day;
        public static short FileGetDayOfWeekNum(FILE_INFO f) => (short)f.Modified.DayOfWeek;
        public static short FileGetHourNum(FILE_INFO f) => (short)f.Modified.Hour;
        public static int FileGetHSeconds(FILE_INFO f) => f.Modified.Millisecond / 10;
        public static short FileGetMinutesNum(FILE_INFO f) => (short)f.Modified.Minute;
        public static short FileGetMonthNum(FILE_INFO f) => (short)f.Modified.Month;
        public static short FileGetSecondsNum(FILE_INFO f) => (short)f.Modified.Second;
        public static short FileGetTicks(FILE_INFO f) => (short)(f.Modified.Millisecond / 10);
        public static short FileGetYearNum(FILE_INFO f) => (short)f.Modified.Year;
        public static int FileLength(FILE_INFO f) => (int)f.lSize;
        public static ushort IsDirectory(FILE_INFO f) => (ushort)((f.iAttributes & 0x10) != 0 ? 1 : 0);
        public static ushort IsHidden(FILE_INFO f) => (ushort)((f.iAttributes & 0x02) != 0 ? 1 : 0);
        public static ushort IsReadonly(FILE_INFO f) => (ushort)((f.iAttributes & 0x01) != 0 ? 1 : 0);
        public static ushort IsSystem(FILE_INFO f) => (ushort)((f.iAttributes & 0x04) != 0 ? 1 : 0);
        public static ushort IsVolume(FILE_INFO f) => (ushort)((f.iAttributes & 0x08) != 0 ? 1 : 0);

        // ---------------- sockets

        public static short SocketConnectClient(SplusTcpClient socket, CrestronString address, ushort port, ushort reconnect)
        {
            if (socket != null) { socket.RequestedAddress = S(address); socket.PortNumber = port; }
            return SocketConnectClient(socket, S(address), port, reconnect);
        }

        public static short SocketUDP_Enable(SplusUdpSocket socket, CrestronString address, ushort port) => SocketUDP_Enable(socket, S(address), port);

        public static short SocketServerStartListen(SplusTcpServer socket, CrestronString address, ushort port) => SocketServerStartListen(socket, S(address), port);

        /// <summary>A TCP server accepts one client at a time, as in SIMPL+.</summary>
        public static short SocketServerStartListen(SplusTcpServer socket, string address, ushort port)
        {
            if (socket == null) return -1;
            try
            {
                socket.Listener?.Stop();
                var ip = string.IsNullOrEmpty(address) || address == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(address);
                socket.Listener = new TcpListener(ip, port);
                socket.Listener.Start();
                socket.PortNumber = port;
                socket.SocketStatus = 1;
                socket.Rec?.Close("stopped");
                socket.Rec = socket.Owner?.Slot?.Io.Sockets.Open("SIMPL+ TCP server", "server", "tcp", $"{(string.IsNullOrEmpty(address) ? "0.0.0.0" : address)}:{port}", "");
                socket.Rec?.Connected();
                socket.Raise(socket.OnSocketStatus);
                _ = AcceptLoop(socket, socket.Listener);
                return 0;
            }
            catch (Exception e) { socket.Owner?.Log("Warning", $"SIMPL+ listen on {port}: {e.Message}"); return -1; }
        }

        private static async Task AcceptLoop(SplusTcpServer socket, TcpListener listener)
        {
            while (socket.Listener == listener && socket.Owner?.Stopped != true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch { return; }
                if (socket.Client?.Connected == true) { client.Dispose(); continue; }
                socket.Client = client;
                socket.ClientRec = socket.Owner?.Slot?.Io.Sockets.Open("SIMPL+ TCP server connection", "server", "tcp", client.Client.LocalEndPoint?.ToString() ?? "", client.Client.RemoteEndPoint?.ToString() ?? "");
                socket.ClientRec?.Connected();
                socket.RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                socket.SocketStatus = 2;
                socket.Raise(socket.OnSocketConnect);
                socket.Raise(socket.OnSocketStatus);
                await ReceiveServer(socket, client);
                if (socket.Listener == listener) { socket.SocketStatus = 1; }
            }
        }

        private static async Task ReceiveServer(SplusTcpServer socket, TcpClient client)
        {
            var buf = new byte[4096];
            try
            {
                var stream = client.GetStream();
                while (socket.Owner?.Stopped != true)
                {
                    var n = await stream.ReadAsync(buf, 0, buf.Length);
                    if (n == 0) break;
                    socket.ClientRec?.Received(buf, 0, n);
                    socket.SocketRxBuf.UpdateValue(S(socket.SocketRxBuf) + Encoding.Latin1.GetString(buf, 0, n));
                    socket.Raise(socket.OnSocketReceive);
                }
            }
            catch { }
            socket.ClientRec?.Close("closed by remote");
            client.Dispose();
            if (socket.Client == client) socket.Client = null;
            socket.Raise(socket.OnSocketDisconnect);
            socket.Raise(socket.OnSocketStatus);
        }

        public static short SocketServerStopListen(SplusTcpServer socket)
        {
            if (socket == null) return -1;
            socket.Listener?.Stop(); socket.Listener = null;
            socket.Client?.Dispose(); socket.Client = null;
            socket.ClientRec?.Close("closed"); socket.Rec?.Close("stopped");
            socket.SocketStatus = 0;
            socket.Raise(socket.OnSocketStatus);
            return 0;
        }

        public static short SocketSend(SplusTcpServer s, CrestronString d) => SocketSend((SocketInfo)s, d);
        public static short SocketSend(SplusTcpServer s, string d) => SocketSend((SocketInfo)s, new CrestronString(d));
        public static short SocketSend(SplusTcpClient s, CrestronString d) => SocketSend((SocketInfo)s, d);
        public static short SocketSend(SplusTcpClient s, string d) => SocketSend((SocketInfo)s, new CrestronString(d));
        public static short SocketSend(SplusUdpSocket s, CrestronString d) => SocketSend((SocketInfo)s, d);
        public static short SocketSend(SplusUdpSocket s, string d) => SocketSend((SocketInfo)s, new CrestronString(d));

        public static short SocketGetStatus(SplusTcpClient s) => s?.SocketStatus ?? 0;
        public static short SocketGetStatus(SplusTcpServer s) => s?.SocketStatus ?? 0;
        public static int SocketGetPortNumber(SplusTcpClient s) => s?.PortNumber ?? 0;
        public static int SocketGetPortNumber(SplusTcpServer s) => s?.PortNumber ?? 0;
        public static int SocketGetPortNumber(SplusUdpSocket s) => s?.PortNumber ?? 0;

        private static short Put(ref CrestronString dest, string value) { if (dest is null) dest = new CrestronString(); dest.UpdateValue(value); return value.Length == 0 ? (short)-1 : (short)0; }
        private static short Put(ref string dest, string value) { dest = value; return value.Length == 0 ? (short)-1 : (short)0; }
        private static string Remote(SplusTcpServer s) => (s?.Client?.Client?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
        private static string Remote(SplusTcpClient s) => (s?.Client?.Client?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
        public static short SocketGetRemoteIPAddress(SplusTcpServer s, ref CrestronString a) => Put(ref a, Remote(s));
        public static short SocketGetRemoteIPAddress(SplusTcpServer s, ref string a) => Put(ref a, Remote(s));
        public static short SocketGetRemoteIPAddress(SplusTcpClient s, ref CrestronString a) => Put(ref a, Remote(s));
        public static short SocketGetRemoteIPAddress(SplusTcpClient s, ref string a) => Put(ref a, Remote(s));
        public static short SocketGetAddressAsRequested(SplusTcpServer s, ref CrestronString a) => Put(ref a, s?.RequestedAddress ?? "");
        public static short SocketGetAddressAsRequested(SplusTcpServer s, ref string a) => Put(ref a, s?.RequestedAddress ?? "");
        public static short SocketGetAddressAsRequested(SplusTcpClient s, ref CrestronString a) => Put(ref a, s?.RequestedAddress ?? "");
        public static short SocketGetAddressAsRequested(SplusTcpClient s, ref string a) => Put(ref a, s?.RequestedAddress ?? "");
        public static short SocketGetSenderIPAddress(SplusUdpSocket s, ref CrestronString a) => Put(ref a, s?.LastSender?.Address.ToString() ?? "");
        public static short SocketGetSenderIPAddress(SplusUdpSocket s, ref string a) => Put(ref a, s?.LastSender?.Address.ToString() ?? "");
        public static short SocketIsBroadcast(SplusUdpSocket s) => (short)(s?.Remote?.Address.Equals(IPAddress.Broadcast) == true ? 1 : 0);
        public static short SocketIsMulticast(SplusUdpSocket s) => (short)(s?.Remote != null && s.Remote.Address.GetAddressBytes()[0] is >= 224 and <= 239 ? 1 : 0);
    }
}
