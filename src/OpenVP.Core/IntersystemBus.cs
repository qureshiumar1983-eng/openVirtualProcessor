using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// Links EISC (Ethernet intersystem communication) endpoints inside the processor.
///
/// Two programs that each declare an EISC with the same IP ID pointing at a local address
/// (127.0.0.2, localhost or 127.0.0.1) are joined: whatever one side sets on its inputs appears
/// on the other side's outputs. This is how a SIMPL Windows slot and a SIMPL# slot share joins,
/// the same pattern people use on real multi-slot processors.
/// </summary>
public sealed class IntersystemBus
{
    private readonly ConcurrentDictionary<uint, List<JoinDevice>> _links = new();
    private readonly object _gate = new();
    // The connection each endpoint holds for each of its peers, so the IP table can say who an EISC is linked to.
    private readonly Dictionary<(JoinDevice endpoint, JoinDevice peer), DeviceConnection> _peerLinks = new();

    /// <summary>Optional: describes a slot for the IP table ("slot 2 · MyProgram"). Set by the processor.</summary>
    public Func<int, string>? DescribeSlot { get; set; }

    private void Link(JoinDevice endpoint, JoinDevice peer)
    {
        var c = new DeviceConnection("EISC", "127.0.0.2")
        {
            Peer = DescribeSlot?.Invoke(peer.Slot) ?? $"slot {peer.Slot}",
            ClientKind = "EISC link to another slot",
            ProbeStatus = "internal link"
        };
        c.SetDetail("Peer slot", peer.Slot.ToString());
        c.SetDetail("Peer IP ID", peer.IpId.ToString("X2"));
        c.SetDetail("Peer type", peer.Type);
        _peerLinks[(endpoint, peer)] = c;
        endpoint.AttachConnection(c);
    }

    private void Unlink(JoinDevice endpoint, JoinDevice peer)
    {
        if (_peerLinks.Remove((endpoint, peer), out var c)) endpoint.DetachConnection(c);
    }

    public static bool IsLocalAddress(string address) =>
        address is "127.0.0.1" or "127.0.0.2" or "localhost" or "::1" or "";

    /// <summary>Attach an EISC endpoint. Returns how many peers it is now linked to.</summary>
    public int Attach(JoinDevice endpoint)
    {
        lock (_gate)
        {
            var list = _links.GetOrAdd(endpoint.IpId, _ => new List<JoinDevice>());
            if (list.Contains(endpoint)) return list.Count - 1;
            // A slot never links to itself. Another device of the same slot and IP ID is what a replaced program left behind (its
            // runtime was swapped without a clean stop), so drop it and its links before the new one joins.
            foreach (var stale in list.Where(e => e.Slot == endpoint.Slot).ToList()) DetachCore(list, stale);
            list.Add(endpoint);
            endpoint.FeedbackChanged += Forward;

            // Each endpoint counts its peers as connections, so it reports online once linked.
            foreach (var peer in list.Where(p => p != endpoint && p.Slot != endpoint.Slot))
            {
                Link(peer, endpoint);
                Link(endpoint, peer);
                // Bring the newcomer up to date with what the peer already set.
                foreach (var (k, v) in peer.FeedbackSnapshot()) endpoint.ReceiveInput(k, v, InputSource.Eisc);
                foreach (var (k, v) in endpoint.FeedbackSnapshot()) peer.ReceiveInput(k, v, InputSource.Eisc);
            }
            return list.Count - 1;
        }
    }

    public void Detach(JoinDevice endpoint)
    {
        lock (_gate)
        {
            if (!_links.TryGetValue(endpoint.IpId, out var list)) return;
            DetachCore(list, endpoint);
        }
    }

    private void DetachCore(List<JoinDevice> list, JoinDevice endpoint)
    {
        if (!list.Remove(endpoint)) return;
        endpoint.FeedbackChanged -= Forward;
        foreach (var peer in list)
        {
            Unlink(peer, endpoint);
            Unlink(endpoint, peer);
        }
    }

    /// <summary>How many other endpoints this one is linked to (0 when it isn't attached). For tests and the IP table.</summary>
    public int PeerCount(JoinDevice endpoint)
    {
        lock (_gate) return _links.TryGetValue(endpoint.IpId, out var list) && list.Contains(endpoint) ? list.Count(p => p != endpoint) : 0;
    }

    private void Forward(JoinDevice source, JoinKey key, object value)
    {
        List<JoinDevice> peers;
        lock (_gate)
        {
            if (!_links.TryGetValue(source.IpId, out var list)) return;
            peers = list.Where(p => p != source && p.Slot != source.Slot).ToList();
            foreach (var p in peers)
            {
                // source's connection to p carries the join out; p's connection to source brings it in.
                if (_peerLinks.TryGetValue((source, p), out var outLink)) { Interlocked.Increment(ref outLink.JoinsOut); outLink.RecordOut(0); }
                if (_peerLinks.TryGetValue((p, source), out var inLink)) { Interlocked.Increment(ref inLink.JoinsIn); inLink.RecordIn(0); }
            }
        }
        foreach (var p in peers) p.ReceiveInput(key, value, InputSource.Eisc);
    }
}
