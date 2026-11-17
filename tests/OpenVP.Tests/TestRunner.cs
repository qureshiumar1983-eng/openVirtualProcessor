using System.Reflection;

namespace OpenVP.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public sealed class AssertException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool cond, string what) { if (!cond) throw new AssertException(what); }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException($"{what}: expected <{expected}>, got <{actual}>");
    }

    /// <summary>Poll until the condition holds (events run on slot threads).</summary>
    public static void Eventually(Func<bool> cond, string what, int timeoutMs = 3000)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            if (cond()) return;
            Thread.Sleep(10);
        }
        throw new AssertException($"Timed out waiting for: {what}");
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        var filter = args.FirstOrDefault();
        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => filter == null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();

        int pass = 0, fail = 0;
        foreach (var t in tests)
        {
            var name = $"{t.DeclaringType!.Name}.{t.Name}";
            try
            {
                t.Invoke(null, null);
                Console.WriteLine($"  PASS  {name}");
                pass++;
            }
            catch (TargetInvocationException e)
            {
                Console.WriteLine($"  FAIL  {name}\n        {e.InnerException?.Message}");
                if (e.InnerException is not AssertException) Console.WriteLine(e.InnerException);
                fail++;
            }
        }
        Console.WriteLine($"\n{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}

public static class Paths
{
    public static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenVP.sln"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("OpenVP.sln not found");
        }
    }

    public static string SampleDll =>
        Directory.GetFiles(Path.Combine(RepoRoot, "samples", "SampleProgram", "bin"), "SampleProgram.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).First();

    public static string TempData() => Directory.CreateTempSubdirectory("openvp-test").FullName;
}
