using System.IO.Compression;
using System.Reflection;
using OpenVP.Core;

namespace OpenVP.SimplSharpHost;

/// <summary>Loads SIMPL# Pro programs: a .cpz archive or the program .dll itself.</summary>
public sealed class SimplSharpLoader : IProgramLoader
{
    private readonly string _shimDir;

    public SimplSharpLoader(string? shimDir = null)
    {
        _shimDir = shimDir ?? AppContext.BaseDirectory;
    }

    public bool CanLoad(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cpz" or ".dll" or ".zip";
    }

    public IProgramRuntime Load(string path, ProgramSlot slot)
    {
        var appDir = Path.Combine(slot.Directory, "app");
        if (Directory.Exists(appDir)) Directory.Delete(appDir, recursive: true);
        Directory.CreateDirectory(appDir);

        string programDll;
        if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            // Copy the dll and anything next to it (its own dependencies).
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path))!, "*.dll"))
                File.Copy(f, Path.Combine(appDir, Path.GetFileName(f)), overwrite: true);
            programDll = Path.Combine(appDir, Path.GetFileName(path));
        }
        else
        {
            ZipFile.ExtractToDirectory(path, appDir, overwriteFiles: true);
            programDll = Directory.GetFiles(appDir, "*.dll", SearchOption.AllDirectories)
                .FirstOrDefault(CompatScanner.DefinesControlSystem)
                ?? throw new InvalidDataException("No class deriving from CrestronControlSystem was found in the archive.");
        }

        return new SimplSharpRuntime(slot, programDll, _shimDir);
    }
}

/// <summary>A SIMPL# Pro program running in one slot.</summary>
public sealed class SimplSharpRuntime : IProgramRuntime
{
    private readonly ProgramSlot _slot;
    private readonly string _programDll;
    private readonly string _shimDir;
    private SlotLoadContext? _alc;
    private object? _controlSystem;
    private Type? _bridge;

    public SimplSharpRuntime(ProgramSlot slot, string programDll, string shimDir)
    {
        _slot = slot;
        _programDll = programDll;
        _shimDir = shimDir;
        ProgramName = Path.GetFileNameWithoutExtension(programDll);

        // Check compatibility up front against a reference copy of the shim.
        var shim = Assembly.LoadFrom(Path.Combine(shimDir, "OpenVP.SimplSharp.dll"));
        Compat = CompatScanner.Scan(programDll, shim);
        foreach (var t in Compat.MissingTypes)
        {
            slot.Log("Warning", $"Not implemented in OpenVP yet: type {t}");
            slot.ReportUnsupported("SIMPL# type", t, program: ProgramName);
        }
        foreach (var m in Compat.MissingMembers)
        {
            slot.Log("Warning", $"Not implemented in OpenVP yet: {m}");
            slot.ReportUnsupported("SIMPL# member", m, program: ProgramName);
        }
    }

    public string Kind => "SIMPL#";
    public string ProgramName { get; }
    public CompatReport Compat { get; }

    public void Start()
    {
        if (Compat.ControlSystemType == null)
            throw new InvalidOperationException($"{ProgramName}: no class deriving from CrestronControlSystem.");

        _alc = new SlotLoadContext(_slot.Number, Path.GetDirectoryName(_programDll)!, _shimDir);
        var shim = _alc.LoadFromAssemblyPath(Path.Combine(_shimDir, "OpenVP.SimplSharp.dll"));
        _bridge = shim.GetType("OpenVP.SimplSharp.OpenVPBridge", throwOnError: true)!;
        _bridge.GetProperty("Slot")!.SetValue(null, _slot);

        var program = _alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(_programDll))); // unlocked, so a reload can replace it
        var csType = program.GetType(Compat.ControlSystemType, throwOnError: true)!;

        _slot.Dispatcher.Invoke(() =>
        {
            try
            {
                _controlSystem = Activator.CreateInstance(csType);
                csType.GetMethod("InitializeSystem", Type.EmptyTypes)!.Invoke(_controlSystem, null);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw Explain(e.InnerException);
            }
            catch (Exception e)
            {
                throw Explain(e);
            }
        });
    }

    private Exception Explain(Exception e)
    {
        if (_alc?.Unresolved.Count > 0)
        {
            foreach (var a in _alc.Unresolved.Distinct())
                _slot.ReportUnsupported("SIMPL# assembly", a, "No facade: add it to tools/facade-names.txt and run tools/regen-facades.sh", ProgramName);
            return new InvalidOperationException(
                $"Program needs Crestron assemblies OpenVP has no facade for: {string.Join(", ", _alc.Unresolved.Distinct())}. " +
                "Add them to tools/facade-names.txt and run tools/regen-facades.sh.", e);
        }
        if (e is MissingMethodException or MissingFieldException or TypeLoadException)
        {
            _slot.ReportUnsupported("SIMPL# runtime error", e.Message, e.GetType().Name, ProgramName);
            return new InvalidOperationException($"Program uses a Crestron API OpenVP doesn't implement yet: {e.Message}", e);
        }
        return e;
    }

    public void Stop()
    {
        if (_bridge == null) return;
        try { _slot.Dispatcher.Invoke(() => _bridge.GetMethod("Shutdown")!.Invoke(null, null)); }
        catch (Exception e) { _slot.Log("Error", $"Shutdown: {e.Message}"); }
        _bridge.GetProperty("Slot")!.SetValue(null, null);
        _controlSystem = null;
        _bridge = null;
        var alc = _alc;
        _alc = null;
        alc?.Unload();
    }

    public IReadOnlyList<SignalInfo> GetSignals() => DeviceSignals.List(_slot);

    public bool TrySetSignal(string name, object value) => DeviceSignals.TrySet(_slot, name, value);

    public void Dispose() => Stop();
}

/// <summary>
/// Debugger view of a slot's devices. Names look like IPID03.out.d1 (panel press into the program)
/// and IPID03.in.s5 (program feedback to the panel).
/// </summary>
public static class DeviceSignals
{
    public static IReadOnlyList<SignalInfo> List(ProgramSlot slot)
    {
        var list = new List<SignalInfo>();
        foreach (var d in slot.Devices.OrderBy(d => d.IpId))
        {
            foreach (var (k, v) in d.InputSnapshot().OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Join))
                list.Add(new SignalInfo($"IPID{d.IpId:X2}.out.{k}", k.Kind, v));
            foreach (var (k, v) in d.FeedbackSnapshot().OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Join))
                list.Add(new SignalInfo($"IPID{d.IpId:X2}.in.{k}", k.Kind, v));
        }
        return list;
    }

    public static bool TrySet(ProgramSlot slot, string name, object value)
    {
        var parts = name.Split('.');
        if (parts.Length != 3 || !parts[0].StartsWith("IPID", StringComparison.OrdinalIgnoreCase)) return false;
        if (!uint.TryParse(parts[0][4..], System.Globalization.NumberStyles.HexNumber, null, out var ipid)) return false;
        var dev = slot.FindDevice(ipid);
        if (dev == null || parts[2].Length < 2) return false;
        var kind = JoinKey.ParseKind(parts[2][..1]);
        if (!uint.TryParse(parts[2][1..], out var join)) return false;
        var key = new JoinKey(kind, join);
        if (parts[1] == "out") dev.ReceiveInput(key, value);
        else if (parts[1] == "in") dev.SetFeedback(key, value);
        else return false;
        return true;
    }
}
