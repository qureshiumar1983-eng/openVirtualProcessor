using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Simpl;
using OpenVP.SimplSharpHost;

namespace OpenVP.Server;

/// <summary>What a program check found: the verdict ("will it run?") and what is in the program.</summary>
public sealed record ReadinessReport(string File, string? Kind, ReadinessVerdict Verdict, ProgramSummary? Summary);

/// <summary>
/// Works out whether a program will run, without loading it into a slot. It loads the program into a throwaway
/// one-slot processor of its own (never started, so nothing runs and no device registers), reads what the runtime found
/// (the same compatibility code the console's Compatibility button uses) and throws the processor away.
/// </summary>
public sealed class ReadinessChecker
{
    private readonly string _dataDirectory;
    private readonly SemaphoreSlim _gate = new(2, 2);

    /// <param name="dataDirectory">The real data folder, for the symbol and primitive maps (<c>symbols.json</c>, <c>primitives.json</c>) the loaders read.</param>
    public ReadinessChecker(string dataDirectory) { _dataDirectory = dataDirectory; }

    /// <summary>Checks <paramref name="program"/>, a file with its sidecar files (.sig, .openvp.json, the .lpz behind a .smw) in the same folder.</summary>
    public ReadinessReport Check(string program)
    {
        var name = Path.GetFileName(program);
        _gate.Wait();
        var scratch = Directory.CreateTempSubdirectory("openvp-check").FullName;
        try
        {
            using var p = new Processor(scratch, 1);
            p.AddLoader(new SimplSharpLoader());
            p.AddLoader(new SimplLoader(Path.Combine(_dataDirectory, "symbols.json")));
            p.AddLoader(new LpzLoader(Path.Combine(_dataDirectory, "primitives.json")));
            try { p.LoadProgram(1, program, start: false); }
            catch (NotSupportedException) { return new ReadinessReport(name, null, Readiness.UnsupportedFile(name), null); }
            catch (Exception e) { return new ReadinessReport(name, null, Readiness.WontRun(Readiness.Explain(e)), null); }

            var rt = p.Slot(1).Runtime;
            if (rt is not IProgramAnalysis analysis)
                return new ReadinessReport(name, rt?.Kind, Readiness.Evaluate(Array.Empty<ReadinessProblem>()), null);
            // What the runtime reported while loading, plus what it knows without being started.
            var problems = p.Unsupported.Items.Select(u => new ReadinessProblem(u.Kind, u.Name, u.Detail)).Concat(analysis.Problems()).ToList();
            var verdict = Readiness.Evaluate(problems, blocker: analysis.Blocker);
            return new ReadinessReport(name, rt.Kind, verdict, analysis.Summarize());
        }
        catch (Exception e) { return new ReadinessReport(name, null, Readiness.WontRun(Readiness.Explain(e)), null); }
        finally
        {
            _gate.Release();
            try { Directory.Delete(scratch, true); } catch { /* a runtime still holds a file; the OS temp folder is cleaned up eventually */ }
        }
    }
}
