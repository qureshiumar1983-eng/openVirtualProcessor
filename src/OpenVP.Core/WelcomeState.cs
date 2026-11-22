using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// Remembers, on the server and for everyone, whether the first-run Welcome screen has been finished or
/// skipped. Kept in a small file in the data folder (<c>welcome.json</c>), so it doesn't come back on another
/// PC or in the desktop app.
/// </summary>
public sealed class WelcomeState
{
    private readonly string _file;
    private readonly object _lock = new();
    private bool _dismissed;

    public WelcomeState(string dataDirectory)
    {
        _file = Path.Combine(dataDirectory, "welcome.json");
        try
        {
            if (File.Exists(_file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_file));
                _dismissed = doc.RootElement.TryGetProperty("dismissed", out var d) && d.ValueKind == JsonValueKind.True;
            }
        }
        catch { _dismissed = false; } // unreadable file: show the welcome again, it's harmless
    }

    /// <summary>True until someone clicks Get started or Skip.</summary>
    public bool Show { get { lock (_lock) return !_dismissed; } }

    public void Dismiss()
    {
        lock (_lock)
        {
            if (_dismissed) return;
            _dismissed = true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllText(_file, JsonSerializer.Serialize(new { dismissed = true, at = DateTime.UtcNow }));
            }
            catch { /* can't save: it stays dismissed until the server restarts */ }
        }
    }
}
