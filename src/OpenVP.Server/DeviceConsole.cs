using System.Text;
using System.Text.RegularExpressions;
using OpenVP.Core;
using OpenVP.Panels;
using OpenVP.SimplSharp.SshCore;

namespace OpenVP.Server;

/// <summary>
/// "Log in and read" for the IP table: opens an SSH console on a connected Crestron device with credentials
/// the user types in, runs a fixed list of read-only commands, and files what it learns on the connection.
/// The credentials are used for this one login and are never stored or logged. Only the commands below
/// are ever sent, so this cannot change anything on the device.
/// </summary>
public static partial class DeviceConsole
{
    /// <summary>Read-only Crestron console commands. Devices that don't know one just answer "Bad or incomplete command".</summary>
    public static readonly string[] Commands = { "hostname", "ver", "uptime", "ipconfig", "ipt", "time" };

    public sealed record Result(bool Ok, string Message, IReadOnlyList<(string Command, string Output)> Outputs);

    public static async Task<Result> ReadAsync(DeviceConnection c, string username, string password, int port = 22, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(c.RemoteAddress)) return new Result(false, "This connection has no network address to log in to.", []);
        c.ProbeStatus = $"logging in to {c.RemoteAddress}:{port}…";
        try
        {
            var outputs = await Task.Run(() => Run(c.RemoteAddress, port, username, password, ct), ct);
            Apply(c, outputs);
            c.ProbeStatus = $"read over SSH as '{username}' at {DateTimeOffset.Now:HH:mm:ss}";
            c.LastProbe = DateTimeOffset.Now;
            return new Result(true, $"Read {outputs.Count} commands from {c.RemoteAddress}.", outputs);
        }
        catch (Exception e)
        {
            var msg = e is AggregateException { InnerException: { } inner } ? inner.Message : e.Message;
            c.ProbeStatus = $"login failed: {msg}";
            return new Result(false, $"Could not log in to {c.RemoteAddress}:{port}: {msg}", []);
        }
    }

    private static List<(string, string)> Run(string host, int port, string user, string password, CancellationToken ct)
    {
        using var ssh = new SshConnection();
        var buffer = new StringBuilder();
        var gate = new object();
        var lastData = DateTime.UtcNow;
        ssh.DataReceived += data =>
        {
            lock (gate) { buffer.Append(Encoding.UTF8.GetString(data)); lastData = DateTime.UtcNow; }
        };
        ssh.Connect(host, port, user, password, tryKeyboardInteractive: true, TimeSpan.FromSeconds(8));
        ssh.OpenShell("vt100", 200, 50, 0, 0);

        string Drain(TimeSpan max)
        {
            var until = DateTime.UtcNow + max;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                Thread.Sleep(100);
                lock (gate)
                {
                    var text = buffer.ToString();
                    var quiet = DateTime.UtcNow - lastData > TimeSpan.FromMilliseconds(700);
                    if (quiet && (PromptAtEnd().IsMatch(text) || DateTime.UtcNow - lastData > TimeSpan.FromSeconds(2))) break;
                }
            }
            lock (gate) { var s = buffer.ToString(); buffer.Clear(); return s; }
        }

        var banner = Drain(TimeSpan.FromSeconds(4));
        var results = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(banner)) results.Add(("(login banner)", Clean(banner, null)));
        foreach (var cmd in Commands)
        {
            ct.ThrowIfCancellationRequested();
            ssh.Send(Encoding.ASCII.GetBytes(cmd + "\r\n"));
            results.Add((cmd, Clean(Drain(TimeSpan.FromSeconds(6)), cmd)));
        }
        return results;
    }

    /// <summary>Strip terminal codes, the echoed command and the trailing prompt.</summary>
    private static string Clean(string text, string? cmd)
    {
        text = AnsiCodes().Replace(text, "").Replace("\r", "");
        var lines = text.Split('\n').ToList();
        if (cmd != null && lines.Count > 0 && lines[0].TrimEnd().EndsWith(cmd, StringComparison.OrdinalIgnoreCase)) lines.RemoveAt(0);
        while (lines.Count > 0 && (string.IsNullOrWhiteSpace(lines[^1]) || PromptAtEnd().IsMatch(lines[^1]))) lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).Trim('\n');
    }

    /// <summary>File the raw output and pull out the fields the IP table shows.</summary>
    public static void Apply(DeviceConnection c, IEnumerable<(string Command, string Output)> outputs)
    {
        foreach (var (cmd, output) in outputs)
        {
            c.SetDetail($"Console: {cmd}", output);
            if (output.Contains("Bad or incomplete command", StringComparison.OrdinalIgnoreCase)) continue;
            switch (cmd)
            {
                case "hostname":
                    var h = HostnameLine().Match(output);
                    c.Hostname = h.Success ? h.Groups[1].Value : output.Split('\n').FirstOrDefault()?.Trim() is { Length: > 0 and < 64 } line ? line : c.Hostname;
                    break;
                case "ver":
                    var first = output.Split('\n').FirstOrDefault(l => l.Contains('[')) ?? output.Split('\n').FirstOrDefault() ?? "";
                    var v = VerLine().Match(first);
                    if (v.Success)
                    {
                        c.Model = v.Groups["model"].Value.Trim();
                        c.Firmware = v.Groups["fw"].Value;
                        c.ClientKind = ClientIdentifier.DescribeModel(c.Model, c.Transport);
                    }
                    var mac = MacLine().Match(output);
                    if (mac.Success) c.MacAddress ??= FormatMac(mac.Groups[1].Value);
                    break;
                case "uptime":
                    c.SetDetail("Uptime", output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim());
                    break;
                case "ipconfig":
                    var m = IpconfigMac().Match(output);
                    if (m.Success) c.MacAddress = FormatMac(m.Groups[1].Value);
                    break;
            }
        }
    }

    private static string FormatMac(string raw)
    {
        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 ? string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant() : raw;
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]|\x1B[()][A-Za-z0-9]|\x07")]
    private static partial Regex AnsiCodes();

    [GeneratedRegex(@"[\w.\-]+>\s*$")]
    private static partial Regex PromptAtEnd();

    [GeneratedRegex(@"Host\s*Name\s*:\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex HostnameLine();

    [GeneratedRegex(@"^(?<model>[^\[]+?)\s*(?:Cntrl Eng|Control Engine)?\s*\[\s*v?(?<fw>[0-9][^\s,\]\(]*)", RegexOptions.IgnoreCase)]
    private static partial Regex VerLine();

    [GeneratedRegex(@"@E-([0-9A-Fa-f]{12})")]
    private static partial Regex MacLine();

    [GeneratedRegex(@"MAC Address(?:\(es\))?[ .:]*([0-9A-Fa-f]{2}(?:[.:\-][0-9A-Fa-f]{2}){5})", RegexOptions.IgnoreCase)]
    private static partial Regex IpconfigMac();
}
