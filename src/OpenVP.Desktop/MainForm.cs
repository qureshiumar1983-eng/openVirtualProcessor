using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenVP.Desktop;

/// <summary>A window showing one OpenVP page. The first one is the main window; links that open a new
/// window (test panels, the debugger) get their own BrowserForm sharing the same browser profile.</summary>
internal class BrowserForm : Form
{
    protected readonly WebView2 Web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(32, 32, 32) };
    private static CoreWebView2Environment? _env;

    public BrowserForm()
    {
        Text = "OpenVP";
        Icon = LoadIcon();
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        Size = new Size(1100, 800);
        Controls.Add(Web);
    }

    protected static async Task<CoreWebView2Environment> EnvironmentAsync() =>
        _env ??= await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.AppDataDir, "WebView2"));

    protected async Task InitWebAsync()
    {
        await Web.EnsureCoreWebView2Async(await EnvironmentAsync());
        var core = Web.CoreWebView2;
        core.DocumentTitleChanged += (_, _) => Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "OpenVP" : core.DocumentTitle;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationStarting += (_, e) =>
        {
            // Keep the app on the OpenVP server; anything else (docs links etc.) opens in the normal browser.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && !IsOpenVp(u) && u.Scheme != "about" && u.Scheme != "data")
            {
                e.Cancel = true;
                OpenExternal(u);
            }
        };
    }

    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || !IsOpenVp(u))
        {
            e.Handled = true;
            if (u != null) OpenExternal(u);
            return;
        }
        using var deferral = e.GetDeferral();
        var popup = new BrowserForm();
        popup.Show();
        await popup.InitWebAsync();
        e.NewWindow = popup.Web.CoreWebView2;
        e.Handled = true;
    }

    private static bool IsOpenVp(Uri u) =>
        u.Scheme == Uri.UriSchemeHttp && u.IsLoopback && u.Port == Program.Port;

    private static void OpenExternal(Uri u)
    {
        if (u.Scheme is "http" or "https" or "mailto")
            try { Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true }); } catch { }
    }

    private static Icon? LoadIcon()
    {
        using var s = typeof(BrowserForm).Assembly.GetManifestResourceStream("OpenVP.Desktop.openvp.ico");
        return s == null ? null : new Icon(s);
    }
}

/// <summary>The main window. Closing it quits the app (and stops the server).</summary>
internal sealed class MainForm : BrowserForm
{
    public MainForm()
    {
        Size = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Load += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            await InitWebAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (MessageBox.Show(
                    "OpenVP needs the Microsoft Edge WebView2 Runtime, which isn't installed.\n\nOpen the download page now?",
                    "OpenVP", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
            Close();
            return;
        }
        Web.CoreWebView2.Navigate(Program.BaseUri.ToString());
    }
}
