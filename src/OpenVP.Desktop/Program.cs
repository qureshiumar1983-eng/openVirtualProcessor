using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

namespace OpenVP.Desktop;

/// <summary>
/// Starts openvp.exe (the server) hidden, waits for it to answer, then opens the web console in a window.
/// Closing the last window stops the server. If an OpenVP server is already running on the port, the window
/// just connects to it and leaves it running.
/// </summary>
internal static class Program
{
    public const int Port = 8080;
    public static readonly Uri BaseUri = new($"http://localhost:{Port}/");

    /// <summary>%LOCALAPPDATA%\OpenVP: working folder for the server (its data folder and log live here).</summary>
    public static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenVP");

    public static string LogPath => Path.Combine(AppDataDir, "openvp.log");

    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, @"Local\OpenVP.Desktop", out var first);
        ApplicationConfiguration.Initialize();
        if (!first)
        {
            MessageBox.Show("OpenVP is already open.", "OpenVP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Directory.CreateDirectory(AppDataDir);
        using var server = new ServerProcess();
        try
        {
            if (!IsOpenVpRunning())
            {
                if (IsPortTaken())
                {
                    Fail($"Port {Port} is already used by another program, so OpenVP can't start.\n\nClose that program and try again.");
                    return;
                }
                var exe = Path.Combine(AppContext.BaseDirectory, "server", "openvp.exe");
                if (!File.Exists(exe))
                {
                    Fail($"Can't find the OpenVP server:\n{exe}\n\nBuild the app with publish-desktop.bat.");
                    return;
                }
                server.Start(exe, AppDataDir, LogPath);
            }
        }
        catch (Exception e)
        {
            Fail("OpenVP server didn't start: " + e.Message);
            return;
        }

        Application.Run(new MainForm(server));
    }

    /// <summary>True if something on the port answers like an OpenVP server.</summary>
    public static bool IsOpenVpRunning()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var r = http.GetAsync(new Uri(BaseUri, "api/info")).GetAwaiter().GetResult();
            return r.IsSuccessStatusCode && r.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("OpenVP");
        }
        catch { return false; }
    }

    private static bool IsPortTaken()
    {
        try
        {
            using var c = new TcpClient();
            return c.ConnectAsync("127.0.0.1", Port).Wait(500) && c.Connected;
        }
        catch { return false; }
    }

    public static void Fail(string message) =>
        MessageBox.Show(message, "OpenVP", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
