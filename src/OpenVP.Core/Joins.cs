namespace OpenVP.Core;

/// <summary>The three Crestron signal types, always in this order: digital, analog, serial.</summary>
public enum SigKind { Digital, Analog, Serial }

/// <summary>A join on a device: its type plus its 1-based join number. SmartObject is the Smart Graphics
/// object ID the join belongs to (0 = the device's own joins).</summary>
public readonly record struct JoinKey(SigKind Kind, uint Join)
{
    public JoinKey(SigKind kind, uint join, uint smartObject) : this(kind, join) { SmartObject = smartObject; }

    public uint SmartObject { get; init; }

    public override string ToString() => SmartObject == 0 ? $"{KindLetter(Kind)}{Join}" : $"so{SmartObject}.{KindLetter(Kind)}{Join}";

    public static char KindLetter(SigKind k) => k switch
    {
        SigKind.Digital => 'd',
        SigKind.Analog => 'a',
        _ => 's'
    };

    public static SigKind ParseKind(string s) => s.ToLowerInvariant() switch
    {
        "b" or "d" or "bool" or "digital" => SigKind.Digital,
        "n" or "a" or "ushort" or "analog" or "number" => SigKind.Analog,
        "s" or "string" or "serial" => SigKind.Serial,
        _ => throw new ArgumentException($"Unknown signal type '{s}'")
    };
}

/// <summary>Helpers to keep join values in their canonical CLR types: bool, ushort, string.</summary>
public static class SigValue
{
    public static object Default(SigKind kind) => kind switch
    {
        SigKind.Digital => false,
        SigKind.Analog => (ushort)0,
        _ => string.Empty
    };

    public static object Coerce(SigKind kind, object? value)
    {
        switch (kind)
        {
            case SigKind.Digital:
                return value switch
                {
                    bool b => b,
                    string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
                    null => false,
                    _ => Convert.ToInt64(value) != 0
                };
            case SigKind.Analog:
                return value switch
                {
                    ushort u => u,
                    bool b => (ushort)(b ? 1 : 0),
                    string s => ushort.TryParse(s, out var u2) ? u2 : (ushort)0,
                    null => (ushort)0,
                    _ => (ushort)Math.Clamp(Convert.ToInt64(value), 0, ushort.MaxValue)
                };
            default:
                return value?.ToString() ?? string.Empty;
        }
    }

    public static bool AreEqual(object a, object b) => Equals(a, b);
}
