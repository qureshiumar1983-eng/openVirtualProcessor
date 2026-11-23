using System.Text.RegularExpressions;

namespace OpenVP.Core;

/// <summary>One control of a generated panel: a button, slider or text box tied to a program signal.</summary>
/// <param name="Id">Stable key that saved layouts refer to (the signal name, or "IPID03.d10" for a device join).</param>
/// <param name="DriveName">The signal name to force when the control is used (for a device join, the panel-to-program side).</param>
/// <param name="FeedbackName">The signal name to read the value from (for a device join, the program-to-panel side).</param>
public sealed record AutoControl(string Id, string Label, string Kind, string Group, string DriveName, string FeedbackName, object? Value, string? Device);

public sealed record AutoGroup(string Id, string Title, string? Subtitle, IReadOnlyList<AutoControl> Controls);

public sealed record AutoPanelModel(int Slot, string Program, IReadOnlyList<AutoGroup> Groups, int ControlCount, bool Truncated);

/// <summary>
/// Builds a panel from a program's signals. Digital signals become buttons with feedback, analog ones sliders with a
/// value read-out, serial ones a text box with the current text. Controls are labelled from the signal names (the
/// <c>.sig</c> names for SIMPL Windows programs) and grouped: first by device (the IP ID for a SIMPL# program's panels,
/// the device symbol a signal is wired to for a SIMPL Windows one), then by a shared name prefix ("Lobby_Light_On" and
/// "Lobby_Light_Off" go under "Lobby"), and what's left under "Other".
/// </summary>
public static class AutoPanel
{
    /// <summary>More controls than this and the panel is cut off (a browser can't usefully draw 50,000 buttons); the model says so.</summary>
    public const int MaxControls = 4000;

    private static readonly Regex DeviceJoin = new(@"^IPID(?<ip>[0-9A-Fa-f]{1,2})\.(?<dir>in|out)\.(?:so(?<so>\d+)\.)?(?<k>[dan s])(?<j>\d+)$".Replace(" ", ""), RegexOptions.Compiled);
    private static readonly char[] Separators = ['_', '.', ':', '/', '-', ' ', '\\'];

    public static string KindName(SigKind k) => k switch { SigKind.Digital => "digital", SigKind.Analog => "analog", _ => "serial" };

    /// <param name="deviceTypes">For device-join signals: what each IP ID is ("XPanel", "Touch panel"), for the group subtitle.</param>
    /// <param name="deviceOf">For named signals: the device a signal is wired to, if the program says (null = not a device pin).</param>
    public static AutoPanelModel Build(int slot, string program, IReadOnlyList<SignalInfo> signals,
        IReadOnlyDictionary<uint, string>? deviceTypes = null, Func<string, string?>? deviceOf = null)
    {
        var controls = new List<AutoControl>();
        var joins = new Dictionary<string, (SignalInfo? In, SignalInfo? Out, uint Ip, uint So, SigKind Kind, uint Join)>();
        var order = new List<string>();
        var named = new List<SignalInfo>();
        foreach (var s in signals)
        {
            var m = DeviceJoin.Match(s.Name);
            if (!m.Success) { named.Add(s); continue; }
            var ip = Convert.ToUInt32(m.Groups["ip"].Value, 16);
            var so = m.Groups["so"].Success ? uint.Parse(m.Groups["so"].Value) : 0u;
            var kind = JoinKey.ParseKind(m.Groups["k"].Value);
            var join = uint.Parse(m.Groups["j"].Value);
            var key = $"IPID{ip:X2}.{(so != 0 ? "so" + so + "." : "")}{JoinKey.KindLetter(kind)}{join}";
            if (!joins.TryGetValue(key, out var e)) { e = (null, null, ip, so, kind, join); order.Add(key); }
            if (m.Groups["dir"].Value == "in") e.In = s; else e.Out = s;
            joins[key] = e;
        }

        var groups = new List<AutoGroup>();
        // Device joins: one group per IP ID (and smart object), joins in numeric order, digital then analog then serial.
        foreach (var byIp in order.Select(k => (key: k, j: joins[k])).GroupBy(x => x.j.Ip).OrderBy(g => g.Key))
        {
            deviceTypes?.TryGetValue(byIp.Key, out var type);
            var title = $"IP ID {byIp.Key:X2}";
            var sub = deviceTypes != null && deviceTypes.TryGetValue(byIp.Key, out var t) ? t : null;
            var list = new List<AutoControl>();
            foreach (var (key, j) in byIp.OrderBy(x => x.j.So).ThenBy(x => x.j.Kind).ThenBy(x => x.j.Join))
            {
                var drive = j.Out?.Name ?? $"IPID{j.Ip:X2}.out.{(j.So != 0 ? "so" + j.So + "." : "")}{JoinKey.KindLetter(j.Kind)}{j.Join}";
                var fb = j.In?.Name ?? $"IPID{j.Ip:X2}.in.{(j.So != 0 ? "so" + j.So + "." : "")}{JoinKey.KindLetter(j.Kind)}{j.Join}";
                var label = $"{(j.So != 0 ? $"Smart object {j.So} · " : "")}{char.ToUpperInvariant(KindName(j.Kind)[0])}{KindName(j.Kind)[1..]} {j.Join}";
                list.Add(new AutoControl(key, label, KindName(j.Kind), $"ipid{byIp.Key:X2}", drive, fb, (j.In ?? j.Out)?.Value, $"IPID{byIp.Key:X2}"));
            }
            groups.Add(new AutoGroup($"ipid{byIp.Key:X2}", title, sub, list));
        }

        // Named signals: device first, then prefix.
        var byDevice = new Dictionary<string, List<SignalInfo>>(StringComparer.OrdinalIgnoreCase);
        var rest = new List<SignalInfo>();
        foreach (var s in named)
        {
            var dev = deviceOf?.Invoke(s.Name);
            if (!string.IsNullOrWhiteSpace(dev)) { if (!byDevice.TryGetValue(dev, out var l)) byDevice[dev] = l = new(); l.Add(s); }
            else rest.Add(s);
        }
        foreach (var (dev, list) in byDevice.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            groups.Add(MakeGroup("dev:" + dev, dev, "device", list, stripPrefix: null));

        var prefixes = rest.GroupBy(s => Prefix(s.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var loose = new List<SignalInfo>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (prefix, list) in prefixes.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            // A prefix shared by one signal isn't a group, and a prefix that swallows everything tells you nothing.
            if (list.Count < 2 || (prefixes.Count == 1 && list.Count == rest.Count && rest.Count > 12)) continue;
            groups.Add(MakeGroup("pfx:" + prefix, prefix, "name prefix", list, prefix));
            foreach (var s in list) placed.Add(s.Name);
        }
        loose.AddRange(rest.Where(s => !placed.Contains(s.Name)));
        if (loose.Count > 0) groups.Add(MakeGroup("other", "Other", null, loose, null));

        var total = groups.Sum(g => g.Controls.Count);
        var truncated = false;
        if (total > MaxControls)
        {
            truncated = true;
            var left = MaxControls;
            var cut = new List<AutoGroup>();
            foreach (var g in groups)
            {
                if (left <= 0) break;
                var take = g.Controls.Take(left).ToList();
                left -= take.Count;
                cut.Add(g with { Controls = take });
            }
            groups = cut;
            total = MaxControls;
        }
        return new AutoPanelModel(slot, program, groups, total, truncated);
    }

    private static AutoGroup MakeGroup(string id, string title, string? subtitle, List<SignalInfo> list, string? stripPrefix)
    {
        var controls = list.OrderBy(s => s.Kind).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new AutoControl(s.Name, Label(s.Name, stripPrefix), KindName(s.Kind), id, s.Name, s.Name, s.Value, null)).ToList();
        return new AutoGroup(id, title, subtitle, controls);
    }

    /// <summary>The first word of a signal name ("Lobby" for "Lobby_Light_On"), or "" when it has none worth grouping by.</summary>
    public static string Prefix(string name)
    {
        var i = name.IndexOfAny(Separators);
        if (i <= 1) return "";
        var p = name[..i];
        return p.All(char.IsDigit) ? "" : p;
    }

    /// <summary>"Lobby_Light_On" → "Light On" under the "Lobby" group; "AV1_Power" → "Power"; plain names are only tidied.</summary>
    public static string Label(string name, string? stripPrefix)
    {
        var n = name;
        if (stripPrefix != null && n.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase) && n.Length > stripPrefix.Length + 1
            && Array.IndexOf(Separators, n[stripPrefix.Length]) >= 0)
            n = n[(stripPrefix.Length + 1)..];
        n = n.Replace('_', ' ').Trim();
        return n.Length == 0 ? name : n;
    }
}
