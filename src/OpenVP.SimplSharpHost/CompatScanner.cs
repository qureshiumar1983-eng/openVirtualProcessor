using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OpenVP.SimplSharpHost;

/// <summary>What a program uses from the Crestron API, and which parts OpenVP doesn't implement yet.</summary>
public sealed record CompatReport(
    string Assembly,
    string? ControlSystemType,
    IReadOnlyList<string> CrestronAssemblies,
    IReadOnlyList<string> UsedTypes,
    IReadOnlyList<string> MissingTypes,
    IReadOnlyList<string> MissingMembers)
{
    public bool FullySupported => MissingTypes.Count == 0 && MissingMembers.Count == 0;

    public override string ToString()
    {
        var lines = new List<string>
        {
            $"Assembly: {Path.GetFileName(Assembly)}",
            $"Entry class: {ControlSystemType ?? "(none found)"}",
            $"Crestron assemblies referenced: {string.Join(", ", CrestronAssemblies)}",
            $"Crestron types used: {UsedTypes.Count}"
        };
        if (FullySupported) lines.Add("Everything it uses is implemented.");
        foreach (var t in MissingTypes) lines.Add($"  MISSING TYPE   {t}");
        foreach (var m in MissingMembers) lines.Add($"  MISSING MEMBER {m}");
        return string.Join('\n', lines);
    }
}

/// <summary>
/// Reads a compiled program's metadata (without running it) and checks each Crestron type and member
/// it references against the shim. Runs before load so a missing API shows up as a clear list rather
/// than a MissingMethodException at 2 a.m.
/// </summary>
public static class CompatScanner
{
    public static CompatReport Scan(string assemblyPath, Assembly shim)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var crestronScopes = new HashSet<AssemblyReferenceHandle>();
        var crestronNames = new SortedSet<string>();
        foreach (var h in md.AssemblyReferences)
        {
            var name = md.GetString(md.GetAssemblyReference(h).Name);
            if (SlotLoadContext.LooksLikeCrestron(name)) { crestronScopes.Add(h); crestronNames.Add(name); }
        }

        var used = new SortedSet<string>();
        var missingTypes = new SortedSet<string>();
        var missingMembers = new SortedSet<string>();
        var typeRefNames = new Dictionary<TypeReferenceHandle, string>();

        string? FullName(TypeReferenceHandle h)
        {
            if (typeRefNames.TryGetValue(h, out var cached)) return cached;
            var tr = md.GetTypeReference(h);
            string? result = null;
            if (tr.ResolutionScope.Kind == HandleKind.AssemblyReference &&
                crestronScopes.Contains((AssemblyReferenceHandle)tr.ResolutionScope))
            {
                var ns = md.GetString(tr.Namespace);
                result = (ns.Length > 0 ? ns + "." : "") + md.GetString(tr.Name);
            }
            else if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var parent = FullName((TypeReferenceHandle)tr.ResolutionScope);
                if (parent != null) result = parent + "+" + md.GetString(tr.Name);
            }
            typeRefNames[h] = result!;
            return result;
        }

        foreach (var h in md.TypeReferences)
        {
            var name = FullName(h);
            if (name == null) continue;
            used.Add(name);
            if (shim.GetType(name) == null) missingTypes.Add(name);
        }

        foreach (var h in md.MemberReferences)
        {
            var mr = md.GetMemberReference(h);
            string? owner = mr.Parent.Kind switch
            {
                HandleKind.TypeReference => FullName((TypeReferenceHandle)mr.Parent),
                HandleKind.TypeSpecification => null, // generic instantiations: skip
                _ => null
            };
            if (owner == null || missingTypes.Contains(owner)) continue;
            var type = shim.GetType(owner);
            if (type == null) continue;
            var member = md.GetString(mr.Name);
            int? args = null; // how many arguments the program passes: a method with that name but another count is a missing overload
            try
            {
                var sig = md.GetBlobReader(mr.Signature);
                var header = sig.ReadSignatureHeader();
                if (header.Kind == SignatureKind.Method)
                {
                    if (header.IsGeneric) sig.ReadCompressedInteger();
                    args = sig.ReadCompressedInteger();
                }
            }
            catch (BadImageFormatException) { }
            if (!IsMemberSupported(type, member, args))
                missingMembers.Add(args is { } n && HasMember(type, member, All) ? $"{owner}.{member}({n} argument{(n == 1 ? "" : "s")})" : $"{owner}.{member}");
        }

        string? entry = null;
        foreach (var h in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(h);
            if (td.BaseType.Kind == HandleKind.TypeReference &&
                FullName((TypeReferenceHandle)td.BaseType) == "Crestron.SimplSharpPro.CrestronControlSystem")
            {
                var ns = md.GetString(td.Namespace);
                entry = (ns.Length > 0 ? ns + "." : "") + md.GetString(td.Name);
            }
        }

        return new CompatReport(assemblyPath, entry, crestronNames.ToList(), used.ToList(), missingTypes.ToList(), missingMembers.ToList());
    }

    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    /// <summary>True if <paramref name="type"/> (or a base type) has a member of that name; for a method or constructor, one taking <paramref name="args"/> arguments when that is known.</summary>
    public static bool IsMemberSupported(Type type, string member, int? args)
    {
        if (member == ".ctor")
            return args == null ? type.GetConstructors(All).Length > 0 : type.GetConstructors(All).Any(c => c.GetParameters().Length == args);
        if (args == null) return HasMember(type, member, All);
        var named = false;
        for (var t = type; t != null; t = t.BaseType)
            foreach (var m in t.GetMember(member, All))
            {
                named = true;
                if (m is MethodBase mb && mb.GetParameters().Length == args) return true;
            }
        if (!named) // an interface's methods aren't on its base types
            foreach (var i in type.GetInterfaces())
                if (i.GetMember(member, All).OfType<MethodBase>().Any(mb => mb.GetParameters().Length == args)) return true;
        return false;
    }

    private static bool HasMember(Type type, string name, BindingFlags flags)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.GetMember(name, flags).Length > 0) return true;
        return false;
    }

    /// <summary>True if the file is a managed assembly that defines a CrestronControlSystem subclass.</summary>
    public static bool DefinesControlSystem(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return false;
            var md = pe.GetMetadataReader();
            foreach (var h in md.TypeDefinitions)
            {
                var bt = md.GetTypeDefinition(h).BaseType;
                if (bt.Kind != HandleKind.TypeReference) continue;
                var tr = md.GetTypeReference((TypeReferenceHandle)bt);
                if (md.GetString(tr.Name) == "CrestronControlSystem") return true;
            }
        }
        catch (BadImageFormatException) { }
        return false;
    }
}
