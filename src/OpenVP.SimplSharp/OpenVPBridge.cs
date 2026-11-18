using System;
using System.Collections.Generic;
using OpenVP.Core;

namespace OpenVP.SimplSharp
{
    /// <summary>
    /// Connects this copy of the API to the slot it runs in. Each program slot loads its own copy
    /// of this assembly into its own load context, so these statics are per slot.
    /// </summary>
    public static class OpenVPBridge
    {
        private static readonly object Gate = new object();
        private static readonly List<IDisposable> Resources = new List<IDisposable>();

        /// <summary>Set by the host right after loading this assembly into a slot.</summary>
        public static ProgramSlot Slot { get; set; }

        /// <summary>The control system instance created from the program.</summary>
        public static Crestron.SimplSharpPro.CrestronControlSystem ControlSystem { get; internal set; }

        [ThreadStatic] internal static Action<string> ConsoleSink;

        internal static ProgramSlot RequireSlot =>
            Slot ?? throw new InvalidOperationException("This SIMPL# API only works inside an OpenVP program slot.");

        /// <summary>
        /// Where library callbacks (CTimer, socket and device events) run. Unset: the slot's own thread, which
        /// is right for a SIMPL# Pro program, whose code all runs there. A compiled SIMPL Windows program sets
        /// its own: there the slot thread runs the logic engine, which the step debugger holds at every pause,
        /// and the SIMPL+ modules calling these libraries must keep running while it does.
        /// </summary>
        public static SlotDispatcher Callbacks { get; set; }

        internal static void Post(Action action, string label = null)
        {
            var target = Callbacks ?? Slot?.Dispatcher;
            if (target == null) { action(); return; }
            target.Post(action, label);
        }

        /// <summary>Allocation-free variant: pass a static lambda and what it needs.</summary>
        /// <param name="label">What this event is, for the metrics page: a string, or the user delegate behind it.</param>
        internal static void Post(Action<object?, object?, int> action, object? a, object? b, int i, object label = null)
        {
            var target = Callbacks ?? Slot?.Dispatcher;
            if (target == null) { action(a, b, i); return; }
            target.Post(action, a, b, i, label);
        }

        internal static void Log(string level, string message) => Slot?.Log(level, message);

        internal static void Track(IDisposable resource)
        {
            lock (Gate) Resources.Add(resource);
        }

        internal static void Untrack(IDisposable resource)
        {
            lock (Gate) Resources.Remove(resource);
        }

        /// <summary>Called by the host when the slot stops: tell the program, then release timers and sockets.</summary>
        public static void Shutdown()
        {
            try { Crestron.SimplSharp.CrestronEnvironment.Raise(Crestron.SimplSharp.eProgramStatusEventType.Stopping); }
            catch (Exception e) { Log("Error", "Program's Stopping handler threw: " + e.Message); }

            IDisposable[] items;
            lock (Gate) { items = Resources.ToArray(); Resources.Clear(); }
            foreach (var r in items)
            {
                try { r.Dispose(); } catch { /* best effort */ }
            }
        }
    }
}
