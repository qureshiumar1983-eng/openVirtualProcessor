// Performance harness for OpenVP (docs/performance-plan.md). Usage: OpenVP.Bench [scenario|all] [--quick]
// Builds synthetic compiled programs (.lpz) and drives them through a real Processor/slot.
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OpenVP.Core;
using OpenVP.Lpz;

// --quick shrinks every scenario about 10x so CI finishes in seconds.
var quick = args.Contains("--quick");
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
// Temp data goes under the system temp dir, not next to the binaries.
var data = Path.Combine(Path.GetTempPath(), "openvp-bench-" + Environment.ProcessId);
if (Directory.Exists(data)) Directory.Delete(data, true);
Directory.CreateDirectory(data);
AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(data, true); } catch { } };
var only = positional.Length > 0 ? positional[0] : "all";
int Q(int n) => quick ? Math.Max(1, n / 10) : n;
bool Run(string name) => only == "all" || only == name;

Console.WriteLine($".NET {Environment.Version}, {Environment.ProcessorCount} cores, ServerGC={System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine();

// ------------------------------------------------------------ micro: per-change fixed costs
if (Run("micro"))
{
    using var p = new Processor(Path.Combine(data, "micro"), 1);
    var slot = p.Slot(1);
    Micro("DateTimeOffset.Now", Q(2_000_000), () => { _ = DateTimeOffset.Now; });
    Micro("DateTime.UtcNow", Q(2_000_000), () => { _ = DateTime.UtcNow; });
    Micro("Stopwatch.GetTimestamp", Q(2_000_000), () => { _ = Stopwatch.GetTimestamp(); });
    object t = true;
    Micro("SigValue.Coerce(Digital, bool)", Q(2_000_000), () => { _ = SigValue.Coerce(SigKind.Digital, t); });
    Micro("SigValue.AreEqual(bool, bool)", Q(2_000_000), () => { _ = SigValue.AreEqual(t, false); });
    Micro("slot.Trace(), no debugger open", Q(1_000_000), () => slot.Trace("Room_Power_On", SigKind.Digital, t, "S-1.2"));
    Micro("$\"IPID{id:X2}.in.{key}\" (SIMPL# trace name)", Q(1_000_000), () => { _ = $"IPID{3u:X2}.in.{new JoinKey(SigKind.Digital, 10)}"; });
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var e = new TraceEvent(DateTimeOffset.Now, 1, "Room_Power_On", SigKind.Digital, true, "S-1.2");
    var subQ = Channel.CreateBounded<object>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    Micro("Debugger subscriber, new: enqueue the raw TraceEvent", Q(1_000_000), () => subQ.Writer.TryWrite(e));
    Micro("JSON-serialize one trace event (old subscriber, on slot thread)", Q(500_000), () => { _ = JsonSerializer.Serialize(new { t = "trace", time = e.Time, slot = e.Slot, signal = e.Signal, kind = e.Kind.ToString(), value = e.Value, source = e.Source }, json); });
    var dev = new JoinDevice(1, 3, "panel", "x");
    var k = new JoinKey(SigKind.Digital, 10);
    bool flip = false;
    Micro("JoinDevice.SetFeedback (changing)", Q(1_000_000), () => { flip = !flip; dev.SetFeedback(k, flip); });
    Micro("JoinDevice.SetFeedback (same value)", Q(1_000_000), () => dev.SetFeedback(k, flip));
    Micro("Dispatcher.Post (enqueue only, cross-thread)", Q(1_000_000), () => slot.Dispatcher.Post(static () => { }));
    slot.Dispatcher.Invoke(() => { });
    Console.WriteLine();
}

// ------------------------------------------------------------ LPZ throughput: deep chain
if (Run("chain"))
{
    int N = Q(1000), K = Q(2000);
    var path = ChainProgram(data, "chain", N);
    Throughput($"LPZ NOT-chain, depth {N} ({K} presses)", path, K, (long)K * (N + 1), debugger: false);
    Throughput("  same, debugger window open (server's /ws/trace subscriber)", path, K, (long)K * (N + 1), debugger: true);
    Console.WriteLine();
}

// ------------------------------------------------------------ debugger subscriber cost on the slot thread
if (Run("debugger"))
{
    int N = Q(1000), K = Q(2000);
    var path = ChainProgram(data, "chain-dbg", N);
    long changes = (long)K * (N + 1);
    Throughput($"LPZ NOT-chain, depth {N}, no debugger", path, K, changes, debugger: false);
    Throughput("  debugger open, OLD server: JSON on the slot thread, frame per event", path, K, changes, debugger: true, oldServer: true);
    Throughput("  debugger open, NEW server: raw event queued, JSON batches in the send loop", path, K, changes, debugger: true);
    Console.WriteLine();
}

// ------------------------------------------------------------ LPZ throughput: wide fan-out to panel feedback
if (Run("fanout"))
{
    int F = Q(1000), K = Q(2000);
    var path = FanoutProgram(data, "fanout", F);
    Throughput($"LPZ fan-out 1→{F} ANDs → {F} panel feedback joins ({K} presses)", path, K, (long)K * (F + 1), debugger: false);
    Console.WriteLine();
}

// ------------------------------------------------------------ LPZ: interlock with many outputs (mostly no-op sets)
if (Run("interlock"))
{
    int W = Q(200), K = Q(4000);
    var path = InterlockProgram(data, "interlock", W);
    using var p = Start(path, out var panel);
    var slot = p.Slot(1);
    Warm(slot, panel, 200, i => (new JoinKey(SigKind.Digital, (uint)(i % W + 1)), i % 2 == 0));
    var (sec, bytes, gcs) = Measure(() =>
    {
        for (int i = 0; i < K; i++)
        {
            var key = new JoinKey(SigKind.Digital, (uint)(i % W + 1));
            panel.ReceiveInput(key, true);
            panel.ReceiveInput(key, false);
        }
        slot.Dispatcher.Invoke(() => { });
    });
    long sets = (long)K * W; // every rising press sets all W outputs
    long changes = K * 2L + K * 2L; // press up/down + one output up, one down
    Console.WriteLine($"{$"LPZ interlock, {W} outputs ({K} presses)",-74} {sets / sec,12:N0} output sets/s   {changes / sec,10:N0} changes/s   {bytes / (double)sets,7:N1} B/set   gen0 GCs {gcs}");
    Console.WriteLine();
}

// ------------------------------------------------------------ LPZ: wide AND/OR/NOR gates (every input change re-evaluates the gate)
if (Run("wide"))
{
    int W = 64, G = Q(48), K = Q(40_000);
    var path = WideGatesProgram(data, "wide", W, G);
    using var p = Start(path, out var panel);
    var slot = p.Slot(1);
    Warm(slot, panel, 200, i => (new JoinKey(SigKind.Digital, (uint)(i % W + 1)), i % 2 == 0));
    var (sec, bytes, gcs) = Measure(() =>
    {
        // Press all W inputs high one by one, then release them: every press re-runs all G gates.
        for (int i = 0; i < K; i++)
            panel.ReceiveInput(new JoinKey(SigKind.Digital, (uint)(i % W + 1)), (i / W) % 2 == 0);
        slot.Dispatcher.Invoke(() => { });
    });
    long evals = (long)K * G;
    Console.WriteLine($"{$"LPZ {G} gates (AND/OR/NOR) of {W} inputs each ({K} presses)",-74} {evals / sec,12:N0} gate evals/s   {sec * 1e9 / evals,6:N1} ns/eval   {bytes / (double)K,6:N1} B/press   gen0 GCs {gcs}");
    Console.WriteLine();
}

// ------------------------------------------------------------ latency: panel press → feedback
if (Run("latency"))
{
    var path = ChainProgram(data, "lat", 1);
    using var p = Start(path, out var panel);
    var key = new JoinKey(SigKind.Digital, 1);
    var got = new ManualResetEventSlim();
    long t1 = 0;
    panel.FeedbackChanged += (_, k, _) => { if (k == key) { t1 = Stopwatch.GetTimestamp(); got.Set(); } };
    bool v = false;
    double[] Sample(int n, bool idle)
    {
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (idle) Thread.Sleep(1);
            got.Reset();
            v = !v;
            var t0 = Stopwatch.GetTimestamp();
            panel.ReceiveInput(key, v);
            got.Wait();
            r[i] = (t1 - t0) * 1e6 / Stopwatch.Frequency;
        }
        return r;
    }
    Sample(Q(2000), false);
    Report("LPZ press→feedback, back-to-back (µs)", Sample(Q(20000), false));
    Report("LPZ press→feedback, slot idle 1 ms between presses (µs)", Sample(Q(2000), true));
    Console.WriteLine();
}

// ------------------------------------------------------------ SIMPL+ round trip
if (Run("splus"))
{
    var path = SplusProgram(data, "splus");
    if (path == null) Console.WriteLine("SIMPL+ sample not built; skipped");
    else
    {
        using var p = Start(path, out var panel, ipid: 4);
        var fb = new JoinKey(SigKind.Analog, 1);
        var got = new ManualResetEventSlim();
        long t1 = 0; ushort want = 0;
        panel.FeedbackChanged += (_, k, val) => { if (k == fb && val is ushort u && u == want) { t1 = Stopwatch.GetTimestamp(); got.Set(); } };
        Thread.Sleep(2500); // modules mark themselves started after up to 2 s
        double[] Sample(int n, bool idle)
        {
            var r = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (idle) Thread.Sleep(1);
                got.Reset();
                var x = (ushort)(1 + i % 1000);
                want = (ushort)(x * 2);
                var t0 = Stopwatch.GetTimestamp();
                panel.ReceiveInput(new JoinKey(SigKind.Analog, 1), x);
                if (!got.Wait(2000)) { Console.WriteLine("  timeout"); break; }
                r[i] = (t1 - t0) * 1e6 / Stopwatch.Frequency;
            }
            return r;
        }
        Sample(Q(500), false);
        Report("SIMPL+ CHANGE round trip (panel→S+→panel), back-to-back (µs)", Sample(Q(5000), false));
        Report("SIMPL+ CHANGE round trip, slot idle 1 ms between (µs)", Sample(Q(1000), true));

        // Burst: 1000 changes posted at once; does the last value win (event order)?
        int mismatches = 0, rounds = quick ? 3 : 20;
        for (int round = 0; round < rounds; round++)
        {
            for (ushort x = 1; x <= 1000; x++) panel.ReceiveInput(new JoinKey(SigKind.Analog, 1), (ushort)(x + round * 1000 % 30000));
            Thread.Sleep(300);
            p.Slot(1).Dispatcher.Invoke(() => { });
            var last = (ushort)(1000 + round * 1000 % 30000);
            if (panel.GetFeedback(fb) is ushort u && u != (ushort)(last * 2)) mismatches++;
        }
        Console.WriteLine($"SIMPL+ burst of 1000 CHANGEs: final output wrong (out-of-order events) in {mismatches}/{rounds} rounds");
        Console.WriteLine();
    }
}

// ------------------------------------------------------------ timers: does the runtime keep every timer it ever made?
if (Run("timers"))
{
    var path = RetriggerProgram(data, "timers");
    using var p = Start(path, out var panel);
    var slot = p.Slot(1);
    var rt = slot.Runtime!;
    var field = rt.GetType().GetField("_timers", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var timerSet = field.GetValue(rt)!;
    int Live() => (int)timerSet.GetType().GetProperty("Count")!.GetValue(timerSet)!;
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var before = GC.GetTotalMemory(true);
    var key = new JoinKey(SigKind.Digital, 1);
    for (int i = 0; i < Q(100_000); i++) { panel.ReceiveInput(key, true); panel.ReceiveInput(key, false); }
    slot.Dispatcher.Invoke(() => { });
    var after = GC.GetTotalMemory(true);
    Console.WriteLine($"Retriggerable one-shot pressed {Q(100_000):N0} times: runtime holds {Live():N0} timers, heap +{(after - before) / 1048576.0:N1} MB (a retriggered timer should release its predecessor)");
    Console.WriteLine();
}

// ------------------------------------------------------------ load time
if (Run("load"))
{
    foreach (var n in new[] { Q(5_000), Q(50_000) })
    {
        var path = BigProgram(data, $"big{n}", n);
        using var p = new Processor(Path.Combine(data, $"bigp{n}"), 1);
        p.AddLoader(new LpzLoader());
        GC.Collect();
        var mem0 = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        p.LoadProgram(1, path);
        sw.Stop();
        var mem1 = GC.GetTotalMemory(true);
        Console.WriteLine($"Load + start a {n:N0}-symbol .lpz: {sw.ElapsedMilliseconds,6:N0} ms, live heap +{(mem1 - mem0) / 1048576.0:N1} MB, state {p.Slot(1).State}");
    }
    Console.WriteLine();
}

// ============================================================ helpers

static void Micro(string name, int n, Action a)
{
    for (int i = 0; i < n / 10; i++) a();
    var b0 = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < n; i++) a();
    sw.Stop();
    var b1 = GC.GetAllocatedBytesForCurrentThread();
    Console.WriteLine($"  {name,-58} {sw.Elapsed.TotalNanoseconds / n,8:N1} ns   {(b1 - b0) / (double)n,6:N1} B");
}

static (double sec, long bytes, int gen0) Measure(Action a)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var g0 = GC.CollectionCount(0);
    var b0 = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();
    a();
    sw.Stop();
    var b1 = GC.GetTotalAllocatedBytes(true);
    return (sw.Elapsed.TotalSeconds, b1 - b0, GC.CollectionCount(0) - g0);
}

static void Warm(ProgramSlot slot, JoinDevice panel, int n, Func<int, (JoinKey, bool)> f)
{
    for (int i = 0; i < n; i++) { var (k, v) = f(i); panel.ReceiveInput(k, v); }
    slot.Dispatcher.Invoke(() => { });
}

void Throughput(string name, string path, int presses, long changes, bool debugger, bool oldServer = false)
{
    using var p = Start(path, out var panel);
    var slot = p.Slot(1);
    var key = new JoinKey(SigKind.Digital, 1);
    Task? drain = null;
    var cts = new CancellationTokenSource();
    if (debugger && oldServer)
    {
        // The pre-2.2 server: serialize each change to a string on the slot thread.
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });
        slot.Traced += e => outbox.Writer.TryWrite(JsonSerializer.Serialize(new { t = "trace", time = e.Time, slot = e.Slot, signal = e.Signal, kind = e.Kind.ToString(), value = e.Value, source = e.Source }, json));
        drain = Task.Run(async () =>
        {
            try { while (await outbox.Reader.WaitToReadAsync(cts.Token)) while (outbox.Reader.TryRead(out var m)) _ = Encoding.UTF8.GetBytes(m); }
            catch (OperationCanceledException) { }
        });
    }
    else if (debugger)
    {
        // What /ws/trace does now: the slot thread only enqueues the raw event. The send loop writes up to
        // 200 events per frame as one JSON array (same shape as OpenVpServer.WriteWsEvent).
        var outbox = Channel.CreateBounded<object>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        slot.Traced += e => outbox.Writer.TryWrite(e);
        drain = Task.Run(async () =>
        {
            var frame = new System.Buffers.ArrayBufferWriter<byte>(16 * 1024);
            using var w = new Utf8JsonWriter(frame);
            try
            {
                while (await outbox.Reader.WaitToReadAsync(cts.Token))
                    while (outbox.Reader.TryPeek(out _))
                    {
                        frame.Clear(); w.Reset(frame); w.WriteStartArray();
                        for (var n = 0; n < 200 && outbox.Reader.TryRead(out var item); n++)
                        {
                            var e = (TraceEvent)item;
                            w.WriteStartObject();
                            w.WriteString("t", "trace"); w.WriteString("time", e.Time); w.WriteNumber("slot", e.Slot);
                            w.WriteString("signal", e.Signal); w.WriteString("kind", e.Kind.ToString());
                            w.WritePropertyName("value");
                            if (e.Value is bool bv) w.WriteBooleanValue(bv); else JsonSerializer.Serialize(w, e.Value);
                            w.WriteString("source", e.Source);
                            w.WriteEndObject();
                        }
                        w.WriteEndArray(); w.Flush();
                    }
            }
            catch (OperationCanceledException) { }
        });
    }
    Warm(slot, panel, 100, i => (key, i % 2 == 0));
    var (sec, bytes, gcs) = Measure(() =>
    {
        for (int i = 0; i < presses; i++) panel.ReceiveInput(key, i % 2 == 0);
        slot.Dispatcher.Invoke(() => { });
    });
    cts.Cancel();
    Console.WriteLine($"{name,-74} {changes / sec,12:N0} changes/s   {sec * 1e9 / changes,6:N0} ns/change   {bytes / (double)changes,6:N1} B/change   gen0 GCs {gcs}");
}

static void Report(string name, double[] us)
{
    var s = us.Where(x => x > 0).OrderBy(x => x).ToArray();
    double P(double q) => s[Math.Min(s.Length - 1, (int)(q * s.Length))];
    Console.WriteLine($"{name,-74} p50 {P(0.50),7:N1}   p99 {P(0.99),7:N1}   max {s[^1],8:N1}");
}

Processor Start(string path, out JoinDevice panel, uint ipid = 3)
{
    var p = new Processor(Path.Combine(data, "p" + Guid.NewGuid().ToString("N")[..8]), 1);
    p.AddLoader(new LpzLoader());
    p.LoadProgram(1, path);
    if (p.Slot(1).State != SlotState.Running) throw new Exception(p.Slot(1).LastError);
    panel = p.Slot(1).FindDevice(ipid)!;
    return p;
}

// ------------------------------------------------------------ synthetic programs

static byte[] Field(int tag, byte[] value)
{
    var f = new byte[4 + value.Length];
    BitConverter.GetBytes((ushort)(value.Length + 2)).CopyTo(f, 0);
    BitConverter.GetBytes((ushort)tag).CopyTo(f, 2);
    value.CopyTo(f, 4);
    return f;
}
static byte[] Int(int tag, int v) => Field(tag, BitConverter.GetBytes(v));
static byte[] Str(int tag, string v) => Field(tag, Encoding.Unicode.GetBytes(v));
static byte[] Record(int type, string path, int[] ins, int[] outs, params byte[][] extra)
{
    var fields = new List<byte>(Field(4, Encoding.Unicode.GetBytes(path)));
    foreach (var f in extra) fields.AddRange(f);
    var outsAt = 16 + fields.Count;
    var insAt = outsAt + outs.Length * 4;
    var size = insAt + ins.Length * 4;
    var r = new List<byte>();
    r.AddRange(BitConverter.GetBytes(size)); r.AddRange(BitConverter.GetBytes(type));
    r.AddRange(BitConverter.GetBytes(insAt)); r.AddRange(BitConverter.GetBytes(outsAt));
    r.AddRange(fields);
    foreach (var o in outs) r.AddRange(BitConverter.GetBytes(o));
    foreach (var i in ins) r.AddRange(BitConverter.GetBytes(i));
    return r.ToArray();
}
static byte[] Panel(int ipid, int[] ins, int[] outs) =>
    Record(-12, $"IP-ID-{ipid:X2}", ins, outs, Int(6, ipid), Int(10, ins.Length), Int(11, outs.Length), Int(12, ins.Length), Int(13, outs.Length));
static string Pack(string dir, string name, List<byte> bin, int ipid = 3, string? dll = null)
{
    var path = Path.Combine(dir, name + ".lpz");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    void Put(string n, byte[] c) { using var s = zip.CreateEntry(n).Open(); s.Write(c); }
    Put("boot.bt", Encoding.ASCII.GetBytes($"[LG_BOOT_DATA]\r\nBinFileName={name}.bin\r\nSMWFileName={name}.smw\r\nRackType=CP4\r\n"));
    Put(name + ".bin", bin.ToArray());
    Put(name + ".dip", Encoding.ASCII.GetBytes($"[IPTable]\r\nid0={ipid:X2}\r\naddr0=127.0.0.1\r\n"));
    if (dll != null) Put(Path.GetFileName(dll), File.ReadAllBytes(dll));
    return path;
}
static List<byte> Header(string name) => new(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, $"Program File: {name}.smw")));

// press d1 (sig 10) → NOT → NOT → … (depth n) → feedback d1
static string ChainProgram(string dir, string name, int n)
{
    var bin = Header(name);
    int first = 10, last = 10 + n;
    bin.AddRange(Panel(3, [last], [first]));
    for (int i = 0; i < n; i++) bin.AddRange(Record(1, $"S-{i + 1}", [first + i], [first + i + 1]));
    return Pack(dir, name, bin);
}
// press d1 → f ANDs (press AND constant 1) → f panel feedback joins
static string FanoutProgram(string dir, string name, int f)
{
    var bin = Header(name);
    var outs = Enumerable.Range(11, f).ToArray();
    bin.AddRange(Panel(3, outs, [10]));
    for (int i = 0; i < f; i++) bin.AddRange(Record(0, $"S-{i + 1}", [10, 1], [outs[i]]));
    return Pack(dir, name, bin);
}
// presses d1..dw → interlock (input 1 = clear, unused) → w feedback joins
static string InterlockProgram(string dir, string name, int w)
{
    var bin = Header(name);
    var presses = Enumerable.Range(10, w).ToArray();
    var fb = Enumerable.Range(10 + w, w).ToArray();
    bin.AddRange(Panel(3, fb, presses));
    bin.AddRange(Record(11, "S-1", [0, .. presses], fb));
    return Pack(dir, name, bin);
}
// press d1..dw → g gates (AND, OR and NOR in turn) that each read all w presses → g feedback joins
static string WideGatesProgram(string dir, string name, int w, int g)
{
    var bin = Header(name);
    var presses = Enumerable.Range(10, w).ToArray();
    var fb = Enumerable.Range(10 + w, g).ToArray();
    bin.AddRange(Panel(3, fb, presses));
    for (int i = 0; i < g; i++) bin.AddRange(Record(i % 3 == 0 ? 0 : i % 3 == 1 ? 2 : 3, $"S-{i + 1}", presses, [fb[i]]));
    return Pack(dir, name, bin);
}
// press d1 → retriggerable one-shot, 10 s
static string RetriggerProgram(string dir, string name)
{
    var bin = Header(name);
    bin.AddRange(Panel(3, [11], [10]));
    bin.AddRange(Record(17, "S-1", [10], [11], Int(15, 1), Int(2, 1000)));
    return Pack(dir, name, bin);
}
// n symbols: a mix of chains of NOT/AND/Interlock, 10 panels
static string BigProgram(string dir, string name, int n)
{
    var bin = Header(name);
    int sig = 10;
    for (int ip = 3; ip < 13; ip++) bin.AddRange(Panel(ip, Enumerable.Range(sig, 50).ToArray(), Enumerable.Range(sig + 50, 50).ToArray()));
    sig = 1000;
    for (int i = 0; i < n; i++)
    {
        switch (i % 3)
        {
            case 0: bin.AddRange(Record(1, $"S-{i}", [sig], [sig + 1])); sig += 1; break;
            case 1: bin.AddRange(Record(0, $"S-{i}", [sig, sig - 1, 1], [sig + 1])); sig += 1; break;
            default: bin.AddRange(Record(11, $"S-{i}", [0, sig, sig - 1, sig - 2], [sig + 1, sig + 2, sig + 3])); sig += 3; break;
        }
    }
    return Pack(dir, name, bin, 3);
}
static string? SplusProgram(string dir, string name)
{
    // Walk up from the binaries to the folder that holds OpenVP.sln.
    var repo = new DirectoryInfo(AppContext.BaseDirectory);
    while (repo != null && !File.Exists(Path.Combine(repo.FullName, "OpenVP.sln"))) repo = repo.Parent;
    if (repo == null) return null;
    var root = Path.Combine(repo.FullName, "samples", "SplusModule", "bin");
    if (!Directory.Exists(root)) return null;
    var dll = Directory.GetFiles(root, "Sample_Splus.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    if (dll == null) return null;
    const int Press = 10, Start = 11, Level = 12, Nm = 13, Busy = 20, Doubled = 21, Greeting = 22, Ticks = 23;
    var bin = Header(name);
    bin.AddRange(Record(-12, "IP-ID-04", [Busy, Doubled, Greeting, Ticks], [Press, Start, Level, Nm],
        Int(6, 4), Int(10, 1), Int(11, 2), Int(12, 2), Int(13, 3)));
    bin.AddRange(Record(-48, "S-1", [Press, Start, Level, Nm], [Busy, Doubled, Greeting, Ticks],
        Int(10, 2), Int(12, 4), Int(11, 1), Int(13, 4), Str(3, "Sample Module"), Str(3, "Sample_Splus.dll"), Int(2, 3)));
    return Pack(dir, name, bin, 4, dll);
}
