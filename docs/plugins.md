# Plugins

A Labs feature: switch **Plugins** on in Labs (Admin), then enable each plugin on the Plugins page. Nothing loads otherwise.

> **A plugin is not sandboxed.** It is ordinary .NET code (or a web page) running inside the OpenVP process with all of OpenVP's rights. The permissions in `plugin.json` decide what OpenVP's plugin API will do for it; they cannot stop code that calls the .NET libraries directly. Enable only plugins whose source you have read or whose author you trust. A plugin's page runs inside the console with the viewer's permissions.

## A plugin folder

```
data/plugins/hello/
  plugin.json
  Hello.dll          optional: the .NET assembly
  ui/index.html      optional: a page for the console
```

`plugin.json`:

```json
{
  "name": "hello",
  "version": "1.0.0",
  "author": "You",
  "description": "What it does, in a sentence.",
  "permissions": ["signals.read", "console", "routes", "ui"],
  "assembly": "Hello.dll",
  "entry": "HelloPlugin.HelloPlugin",
  "ui": { "page": "index.html", "menu": "Hello plugin" }
}
```

- `name` is the folder name: 1 to 32 lowercase letters, digits, `-` or `_`.
- `assembly` (a `.dll` in the folder) and/or `ui` must be present. `entry` names the class when the assembly has more than one implementation.
- `permissions` are what the plugin asks for. The Admin sees them and approves them all when enabling. If a new version asks for more, it stays off until it is enabled again.

| Permission | Lets the plugin |
|---|---|
| `signals.read` | read the signals of any slot and subscribe to changes |
| `signals.write` | set signals, like the debugger |
| `console` | add commands to the web and telnet/SSH consoles |
| `routes` | add REST routes under `/api/plugins/<name>/` |
| `ui` | show its `ui/` page in the console and a link in the top bar |

## The assembly

Reference `OpenVP.Core` (it is the host's own copy at run time, so don't ship it) and implement `OpenVP.Core.Plugins.IOpenVpPlugin`:

```csharp
public sealed class HelloPlugin : IOpenVpPlugin
{
    public void Initialize(IPluginContext c)
    {
        c.AddConsoleCommand("hello", "Say hello", args => "Hello! " + args);
        c.AddRoute("GET", "greeting", _ => PluginResponse.Json(new { message = "hi" }));
        c.SubscribeSlot(1);
    }
    public void OnStarted() { }
    public void OnStopped() { }
    public void OnSignalChanged(PluginSignalChange change) { }
}
```

All methods run on the plugin's own thread, one at a time. Never on a slot thread or a web request thread. The plugin gets a data folder (`IPluginContext.DataDirectory`, under `data/plugin-data/<name>/`) and a log (`Log`), which shows on the Plugins page.

Console commands work in every slot's console and in the telnet/SSH console. A route answers `GET` for anyone who can view and `POST`, `PUT`, `DELETE` for anyone with Operate. The route name `ui` is reserved.

## When a plugin fails

An exception from any hook, a missing permission, or a call that doesn't return in 10 seconds unloads the plugin and disables it. The reason shows on the Plugins page (status *failed and was disabled*) and in the activity feed, the plugin's commands and routes disappear, and OpenVP carries on. It stays off after a restart until an Admin enables it again. A plugin that hangs forever keeps its own thread; restart OpenVP to free it.

## The example

`examples/plugins/hello` has one of everything, including a `hello-crash` command that throws on purpose.

```bash
dotnet build examples/plugins/hello -c Release -o data/plugins/hello
```

Then Labs → Plugins on, Plugins → Enable. Try `hello there` in the web console, `GET /api/plugins/hello/greeting`, and `hello-crash`.

## A page-only plugin

Leave out `assembly`, keep `ui`, and put `index.html` in `ui/`. It is served at `/api/plugins/<name>/ui/index.html` and, if `ui.menu` is set, linked from the console while the plugin is enabled.
