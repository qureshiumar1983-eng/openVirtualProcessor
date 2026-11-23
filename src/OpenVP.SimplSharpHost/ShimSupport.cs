using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenVP.SimplSharpHost;

/// <summary>
/// Whether an entry of the unsupported log (docs/unsupported.md) for a SIMPL# API is something the API shim implements now, so a row written
/// by an older build disappears from the file and the dashboard once the type or member is added. The server gives this to
/// <c>UnsupportedLog.SetSupportCheck</c> next to the SIMPL Windows check.
/// </summary>
public static partial class ShimSupport
{
    [GeneratedRegex(@"^(?<owner>.+?)\.(?<member>\.ctor|[^.]+)(?:\((?<n>\d+) arguments?\))?$")]
    private static partial Regex MemberName();

    private static Assembly? _shim;
    private static bool _tried;

    private static Assembly? Shim()
    {
        if (_tried) return _shim;
        _tried = true;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OpenVP.SimplSharp.dll");
            _shim = File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        catch (Exception e) when (e is IOException or BadImageFormatException) { _shim = null; }
        return _shim;
    }

    public static bool IsSupported(string kind, string name) => IsSupported(Shim(), kind, name);

    public static bool IsSupported(Assembly? shim, string kind, string name)
    {
        if (shim == null) return false;
        if (kind.Equals("SIMPL# type", StringComparison.OrdinalIgnoreCase)) return shim.GetType(name) != null;
        if (!kind.Equals("SIMPL# member", StringComparison.OrdinalIgnoreCase)) return false;
        var m = MemberName().Match(name);
        if (!m.Success) return false;
        var type = shim.GetType(m.Groups["owner"].Value);
        if (type == null) return false;
        return CompatScanner.IsMemberSupported(type, m.Groups["member"].Value, m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : null);
    }
}
