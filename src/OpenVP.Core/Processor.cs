using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// The virtual processor: up to N program slots, a shared log, a signal trace for the debugger,
/// and the intersystem bus that links EISC endpoints between slots.
/// </summary>
public sealed class Processor : IDisposable
{
    private readonly List<IProgramLoader> _loaders = new();
    private readonly ConcurrentQueue<LogEntry> _log = new();
    private const int MaxLogEntries = 5000;

    public Processor(string dataDirectory, int slotCount = 10)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        System.IO.Directory.CreateDirectory(DataDirectory);
        Slots = Enumerable.Range(1, slotCount).Select(n => new ProgramSlot(this, n)).ToArray();
        Bus = new IntersystemBus();
        Metrics = new MetricsSampler(this);
    }

    /// <summary>CPU, memory, threads and per-slot activity, sampled once a second.</summary>
    public MetricsSampler Metrics { get; }

    public string DataDirectory { get; }
    public IReadOnlyList<ProgramSlot> Slots { get; }
    public IntersystemBus Bus { get; }

    public event Action<LogEntry>? LogWritten;
    public event Action<TraceEvent>? Traced;
    /// <summary>A slot's step debugger changed mode, break list, or paused/resumed.</summary>
    public event Action<ProgramSlot>? StepChanged;
    public event Action<JoinDevice>? DeviceRegistered;
    public event Action<JoinDevice>? DeviceUnregistered;

    public ProgramSlot Slot(int number) =>
        number >= 1 && number <= Slots.Count ? Slots[number - 1] : throw new ArgumentOutOfRangeException(nameof(number), $"Slot must be 1–{Slots.Count}");

    public void AddLoader(IProgramLoader loader) => _loaders.Add(loader);

    /// <summary>Load a program file into a slot, picking the runtime by file type.</summary>
    public void LoadProgram(int slotNumber, string path, bool start = true)
    {
        var slot = Slot(slotNumber);
        var loader = _loaders.FirstOrDefault(l => l.CanLoad(path))
            ?? throw new NotSupportedException($"No runtime can load '{Path.GetFileName(path)}'. Supported: .cpz/.dll (SIMPL#), .lpz (compiled SIMPL Windows), .smw (SIMPL Windows source).");
        lock (slot.Lifecycle)
        {
            slot.Unload();
            System.IO.Directory.CreateDirectory(slot.Directory);
            IProgramRuntime runtime;
            try { runtime = loader.Load(path, slot); }
            catch (Exception e) { slot.Fail(e); throw; }
            slot.Load(path, runtime);
            if (start) slot.Start();
        }
    }

    /// <summary>
    /// Run <paramref name="action"/> while holding the slot's lifecycle lock, so nothing else can load,
    /// start, stop or unload that slot meanwhile (for example, replacing its program files).
    /// </summary>
    public T WithSlotLocked<T>(int slotNumber, Func<ProgramSlot, T> action)
    {
        var slot = Slot(slotNumber);
        lock (slot.Lifecycle) return action(slot);
    }

    public bool IsValidSlot(int number) => number >= 1 && number <= Slots.Count;

    /// <summary>Find a device by IP ID. Searches one slot, or all slots if slot is null.</summary>
    public JoinDevice? FindDevice(uint ipId, int? slot = null)
    {
        if (slot is int s) return Slot(s).FindDevice(ipId);
        return Slots.Select(x => x.FindDevice(ipId)).FirstOrDefault(d => d != null);
    }

    public IEnumerable<JoinDevice> AllDevices => Slots.SelectMany(s => s.Devices);

    public IReadOnlyList<LogEntry> RecentLog(int? slot = null, int max = 500) =>
        _log.Where(e => slot == null || e.Slot == slot).TakeLast(max).ToList();

    internal void WriteLog(LogEntry entry)
    {
        _log.Enqueue(entry);
        while (_log.Count > MaxLogEntries && _log.TryDequeue(out _)) { }
        LogWritten?.Invoke(entry);
    }

    internal void WriteTrace(TraceEvent e) => Traced?.Invoke(e);
    internal void NotifyStepChanged(ProgramSlot s) => StepChanged?.Invoke(s);
    internal void NotifyDeviceRegistered(JoinDevice d) => DeviceRegistered?.Invoke(d);
    internal void NotifyDeviceUnregistered(JoinDevice d) => DeviceUnregistered?.Invoke(d);

    public void Dispose()
    {
        Metrics.Dispose();
        foreach (var s in Slots) s.Dispose();
    }
}
