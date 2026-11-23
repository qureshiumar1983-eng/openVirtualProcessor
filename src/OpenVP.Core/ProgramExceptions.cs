using System.Diagnostics;
using System.Reflection;

namespace OpenVP.Core;

/// <summary>One stack frame of a program exception. <see cref="File"/> and <see cref="Line"/> are there only when the program's .pdb was loaded.</summary>
public sealed record ExceptionFrame(string Method, string? File, int Line);

/// <summary>An exception a SIMPL# program threw, with where.</summary>
/// <param name="Source">What was running: an event handler, "CTimer Class.Method", "CrestronThread name", "CrestronInvoke Class.Method".</param>
public sealed record ProgramException(DateTimeOffset Time, int Slot, string Source, string Type, string Message, string Stack,
    IReadOnlyList<ExceptionFrame> Frames, bool HasSymbols);

/// <summary>Builds the report for an exception: type, message, the full stack trace, and frames mapped to file and line when symbols are loaded.</summary>
public static class ExceptionReport
{
    /// <summary>Peel off the wrappers reflection and tasks put around the real exception.</summary>
    public static Exception Unwrap(Exception e)
    {
        while (true)
        {
            if (e is TargetInvocationException { InnerException: { } t }) e = t;
            else if (e is AggregateException { InnerExceptions.Count: 1 } a) e = a.InnerExceptions[0];
            else return e;
        }
    }

    public static ProgramException Build(Exception e, string source, int slot, DateTimeOffset? time = null)
    {
        e = Unwrap(e);
        var frames = new List<ExceptionFrame>();
        var hasSymbols = false;
        try
        {
            foreach (var f in new StackTrace(e, fNeedFileInfo: true).GetFrames())
            {
                var m = f.GetMethod();
                if (m == null) continue;
                // Hide the plumbing that every program exception passes through.
                var type = m.DeclaringType;
                var name = type == null ? m.Name : $"{type.FullName}.{m.Name}";
                var file = f.GetFileName();
                var line = f.GetFileLineNumber();
                if (!string.IsNullOrEmpty(file)) hasSymbols = true; else file = null;
                frames.Add(new ExceptionFrame(name, file, file == null ? 0 : line));
            }
        }
        catch { /* a trace we can't walk is still reported as text */ }
        return new ProgramException(time ?? DateTimeOffset.Now, slot, source, e.GetType().FullName ?? e.GetType().Name, e.Message, e.ToString(), frames, hasSymbols);
    }

    /// <summary>The file name without its folders, whichever slash the build machine used.</summary>
    public static string ShortFile(string? file)
    {
        if (string.IsNullOrEmpty(file)) return "";
        var i = Math.Max(file.LastIndexOf('\\'), file.LastIndexOf('/'));
        return i < 0 ? file : file[(i + 1)..];
    }
}

/// <summary>The last exceptions a slot's program threw, newest last. Thread safe.</summary>
public sealed class ExceptionLog
{
    private readonly object _gate = new();
    private readonly List<ProgramException> _items = new();
    public const int Max = 100;

    public void Add(ProgramException e)
    {
        lock (_gate) { _items.Add(e); if (_items.Count > Max) _items.RemoveAt(0); }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ProgramException> Recent() { lock (_gate) return _items.AsEnumerable().Reverse().ToList(); }

    public int Count { get { lock (_gate) return _items.Count; } }
    public void Clear() { lock (_gate) _items.Clear(); }
}

/// <summary>Finds the symbol file for a program assembly and says whether OpenVP can use it.</summary>
public static class ProgramSymbols
{
    /// <summary>The .pdb next to <paramref name="dllPath"/> (same name), or null.</summary>
    public static string? Find(string dllPath)
    {
        var pdb = Path.ChangeExtension(dllPath, ".pdb");
        return File.Exists(pdb) ? pdb : null;
    }

    /// <summary>A portable PDB starts with "BSJB". Windows (full) PDBs, which older Crestron toolchains write, can't be read on this runtime.</summary>
    public static bool IsPortable(byte[] pdb) => pdb.Length > 4 && pdb[0] == 'B' && pdb[1] == 'S' && pdb[2] == 'J' && pdb[3] == 'B';
}
