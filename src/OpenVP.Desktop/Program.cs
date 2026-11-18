using System.Net.Http;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using OpenVP.Server;

namespace OpenVP.Desktop;

/// <summary>
/// Starts the OpenVP server inside this process, then opens the web console in a window. Closing the main
/// window stops the server. If an OpenVP server is already running on the port (say, one started from the
/// command line), the window just connects to it.
/// </summary>
internal static class Program
{
    public const int Port = 8080;
    public static readonly Uri BaseUri = new($"http://localhost:{Port}/");

    /// <summary>%LOCALAPPDATA%\OpenVP: the data folder, the server log and the browser profile live here.</summary>
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
        using var log = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        Console.SetOut(TextWriter.Synchronized(log));
        Console.SetError(Console.Out);

        WebApplication? server = null;
        if (!IsOpenVpRunning())
        {
            if (IsPortTaken())
            {
                Fail($"Port {Port} is already used by another program, so OpenVP can't start.\n\nClose that program and try again.");
                return;
            }
            try
            {
                server = OpenVpServer.Create([
                    $"--urls=http://0.0.0.0:{Port}",
                    $"--OpenVP:DataDirectory={Path.Combine(AppDataDir, "data")}",
                ]);
                server.StartAsync().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Fail($"The OpenVP server didn't start:\n{e.Message}\n\nLog: {LogPath}");
                return;
            }
        }

        try
        {
            Application.Run(new MainForm());
        }
        finally
        {
            if (server != null)
            {
                try { server.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token).GetAwaiter().GetResult(); } catch { }
                try { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
        }
    }

    /// <summary>True if something on the port answers like an OpenVP server.</summary>
    private static bool IsOpenVpRunning()
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
