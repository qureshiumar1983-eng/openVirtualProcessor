using System.Net;
using System.Net.Sockets;
using System.Text;
using Crestron.Logos.SplusObjects;
using OpenVP.Core;

namespace OpenVP.Tests;

/// <summary>SIMPL+ runtime pieces exercised directly, with modules written the way the compiler emits them.</summary>
public static class SplusRuntimeTests
{
    /// <summary>A module with a catch-all EVENT and one PUSH handler, like a compiled module that has both.</summary>
    private sealed class EventModule : SplusObject
    {
        public DigitalInput Press = null!;
        public AnalogInput Level = null!;
        public StringInput Name = null!;
        public int Pushes, Events;
        public readonly List<uint> EventIds = new();

        public EventModule() : base("S-1", "", CrestronStringEncoding.eEncodingASCII) { }

        public override void LogosSplusInitialize()
        {
            Press = new DigitalInput(0, this); m_DigitalInputList.Add(0, Press);
            Level = new AnalogInput(0, this); m_AnalogInputList.Add(0, Level);
            Name = new StringInput(1, 20, this); m_StringInputList.Add(1, Name);
            Press.OnDigitalPush.Add(new InputChangeHandlerWrapper(_ => { Interlocked.Increment(ref Pushes); return null!; }, false));
            OnEventStatement.Add(new InputChangeHandlerWrapper(OnEvent, false));
        }

        private object OnEvent(object args)
        {
            var e = (SignalEventArgs)args;
            lock (EventIds) EventIds.Add(e.Id);
            Interlocked.Increment(ref Events);
            return null!;
        }
    }

    private static ISplusModule Start(SplusObject m)
    {
        var mod = (ISplusModule)m;
        mod.Initialize();
        mod.StartMain();
        return mod;
    }

    [Test]
    public static void Event_statement_runs_for_every_input_change()
    {
        var m = new EventModule();
        var mod = Start(m);
        try
        {
            mod.Input(SigKind.Digital, 0, true);
            Assert.Eventually(() => m.Pushes == 1 && m.Events == 1, "push runs its own handler and EVENT");
            mod.Input(SigKind.Digital, 0, false);
            Assert.Eventually(() => m.Events == 2, "release runs EVENT (no release handler)");
            mod.Input(SigKind.Analog, 0, (ushort)7);
            Assert.Eventually(() => m.Events == 3, "analog change runs EVENT");
            mod.Input(SigKind.Serial, 1, "x");
            Assert.Eventually(() => m.Events == 4, "serial change runs EVENT");
            Assert.Equal(1, m.Pushes, "PUSH ran once");
        }
        finally { mod.Stop(); }
    }

    /// <summary>A module that fails while setting up, as one using a SIMPL+ API OpenVP lacks does.</summary>
    private sealed class FailingModule : SplusObject
    {
        public FailingModule() : base("S-9", "", CrestronStringEncoding.eEncodingASCII) { }
        public override void LogosSplusInitialize() => throw new MissingFieldException("SplusObject", "SomethingNew");
    }

    [Test]
    public static void A_module_that_cannot_start_does_not_hold_up_the_others()
    {
        // Before: the failed module stayed in the program-wide start-up barrier, so every other module's
        // events and WaitForInitializationComplete waited for it (10 s per event, forever for Main).
        var bad = new FailingModule();
        var threw = false;
        try { ((ISplusModule)bad).Initialize(); } catch (MissingFieldException) { threw = true; }
        Assert.True(threw, "the failure reaches the runtime, which reports the module can't run");

        var m = new EventModule();
        var mod = Start(m);
        try
        {
            mod.Input(SigKind.Digital, 0, true);
            Assert.Eventually(() => m.Pushes == 1, "the other module's events run", 3000);
        }
        finally { mod.Stop(); }
    }

    /// <summary>Like a Q-SYS processor module: after the barrier its Main registers what control modules need.</summary>
    private sealed class OrderModule(string name, Func<object?> main) : SplusObject(name, "", CrestronStringEncoding.eEncodingASCII)
    {
        public override object FunctionMain(object obj)
        {
            WaitForInitializationComplete();
            return main()!;
        }
    }

    [Test]
    public static void Function_mains_take_turns_in_program_order_after_the_barrier()
    {
        var registered = false;
        bool? sawRegistered = null;
        // Created in program order: processor first. Its Main does its setup without yielding.
        var processor = new OrderModule("S-1", () => { Thread.Sleep(300); registered = true; return null; });
        var control = new OrderModule("S-2", () => { sawRegistered = registered; return null; });
        var mc = (ISplusModule)control;
        var mp = (ISplusModule)processor;
        try
        {
            mc.Initialize(); mp.Initialize();
            mc.StartMain();   // started first, but second in program order
            mp.StartMain();
            Assert.Eventually(() => sawRegistered != null, "control module's Main ran");
            Assert.True(sawRegistered == true, "the control module's Main waited for the processor module's Main to hand over");
        }
        finally { mc.Stop(); mp.Stop(); }
    }

    /// <summary>A module with a UDP socket, as compiled modules declare one.</summary>
    private sealed class UdpModule : SplusObject
    {
        public SplusUdpSocket Socket = null!;
        public UdpModule() : base("S-2", "", CrestronStringEncoding.eEncodingASCII) { }
        public override void LogosSplusInitialize() => Socket = new SplusUdpSocket(1024, this);
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    [Test]
    public static void Udp_sockets_on_one_port_share_it_and_close_with_their_module()
    {
        var port = FreeUdpPort();
        var a = new UdpModule();
        var b = new UdpModule();
        var ma = Start(a);
        var mb = Start(b);
        try
        {
            // Several modules enabling UDP on one port: on a processor each one works.
            Assert.Equal((short)0, a.Socket.Enable(new CrestronString("127.0.0.2"), port), "first socket on the port");
            Assert.Equal((short)0, b.Socket.Enable(new CrestronString("127.0.0.3"), port), "second socket on the same port");

            void SendFrom(string from, string text)
            {
                using var c = new UdpClient(new IPEndPoint(IPAddress.Parse(from), 0));
                var bytes = Encoding.ASCII.GetBytes(text);
                c.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
            }

            SendFrom("127.0.0.2", "A");
            Assert.Eventually(() => a.Socket.SocketRxBuf.Str == "A", "datagram from 127.0.0.2 reaches the socket set to it");
            SendFrom("127.0.0.3", "B");
            Assert.Eventually(() => b.Socket.SocketRxBuf.Str == "B", "datagram from 127.0.0.3 reaches the other socket");
            Assert.Equal("A", a.Socket.SocketRxBuf.Str, "and not the first");
            SendFrom("127.0.0.9", "C");
            Assert.Eventually(() => a.Socket.SocketRxBuf.Str == "AC" && b.Socket.SocketRxBuf.Str == "BC", "a sender neither expects reaches both");

            // Stopping the modules releases the port (it used to stay bound after a program was unloaded).
            ma.Stop();
            mb.Stop();
            using var again = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            Assert.True(again.Client.IsBound, "port free once both modules stopped");
        }
        finally { ma.Stop(); mb.Stop(); }
    }
}
