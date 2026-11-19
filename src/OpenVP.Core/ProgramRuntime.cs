namespace OpenVP.Core;

public enum SlotState { Empty, Loaded, Running, Stopped, Error }

/// <summary>A named signal a debugger can watch and drive.</summary>
public sealed record SignalInfo(string Name, SigKind Kind, object Value);

/// <summary>
/// One trace line for the debugger. Symbol / Cause / Depth / Chain describe causality (docs/debugger-paths.md):
/// Symbol is the graph symbol whose output produced this change (null when it came from outside the logic),
/// Cause the signal whose change made that symbol act, Depth the hops from the external change that started the
/// burst, Chain the id of that burst (0 when the runtime doesn't track it).
/// </summary>
public sealed record TraceEvent(DateTimeOffset Time, int Slot, string Signal, SigKind Kind, object Value, string Source,
    string? Symbol = null, string? Cause = null, int Depth = 0, long Chain = 0);

/// <summary>
/// A program type the processor knows how to run. Implemented by the SIMPL# host and the
/// SIMPL Windows interpreter.
/// </summary>
public interface IProgramRuntime : IDisposable
{
    /// <summary>"SIMPL#" or "SIMPL Windows".</summary>
    string Kind { get; }

    /// <summary>Program name shown in the console.</summary>
    string ProgramName { get; }

    void Start();
    void Stop();

    /// <summary>Signals the debugger can see. For SIMPL# this is the join table of every registered device.</summary>
    IReadOnlyList<SignalInfo> GetSignals();

    /// <summary>Drive a signal from the debugger. Returns false if the name is unknown.</summary>
    bool TrySetSignal(string name, object value);

    /// <summary>
    /// Pulse a digital signal from the debugger: high, then low, with no time in between, as a processor's
    /// debugger does. Both edges are acted on (the rise settles first, then the fall). Returns false if the
    /// name is unknown or not a digital signal.
    /// </summary>
    bool TryPulseSignal(string name) => TrySetSignal(name, true) && TrySetSignal(name, false);
}

/// <summary>One symbol of a program graph. Id equals <see cref="TraceEvent.Symbol"/> for changes the symbol drives.</summary>
public sealed record GraphSymbol(string Id, string Name, string Path, string Group, bool Device,
    IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

public sealed record GraphSignal(string Name, string Kind);

/// <summary>The wiring of a program, for the debugger's path view.</summary>
public sealed record ProgramGraph(string Kind, IReadOnlyList<GraphSymbol> Symbols, IReadOnlyList<GraphSignal> Signals);

/// <summary>Optional: a runtime that can describe its wiring.</summary>
public interface IProgramGraphSource
{
    ProgramGraph GetGraph();
}

/// <summary>Loads a file into a runtime. One factory per program type.</summary>
public interface IProgramLoader
{
    /// <summary>True if this loader handles the given file (by extension or content).</summary>
    bool CanLoad(string path);

    IProgramRuntime Load(string path, ProgramSlot slot);
}
