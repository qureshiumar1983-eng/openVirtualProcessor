namespace OpenVP.Core.Plugins;

/// <summary>
/// What an OpenVP plugin implements. A plugin is a .NET assembly in <c>data/plugins/&lt;name&gt;/</c> (next to its <c>plugin.json</c>) with one public,
/// parameterless class that implements this interface. Every method runs on the plugin's own worker thread, one call at a time, never on a slot
/// thread or a web request thread, so a slow or crashing plugin can't stall or crash the processor: an exception (or a call that doesn't return
/// in 10 seconds) logs, unloads and disables the plugin.
/// </summary>
public interface IOpenVpPlugin
{
    /// <summary>Called once when the plugin is enabled. Register console commands and routes and subscribe to slots here.</summary>
    void Initialize(IPluginContext context);

    /// <summary>The processor is up (or the plugin was enabled while it runs).</summary>
    void OnStarted() { }

    /// <summary>The plugin is about to be unloaded: it was disabled, Plugins was switched off in Labs, or OpenVP is shutting down.</summary>
    void OnStopped() { }

    /// <summary>A signal changed in a slot the plugin subscribed to (needs the <c>signals.read</c> permission).</summary>
    void OnSignalChanged(PluginSignalChange change) { }
}

/// <param name="Kind">digital, analog or serial.</param>
public sealed record PluginSignalChange(int Slot, string Signal, string Kind, object Value, DateTimeOffset Time);

public sealed record PluginSignal(string Name, string Kind, object Value);

/// <summary>A request to a route the plugin added under <c>/api/plugins/&lt;name&gt;/</c>.</summary>
/// <param name="Path">The part after the plugin's name, without a leading slash.</param>
public sealed record PluginRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, string Body, string User);

public sealed record PluginResponse(int Status, string Body, string ContentType = "application/json; charset=utf-8")
{
    public static PluginResponse Json(object value, int status = 200) => new(status, System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    public static PluginResponse Text(string text, int status = 200) => new(status, text, "text/plain; charset=utf-8");
    public static PluginResponse NotFound(string message = "Not found.") => Json(new { error = message }, 404);
}

/// <summary>What a plugin may do. Each method needs the permission named in its comment, declared in <c>plugin.json</c> and approved by the Admin who enabled the plugin; without it the call throws, which counts as a crash.</summary>
public interface IPluginContext
{
    string Name { get; }
    /// <summary>A folder the plugin can keep files in (it exists, and survives updates of the plugin).</summary>
    string DataDirectory { get; }
    int SlotCount { get; }
    /// <summary>Writes to the OpenVP log and the plugin's page. Level: Notice, Warning or Error.</summary>
    void Log(string level, string message);
    /// <summary><c>signals.read</c>: start getting <see cref="IOpenVpPlugin.OnSignalChanged"/> for this slot.</summary>
    void SubscribeSlot(int slot);
    /// <summary><c>signals.read</c>: the signals of the slot's program.</summary>
    IReadOnlyList<PluginSignal> GetSignals(int slot);
    /// <summary><c>signals.write</c>: set a signal the way the debugger does. False if the slot has no such signal.</summary>
    bool SetSignal(int slot, string name, object value);
    /// <summary><c>console</c>: a command available in the web console and the telnet/SSH console (for any slot).</summary>
    void AddConsoleCommand(string name, string help, Func<string, string> handler);
    /// <summary><c>routes</c>: a route under <c>/api/plugins/&lt;name&gt;/&lt;path&gt;</c>. GET needs View; other methods need Operate. <paramref name="method"/> is GET, POST, PUT or DELETE.</summary>
    void AddRoute(string method, string path, Func<PluginRequest, PluginResponse> handler);
}

public static class PluginPermissions
{
    public const string SignalsRead = "signals.read", SignalsWrite = "signals.write", Console = "console", Routes = "routes", Ui = "ui";
    public static readonly IReadOnlyList<string> All = [SignalsRead, SignalsWrite, Console, Routes, Ui];

    public static string Describe(string p) => p switch
    {
        SignalsRead => "Read the signals of any slot and be told when they change",
        SignalsWrite => "Set signals in any slot, like the debugger does",
        Console => "Add commands to the web and telnet/SSH console",
        Routes => "Add REST routes under /api/plugins/<name>/",
        Ui => "Show its own page inside the console (runs with your permissions)",
        _ => p
    };
}

public sealed class PluginPermissionException(string permission) : Exception($"The plugin needs the '{permission}' permission, which it didn't declare or wasn't approved.");
