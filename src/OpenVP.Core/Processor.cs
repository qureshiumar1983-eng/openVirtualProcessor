using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// The virtual processor: up to N program slots, a shared log, per-slot signal traces for the debugger,
/// and the intersystem bus that links EISC endpoints between slots.
/// </summary>
public sealed class Processor : IDisposable
{
    private readonly List<IProgramLoader> _loaders = new();
    private readonly ConcurrentQueue<LogEntry> _log = new();
    private const int MaxLogEntries = 5000;

    public Processor(string dataDirectory, int slotCount = 10) : this(dataDirectory, slotCount, 0) { }

    /// <param name="slotSpinMicroseconds">How long each slot thread keeps polling its queue before it blocks
    /// (<c>OpenVP:SlotSpinMicroseconds</c>). 0 keeps the default behavior; see <see cref="SlotDispatcher"/>.</param>
    public Processor(string dataDirectory, int slotCount, int slotSpinMicroseconds)
    {
        SlotSpinMicroseconds = Math.Clamp(slotSpinMicroseconds, 0, 10_000);
        DataDirectory = Path.GetFullPath(dataDirectory);
        System.IO.Directory.CreateDirectory(DataDirectory);
        Slots = Enumerable.Range(1, slotCount).Select(n => new ProgramSlot(this, n)).ToArray();
        Bus = new IntersystemBus
        {
            DescribeSlot = n => n >= 1 && n <= Slots.Count && Slots[n - 1].Runtime is { } rt ? $"slot {n} · {rt.ProgramName}" : $"slot {n}"
        };
        Metrics = new MetricsSampler(this);
    }

    /// <summary>CPU, memory, threads and per-slot activity, sampled once a second.</summary>
    public MetricsSampler Metrics { get; }

    public string DataDirectory { get; }
    public int SlotSpinMicroseconds { get; }
    public IReadOnlyList<ProgramSlot> Slots { get; }
    public IntersystemBus Bus { get; }

    public event Action<LogEntry>? LogWritten;
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
            SlotFiles.EnsureRoots(slot.Directory);
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

    internal void NotifyStepChanged(ProgramSlot s) => StepChanged?.Invoke(s);
    internal void NotifyDeviceRegistered(JoinDevice d) => DeviceRegistered?.Invoke(d);
    internal void NotifyDeviceUnregistered(JoinDevice d) => DeviceUnregistered?.Invoke(d);

    public void Dispose()
    {
        Metrics.Dispose();
        foreach (var s in Slots) s.Dispose();
    }
}
