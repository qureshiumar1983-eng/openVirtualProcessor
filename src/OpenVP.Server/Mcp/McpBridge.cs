using ModelContextProtocol;
using OpenVP.Core;
using OpenVP.Core.Security;

namespace OpenVP.Server.Mcp;

/// <summary>
/// What the MCP tools (<see cref="OpenVpTools"/>) need from the server. The processor, sign-in and recordings are the same objects the
/// REST API uses; loading, starting and stopping are delegates the server fills in with the code behind its own endpoints, so a program
/// loaded through MCP gets the same snapshot, version, crash history and activity entry as one loaded from the console.
/// </summary>
public sealed class McpBridge(Processor processor, Authz authz)
{
    public Processor Processor { get; } = processor;
    public Authz Authz { get; } = authz;
    public SlotRecordings? Recordings { get; set; }

    /// <summary>Load a program from a file on this computer: (slot, path, start, who) → null when it loaded, else why not.</summary>
    public Func<int, string, bool, string, Task<string?>>? LoadFromPath { get; set; }
    public Func<int, Task>? Start { get; set; }
    public Action<int>? Stop { get; set; }
    public Func<int, Task>? Restart { get; set; }
    /// <summary>The slot as the REST API shows it (GET /api/slots/{n}).</summary>
    public Func<ProgramSlot, object>? SlotDto { get; set; }
    /// <summary>Who is calling, for the activity feed ("this computer", an address).</summary>
    public Func<HttpContext?, string> Who { get; set; } = _ => Actors.ThisComputer;
    /// <summary>Adds an entry to the activity feed (and so the audit log): (action, text, slot).</summary>
    public Action<string, string, int?>? Note { get; set; }

    /// <summary>
    /// The caller may do this, or an <see cref="McpException"/> that the client shows as the tool's error. Same rules as the REST
    /// endpoint with the same job (see PolicyTable): the permission, a slot the account may use, and a slot lock if the endpoint honours one.
    /// </summary>
    public Principal Require(Perm perm, int? slot = null, bool locks = false)
    {
        var p = Principal.Current ?? throw new McpException("OpenVP couldn't tell who is calling. Send the access key or an API token as 'Authorization: Bearer ...'.");
        if (!p.Has(perm))
        {
            Authz.Denied($"deny:{p.LockId}:mcp:{perm}", "permission.denied", $"{p.Display} tried an MCP tool that needs {perm}", p.ActivityUser ?? p.Name, p.Ip, slot);
            throw new McpException($"Your account isn't allowed to do that (it needs the {perm} permission).");
        }
        if (slot is { } n)
        {
            if (!Processor.IsValidSlot(n)) throw new McpException($"Slot must be 1–{Processor.Slots.Count}.");
            if (!p.CanSlot(n))
            {
                Authz.Denied($"deny:{p.LockId}:{n}:mcp", "permission.denied", $"{p.Display} tried an MCP tool on a slot it isn't allowed to use", p.ActivityUser ?? p.Name, p.Ip, n);
                throw new McpException($"Your account isn't allowed to use slot {n}.");
            }
            if (locks && Authz.Locks.Blocking(n, p) is { } l)
            {
                Authz.Denied($"lock:{p.LockId}:mcp:{n}", "lock.denied", $"{p.Display} tried an MCP tool but slot {n} is locked by {l.By}", p.ActivityUser ?? p.Name, p.Ip, n);
                throw new McpException(SlotLocks.Message(l));
            }
        }
        return p;
    }
}
