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

    public static bool IsLocalAddress(string address) =>
        address is "127.0.0.1" or "127.0.0.2" or "localhost" or "::1" or "";

    /// <summary>Attach an EISC endpoint. Returns how many peers it is now linked to.</summary>
    public int Attach(JoinDevice endpoint)
    {
        lock (_gate)
        {
            var list = _links.GetOrAdd(endpoint.IpId, _ => new List<JoinDevice>());
            if (list.Contains(endpoint)) return list.Count - 1;
            list.Add(endpoint);
            endpoint.FeedbackChanged += Forward;

            // Each endpoint counts its peers as connections, so it reports online once linked.
            foreach (var peer in list.Where(p => p != endpoint))
            {
                peer.AttachConnection();
                endpoint.AttachConnection();
                // Bring the newcomer up to date with what the peer already set.
                foreach (var (k, v) in peer.FeedbackSnapshot()) endpoint.ReceiveInput(k, v);
                foreach (var (k, v) in endpoint.FeedbackSnapshot()) peer.ReceiveInput(k, v);
            }
            return list.Count - 1;
        }
    }

    public void Detach(JoinDevice endpoint)
    {
        lock (_gate)
        {
            if (!_links.TryGetValue(endpoint.IpId, out var list) || !list.Remove(endpoint)) return;
            endpoint.FeedbackChanged -= Forward;
            foreach (var peer in list)
            {
                peer.DetachConnection();
                endpoint.DetachConnection();
            }
        }
    }

    private void Forward(JoinDevice source, JoinKey key, object value)
    {
        List<JoinDevice> peers;
        lock (_gate)
        {
            if (!_links.TryGetValue(source.IpId, out var list)) return;
            peers = list.Where(p => p != source).ToList();
        }
        foreach (var p in peers) p.ReceiveInput(key, value);
    }
}
