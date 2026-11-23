using System.IO.Compression;

namespace OpenVP.Server;

/// <summary>One downloadable starter project.</summary>
public sealed record TemplateInfo(string Id, string Name, string Description, string FileName, string ContentType);

/// <summary>
/// Starter projects, built into a zip when asked for from files shipped next to the server (<c>templates/</c>, copied from the
/// repository's <c>templates/</c> folder when it is built or published).
/// </summary>
public sealed class Templates
{
    public const string SimplSharpId = "simpl-sharp";
    public const string Ch5Id = "ch5-panel";

    private readonly string _root;

    /// <param name="root">The folder that holds <c>simpl-sharp/</c>, <c>ch5-panel/</c> and <c>lib/</c> (the build output's <c>templates</c> folder).</param>
    public Templates(string root) { _root = root; }

    public static Templates Default() => new(Path.Combine(AppContext.BaseDirectory, "templates"));

    public IReadOnlyList<TemplateInfo> List() => new[]
    {
        new TemplateInfo(SimplSharpId, "SIMPL# Pro starter project",
            "A C# project (StarterProgram.csproj and ControlSystem.cs) with a touch panel at IP ID 03 and an EISC at IP ID 10. Builds with dotnet build against OpenVP's copy of the SIMPL# Pro API, then loads as a .dll.",
            "OpenVP-SimplSharp-Starter.zip", "application/zip"),
        new TemplateInfo(Ch5Id, "CH5 / HTML panel starter",
            "A web page with buttons, a slider and text feedback on numeric joins, using openvp-crcomlib.js. Upload it as a shared panel's CH5 project (.ch5z), or open it as a local page.",
            "OpenVP-Ch5-Panel-Starter.ch5z", "application/zip"),
    };

    public bool Available(string id) => id switch
    {
        SimplSharpId => Directory.Exists(Path.Combine(_root, SimplSharpId)) && File.Exists(Path.Combine(_root, "lib", "SimplSharpPro.dll")),
        Ch5Id => File.Exists(Path.Combine(_root, Ch5Id, "index.html")),
        _ => false
    };

    /// <summary>The starter as a zip, or null for an unknown id. Throws <see cref="FileNotFoundException"/> if its files aren't installed.</summary>
    public byte[]? Build(string id) => id switch
    {
        SimplSharpId => SimplSharpZip(),
        Ch5Id => Ch5Zip(),
        _ => null
    };

    /// <summary>StarterProgram/ with the project, the source, a README and lib/ (the API assemblies it builds against).</summary>
    public byte[] SimplSharpZip()
    {
        var src = Path.Combine(_root, SimplSharpId);
        var lib = Path.Combine(_root, "lib");
        foreach (var need in new[] { Path.Combine(src, "StarterProgram.csproj"), Path.Combine(src, "ControlSystem.cs"), Path.Combine(lib, "SimplSharpPro.dll"), Path.Combine(lib, "OpenVP.Core.dll") })
            if (!File.Exists(need)) throw new FileNotFoundException($"The SIMPL# starter isn't installed: {Path.GetFileName(need)} is missing.", need);
        return Zip(zip =>
        {
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                Add(zip, "StarterProgram/" + Path.GetRelativePath(src, f).Replace('\\', '/'), f);
            foreach (var f in new[] { "SimplSharpPro.dll", "OpenVP.Core.dll" })
                Add(zip, "StarterProgram/lib/" + f, Path.Combine(lib, f));
        });
    }

    /// <summary>The CH5 page with index.html at the top, which is what a .ch5z needs.</summary>
    public byte[] Ch5Zip()
    {
        var src = Path.Combine(_root, Ch5Id);
        if (!File.Exists(Path.Combine(src, "index.html"))) throw new FileNotFoundException("The CH5 starter isn't installed: index.html is missing.", Path.Combine(src, "index.html"));
        return Zip(zip =>
        {
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                Add(zip, Path.GetRelativePath(src, f).Replace('\\', '/'), f);
        });
    }

    private static byte[] Zip(Action<ZipArchive> fill)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) fill(zip);
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string file)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.LastWriteTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var s = e.Open();
        using var f = File.OpenRead(file);
        f.CopyTo(s);
    }
}
