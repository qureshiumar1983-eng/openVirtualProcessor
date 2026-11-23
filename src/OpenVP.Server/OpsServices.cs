using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Core.Ops;

namespace OpenVP.Server;

/// <summary>Everything the files, backup, version, schedule, push and git endpoints use, built once in <see cref="OpenVpServer.Create"/>.</summary>
public sealed class OpsServices
{
    public required Processor Processor { get; init; }
    public required ActivityLog Activity { get; init; }
    public required Trash Trash { get; init; }
    public required SlotSnapshots Snapshots { get; init; }
    public required ProgramVersions Versions { get; init; }
    public required ProgramProfiler Profiler { get; init; }
    public required BackupService Backups { get; init; }
    public required Scheduler Scheduler { get; init; }
    public required TargetStore Targets { get; init; }
    public required PushClient PushClient { get; init; }
    public required PushManager Push { get; init; }
    public required GitConfigSync Git { get; init; }
    public required ProcessorExporter Exporter { get; init; }
    public required ProcessorImporter Importer { get; init; }
    public required SecretStore Secrets { get; init; }
    public required SemaphoreSlim[] SlotLocks { get; init; }
    public required Func<HttpContext, string> Who { get; init; }
    public required ImportPreviews Previews { get; init; }
    /// <summary>Loads files (the program first) into a slot as an upload. Call with the slot's lock held.</summary>
    public required Func<int, IReadOnlyList<(string Name, byte[] Data)>, bool, string, string, IResult> Install { get; init; }
    public required Action<int> StopWatching { get; init; }
}

/// <summary>Uploaded or backup export files waiting in <c>data/tmp/previews</c> between the preview and the import.</summary>
public sealed class ImportPreviews
{
    private readonly string _dir;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _items = new();
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public ImportPreviews(string dataDirectory)
    {
        _dir = Path.Combine(dataDirectory, "tmp", "previews");
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public string NewPath(out string id)
    {
        Directory.CreateDirectory(_dir);
        Purge();
        id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
        return Path.Combine(_dir, id + ".openvp");
    }

    public void Keep(string id) { lock (_gate) _items[id] = DateTimeOffset.UtcNow; }

    public string? Get(string? id)
    {
        if (string.IsNullOrEmpty(id) || !id.All(Uri.IsHexDigit) || id.Length != 20) return null;
        lock (_gate) { if (!_items.ContainsKey(id)) return null; }
        var p = Path.Combine(_dir, id + ".openvp");
        return File.Exists(p) ? p : null;
    }

    public void Remove(string? id)
    {
        if (Get(id) is not { } p) return;
        lock (_gate) _items.Remove(id!);
        try { File.Delete(p); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void Purge()
    {
        lock (_gate)
            foreach (var (id, t) in _items.ToList())
                if (DateTimeOffset.UtcNow - t > Ttl)
                {
                    _items.Remove(id);
                    try { File.Delete(Path.Combine(_dir, id + ".openvp")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
    }
}
