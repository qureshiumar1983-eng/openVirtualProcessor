using OpenVP.Core;
using OpenVP.Simpl;

namespace OpenVP.Tests;

public static class EiscBusTests
{
    private static JoinKey D(uint j) => new(SigKind.Digital, j);

    [Test]
    public static void A_replaced_program_cannot_leave_a_slot_linked_to_itself()
    {
        using var p = new Processor(Paths.TempData(), 2);
        var s1 = new JoinDevice(1, 0x10, "EISC", "slot 1 program");
        var s2 = new JoinDevice(2, 0x10, "EISC", "slot 2 program");
        p.Bus.Attach(s1); p.Bus.Attach(s2);
        Assert.Equal(1, p.Bus.PeerCount(s1), "linked to the other slot");
        Assert.True(s1.Online && s2.Online, "both online through the link");

        // Slot 1's program is replaced without the old one leaving the bus (a runtime that was swapped, not stopped).
        var s1b = new JoinDevice(1, 0x10, "EISC", "slot 1 new program");
        p.Bus.Attach(s1b);
        Assert.Equal(1, p.Bus.PeerCount(s1b), "the new EISC has one peer: slot 2");
        Assert.Equal(1, p.Bus.PeerCount(s2), "slot 2 has one peer, not two (the stale one is gone)");
        Assert.Equal(0, p.Bus.PeerCount(s1), "the stale device is off the bus");
        Assert.True(s1.Connections.Count == 0 && !s1.Online, "and holds no links");
        Assert.True(s1b.Connections.All(c => c.GetDetail("Peer slot") == "2"), "no link names slot 1 as a peer");
        Assert.True(s2.Connections.All(c => c.GetDetail("Peer slot") == "1") && s2.Connections.Count == 1, "slot 2 sees exactly one link, to slot 1");

        // And joins still flow, once.
        var got = 0;
        s2.InputReceived += (_, k, v) => { if (k == D(1) && v is true) got++; };
        s1b.SetFeedback(D(1), true);
        Assert.Equal(1, got, "the new program's join arrives once");
        s1.SetFeedback(D(2), true);
        Assert.True(!(bool)s2.GetInput(D(2)), "the stale device no longer forwards");
    }

    [Test]
    public static void A_lone_replacement_does_not_link_to_the_device_it_replaced()
    {
        using var p = new Processor(Paths.TempData(), 2);
        var old = new JoinDevice(1, 0x10, "EISC", "old");
        p.Bus.Attach(old);
        var fresh = new JoinDevice(1, 0x10, "EISC", "new");
        p.Bus.Attach(fresh);
        Assert.Equal(0, p.Bus.PeerCount(fresh), "nothing to link to");
        Assert.True(!fresh.Online && fresh.Connections.Count == 0, "so it is not online and has no link to itself");
        Assert.True(old.Connections.Count == 0, "the old one is off the bus");
    }

    [Test]
    public static void Unregistering_a_device_takes_it_off_the_bus_even_if_the_runtime_forgot()
    {
        using var p = new Processor(Paths.TempData(), 2);
        var d1 = p.Slot(1).RegisterDevice(0x10, "EISC", "a");
        var d2 = p.Slot(2).RegisterDevice(0x10, "EISC", "b");
        p.Bus.Attach(d1); p.Bus.Attach(d2);
        Assert.Equal(1, p.Bus.PeerCount(d2), "linked");
        p.Slot(1).UnregisterDevice(0x10); // no Bus.Detach by the caller
        Assert.Equal(0, p.Bus.PeerCount(d2), "the other end lost its peer");
        Assert.True(d2.Connections.Count == 0, "and its connection");
    }

    [Test]
    public static void Reloading_a_smw_program_keeps_exactly_one_link_per_slot()
    {
        var dir = Path.Combine(Paths.RepoRoot, "examples", "programs");
        using var p = new Processor(Paths.TempData(), 3);
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, Path.Combine(dir, "eisc-a", "EiscPairA.smw"));
        p.LoadProgram(2, Path.Combine(dir, "eisc-b", "EiscPairB.smw"));
        for (var i = 0; i < 3; i++) p.LoadProgram(1, Path.Combine(dir, "eisc-a", "EiscPairA.smw")); // replaced again and again
        var a = p.Slot(1).FindDevice(0x10)!; var b = p.Slot(2).FindDevice(0x10)!;
        Assert.Equal(1, p.Bus.PeerCount(a), "slot 1 has one peer");
        Assert.Equal(1, p.Bus.PeerCount(b), "slot 2 has one peer");
        Assert.Equal(1, b.Connections.Count, "one link on slot 2's EISC");
        Assert.True(a.Connections.All(c => c.GetDetail("Peer slot") == "2"), "slot 1 never links to itself");
        // Presses cross over.
        p.Slot(1).FindDevice(3)!.ReceiveInput(D(1), true);
        Assert.Eventually(() => (bool)p.Slot(2).FindDevice(3)!.GetFeedback(D(1)), "A's toggle reaches B's panel");
    }
}
