// Lists the compiled record types OpenVP can't run yet across a set of SIMPL Windows programs, with example
// records (pin signal names, parameters, and the matching source symbol when a program ships its source).
//
//   dotnet run --project tools/LpzSurvey -- <folder or .lpz> [...] [--all] [--type N] [--examples N] [--out file]
//
// Folders are searched recursively for .lpz files. Put each program's .sig next to its .lpz (same name, or
// named after the .smw) to get signal names; programs compiled with "archive source" carry their own.
//   --all         include types OpenVP already runs (to check them against real programs)
//   --type N      only report type N (implies --all for that type)
//   --examples N  examples per type (default 4)
//   --out file    write the report to a file instead of the console
//
// The report contains signal names and parameter values from your programs (no logic or code). Review it
// before sharing.
using OpenVP.Lpz;

var paths = new List<string>();
bool all = false; int? only = null; int examples = 4; string? outFile = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--all": all = true; break;
        case "--type" when i + 1 < args.Length: only = int.Parse(args[++i]); all = true; break;
        case "--examples" when i + 1 < args.Length: examples = int.Parse(args[++i]); break;
        case "--out" when i + 1 < args.Length: outFile = args[++i]; break;
        case "-h" or "--help": paths.Clear(); i = args.Length; break;
        default: paths.Add(args[i]); break;
    }
}
if (paths.Count == 0)
{
    Console.Error.WriteLine("usage: LpzSurvey <folder or .lpz> [...] [--all] [--type N] [--examples N] [--out file]");
    return 2;
}

var files = paths.SelectMany(p =>
        Directory.Exists(p) ? Directory.EnumerateFiles(p, "*.lpz", SearchOption.AllDirectories)
        : File.Exists(p) ? new[] { p }
        : throw new FileNotFoundException($"Not found: {p}"))
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

var survey = new LpzSurvey { MaxExamples = examples, IncludeRunnable = all };
foreach (var f in files)
{
    Console.Error.WriteLine($"reading {f}");
    survey.Add(f);
}

var report = survey.Report();
if (only is int t)
{
    // Keep the header and just the requested type's section.
    var sections = report.Split("\n== ");
    report = sections[0].TrimEnd() + "\n" + string.Concat(sections.Skip(1).Where(s => s.StartsWith($"type {t} ") || s.StartsWith($"type {t}:")).Select(s => "\n== " + s));
}
if (outFile != null) { File.WriteAllText(outFile, report); Console.Error.WriteLine($"wrote {outFile}"); }
else Console.Write(report);
return 0;
