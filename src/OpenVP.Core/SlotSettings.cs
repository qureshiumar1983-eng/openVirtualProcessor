using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>What to do when a slot's program faults (see <see cref="CrashSupervisor"/>).</summary>
public sealed record CrashPolicy
{
    public const string Stop = "stop", Restart = "restart", Limited = "limited";

    /// <summary><c>stop</c> (stay stopped), <c>restart</c>, or <c>limited</c> (restart up to
    /// <see cref="MaxRestarts"/> times in <see cref="WindowMinutes"/> minutes, then stay stopped).</summary>
    public string Mode { get; init; } = Stop;
    public int MaxRestarts { get; init; } = 3;
    public int WindowMinutes { get; init; } = 10;
    /// <summary>Count the slot as crashed when one event has run this many seconds (the stuck-slot detector). 0 = never.</summary>
    public int StuckSeconds { get; init; }
    /// <summary>Count an unhandled exception in a program event handler as a crash. Off by default: such an exception is only logged.</summary>
    public bool FaultOnUnhandled { get; init; }
}

/// <summary>What a slot loads again after OpenVP restarts: the uploaded copy, or the file on disk it was loaded from.</summary>
public sealed record ProgramMemory
{
    public const string Upload = "upload", Path = "path";

    /// <summary><c>upload</c> (the copy in data/uploads/slotNN) or <c>path</c> (a file on this computer).</summary>
    public string Source { get; init; } = Upload;
    /// <summary>The program's file name inside the slot's upload folder.</summary>
    public string? File { get; init; }
    /// <summary>For <c>path</c>: the file's full path on this computer.</summary>
    public string? FilePath { get; init; }
    /// <summary>For <c>path</c>: extra files chosen with it.</summary>
    public string[]? Files { get; init; }
    /// <summary>Whether the program was running, so it is started again.</summary>
    public bool Running { get; init; } = true;
    /// <summary>Whether the slot reloads when the file changes.</summary>
    public bool Watch { get; init; }
}

/// <summary>Everything a slot remembers in its <c>slot.json</c>: Room ID, friendly name and colour, start-up behaviour,
/// crash policy and the program to bring back.</summary>
public sealed record SlotSettings
{
    private static readonly Regex ColorRe = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    public const int MaxNameLength = 40;

    public string? RoomId { get; init; }
    /// <summary>Optional friendly name ("Boardroom"), shown next to the slot number.</summary>
    public string? Name { get; init; }
    /// <summary>Optional colour as <c>#rrggbb</c>.</summary>
    public string? Color { get; init; }
    /// <summary>Bring the remembered program back when OpenVP starts.</summary>
    public bool Autostart { get; init; } = true;
    /// <summary>Slots come up in this order, lowest first. Null means the slot's own number.</summary>
    public int? StartOrder { get; init; }
    /// <summary>Seconds to wait before this slot is restored, after the one before it.</summary>
    public int StartDelaySeconds { get; init; }
    public CrashPolicy Crash { get; init; } = new();
    public ProgramMemory? Program { get; init; }

    /// <summary>Check the fields a user can edit and normalize them. Throws <see cref="ArgumentException"/>.</summary>
    public SlotSettings Validated()
    {
        var name = Name?.Trim();
        if (string.IsNullOrEmpty(name)) name = null;
        else
        {
            if (name.Length > MaxNameLength) throw new ArgumentException($"The name can be at most {MaxNameLength} characters.");
            if (name.Any(char.IsControl)) throw new ArgumentException("The name can't contain control characters.");
        }
        var color = Color?.Trim();
        if (string.IsNullOrEmpty(color)) color = null;
        else if (!ColorRe.IsMatch(color)) throw new ArgumentException("The colour must look like #3b82f6.");
        else color = color.ToLowerInvariant();
        if (StartOrder is < 1 or > 999) throw new ArgumentException("Start order must be 1 to 999.");
        if (StartDelaySeconds is < 0 or > 3600) throw new ArgumentException("Start delay must be 0 to 3600 seconds.");
        var c = Crash ?? new CrashPolicy();
        if (c.Mode is not (CrashPolicy.Stop or CrashPolicy.Restart or CrashPolicy.Limited))
            throw new ArgumentException("Crash policy must be stop, restart or limited.");
        if (c.MaxRestarts is < 1 or > 100) throw new ArgumentException("Restarts must be 1 to 100.");
        if (c.WindowMinutes is < 1 or > 1440) throw new ArgumentException("The window must be 1 to 1440 minutes.");
        if (c.StuckSeconds != 0 && c.StuckSeconds is < 2 or > 3600) throw new ArgumentException("Stuck seconds must be 0 (never) or 2 to 3600.");
        return this with { Name = name, Color = color };
    }

    /// <summary>Applies the fields present in a JSON object (the body of <c>PUT /api/slots/{n}/settings</c>); anything absent is kept.
    /// <c>null</c> clears <c>name</c>, <c>color</c> and <c>startOrder</c>. Throws <see cref="ArgumentException"/> for a field of the wrong type.</summary>
    public SlotSettings WithPatch(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("Send a JSON object.");
        var s = this;
        foreach (var p in patch.EnumerateObject())
        {
            switch (p.Name)
            {
                case "name": s = s with { Name = Str(p) }; break;
                case "color": s = s with { Color = Str(p) }; break;
                case "autostart": s = s with { Autostart = Bool(p) }; break;
                case "startOrder": s = s with { StartOrder = p.Value.ValueKind == JsonValueKind.Null ? null : Int(p) }; break;
                case "startDelaySeconds": s = s with { StartDelaySeconds = Int(p) }; break;
                case "crash":
                    if (p.Value.ValueKind != JsonValueKind.Object) throw new ArgumentException("crash must be an object.");
                    var c = s.Crash;
                    foreach (var q in p.Value.EnumerateObject())
                        c = q.Name switch
                        {
                            "mode" => c with { Mode = Str(q) ?? "" },
                            "maxRestarts" => c with { MaxRestarts = Int(q) },
                            "windowMinutes" => c with { WindowMinutes = Int(q) },
                            "stuckSeconds" => c with { StuckSeconds = Int(q) },
                            "faultOnUnhandled" => c with { FaultOnUnhandled = Bool(q) },
                            _ => c
                        };
                    s = s with { Crash = c };
                    break;
            }
        }
        return s;
    }

    private static string? Str(JsonProperty p) => p.Value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => p.Value.GetString(),
        _ => throw new ArgumentException($"{p.Name} must be text.")
    };
    private static bool Bool(JsonProperty p) => p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? p.Value.GetBoolean() : throw new ArgumentException($"{p.Name} must be true or false.");
    private static int Int(JsonProperty p) => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var i)
        ? i : throw new ArgumentException($"{p.Name} must be a whole number.");

    // ------------------------------------------------------------------ the file

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads a <c>slot.json</c>. Older files only have <c>roomId</c>. Throws <see cref="JsonException"/> if it can't be read.</summary>
    public static SlotSettings FromJson(string text)
    {
        var s = JsonSerializer.Deserialize<SlotSettings>(text, Json) ?? new SlotSettings();
        s = s with { Crash = s.Crash ?? new CrashPolicy() };
        return s;
    }

    /// <summary>Writes the file in one step, so a crash can't leave half of it.</summary>
    public static void Save(string path, SlotSettings s)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, s.ToJson());
        File.Move(tmp, path, true);
    }
}
