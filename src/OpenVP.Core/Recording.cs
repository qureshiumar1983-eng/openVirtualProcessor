using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>
/// What a slot's program was sent and what it answered, for one stretch of time. Saved as JSON
/// (<c>data/recordings/slotNN/&lt;id&gt;.json</c>) and replayed by <see cref="RecordingReplayer"/>.
/// Times are milliseconds since the recording started.
/// </summary>
public sealed class Recording
{
    public const int CurrentFormat = 1;
    /// <summary>Most events of one kind a recording keeps; recording stops by itself when it is reached.</summary>
    public const int MaxEvents = 200_000;

    public int Format { get; set; } = CurrentFormat;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The slot it was recorded in. A recording can be replayed in any slot whose program has the same IP IDs.</summary>
    public int Slot { get; set; }
    public string? Program { get; set; }
    public string? ProgramFile { get; set; }
    public string? ProgramKind { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long DurationMs { get; set; }
    /// <summary>"stopped", "program unloaded" or "limit reached".</summary>
    public string EndedBy { get; set; } = "stopped";
    public RecordingCounts Counts { get; set; } = new();
    /// <summary>Each device's feedback when recording started and when it stopped.</summary>
    public List<RecordedDevice> Start { get; set; } = new();
    /// <summary>The input each device held when recording started (what a panel last sent), so a replay can put them back first.</summary>
    public List<RecordedDevice> StartInputs { get; set; } = new();
    public List<RecordedDevice> End { get; set; } = new();
    public List<RecordedInput> Inputs { get; set; } = new();
    public List<RecordedForce> Forces { get; set; } = new();
    public List<RecordedFeedback> Feedback { get; set; } = new();

    public void UpdateCounts() => Counts = new RecordingCounts
    {
        Inputs = Inputs.Count(i => i.Source == RecordedInput.PanelSource),
        EiscInputs = Inputs.Count(i => i.Source == RecordedInput.EiscSource),
        Forces = Forces.Count,
        Feedback = Feedback.Count
    };
}

public sealed class RecordingCounts
{
    /// <summary>Inputs from panels (browser, CIP).</summary>
    public int Inputs { get; set; }
    public int EiscInputs { get; set; }
    public int Forces { get; set; }
    public int Feedback { get; set; }
}

/// <summary>A device's join values at one moment. Only joins that had been set are listed.</summary>
public sealed class RecordedDevice
{
    public uint Ip { get; set; }
    public string? Type { get; set; }
    public List<RecordedJoin> Joins { get; set; } = new();
}

public class RecordedJoin
{
    /// <summary>"d", "a" or "s".</summary>
    public string Kind { get; set; } = "d";
    public uint Join { get; set; }
    /// <summary>Smart Graphics object ID (0 for the device's own joins).</summary>
    public uint So { get; set; }
    /// <summary>true/false, a number 0-65535, or text.</summary>
    public object? Value { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public JoinKey Key => new(JoinKey.ParseKind(Kind), Join, So);

    public static RecordedJoin From(JoinKey key, object value) =>
        new() { Kind = JoinKey.KindLetter(key.Kind).ToString(), Join = key.Join, So = key.SmartObject, Value = value };

    public string Describe() => (So == 0 ? "" : $"so{So}.") + $"{Kind}{Join}";
}

/// <summary>An input to the program from a device: a press, a level or text.</summary>
public sealed class RecordedInput : RecordedJoin
{
    public const string PanelSource = "panel", EiscSource = "eisc";
    public long T { get; set; }
    public int Seq { get; set; }
    public uint Ip { get; set; }
    /// <summary>"panel" or "eisc".</summary>
    public string Source { get; set; } = PanelSource;
}

/// <summary>A signal forced in the debugger.</summary>
public sealed class RecordedForce
{
    public long T { get; set; }
    public int Seq { get; set; }
    public string Signal { get; set; } = "";
    public bool Pulse { get; set; }
    /// <summary>true/false, a whole number or text. Null for a pulse.</summary>
    public object? Value { get; set; }
}

/// <summary>A feedback change the program sent to a device.</summary>
public sealed class RecordedFeedback : RecordedJoin
{
    public long T { get; set; }
    public int Seq { get; set; }
    public uint Ip { get; set; }
}

/// <summary>A recording file that can't be used.</summary>
public sealed class RecordingFormatException(string message) : Exception(message);

public static class RecordingJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(Recording r) => JsonSerializer.Serialize(r, Options);

    public static byte[] SerializeBytes(Recording r) => JsonSerializer.SerializeToUtf8Bytes(r, Options);

    /// <summary>Reads and checks a recording. Throws <see cref="RecordingFormatException"/> with a message fit to show the user.</summary>
    public static Recording Parse(string json)
    {
        Recording? r;
        try { r = JsonSerializer.Deserialize<Recording>(json, Options); }
        catch (JsonException e) { throw new RecordingFormatException($"That isn't a recording file: {e.Message}"); }
        if (r == null) throw new RecordingFormatException("That isn't a recording file.");
        Normalize(r);
        return r;
    }

    public static Recording Parse(ReadOnlySpan<byte> utf8)
    {
        Recording? r;
        try { r = JsonSerializer.Deserialize<Recording>(utf8, Options); }
        catch (JsonException e) { throw new RecordingFormatException($"That isn't a recording file: {e.Message}"); }
        if (r == null) throw new RecordingFormatException("That isn't a recording file.");
        Normalize(r);
        return r;
    }

    /// <summary>Checks every field and turns JSON values into bool / ushort / string (joins) or bool / int / string (forces); sorts events by time.</summary>
    public static void Normalize(Recording r)
    {
        if (r.Format != Recording.CurrentFormat) throw new RecordingFormatException($"This recording is format {r.Format}; this OpenVP reads format {Recording.CurrentFormat}.");
        r.Start ??= new(); r.StartInputs ??= new(); r.End ??= new(); r.Inputs ??= new(); r.Forces ??= new(); r.Feedback ??= new();
        r.Name ??= ""; r.Id ??= "";
        if (r.DurationMs < 0 || r.DurationMs > 7L * 24 * 3600 * 1000) throw new RecordingFormatException("The recording's duration is out of range.");
        if (r.Inputs.Count > Recording.MaxEvents || r.Forces.Count > Recording.MaxEvents || r.Feedback.Count > Recording.MaxEvents)
            throw new RecordingFormatException($"A recording can hold at most {Recording.MaxEvents} events of each kind.");

        foreach (var d in r.Start.Concat(r.StartInputs).Concat(r.End))
        {
            if (d == null) throw new RecordingFormatException("A device entry is empty.");
            d.Joins ??= new();
            foreach (var j in d.Joins) CheckJoin(j, "device state");
        }
        foreach (var i in r.Inputs)
        {
            CheckJoin(i, "input"); CheckTime(i.T, r);
            if (i.Source != RecordedInput.PanelSource && i.Source != RecordedInput.EiscSource)
                throw new RecordingFormatException($"An input has an unknown source '{i.Source}'.");
        }
        foreach (var f in r.Feedback) { CheckJoin(f, "feedback"); CheckTime(f.T, r); }
        foreach (var f in r.Forces)
        {
            CheckTime(f.T, r);
            if (string.IsNullOrWhiteSpace(f.Signal) || f.Signal.Length > 512) throw new RecordingFormatException("A force has no signal name (or one that is too long).");
            if (f.Pulse) f.Value = null;
            else f.Value = ForceValue(f.Value);
        }
        r.Inputs = r.Inputs.OrderBy(x => x.T).ThenBy(x => x.Seq).ToList();
        r.Forces = r.Forces.OrderBy(x => x.T).ThenBy(x => x.Seq).ToList();
        r.Feedback = r.Feedback.OrderBy(x => x.T).ThenBy(x => x.Seq).ToList();
        r.UpdateCounts();
    }

    private static void CheckTime(long t, Recording r)
    {
        if (t < 0 || t > r.DurationMs + 1000) throw new RecordingFormatException($"An event at {t} ms is outside the recording ({r.DurationMs} ms).");
    }

    private static void CheckJoin(RecordedJoin j, string what)
    {
        if (j.Kind is not ("d" or "a" or "s")) throw new RecordingFormatException($"A {what} entry has an unknown join type '{j.Kind}'.");
        if (j.Join == 0 || j.Join > 65535) throw new RecordingFormatException($"A {what} entry has join number {j.Join}.");
        var kind = JoinKey.ParseKind(j.Kind);
        object? v = j.Value is JsonElement e ? Unwrap(e) : j.Value;
        if (v is null) throw new RecordingFormatException($"A {what} entry on {j.Describe()} has no value.");
        switch (kind)
        {
            case SigKind.Digital when v is not (bool or long or double or int): throw new RecordingFormatException($"{j.Describe()} ({what}) should be true or false.");
            case SigKind.Analog when v is not (long or double or int or ushort): throw new RecordingFormatException($"{j.Describe()} ({what}) should be a number.");
            case SigKind.Serial when v is not string: throw new RecordingFormatException($"{j.Describe()} ({what}) should be text.");
        }
        if (kind == SigKind.Serial && ((string)v).Length > 65536) throw new RecordingFormatException($"{j.Describe()} ({what}) is too long.");
        if (kind == SigKind.Analog && Convert.ToDouble(v) is < 0 or > 65535) throw new RecordingFormatException($"{j.Describe()} ({what}) is out of range 0-65535.");
        j.Value = SigValue.Coerce(kind, v);
    }

    private static object? Unwrap(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        _ => null
    };

    private static object ForceValue(object? value)
    {
        var v = value is JsonElement e ? Unwrap(e) : value;
        return v switch
        {
            bool or string => v,
            int i => i,
            long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
            double d => (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue),
            ushort u => (int)u,
            _ => throw new RecordingFormatException("A force has no usable value.")
        };
    }
}

/// <summary>Names and IDs for recordings. IDs are generated here and nothing else is accepted from a request.</summary>
public static class RecordingIds
{
    public const int MaxNameLength = 100;
    private static readonly Regex IdPattern = new(@"^\d{8}T\d{6}Z-[0-9a-f]{4}\z", RegexOptions.Compiled);

    /// <summary>True for an ID this class generates; anything else (paths, "..", empty) is refused.</summary>
    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);

    public static string NewId(DateTimeOffset now) =>
        $"{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{Random.Shared.Next(0x10000):x4}";

    /// <summary>Trims a name and checks it: 1-100 characters, no control characters. Throws ArgumentException.</summary>
    public static string ValidateName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) throw new ArgumentException("Give the recording a name.");
        if (n.Length > MaxNameLength) throw new ArgumentException($"A name can be at most {MaxNameLength} characters.");
        if (n.Any(char.IsControl)) throw new ArgumentException("A name can't contain control characters.");
        return n;
    }

    public static string DefaultName(string? program, DateTimeOffset time)
    {
        var p = string.IsNullOrWhiteSpace(program) ? "Recording" : new string(program.Trim().Where(c => !char.IsControl(c)).ToArray());
        var stamp = time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        if (p.Length + 1 + stamp.Length > MaxNameLength) p = p[..(MaxNameLength - 1 - stamp.Length)];
        return $"{p} {stamp}";
    }
}
