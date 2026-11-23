using OpenVP.Core;

namespace OpenVP.Tests;

public static class WelcomeStateTests
{
    [Test]
    public static void ShowsUntilDismissedAndIsRemembered()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-welcome").FullName;
        var w = new WelcomeState(dir);
        Assert.True(w.Show, "shows on first run");
        w.Dismiss();
        Assert.True(!w.Show, "hidden after dismiss");
        w.Dismiss();
        Assert.True(!new WelcomeState(dir).Show, "still hidden after a restart");
    }

    [Test]
    public static void MissingFolderAndBrokenFileAreHarmless()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("openvp-welcome").FullName, "not", "yet");
        var w = new WelcomeState(dir);
        w.Dismiss();
        Assert.True(File.Exists(Path.Combine(dir, "welcome.json")), "folder created on dismiss");
        File.WriteAllText(Path.Combine(dir, "welcome.json"), "{ nonsense");
        Assert.True(new WelcomeState(dir).Show, "broken file means show again");
    }
}
