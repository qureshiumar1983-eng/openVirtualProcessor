namespace OpenVP.Core;

/// <summary>
/// How a SIMPL+ module (loaded into a slot's own load context) talks to the logic engine that hosts it.
/// Lives in Core because Core is shared between the host and every slot's load context.
/// </summary>
public interface ISplusHost
{
    /// <summary>The module set an output. Kind tells digital/analog/serial; id is the module's output id.</summary>
    void Output(SigKind kind, uint id, object value);

    /// <summary>True if the module's input or output with this id is wired to a real signal.</summary>
    bool IsConnected(bool isInput, SigKind kind, uint id);

    ProgramSlot Slot { get; }

    /// <summary>Folder for this module's files (FileOpen etc.).</summary>
    string FileRoot { get; }

    void Log(string level, string message);
}

/// <summary>What the host can do to a module instance. Implemented by SplusObject.</summary>
public interface ISplusModule
{
    void AttachHost(ISplusHost host);
    /// <summary>Kinds of the module's inputs/outputs, known after initialization.</summary>
    IReadOnlyDictionary<(bool isInput, uint id), SigKind> Pins { get; }
    void SetParameter(uint id, object value);
    /// <summary>Set an input's value without running events (initial values).</summary>
    void Preset(SigKind kind, uint id, object value);
    /// <summary>Set an input's starting value as a change from 0 (or empty): on a processor every signal starts there, so a
    /// value the logic already holds when the module starts runs the input's PUSH/CHANGE once the module lets events in.
    /// Call before <see cref="StartMain"/>.</summary>
    void PresetFromZero(SigKind kind, uint id, object value) => Preset(kind, id, value);
    /// <summary>Set an input and run its events.</summary>
    void Input(SigKind kind, uint id, object value);
    void Initialize();
    void StartMain();
    void Stop();
}
