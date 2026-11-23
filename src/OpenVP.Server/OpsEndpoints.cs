using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using OpenVP.Core;
using OpenVP.Core.Ops;

namespace OpenVP.Server;

/// <summary>
/// The REST endpoints for whole-processor export and import, backups, program versions and diffs, the text file editor, scheduled actions, push targets and git-backed config.
/// The logic is in <c>OpenVP.Core.Ops</c>; these methods only read the request, take the slot locks, call it and write the answer.
/// </summary>
public static class OpsEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static string? Str(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? Bool(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static int? Int(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    private static int[] Ints(JsonElement b, string k) => b.ValueKind == JsonValueKind.Object && b.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32()).Distinct().ToArray() : [];

    private static IResult Error(int status, string message, object? extra = null) =>
        Results.Json(extra == null ? new { error = message } : (object)new { error = message, detail = extra }, statusCode: status);

    private static IResult Archive(ArchiveException e) => e.Code switch
    {
        "too-big" => Results.Json(new { error = e.Message, code = e.Code }, statusCode: 413),
        "wrong-password" or "need-password" => Results.Json(new { error = e.Message, code = e.Code }, statusCode: 422),
        _ => Results.Json(new { error = e.Message, code = e.Code }, statusCode: 400)
    };

    public static void Map(WebApplication app, RouteGroupBuilder slotApi, OpsServices s)
    {
        MapExportImport(app, s);
        MapBackups(app, s);
        MapVersions(app, slotApi, s);
        MapEditor(slotApi, s);
        MapSchedules(app, s);
        MapTargets(app, s);
        MapGit(app, s);
    }

    // =================================================================== export and import

    private static void MapExportImport(WebApplication app, OpsServices s)
    {
        app.MapPost("/api/export", (HttpContext ctx, JsonElement body) =>
        {
            var opts = new ExportOptions
            {
                IncludeHistory = Bool(body, "history") ?? false, IncludeSnapshots = Bool(body, "snapshots") ?? false,
                IncludeSecrets = Bool(body, "secrets") ?? false, Password = Str(body, "password"), Slots = Ints(body, "slots") is { Length: > 0 } sl ? sl : null,
                Reason = "export"
            };
            var tmpDir = Path.Combine(s.Processor.DataDirectory, "tmp");
            Directory.CreateDirectory(tmpDir);
            var tmp = Path.Combine(tmpDir, "export-" + Guid.NewGuid().ToString("N")[..10] + ".openvp");
            try
            {
                ArchiveManifest m;
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) m = s.Exporter.Export(fs, opts);
                s.Activity.Add("export", $"Exported the processor ({m.Slots.Count} slot{(m.Slots.Count == 1 ? "" : "s")}{(m.HasSecrets ? ", with secrets protected by a password" : "")})", null, s.Who(ctx));
                var name = $"openvp-export-{DateTime.Now:yyyyMMdd-HHmm}.openvp";
                return Results.File(new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose), "application/octet-stream", name);
            }
            catch (ArgumentException e) { try { File.Delete(tmp); } catch { } return Error(400, e.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { try { File.Delete(tmp); } catch { } return Error(500, "Couldn't write the export: " + e.Message); }
        });

        object PreviewDto(ImportPreview p, string previewId) => new
        {
            previewId, formatVersion = p.FormatVersion, created = p.Created, machine = p.Machine, openvpVersion = p.OpenVpVersion, reason = p.Reason,
            hasSecrets = p.HasSecrets, secretCount = p.SecretCount, passwordOk = p.PasswordOk, includesHistory = p.IncludesHistory, includesSnapshots = p.IncludesSnapshots,
            slots = p.Slots, settings = p.Settings, recentPrograms = p.RecentPrograms, size = p.Size, warnings = p.Warnings
        };

        // Upload a file (or name one already uploaded, with a password) and see what is in it. Nothing is changed.
        app.MapPost("/api/import/preview", async (HttpContext ctx) =>
        {
            string? path, id, password = null;
            try
            {
                if (ctx.Request.HasFormContentType)
                {
                    var form = await ctx.Request.ReadFormAsync();
                    var f = form.Files["file"] ?? form.Files.FirstOrDefault();
                    if (f == null) return Error(400, "Choose an .openvp file.");
                    password = form["password"].ToString();
                    path = s.Previews.NewPath(out id);
                    await using (var fs = File.Create(path)) await f.CopyToAsync(fs);
                    s.Previews.Keep(id);
                }
                else
                {
                    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                    id = Str(body, "previewId");
                    password = Str(body, "password");
                    path = s.Previews.Get(id);
                    if (path == null) return Error(404, "That file is no longer waiting. Choose it again.");
                }
                try { return Results.Ok(PreviewDto(s.Importer.Preview(path, password), id!)); }
                catch { if (ctx.Request.HasFormContentType) s.Previews.Remove(id); throw; }
            }
            catch (ArchiveException e) { return Archive(e); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
        });

        // Import. JSON {previewId, slots, replaceSettings, restoreSecrets, restoreHistory, password} for a file already previewed, or multipart (file + the same fields) in one go (what a push uses).
        app.MapPost("/api/import", async (HttpContext ctx) =>
        {
            string? path = null, previewId = null;
            JsonElement body = default;
            var fields = new Dictionary<string, string>();
            var uploadedHere = ctx.Request.HasFormContentType; // a one-shot upload is thrown away afterwards; a previewed file stays until it is imported, so a wrong password can be retried
            var consume = uploadedHere;
            try
            {
                if (ctx.Request.HasFormContentType)
                {
                    var form = await ctx.Request.ReadFormAsync();
                    var f = form.Files["file"] ?? form.Files.FirstOrDefault();
                    if (f == null) return Error(400, "Choose an .openvp file.");
                    foreach (var k in form.Keys) fields[k] = form[k].ToString();
                    path = s.Previews.NewPath(out previewId);
                    await using (var fs = File.Create(path)) await f.CopyToAsync(fs);
                    s.Previews.Keep(previewId);
                }
                else
                {
                    body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                    previewId = Str(body, "previewId");
                    path = s.Previews.Get(previewId);
                    if (path == null) return Error(404, "That file is no longer waiting. Choose it again.");
                    foreach (var p in body.EnumerateObject())
                        fields[p.Name] = p.Value.ValueKind switch { JsonValueKind.String => p.Value.GetString() ?? "", JsonValueKind.Array => string.Join(',', p.Value.EnumerateArray().Select(e => e.ToString())), _ => p.Value.ToString() };
                }
                bool F(string k) => fields.TryGetValue(k, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);
                var slots = fields.TryGetValue("slots", out var sv) && sv.Length > 0
                    ? sv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, out var n) ? n : -1).Distinct().ToList() : null;
                if (slots != null && slots.Any(n => !s.Processor.IsValidSlot(n))) return Error(400, $"Slots must be numbers 1–{s.Processor.Slots.Count}.");
                var req = new ImportRequest
                {
                    Slots = slots, ReplaceSettings = F("replaceSettings"), RestoreSecrets = F("restoreSecrets"), RestoreHistory = F("restoreHistory"),
                    Password = fields.GetValueOrDefault("password"), Who = s.Who(ctx), How = fields.GetValueOrDefault("how") == "restore" ? "restore" : "import"
                };
                // Look before locking: a bad file or password is reported without touching any slot.
                var preview = s.Importer.Preview(path, req.RestoreSecrets ? req.Password : null);
                var affected = preview.Slots.Where(p => slots == null || slots.Contains(p.Slot)).Select(p => p.Slot).Order().ToList();
                // A locked slot, or one this account can't use, is left as it is; the rest of the import goes ahead.
                var skipped = new List<string>();
                foreach (var n in affected.ToList())
                {
                    if (OpenVP.Core.Security.Principal.Current is { } who && !who.CanSlot(n)) skipped.Add($"Slot {n} was skipped: your account isn't allowed to use it.");
                    else if (s.Locks.Blocking(n, F("overrideLock") ? OpenVP.Core.Security.Principal.Current : null) is { } held) skipped.Add($"Slot {n} was skipped: " + OpenVP.Core.Security.SlotLocks.Message(held) + " An Admin can import into it by sending overrideLock: true.");
                    else continue;
                    affected.Remove(n);
                }
                if (skipped.Count > 0) req = req with { Slots = affected };
                foreach (var n in affected) await s.SlotLocks[n - 1].WaitAsync();
                ImportResult result;
                try { result = await Task.Run(() => s.Importer.Apply(path, req)); }
                finally { foreach (var n in affected) s.SlotLocks[n - 1].Release(); }
                var ok = result.Slots.Count(x => x.Ok);
                consume = true;
                s.Activity.Add(req.How, $"{(req.How == "restore" ? "Restored" : "Imported")} {ok} slot{(ok == 1 ? "" : "s")} from {(preview.Machine is { Length: > 0 } m ? "an export made on " + m : "an export file")}{(result.SettingsReplaced ? " and replaced the processor settings" : "")}", null, req.Who,
                    null, string.Join("; ", result.Slots.Select(x => $"slot {x.Slot}: {x.Message}")));
                return Results.Ok(new { slots = result.Slots, settingsReplaced = result.SettingsReplaced, secretsRestored = result.SecretsRestored, warnings = skipped.Concat(result.Warnings).ToList() });
            }
            catch (ArchiveException e) { if (e.Code is not ("wrong-password" or "need-password")) consume = true; return Archive(e); }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
            finally { if (consume) s.Previews.Remove(previewId); }
        });

        app.MapDelete("/api/import/{id}", (string id) => { s.Previews.Remove(id); return Results.Ok(); });
    }

    // =================================================================== backups

    private static void MapBackups(WebApplication app, OpsServices s)
    {
        object BackupDto(BackupInfo b) => new { id = b.Id, file = b.File, time = b.Time, size = b.Size, reason = b.Reason };
        object State() => new
        {
            settings = new
            {
                enabled = s.Backups.Settings.Enabled, time = s.Backups.Settings.Time, keepDays = s.Backups.Settings.KeepDays, folder = s.Backups.Settings.Folder,
                includeHistory = s.Backups.Settings.IncludeHistory, includeSnapshots = s.Backups.Settings.IncludeSnapshots, includeSecrets = s.Backups.Settings.IncludeSecrets, hasPassword = s.Backups.HasPassword
            },
            folder = s.Backups.Folder,
            next = s.Backups.Settings.Enabled ? s.Backups.Settings.NextDue : null,
            last = s.Backups.Settings.LastRun is null ? null : new { time = s.Backups.Settings.LastRun, ok = s.Backups.Settings.LastOk, message = s.Backups.Settings.LastMessage },
            backups = s.Backups.List().Select(BackupDto)
        };

        app.MapGet("/api/backups", () => State());

        app.MapPut("/api/backups/settings", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var cur = s.Backups.Settings;
                var next = cur with
                {
                    Enabled = Bool(body, "enabled") ?? cur.Enabled, Time = Str(body, "time") ?? cur.Time, KeepDays = Int(body, "keepDays") ?? cur.KeepDays,
                    Folder = body.TryGetProperty("folder", out var f) ? f.ValueKind == JsonValueKind.String ? f.GetString() : null : cur.Folder,
                    IncludeHistory = Bool(body, "includeHistory") ?? cur.IncludeHistory, IncludeSnapshots = Bool(body, "includeSnapshots") ?? cur.IncludeSnapshots,
                    IncludeSecrets = Bool(body, "includeSecrets") ?? cur.IncludeSecrets
                };
                s.Backups.Update(next, Str(body, "password"));
                s.Activity.Add("settings", $"Changed the backup settings ({(next.Enabled ? $"daily at {next.Time}, keep {next.KeepDays} days" : "off")})", null, s.Who(ctx));
                return Results.Ok(State());
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapPost("/api/backups/run", async (HttpContext ctx) =>
        {
            var info = await Task.Run(() => s.Backups.RunNow("manual"));
            if (info == null) return Results.Json(new { error = s.Backups.Settings.LastOk == false ? s.Backups.Settings.LastMessage : "A backup is already running.", state = State() }, statusCode: 500);
            s.Activity.Add("backup", $"Backed up the processor ({info.File}, {info.Size / 1024.0 / 1024.0:0.#} MB)", null, s.Who(ctx));
            return Results.Ok(new { backup = BackupDto(info), state = State() });
        });

        app.MapGet("/api/backups/{id}/download", (string id) =>
            s.Backups.PathOf(id) is { } p ? Results.File(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), "application/octet-stream", Path.GetFileName(p)) : Error(404, "There's no such backup."));

        app.MapDelete("/api/backups/{id}", (string id, HttpContext ctx) =>
        {
            try
            {
                if (!s.Backups.Delete(id)) return Error(404, "There's no such backup.");
                s.Activity.Add("backup.delete", $"Deleted the backup {id}", null, s.Who(ctx));
                return Results.Ok(State());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
        });

        // Restore goes through the same preview as any import.
        app.MapPost("/api/backups/{id}/preview", (string id) =>
        {
            var src = s.Backups.PathOf(id);
            if (src == null) return Error(404, "There's no such backup.");
            try
            {
                var path = s.Previews.NewPath(out var pid);
                File.Copy(src, path, true);
                s.Previews.Keep(pid);
                var p = s.Importer.Preview(path, null);
                return Results.Ok(new
                {
                    previewId = pid, formatVersion = p.FormatVersion, created = p.Created, machine = p.Machine, openvpVersion = p.OpenVpVersion, reason = p.Reason, hasSecrets = p.HasSecrets, secretCount = p.SecretCount, passwordOk = p.PasswordOk,
                    includesHistory = p.IncludesHistory, includesSnapshots = p.IncludesSnapshots, slots = p.Slots, settings = p.Settings, recentPrograms = p.RecentPrograms, size = p.Size, warnings = p.Warnings
                });
            }
            catch (ArchiveException e) { return Archive(e); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
        });
    }

    // =================================================================== versions, diff

    private static void MapVersions(WebApplication app, RouteGroupBuilder slotApi, OpsServices s)
    {
        var sigCache = new Dictionary<int, (string Sig, string? Id)>();
        string? CurrentId(int n)
        {
            var dir = DataLayout.Uploads(s.Processor.DataDirectory, n);
            if (!Directory.Exists(dir)) return null;
            var files = new DirectoryInfo(dir).EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            if (files.Count == 0) return null;
            var sig = string.Join('|', files.Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));
            lock (sigCache)
            {
                if (sigCache.TryGetValue(n, out var c) && c.Sig == sig) return c.Id;
                string? id = null;
                try { id = ProgramVersions.HashOf(files.Select(f => (f.Name, File.ReadAllBytes(f.FullName)))) is { } h ? h[..12] : null; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                sigCache[n] = (sig, id);
                return id;
            }
        }

        object VersionDto(int n, ProgramVersion v, string? current) => new
        {
            id = v.Id, name = v.Name, file = v.File, hash = v.Hash, size = v.Size, files = v.Files, kind = v.Kind, firstLoaded = v.FirstLoaded, loadedAt = v.LoadedAt, loads = v.Loads,
            how = v.How, who = v.Who, lastHow = v.LastHow, lastWho = v.LastWho, note = v.Note, available = s.Versions.FolderOf(n, v.Id) != null, current = v.Id == current
        };

        slotApi.MapGet("/versions", (int n) =>
        {
            var cur = CurrentId(n);
            return Results.Ok(new { keep = s.Versions.Keep, current = cur, program = s.Processor.Slot(n).Runtime?.ProgramName, versions = s.Versions.List(n).Select(v => VersionDto(n, v, cur)) });
        });

        app.MapGet("/api/versions/settings", () => new { keep = s.Versions.Keep });
        app.MapPut("/api/versions/settings", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var keep = Int(body, "keep") ?? throw new ArgumentException("Send {\"keep\": 30}.");
                s.Versions.SetKeep(keep);
                s.Activity.Add("settings", $"Program versions: keep the last {keep} per slot", null, s.Who(ctx));
                return Results.Ok(new { keep });
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        slotApi.MapPut("/versions/{id}", (int n, string id, JsonElement body) =>
        {
            try
            {
                var v = s.Versions.SetNote(n, id, Str(body, "note"));
                return v == null ? Error(404, "There's no such version.") : Results.Ok(VersionDto(n, v, CurrentId(n)));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        slotApi.MapDelete("/versions/{id}", (int n, string id, HttpContext ctx) =>
        {
            var v = s.Versions.Get(n, id);
            if (v == null) return Error(404, "There's no such version.");
            if (id == CurrentId(n)) return Error(409, "This is the program that is loaded now. Load another one first.");
            s.Versions.Delete(n, id);
            s.Activity.Add("version.delete", $"Deleted version {v.Name} ({id[..7]}) of slot {n}", n, s.Who(ctx));
            return Results.Ok(new { deleted = id });
        });

        slotApi.MapGet("/versions/{id}/download", (int n, string id) =>
        {
            var v = s.Versions.Get(n, id);
            var files = s.Versions.ReadFiles(n, id);
            if (v == null || files == null) return Error(404, "There's no such version, or its files are gone.");
            if (files.Count == 1) return Results.File(files[0].Data, "application/octet-stream", files[0].Name);
            return Results.File(s.Versions.ZipOf(n, id)!, "application/zip", $"{Path.GetFileNameWithoutExtension(v.File)}-{id[..7]}.zip");
        });

        slotApi.MapPost("/versions/{id}/load", async (int n, string id, HttpContext ctx, JsonElement? body) =>
        {
            var files = s.Versions.ReadFiles(n, id);
            if (files == null) return Error(404, "There's no such version, or its files are gone.");
            var start = body is { ValueKind: JsonValueKind.Object } b && Bool(b, "start") is { } st ? st : true;
            await s.SlotLocks[n - 1].WaitAsync();
            try
            {
                s.StopWatching(n);
                return s.Install(n, files, start, s.Who(ctx), "version");
            }
            finally { s.SlotLocks[n - 1].Release(); }
        });

        // {"a":"loaded"|"<version id>","b":"loaded"|"<id>"}, optionally "aSlot" / "bSlot" to compare across slots (default: the slot in the address).
        slotApi.MapPost("/versions/diff", async (int n, JsonElement body) =>
        {
            (string Label, string Folder, string Main)? Side(string? what, int? slot)
            {
                var sl = slot ?? n;
                if (!s.Processor.IsValidSlot(sl)) return null;
                if (what == "loaded")
                {
                    var slotObj = s.Processor.Slot(sl);
                    var main = slotObj.Settings.Program?.File ?? (slotObj.ProgramPath is null ? null : Path.GetFileName(slotObj.ProgramPath));
                    var dir = DataLayout.Uploads(s.Processor.DataDirectory, sl);
                    return main == null || !Directory.Exists(dir) ? null : ($"the program loaded in slot {sl}", dir, main);
                }
                var v = s.Versions.Get(sl, what);
                var folder = s.Versions.FolderOf(sl, what);
                return v == null || folder == null ? null : ($"{v.Name} ({v.Id[..7]}, {v.LoadedAt.ToLocalTime():yyyy-MM-dd HH:mm})", folder, v.File);
            }
            var a = Side(Str(body, "a"), Int(body, "aSlot"));
            var b = Side(Str(body, "b"), Int(body, "bSlot"));
            if (a == null || b == null) return Error(404, "One of the two programs isn't there (no program loaded, or the version's files are gone).");
            try
            {
                var report = await Task.Run(() =>
                {
                    var pa = s.Profiler.Build(a.Value.Label, a.Value.Folder, a.Value.Main);
                    var pb = s.Profiler.Build(b.Value.Label, b.Value.Folder, b.Value.Main);
                    return ProgramDiff.Compare(pa, pb);
                });
                return Results.Ok(new { a = report.A, b = report.B, identical = report.Identical, summary = report.Summary, notes = report.Notes, sections = report.Sections.Select(x => new { title = x.Title, added = x.Added, removed = x.Removed, changed = x.Changed, items = x.Items }), text = report.Text() });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
        });
    }

    // =================================================================== text file editor

    private static void MapEditor(RouteGroupBuilder slotApi, OpsServices s)
    {
        string FileOf(int n, string? root, string? path)
        {
            var p = SlotFiles.Resolve(s.Processor.Slot(n).Directory, root, path);
            if (p == SlotFiles.Resolve(s.Processor.Slot(n).Directory, root, "")) throw new ArgumentException("Choose a file.");
            return p;
        }
        IResult Guard(Func<IResult> run)
        {
            try { return run(); }
            catch (EditConflictException e) { return Results.Json(new { error = e.Message, conflict = true }, statusCode: 409); }
            catch (EditInvalidException e) { return Results.Json(new { error = $"Line {e.Problem.Line}: {e.Problem.Message}", invalid = true, line = e.Problem.Line, column = e.Problem.Column }, statusCode: 400); }
            catch (EditTooLargeException e) { return Results.Json(new { error = e.Message, tooLarge = true }, statusCode: 413); }
            catch (Exception e) when (e is ArgumentException or UnauthorizedAccessException) { return Error(400, e.Message); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return Error(404, "No such file."); }
            catch (IOException e) { return Error(409, e.Message); }
        }
        static string KindOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch { ".json" => "json", ".xml" => "xml", _ => "text" };

        slotApi.MapGet("/files/text", (int n, string? root, string? path) => Guard(() =>
        {
            var f = FileOf(n, root, path);
            var t = TextFileEditor.Read(f);
            return Results.Ok(new { name = Path.GetFileName(f), kind = KindOf(f), text = t.Text, tag = t.Tag, size = t.Size, modified = t.Modified, newline = t.Newline, encoding = t.Encoding, maxBytes = TextFileEditor.MaxBytes });
        }));

        slotApi.MapPut("/files/text", (int n, string? root, string? path, HttpContext ctx, JsonElement body) => Guard(() =>
        {
            var f = FileOf(n, root, path);
            var text = Str(body, "text") ?? throw new ArgumentException("Send {\"text\": \"…\", \"tag\": \"…\"}.");
            var (state, undo) = TextFileEditor.Save(f, text, Str(body, "tag"), s.Trash, n, Bool(body, "force") ?? false, Bool(body, "allowInvalid") ?? false);
            var rel = Path.GetRelativePath(SlotFiles.Resolve(s.Processor.Slot(n).Directory, root, ""), f).Replace('\\', '/');
            s.Activity.Add("file.edit", $"Edited {rel} in the {root?.ToUpperInvariant()} folder of slot {n}", n, s.Who(ctx), null, $"{state.Size} bytes");
            return Results.Ok(new { saved = true, tag = state.Tag, size = state.Size, modified = state.Modified, undo });
        }));

        slotApi.MapPost("/files/validate", (JsonElement body) =>
        {
            var name = Str(body, "name") ?? "";
            var p = TextFileEditor.Validate(name, Str(body, "text") ?? "");
            return Results.Ok(p == null ? new { ok = true, line = (int?)null, column = (int?)null, error = (string?)null } : new { ok = false, line = (int?)p.Line, column = (int?)p.Column, error = (string?)p.Message });
        });
    }

    // =================================================================== schedules

    private static void MapSchedules(WebApplication app, OpsServices s)
    {
        string Local(DateTimeOffset? t) => t == null ? "" : TimeZoneInfo.ConvertTime(t.Value, s.Scheduler.Zone).ToString("yyyy-MM-dd HH:mm");
        object Dto(ScheduleView v) => new
        {
            id = v.Def.Id, name = v.Def.Name, enabled = v.Def.Enabled, mode = v.Def.Mode, time = v.Def.Time, days = v.Def.Days, cron = v.Def.Cron, at = v.Def.At, runMissed = v.Def.RunMissed,
            action = new { type = v.Def.Action.Type, slot = v.Def.Action.Slot, versionId = v.Def.Action.VersionId, path = v.Def.Action.Path, signal = v.Def.Action.Signal, value = v.Def.Action.Value, startAfter = v.Def.Action.StartAfter },
            summary = v.Summary, nextRun = v.NextRun, nextRunLocal = Local(v.NextRun), running = v.Running,
            lastRun = v.Def.LastRun, lastRunLocal = Local(v.Def.LastRun), lastResult = v.Def.LastResult, lastOk = v.Def.LastOk, runs = v.Def.Runs,
            done = v.Def.Mode == "once" && !v.Def.Enabled && v.Def.Runs > 0
        };
        object Run(ScheduleRun r) => new { time = r.Time, local = Local(r.Time), scheduleId = r.ScheduleId, name = r.Name, status = r.Status, message = r.Message, action = r.Action, slot = r.Slot, trigger = r.Trigger };
        object State() => new
        {
            zone = s.Scheduler.Zone.Id, zoneName = s.Scheduler.Zone.DisplayName, now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, s.Scheduler.Zone).ToString("yyyy-MM-dd HH:mm"),
            schedules = s.Scheduler.List().Select(Dto), history = s.Scheduler.History(60).Select(Run)
        };

        ScheduleDef Read(JsonElement b)
        {
            var d = JsonSerializer.Deserialize<ScheduleDef>(b.GetRawText(), Web) ?? throw new ArgumentException("Send the schedule as JSON.");
            return d;
        }
        IResult Check(HttpContext ctx, ScheduleDef d) =>
            d.Action?.Type == ScheduleAction.LoadPath && !ApiAuth.IsDirectLocal(ctx)
                ? Error(403, "Loading a program from a path only works when you set it up from the computer OpenVP runs on.") : Results.Ok();

        app.MapGet("/api/schedules", () => State());

        app.MapPost("/api/schedules", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var d = Read(body);
                if (Check(ctx, d) is not IStatusCodeHttpResult { StatusCode: 200 } ) return Check(ctx, d);
                if (d.Action.Slot is int sl && !s.Processor.IsValidSlot(sl)) return Error(400, $"Slot must be 1–{s.Processor.Slots.Count}.");
                var added = s.Scheduler.Add(d);
                s.Activity.Add("schedule.add", $"Added the schedule “{added.Name}” ({Scheduler.Describe(added)})", added.Action.Slot, s.Who(ctx));
                return Results.Ok(State());
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (JsonException e) { return Error(400, "That isn't a valid schedule: " + e.Message); }
        });

        app.MapPut("/api/schedules/{id}", (string id, HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var d = Read(body);
                if (Check(ctx, d) is not IStatusCodeHttpResult { StatusCode: 200 }) return Check(ctx, d);
                if (d.Action.Slot is int sl && !s.Processor.IsValidSlot(sl)) return Error(400, $"Slot must be 1–{s.Processor.Slots.Count}.");
                var upd = s.Scheduler.Update(id, _ => d);
                if (upd == null) return Error(404, "There's no such schedule.");
                s.Activity.Add("schedule.edit", $"Changed the schedule “{upd.Name}” ({(upd.Enabled ? Scheduler.Describe(upd) : "off")})", upd.Action.Slot, s.Who(ctx));
                return Results.Ok(State());
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (JsonException e) { return Error(400, "That isn't a valid schedule: " + e.Message); }
        });

        app.MapDelete("/api/schedules/{id}", (string id, HttpContext ctx) =>
        {
            var d = s.Scheduler.Get(id);
            if (d == null || !s.Scheduler.Remove(id)) return Error(404, "There's no such schedule.");
            s.Activity.Add("schedule.delete", $"Deleted the schedule “{d.Name}”", d.Action.Slot, s.Who(ctx));
            return Results.Ok(State());
        });

        app.MapPost("/api/schedules/{id}/run", async (string id, HttpContext ctx) =>
        {
            var d = s.Scheduler.Get(id);
            if (d == null) return Error(404, "There's no such schedule.");
            var run = await s.Scheduler.RunNowAsync(id);
            if (run == null) return Error(409, "That schedule is already running.");
            return Results.Ok(new { run = Run(run), state = State() });
        });

        // The next few times a schedule as typed would run, without saving it.
        app.MapPost("/api/schedules/preview", (JsonElement body) =>
        {
            try
            {
                var d = Read(body);
                if (d.Mode == "once") d.OnceTime(); else d.Compile();
                var times = s.Scheduler.Preview(d, 5);
                return Results.Ok(new { summary = Scheduler.Describe(d), next = times.Select(t => new { utc = t, local = Local(t) }) });
            }
            catch (Exception e) when (e is ArgumentException or FormatException or JsonException) { return Error(400, e.Message); }
        });
    }

    // =================================================================== push targets

    private static void MapTargets(WebApplication app, OpsServices s)
    {
        object Dto(PushTarget t) => new { id = t.Id, name = t.Name, url = t.Url, fingerprint = t.Fingerprint, hasKey = s.Targets.HasKey(t.Id), lastTest = t.LastTest, lastOk = t.LastOk, lastMessage = t.LastMessage };
        object JobDto(PushJob j) => new
        {
            id = j.Id, what = j.What, started = j.Started, finished = j.Finished, done = j.Done,
            targets = j.Targets.Select(t => new { id = t.TargetId, name = t.Name, url = t.Url, state = t.State, message = t.Message, slots = t.Slots.Select(x => new { slot = x.Slot, state = x.State, message = x.Message }) })
        };

        app.MapGet("/api/targets", () => s.Targets.List().Select(Dto));

        app.MapPost("/api/targets", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var t = s.Targets.Add(Str(body, "name") ?? "", Str(body, "url") ?? "", Str(body, "key"), Str(body, "fingerprint"));
                s.Activity.Add("target.add", $"Added the push target “{t.Name}” ({t.Url})", null, s.Who(ctx));
                return Results.Ok(Dto(t));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapPut("/api/targets/{id}", (string id, HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var t = s.Targets.Update(id, Str(body, "name") ?? "", Str(body, "url") ?? "", body.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null, Str(body, "fingerprint"));
                if (t == null) return Error(404, "There's no such target.");
                s.Activity.Add("target.edit", $"Changed the push target “{t.Name}”", null, s.Who(ctx));
                return Results.Ok(Dto(t));
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapDelete("/api/targets/{id}", (string id, HttpContext ctx) =>
        {
            var t = s.Targets.Get(id);
            if (t == null || !s.Targets.Remove(id)) return Error(404, "There's no such target.");
            s.Activity.Add("target.delete", $"Removed the push target “{t.Name}”", null, s.Who(ctx));
            s.TargetRemoved?.Invoke(id);
            return Results.Ok();
        });

        app.MapPost("/api/targets/{id}/test", async (string id) =>
        {
            var t = s.Targets.Get(id);
            if (t == null) return Error(404, "There's no such target.");
            var r = await s.PushClient.TestAsync(t);
            s.Targets.RecordTest(id, r.Ok, r.Message);
            return Results.Ok(new { ok = r.Ok, message = r.Message, version = r.Version, slots = r.Slots, keyAccepted = r.KeyAccepted });
        });

        // Reads the certificate an https address presents, so you can compare its fingerprint and choose to trust that one certificate.
        app.MapPost("/api/targets/certificate", async (JsonElement body) =>
        {
            try
            {
                var c = await PushClient.FetchCertificateAsync(Str(body, "url") ?? "");
                return Results.Ok(new { fingerprint = c.Fingerprint, subject = c.Subject, issuer = c.Issuer, notBefore = c.NotBefore, notAfter = c.NotAfter, trusted = c.Trusted, problem = c.Error });
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return Error(400, e.Message); }
        });

        app.MapGet("/api/push", () => s.Push.Jobs().Reverse().Select(JobDto));
        app.MapGet("/api/push/{id}", (string id) => s.Push.Get(id) is { } j ? Results.Ok(JobDto(j)) : Error(404, "There's no such push."));

        // {"what":"version","slot":1,"version":"<id>"|"loaded","targets":[{"id":"…","slots":[1,2]}],"start":true}
        // {"what":"export","targets":["id"],"slots":[1],"replaceSettings":false,"secrets":false,"password":"…"}
        app.MapPost("/api/push", async (HttpContext ctx, JsonElement body) =>
        {
            var what = Str(body, "what");
            if (!body.TryGetProperty("targets", out var tarr) || tarr.ValueKind != JsonValueKind.Array || tarr.GetArrayLength() == 0) return Error(400, "Choose at least one target.");
            var chosen = new List<(PushTarget Target, int[] Slots)>();
            foreach (var e in tarr.EnumerateArray())
            {
                var id = e.ValueKind == JsonValueKind.String ? e.GetString() : Str(e, "id");
                var t = s.Targets.Get(id);
                if (t == null) return Error(404, "One of the targets doesn't exist any more.");
                chosen.Add((t, e.ValueKind == JsonValueKind.Object ? Ints(e, "slots") : []));
            }
            var who = s.Who(ctx);
            try
            {
                if (what == "version")
                {
                    var slot = Int(body, "slot") ?? 0;
                    if (!s.Processor.IsValidSlot(slot)) return Error(400, $"Choose the slot to push from (1–{s.Processor.Slots.Count}).");
                    var vid = Str(body, "version");
                    List<(string Name, byte[] Data)>? files; string label;
                    if (vid == "loaded" || vid == null)
                    {
                        var slotObj = s.Processor.Slot(slot);
                        var main = slotObj.Settings.Program?.File ?? (slotObj.ProgramPath is null ? null : Path.GetFileName(slotObj.ProgramPath));
                        var dir = DataLayout.Uploads(s.Processor.DataDirectory, slot);
                        if (main == null || !File.Exists(Path.Combine(dir, main))) return Error(409, "That slot has no program to push.");
                        files = [(main, File.ReadAllBytes(Path.Combine(dir, main))), .. Directory.EnumerateFiles(dir).Where(f => Path.GetFileName(f) != main).OrderBy(f => f, StringComparer.Ordinal).Select(f => (Path.GetFileName(f), File.ReadAllBytes(f)))];
                        label = main;
                    }
                    else
                    {
                        files = s.Versions.ReadFiles(slot, vid);
                        if (files == null) return Error(404, "There's no such version, or its files are gone.");
                        label = s.Versions.Get(slot, vid)!.File;
                    }
                    if (chosen.Any(c => c.Slots.Length == 0 || c.Slots.Any(n => n < 1 || n > 99))) return Error(400, "Choose the slot to load into on each target.");
                    var job = s.Push.PushProgram(label, files, chosen, Bool(body, "start") ?? true);
                    s.Activity.Add("push", $"Pushing {label} to {string.Join(", ", chosen.Select(c => c.Target.Name))}", slot, who);
                    return Results.Ok(JobDto(job));
                }
                if (what == "export")
                {
                    var secrets = Bool(body, "secrets") ?? false;
                    var password = Str(body, "password");
                    var slots = Ints(body, "slots");
                    var tmpDir = Path.Combine(s.Processor.DataDirectory, "tmp");
                    Directory.CreateDirectory(tmpDir);
                    var tmp = Path.Combine(tmpDir, "push-" + Guid.NewGuid().ToString("N")[..10] + ".openvp");
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        s.Exporter.Export(fs, new ExportOptions { IncludeSecrets = secrets, Password = password, Slots = slots.Length > 0 ? slots : null, Reason = "push" });
                    var job = s.Push.PushExport(tmp, chosen.Select(c => c.Target).ToList(), slots.Length > 0 ? slots : null, Bool(body, "replaceSettings") ?? false, password, secrets, () => { try { File.Delete(tmp); } catch { } });
                    s.Activity.Add("push", $"Pushing a whole-processor export to {string.Join(", ", chosen.Select(c => c.Target.Name))}", null, who);
                    return Results.Ok(JobDto(job));
                }
                return Error(400, "what must be \"version\" or \"export\".");
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
        });
    }

    // =================================================================== git

    private static void MapGit(WebApplication app, OpsServices s)
    {
        object State(bool log = true)
        {
            var g = s.Git.Settings;
            return new
            {
                available = s.Git.Available, version = s.Git.GitVersion,
                settings = new { enabled = g.Enabled, repo = g.Repo, remote = g.Remote, push = g.Push, branch = g.Branch, includePrograms = g.IncludePrograms, maxProgramMb = g.MaxProgramMb },
                isRepo = g.Repo != null && Directory.Exists(Path.Combine(g.Repo, ".git")),
                last = g.LastCommit is null ? null : new { time = g.LastCommit, hash = g.LastHash, message = g.LastMessage },
                error = g.LastError, lastPush = g.LastPush is null ? null : new { time = g.LastPush, result = g.LastPushResult },
                debounceSeconds = (int)s.Git.Debounce.TotalSeconds,
                commits = log ? s.Git.Log(30).Select(c => new { hash = c.Hash, author = c.Author, time = c.Time, subject = c.Subject }) : null
            };
        }

        app.MapGet("/api/git", () => State());

        app.MapPut("/api/git", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var cur = s.Git.Settings;
                var next = cur with
                {
                    Enabled = Bool(body, "enabled") ?? cur.Enabled, Repo = body.TryGetProperty("repo", out var r) ? (r.ValueKind == JsonValueKind.String ? r.GetString() : null) : cur.Repo,
                    Remote = body.TryGetProperty("remote", out var rm) ? (rm.ValueKind == JsonValueKind.String ? rm.GetString() : null) : cur.Remote,
                    Push = Bool(body, "push") ?? cur.Push, Branch = Str(body, "branch") ?? cur.Branch, IncludePrograms = Bool(body, "includePrograms") ?? cur.IncludePrograms, MaxProgramMb = Int(body, "maxProgramMb") ?? cur.MaxProgramMb
                };
                s.Git.Update(next);
                s.Activity.Add("settings", $"Git-backed config is {(next.Enabled ? "on" : "off")}{(next.Enabled ? $" ({next.Repo})" : "")}", null, s.Who(ctx));
                if (next.Enabled && !cur.Enabled) s.Git.RequestCommit("Git-backed config turned on");
                return Results.Ok(State());
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
        });

        app.MapPost("/api/git/init", (HttpContext ctx, JsonElement body) =>
        {
            try
            {
                var msg = s.Git.Init(Str(body, "repo"));
                s.Activity.Add("git", "Set up the git repository for config: " + msg, null, s.Who(ctx));
                return Results.Ok(new { message = msg, state = State() });
            }
            catch (ArgumentException e) { return Error(400, e.Message); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Error(500, e.Message); }
        });

        app.MapPost("/api/git/commit", async (HttpContext ctx, JsonElement? body) =>
        {
            try
            {
                var msg = body is { ValueKind: JsonValueKind.Object } b ? Str(b, "message") : null;
                var r = await Task.Run(() => s.Git.CommitNow(string.IsNullOrWhiteSpace(msg) ? "Committed by hand from the console" : msg));
                return r.Hash == null && !r.Message.StartsWith("Nothing") ? Error(500, r.Message) : Results.Ok(new { committed = r.Committed, hash = r.Hash, message = r.Message, files = r.Files, skipped = r.Skipped, state = State() });
            }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
        });

        app.MapPost("/api/git/push", async () =>
        {
            try { return Results.Ok(new { message = await Task.Run(() => s.Git.PushNow()), state = State() }); }
            catch (InvalidOperationException e) { return Error(409, e.Message); }
        });
    }
}
