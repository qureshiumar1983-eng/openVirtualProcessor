using OpenVP.Simpl;

// Survey real SIMPL Windows programs: which logic symbols they use, which OpenVP runs, and the SmC code /
// compiled type pairs they reveal. With --out, also writes symbols.json and primitives.json there (copy
// them into OpenVP's data folder, or next to a program, to use the new codes straight away).
var paths = new List<string>();
string? outDir = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
    else paths.Add(args[i]);
}
if (paths.Count == 0)
{
    Console.Error.WriteLine("usage: SymbolSurvey <folder | program.smw | program.lpz>... [--out dir]");
    return 2;
}

var survey = SymbolSurvey.Run(paths);
Console.Write(survey.Report());
if (outDir != null)
{
    Directory.CreateDirectory(outDir);
    static string Json(SortedDictionary<int, string> m) =>
        System.Text.Json.JsonSerializer.Serialize(m.ToDictionary(p => p.Key.ToString(), p => p.Value), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(outDir, "symbols.json"), Json(survey.SuggestedSymbolCodes()));
    File.WriteAllText(Path.Combine(outDir, "primitives.json"), Json(survey.SuggestedPrimitiveTypes()));
    File.WriteAllText(Path.Combine(outDir, "survey.txt"), survey.Report());
    Console.WriteLine($"Wrote symbols.json, primitives.json and survey.txt to {Path.GetFullPath(outDir)}");
}
return 0;
