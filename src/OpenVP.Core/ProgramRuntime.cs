namespace OpenVP.Core;

public enum SlotState { Empty, Loaded, Running, Stopped, Error }

/// <summary>A named signal a debugger can watch and drive.</summary>
public sealed record SignalInfo(string Name, SigKind Kind, object Value);

/// <summary>One trace line for the debugger.</summary>
public sealed record TraceEvent(DateTimeOffset Time, int Slot, string Signal, SigKind Kind, object Value, string Source);

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
}

/// <summary>Loads a file into a runtime. One factory per program type.</summary>
public interface IProgramLoader
{
    /// <summary>True if this loader handles the given file (by extension or content).</summary>
    bool CanLoad(string path);

    IProgramRuntime Load(string path, ProgramSlot slot);
}
