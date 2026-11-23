using OpenVP.Core.Plugins;

namespace HelloPlugin;

/// <summary>
/// The smallest useful plugin. It shows each hook once:
///   Initialize         register a console command and two routes, and subscribe to slot 1
///   OnStarted/OnStopped  log
///   OnSignalChanged    count what happens in slot 1
/// Every method runs on the plugin's own thread. If one throws, OpenVP unloads and disables the plugin and carries on.
/// </summary>
public sealed class HelloPlugin : IOpenVpPlugin
{
    private IPluginContext _ctx = null!;
    private long _changes;
    private PluginSignalChange? _last;

    public void Initialize(IPluginContext context)
    {
        _ctx = context;
        context.AddConsoleCommand("hello", "Say hello from the example plugin", args => $"Hello from the {context.Name} plugin! {(args.Length > 0 ? "You said: " + args : "")}Slot 1 has had {Interlocked.Read(ref _changes)} signal changes.");
        // Handy for seeing what happens when a plugin fails: this one is switched off and the processor keeps running.
        context.AddConsoleCommand("hello-crash", "Make the example plugin throw (it will be disabled)", _ => throw new InvalidOperationException("hello-crash: this plugin failed on purpose"));
        context.AddRoute("GET", "greeting", _ => PluginResponse.Json(new { message = "Hello from the example plugin", plugin = context.Name, slots = context.SlotCount }));
        context.AddRoute("GET", "stats", _ => PluginResponse.Json(new { changes = Interlocked.Read(ref _changes), last = _last }));
        context.AddRoute("POST", "echo", r => PluginResponse.Json(new { you = r.User, said = r.Body }));
        context.SubscribeSlot(1);
        context.Log("Notice", "Initialized");
    }

    public void OnStarted() => _ctx.Log("Notice", "OpenVP is up");

    public void OnStopped() => _ctx.Log("Notice", "Stopping after " + Interlocked.Read(ref _changes) + " signal changes");

    public void OnSignalChanged(PluginSignalChange change)
    {
        Interlocked.Increment(ref _changes);
        _last = change;
    }
}
