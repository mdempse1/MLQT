using OpenModelicaInterface;

namespace MLQT.Services.Tests;

/// <summary>
/// B338 — finding omc on Linux as well as Windows, by auto-detection and by browsing.
/// </summary>
/// <remarks>
/// Browse always wrote <c>bin/omc.exe</c>, and auto-detection probed only Program Files and
/// <c>omc.exe</c> on PATH, so on the Linux host browsing produced a path that could not exist and
/// nothing was ever found. The platform is a parameter here, so both are asserted on either runner.
/// </remarks>
public sealed class OpenModelicaInstallDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mlqt-omc-detect-{Guid.NewGuid():N}");

    public OpenModelicaInstallDetectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");   // an empty stand-in for the executable
        return path;
    }

    [Fact]
    public void OnLinux_TheDistributionsPlaceIsLookedAtFirst()
    {
        var candidates = OpenModelicaSettings.CandidateOmcPaths(windows: false, "", pathVariable: null, 27).ToList();

        Assert.Equal("/usr/bin/omc", candidates[0]);
        Assert.Contains("/usr/local/bin/omc", candidates);
        Assert.DoesNotContain(candidates, c => c.EndsWith(".exe", StringComparison.Ordinal));
    }

    [Fact]
    public void OnLinux_OmcIsFoundOnThePath()
    {
        var bin = Path.Combine(_root, "somewhere", "bin");
        var omc = Touch("somewhere", "bin", "omc");

        var found = OpenModelicaSettings.FindInstalledOmc(windows: false, programFiles: "",
            pathVariable: $"{Path.Combine(_root, "nothing-here")}{Path.PathSeparator}{bin}", newestMinor: 27);

        Assert.Equal(omc, found);
    }

    [Fact]
    public void OnWindows_TheInstallersFoldersComeBeforeThePath()
    {
        var installed = Touch("Program Files", "OpenModelica1.26.0-64bit", "bin", "omc.exe");
        var onPath = Touch("tools", "omc.exe");

        var found = OpenModelicaSettings.FindInstalledOmc(windows: true, Path.Combine(_root, "Program Files"),
            pathVariable: Path.GetDirectoryName(onPath), newestMinor: 27);

        Assert.Equal(installed, found);
    }

    [Fact]
    public void OnWindows_OmcExeIsFoundOnThePathWhenNothingIsInstalledThere()
    {
        var onPath = Touch("tools", "omc.exe");

        var found = OpenModelicaSettings.FindInstalledOmc(windows: true, Path.Combine(_root, "Program Files"),
            pathVariable: $"{Path.Combine(_root, "empty")}{Path.PathSeparator}{Path.GetDirectoryName(onPath)}", newestMinor: 27);

        Assert.Equal(onPath, found);
    }

    [Fact]
    public void OnWindows_TheNewestVersionedFolderIsTaken()
    {
        Touch("Program Files", "OpenModelica1.24.0-64bit", "bin", "omc.exe");
        var newest = Touch("Program Files", "OpenModelica1.26.1-64bit", "bin", "omc.exe");

        Assert.Equal(newest, OpenModelicaSettings.FindInstalledOmc(windows: true,
            Path.Combine(_root, "Program Files"), pathVariable: null, newestMinor: 27));
    }

    [Fact]
    public void NothingFoundIsEmpty()
    {
        // Windows, so no fixed system path is asked about and the answer does not depend on this machine.
        Assert.Equal("", OpenModelicaSettings.FindInstalledOmc(windows: true, Path.Combine(_root, "Program Files"),
            pathVariable: Path.Combine(_root, "empty"), newestMinor: 27));
    }

    [Fact]
    public void BrowsingToTheInstallationFolderPointsAtItsBin()
    {
        Assert.Equal(Path.Combine(_root, "bin", OpenModelicaSettings.OmcExecutableName),
            OpenModelicaSettings.OmcUnder(_root));
    }

    [Fact]
    public void BrowsingToBinItselfPointsAtTheExecutableInIt()
    {
        var omc = Touch("bin", OpenModelicaSettings.OmcExecutableName);

        Assert.Equal(omc, OpenModelicaSettings.OmcUnder(Path.Combine(_root, "bin")));
    }

    [Fact]
    public void TheExecutableIsNamedForThePlatform()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? "omc.exe" : "omc", OpenModelicaSettings.OmcExecutableName);
    }

    // ---- A blank saved path is looked for again ------------------------------------------------
    //
    // The constructor searched, then the saved "" overwrote what it found, so blanking the field
    // switched detection off for good.

    [Fact]
    public void ASavedBlankPathIsLookedForAgainOnLoad()
    {
        var installed = OpenModelicaSettings.FindInstalledOmc();
        if (installed.Length == 0)
            Assert.Skip("No omc on this machine, so a blank path found again cannot be told from one left blank.");

        var loaded = System.Text.Json.JsonSerializer.Deserialize<OpenModelicaSettings>("""{"OmcPath":""}""")!;

        Assert.Equal(installed, loaded.OmcPath);
    }

    [Fact]
    public void ASavedPathIsKeptOnLoad_EvenOneThatDoesNotExist()
    {
        // The user's choice stands; the warning under the field is what says it is missing.
        var saved = Path.Combine(_root, "nowhere", OpenModelicaSettings.OmcExecutableName);
        var json = System.Text.Json.JsonSerializer.Serialize(new OpenModelicaSettings(saved));

        var loaded = System.Text.Json.JsonSerializer.Deserialize<OpenModelicaSettings>(json)!;

        Assert.Equal(saved, loaded.OmcPath);
    }
}
