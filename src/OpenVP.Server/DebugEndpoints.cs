using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Server;

/// <summary>
/// The debugger's server side beyond the live stream: watches, monitor settings, never-changed signals, bookmarks,
/// saved traces and comparing them, export and import, "why is this signal this value?", program exceptions, the
/// support bundle and signal search across slots. The logic is in OpenVP.Core; these are thin wrappers.
/// </summary>
internal static class DebugEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, RouteGroupBuilder slotApi, Processor processor, ActivityLog activity,
        Func<ProgramSlot, object?> summary, Func<ProgramSlot, string> compat, Func<HttpContext, string> who)
    {
        var traces = new TraceStore(processor.DataDirectory);
        var bookmarks = new BookmarkStore(processor.DataDirectory);

        static IResult Bad(string msg) => Results.BadRequest(new { error = msg });
        static string? Str(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static bool? Bool(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        static int? Int(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        static double? Dbl(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        object MonitorDto(ProgramSlot s)
        {
            var m = s.Monitor;
            var st = m.Settings;
            return new
            {
                oscillation = st.Oscillation, maxChangesPerBurst = st.MaxChangesPerBurst, oscillationChanges = st.OscillationChanges,
                oscillationWindowMs = st.OscillationWindowMs, trackChanges = st.TrackChanges,
                listening = m.Listening,
                recentAlerts = m.Oscillation.Recent(DateTimeOffset.Now),
                alerts = m.Oscillation.Alerts.Take(20).Select(a => new { signal = a.Signal, kind = a.Kind, count = a.Count, time = a.Time, message = a.Message })
            };
        }

        // ------------------------------------------------------------------ watches

        object WatchesDto(ProgramSlot s) => new
        {
            watches = s.Monitor.WatchStatus().Select(w => new
            {
                id = w.Id, expression = w.Expression, pauseStep = w.PauseStep, enabled = w.Enabled, name = w.Name, error = w.Error, state = w.State,
                since = w.Since, hits = w.Hits, lastHit = w.LastHit, unknownSignals = w.Unknown
            }),
            hits = s.Monitor.Watches.RecentHits(50).Select(h => new { id = h.WatchId, expression = h.Expression, time = h.Time, message = h.Message })
        };

        slotApi.MapGet("/watches", (int n) => WatchesDto(processor.Slot(n)));
        slotApi.MapPut("/watches", (int n, JsonElement body) =>
        {
            var s = processor.Slot(n);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("watches", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Bad("Send {\"watches\": [{\"expression\": \"Room_On=1 && Proj_Power=0\", \"pauseStep\": false}]}.");
            var defs = arr.EnumerateArray().Select(w => new WatchDef(Str(w, "id") ?? "", Str(w, "expression") ?? "", Bool(w, "pauseStep") ?? false, Bool(w, "enabled") ?? true, Str(w, "name"))).ToList();
            try { s.Monitor.SetWatches(defs); }
            catch (ArgumentException e) { return Bad(e.Message); }
            return Results.Ok(WatchesDto(s));
        });
        // Check an expression without saving it.
        slotApi.MapPost("/watches/check", (int n, JsonElement body) =>
        {
            var s = processor.Slot(n);
            var text = Str(body, "expression") ?? "";
            if (!SignalExpression.TryParse(text, out var e, out var err)) return Results.Ok(new { ok = false, error = err });
            var names = s.Runtime?.GetSignals().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
            return Results.Ok(new { ok = true, forMs = e!.ForMs, signals = e.Signals, unknown = names == null ? Array.Empty<string>() : e.UnknownSignals(names.Contains) });
        });
        slotApi.MapDelete("/watches/hits", (int n) => { processor.Slot(n).Monitor.Watches.ClearHits(); return Results.Ok(); });

        // ------------------------------------------------------------------ monitor settings, alerts, never-changed

        slotApi.MapGet("/monitor", (int n) => MonitorDto(processor.Slot(n)));
        slotApi.MapPut("/monitor", (int n, JsonElement body) =>
        {
            var s = processor.Slot(n);
            var cur = s.Monitor.Settings;
            s.Monitor.SetSettings(cur with
            {
                Oscillation = Bool(body, "oscillation") ?? cur.Oscillation,
                TrackChanges = Bool(body, "trackChanges") ?? cur.TrackChanges,
                MaxChangesPerBurst = Int(body, "maxChangesPerBurst") ?? cur.MaxChangesPerBurst,
                OscillationChanges = Int(body, "oscillationChanges") ?? cur.OscillationChanges,
                OscillationWindowMs = Int(body, "oscillationWindowMs") ?? cur.OscillationWindowMs
            });
            return Results.Ok(MonitorDto(s));
        });
        slotApi.MapDelete("/monitor/alerts", (int n) => { processor.Slot(n).Monitor.Oscillation.Reset(); return Results.Ok(); });

        slotApi.MapGet("/never-changed", (int n) =>
        {
            var s = processor.Slot(n);
            var m = s.Monitor;
            var tracking = m.Settings.TrackChanges && s.Runtime != null;
            var all = s.Runtime?.GetSignals() ?? Array.Empty<SignalInfo>();
            var never = tracking ? m.Changes.NeverChanged(all) : Array.Empty<SignalInfo>();
            return Results.Ok(new
            {
                tracking, since = m.Changes.Since, total = all.Count, changed = tracking ? m.Changes.ChangedCount : 0, neverCount = never.Count,
                signals = never.Take(20000).Select(x => new { name = x.Name, kind = x.Kind.ToString(), value = x.Value }),
                truncated = never.Count > 20000
            });
        });
        slotApi.MapPost("/never-changed/reset", (int n) =>
        {
            var s = processor.Slot(n);
            s.Monitor.Changes.Reset(DateTimeOffset.Now);
            return Results.Ok(new { since = s.Monitor.Changes.Since });
        });

        // ------------------------------------------------------------------ bookmarks and notes (per program name)

        slotApi.MapGet("/bookmarks", (int n) =>
        {
            var s = processor.Slot(n);
            var p = s.Runtime?.ProgramName;
            if (p == null) return Results.Ok(new { program = (string?)null, starred = Array.Empty<string>(), notes = new Dictionary<string, string>() });
            var b = bookmarks.Get(p);
            return Results.Ok(new { program = p, starred = b.Starred, notes = b.Notes });
        });
        slotApi.MapPut("/bookmarks/star", (int n, JsonElement body) => Bk(processor.Slot(n), b =>
            bookmarks.SetStar(b, Str(body, "signal") ?? "", Bool(body, "starred") ?? true)));
        slotApi.MapPut("/bookmarks/note", (int n, JsonElement body) => Bk(processor.Slot(n), b =>
            bookmarks.SetNote(b, Str(body, "signal") ?? "", Str(body, "note"))));
        IResult Bk(ProgramSlot s, Func<string, Bookmarks> change)
        {
            var p = s.Runtime?.ProgramName;
            if (p == null) return Bad("There is no program in that slot.");
            try { var b = change(p); return Results.Ok(new { program = p, starred = b.Starred, notes = b.Notes }); }
            catch (ArgumentException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 409); }
        }

        // ------------------------------------------------------------------ saved traces: save, import, export, compare

        object TraceInfoDto(TraceInfo t) => new { id = t.Id, name = t.Name, saved = t.Saved, program = t.Program, slot = t.Slot, count = t.Count };

        slotApi.MapGet("/traces", (int n) => traces.List(n).Select(TraceInfoDto));

        // Save the posted trace (the debugger's own rows, or a file the user picked: JSON or CSV) under a name.
        slotApi.MapPost("/traces", async (int n, string? name, HttpRequest req) =>
        {
            string text;
            using (var rd = new StreamReader(req.Body, Encoding.UTF8)) text = await rd.ReadToEndAsync();
            try
            {
                var f = TraceIo.Parse(text);
                if (f.Rows.Count == 0) return Bad("That trace has no changes in it.");
                var s = processor.Slot(n);
                var info = traces.Save(n, name, string.IsNullOrEmpty(f.Program) ? s.Runtime?.ProgramName ?? "" : f.Program, f.Rows);
                return Results.Ok(TraceInfoDto(info));
            }
            catch (TraceFormatException e) { return Bad(e.Message); }
            catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 409); }
        });

        slotApi.MapGet("/traces/{id}", (int n, string id, string? format) =>
        {
            var t = traces.Get(n, id);
            if (t == null) return Results.NotFound(new { error = "There's no such saved trace." });
            return Download(t.Value.Info.Name, t.Value.File, format);
        });
        slotApi.MapPut("/traces/{id}", (int n, string id, JsonElement body) =>
            traces.Rename(n, id, Str(body, "name") ?? "") ? Results.Ok() : Results.NotFound(new { error = "There's no such saved trace." }));
        slotApi.MapDelete("/traces/{id}", (int n, string id) => traces.Delete(n, id) ? Results.Ok() : Results.NotFound(new { error = "There's no such saved trace." }));

        // Turn the posted trace into a CSV or JSON file (the debugger posts the rows it is showing).
        slotApi.MapPost("/trace/export", async (int n, string? format, string? name, HttpRequest req) =>
        {
            string text;
            using (var rd = new StreamReader(req.Body, Encoding.UTF8)) text = await rd.ReadToEndAsync();
            try
            {
                var f = TraceIo.Parse(text);
                var s = processor.Slot(n);
                return Download(name ?? $"slot{n:00}-trace", f with { Slot = n, Program = s.Runtime?.ProgramName ?? f.Program }, format);
            }
            catch (TraceFormatException e) { return Bad(e.Message); }
        });

        IResult Download(string name, TraceFile f, string? format)
        {
            var safe = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
            if (safe.Length == 0) safe = "trace";
            return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                ? Results.File(Encoding.UTF8.GetBytes(TraceIo.ToCsv(f.Rows)), "text/csv; charset=utf-8", safe + ".csv")
                : Results.File(Encoding.UTF8.GetBytes(TraceIo.ToJson(f)), "application/json", safe + ".json");
        }

        // Compare two saved traces: { "a": id, "b": id, "toleranceMs": 50 }
        slotApi.MapPost("/traces/compare", (int n, JsonElement body) =>
        {
            var a = traces.Get(n, Str(body, "a") ?? ""); var b = traces.Get(n, Str(body, "b") ?? "");
            if (a == null || b == null) return Results.NotFound(new { error = "Pick two saved traces." });
            var tol = Dbl(body, "toleranceMs") ?? 50;
            var c = TraceCompare.Compare(a.Value.File.Rows, b.Value.File.Rows, tol);
            return Results.Ok(new
            {
                a = TraceInfoDto(a.Value.Info), b = TraceInfoDto(b.Value.Info), toleranceMs = c.ToleranceMs, same = c.Same, truncated = c.Truncated,
                rowsA = c.RowsA, rowsB = c.RowsB, matched = c.Matched, onlyA = c.OnlyA, onlyB = c.OnlyB, valueDiffs = c.ValueDiffs, timingDiffs = c.TimingDiffs,
                diffs = c.Diffs.Select(d => new
                {
                    kind = d.Kind, signal = d.Signal, offsetMs = d.OffsetMs, deltaMs = d.DeltaMs, text = d.Text,
                    a = d.A == null ? null : new { value = d.A.ValueText, kind = d.A.Kind, source = d.A.Source, time = d.A.Time },
                    b = d.B == null ? null : new { value = d.B.ValueText, kind = d.B.Kind, source = d.B.Source, time = d.B.Time }
                })
            });
        });

        // ------------------------------------------------------------------ why is this signal this value?

        slotApi.MapGet("/why", (int n, string? signal) =>
        {
            var s = processor.Slot(n);
            var rt = s.Runtime;
            if (rt == null) return Results.NotFound(new { error = "No program in that slot." });
            if (string.IsNullOrEmpty(signal)) return Bad("Give a signal name: ?signal=Room_On");
            var now = rt.GetSignals();
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var x in now) values[x.Name] = x.Value;
            if (!values.ContainsKey(signal) && s.Monitor.Buffer.Snapshot().All(e => e.Signal != signal)) return Results.NotFound(new { error = $"There's no signal '{signal}'." });
            ProgramGraph? graph = null;
            try { graph = (rt as IProgramGraphSource)?.GetGraph(); } catch { }
            var ctx = new WhyContext
            {
                Graph = graph, RuntimeKind = rt.Kind,
                Trace = s.Monitor.Buffer.Snapshot().Select(TraceRow.From).ToList(),
                CurrentValue = name => values.TryGetValue(name, out var v) ? TraceRow.Normalize(v is bool ? "Digital" : v is string ? "Serial" : "Analog", v) : null,
                ClientsOf = ipId => uint.TryParse(ipId, System.Globalization.NumberStyles.HexNumber, null, out var id) && s.FindDevice(id) is { } d
                    ? d.Connections.Select(c => c.Hostname is { Length: > 0 } h ? $"{h} ({c.RemoteAddress})" : c.RemoteAddress ?? "").Where(x => x.Length > 0).ToList()
                    : new List<string>(),
                TraceAvailable = s.Monitor.Listening
            };
            var r = WhyChain.Build(signal, ctx);
            return Results.Ok(new
            {
                signal = r.Signal, value = r.Value, summary = r.Summary, complete = r.Complete, note = r.Note,
                steps = r.Steps.Select(x => new { kind = x.Kind, signal = x.Signal, value = x.Value, time = x.Time, text = x.Text, symbol = x.Symbol, cause = x.Cause })
            });
        });

        // ------------------------------------------------------------------ exceptions

        slotApi.MapGet("/exceptions", (int n) => processor.Slot(n).Exceptions.Recent().Select(e => new
        {
            time = e.Time, source = e.Source, type = e.Type, message = e.Message, stack = e.Stack, hasSymbols = e.HasSymbols,
            frames = e.Frames.Select(f => new { method = f.Method, file = f.File, shortFile = ExceptionReport.ShortFile(f.File), line = f.Line })
        }));
        slotApi.MapDelete("/exceptions", (int n) => { processor.Slot(n).Exceptions.Clear(); return Results.Ok(); });

        // ------------------------------------------------------------------ signal search across slots

        app.MapGet("/api/search/signals", (string? q, int? max) =>
            SignalSearch.Find(processor, q ?? "", Math.Clamp(max ?? 50, 1, 200))
                .Select(h => new { slot = h.Slot, program = h.Program, signal = h.Signal, kind = h.Kind, value = h.Value }));

        // ------------------------------------------------------------------ support bundle

        slotApi.MapGet("/support-bundle", (int n, bool? includeProgram, HttpContext ctx) =>
        {
            var s = processor.Slot(n);
            var bytes = BuildBundle(s, includeProgram == true);
            activity.Add("support-bundle", $"Support bundle for slot {n}{(includeProgram == true ? " (with program files)" : "")}", n, who(ctx));
            return Results.File(bytes, "application/zip", $"openvp-support-slot{n:00}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        });

        byte[] BuildBundle(ProgramSlot s, bool includeProgram)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Put(string name, string content)
                {
                    var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                    w.Write(content);
                }
                var rt = s.Runtime;
                var included = new List<string> { "log.txt", "log.json", "info.txt", "settings.json", "summary.json", "metrics.json", "activity.json", "watches-and-monitor.json", "exceptions.json" };
                var log = processor.RecentLog(s.Number, 5000);
                Put("log.txt", string.Join("\n", log.Select(l => $"{l.Time:yyyy-MM-dd HH:mm:ss.fff}  {l.Level,-7} {l.Message}" + (l.Detail != null ? "\n" + l.Detail : ""))));
                Put("log.json", JsonSerializer.Serialize(log, Json));
                Put("info.txt", string.Join("\n",
                    $"OpenVP 0.1.0", $"Created {DateTimeOffset.Now:O}", $"OS {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
                    $".NET {Environment.Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})",
                    $"Slot {s.Number}  Room ID {s.RoomId}  state {s.State}",
                    $"Program {rt?.ProgramName ?? "(none)"}  type {rt?.Kind ?? "-"}  file {(s.ProgramPath is null ? "-" : Path.GetFileName(s.ProgramPath))}",
                    s.LastError is null ? "" : "Last error: " + s.LastError.Split('\n')[0]));
                Put("settings.json", File.Exists(Path.Combine(s.Directory, "slot.json")) ? File.ReadAllText(Path.Combine(s.Directory, "slot.json")) : "{}");
                Put("summary.json", JsonSerializer.Serialize(summary(s), Json));
                var snap = processor.Metrics.Snapshot(includeHistory: false);
                Put("metrics.json", JsonSerializer.Serialize(new { snap.Time, snap.UptimeSeconds, snap.CpuCores, snap.CpuPercent, snap.WorkingSetMb, snap.ProcessThreads, snap.PoolThreads, slot = snap.Slots.FirstOrDefault(x => x.Number == s.Number) }, Json));
                Put("activity.json", JsonSerializer.Serialize(activity.Query(s.Number, 500), Json));
                Put("watches-and-monitor.json", JsonSerializer.Serialize(new { watches = s.Monitor.WatchStatus(), monitor = s.Monitor.Settings, alerts = s.Monitor.Oscillation.Alerts }, Json));
                Put("exceptions.json", JsonSerializer.Serialize(s.Exceptions.Recent(), Json));
                var buf = s.Monitor.Buffer.Snapshot();
                if (buf.Count > 0)
                {
                    Put("trace.json", TraceIo.ToJson(new TraceFile(s.Number, rt?.ProgramName ?? "", DateTimeOffset.Now, buf.Select(TraceRow.From).ToList())));
                    included.Add("trace.json");
                }
                string compatText;
                try { compatText = compat(s); } catch (Exception e) { compatText = "Couldn't build the report: " + e.Message; }
                Put("compatibility.txt", compatText); included.Add("compatibility.txt");

                var skipped = new List<string>();
                if (includeProgram)
                {
                    // The program as it was loaded (the uploaded copy and its .sig, .pdb and sidecars), then the slot's own folder: the extracted
                    // program, NVRAM and User files.
                    var roots = new List<string>();
                    if (s.ProgramPath != null && Path.GetDirectoryName(s.ProgramPath) is { } pd && System.IO.Directory.Exists(pd)) roots.Add(pd);
                    if (System.IO.Directory.Exists(s.Directory)) roots.Add(s.Directory);
                    foreach (var root in roots)
                        foreach (var f in System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                        {
                            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                            if (rel is "slot.json" or "debug.json" || rel.EndsWith(".tmp") || included.Contains("program-files/" + rel)) continue;
                            try
                            {
                                if (new FileInfo(f).Length > 100L * 1024 * 1024) { skipped.Add(rel + " (too large)"); continue; }
                                zip.CreateEntryFromFile(f, "program-files/" + rel, CompressionLevel.Optimal);
                                included.Add("program-files/" + rel);
                            }
                            catch (IOException) { skipped.Add(rel + " (in use)"); }
                        }
                }
                Put("README.txt",
                    "OpenVP support bundle for slot " + s.Number + ".\n\n" +
                    (includeProgram
                        ? "Program files, NVRAM and User files ARE included (program-files/), because they were ticked when this was made.\n"
                        : "Program binaries, NVRAM and User files are NOT included, and neither is the access key.\n") +
                    (buf.Count == 0 ? "No trace is included: no debugger was open on this slot, so no changes were recorded.\n" : $"trace.json holds the last {buf.Count} signal changes seen while a debugger was open.\n") +
                    (skipped.Count > 0 ? "Skipped: " + string.Join(", ", skipped) + "\n" : "") +
                    "\nFiles:\n" + string.Join("\n", included.Select(i => "  " + i)) + "\n");
            }
            return ms.ToArray();
        }
    }
}
