namespace OpenVP.Tests;

/// <summary>Runs tests/js/crcomlib.test.js (the join and contract mapping of openvp-crcomlib.js) with node.</summary>
public static class CrComLibScriptTests
{
    [Test]
    public static void The_script_maps_join_names_and_contracts_correctly()
    {
        var script = Path.Combine(Paths.RepoRoot, "tests", "js", "crcomlib.test.js");
        Assert.True(File.Exists(script), "test script exists");
        System.Diagnostics.ProcessStartInfo psi = new("node", $"\"{script}\"") { RedirectStandardOutput = true, RedirectStandardError = true };
        System.Diagnostics.Process? p;
        try { p = System.Diagnostics.Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { Console.WriteLine("  (node is not installed: the openvp-crcomlib.js mapping tests were not run)"); return; }
        using (p!)
        {
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            Assert.True(p.ExitCode == 0, "node tests/js/crcomlib.test.js failed:\n" + output);
        }
    }

    [Test]
    public static void The_SIMPLplus_highlighter_tokenizes_correctly()
    {
        var script = Path.Combine(Paths.RepoRoot, "tests", "js", "splus.test.js");
        Assert.True(File.Exists(script), "test script exists");
        System.Diagnostics.ProcessStartInfo psi = new("node", $"\"{script}\"") { RedirectStandardOutput = true, RedirectStandardError = true };
        System.Diagnostics.Process? p;
        try { p = System.Diagnostics.Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception) { Console.WriteLine("  (node is not installed: the openvp-splus.js tokenizer tests were not run)"); return; }
        using (p!)
        {
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            Assert.True(p.ExitCode == 0, "node tests/js/splus.test.js failed:\n" + output);
        }
    }
}
