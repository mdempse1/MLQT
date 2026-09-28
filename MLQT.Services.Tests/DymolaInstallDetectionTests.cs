using DymolaInterface;

namespace MLQT.Services.Tests;

/// <summary>
/// B337 — which Dymola installs auto-detection finds, against a fake Program Files.
/// </summary>
/// <remarks>
/// Only <c>Dymola {year}x</c> and <c>Dymola {year}x Refresh 1</c> were probed, so the spring releases
/// (<c>Dymola 2022</c>, <c>Dymola 2023</c>) were never found; and the year was decremented before the
/// refresh name was built, so <c>Dymola 2020x Refresh 1</c> was probed while external-tools.md said
/// 2021 onwards. Here rather than in DymolaInterface.Tests, which no CI job runs.
/// </remarks>
public sealed class DymolaInstallDetectionTests : IDisposable
{
    private readonly string _programFiles =
        Path.Combine(Path.GetTempPath(), $"mlqt-dymola-detect-{Guid.NewGuid():N}");

    public DymolaInstallDetectionTests() => Directory.CreateDirectory(_programFiles);

    public void Dispose()
    {
        try { Directory.Delete(_programFiles, recursive: true); } catch { /* temp */ }
    }

    private string Install(string folder)
    {
        var bin = Path.Combine(_programFiles, folder, "bin64");
        Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, "dymola.exe");
        File.WriteAllText(exe, "");   // not Modelica: an empty stand-in for an executable
        return exe;
    }

    [Theory]
    [InlineData("Dymola 2022")]
    [InlineData("Dymola 2023")]
    [InlineData("Dymola 2021")]
    public void ASpringReleaseOnItsOwnIsFound(string folder)
    {
        var exe = Install(folder);

        Assert.Equal(exe, DymolaSettings.FindInstalledDymola(_programFiles, newestYear: 2027));
    }

    [Fact]
    public void TheNewestReleaseIsTaken_InReleaseOrder()
    {
        // In the order they came out: 2023 (spring 2022), 2023x, 2023x Refresh 1, then 2024x.
        Install("Dymola 2023");
        Install("Dymola 2023x");
        Install("Dymola 2023x Refresh 1");
        var newest = Install("Dymola 2024x");

        Assert.Equal(newest, DymolaSettings.FindInstalledDymola(_programFiles, newestYear: 2027));
    }

    [Fact]
    public void ARefreshIsPreferredToTheReleaseItRefreshes()
    {
        Install("Dymola 2026x");
        var refresh = Install("Dymola 2026x Refresh 1");

        Assert.Equal(refresh, DymolaSettings.FindInstalledDymola(_programFiles, newestYear: 2027));
    }

    [Fact]
    public void NothingBefore2021IsProbed()
    {
        Install("Dymola 2020x Refresh 1");
        Install("Dymola 2020x");
        Install("Dymola 2020");

        Assert.Equal("", DymolaSettings.FindInstalledDymola(_programFiles, newestYear: 2027));
        Assert.DoesNotContain(DymolaSettings.CandidateInstallNames(2027), n => n.Contains("2020"));
    }

    [Fact]
    public void TheCandidatesAreExactlyTheThreeNamesPerYear_NewestFirst()
    {
        Assert.Equal(
            ["Dymola 2022x Refresh 1", "Dymola 2022x", "Dymola 2022",
             "Dymola 2021x Refresh 1", "Dymola 2021x", "Dymola 2021"],
            DymolaSettings.CandidateInstallNames(2022));
    }

    // ---- Linux, and PATH (B395) ----------------------------------------------------------------
    //
    // Only Program Files was looked at, so nothing was ever found on the Linux host. The platform
    // and the Linux folders are parameters, so every branch is asserted on either runner.

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_programFiles, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");   // an empty stand-in for an executable
        return path;
    }

    private string Opt => Path.Combine(_programFiles, "opt");
    private string LocalBin => Path.Combine(_programFiles, "usr-local-bin");

    private string FindOnLinux(string? pathVariable = null) => DymolaSettings.FindInstalledDymola(
        windows: false, programFiles: "", pathVariable, newestYear: 2027, Opt, LocalBin);

    [Fact]
    public void OnLinux_TheInstallationUnderOptIsFound()
    {
        var dymola = Touch("opt", "dymola-2025x-x86_64", "bin64", "dymola");

        Assert.Equal(dymola, FindOnLinux());
    }

    [Fact]
    public void OnLinux_TheNewestInstallationIsTaken_InReleaseOrder()
    {
        Touch("opt", "dymola-2024x-x86_64", "bin64", "dymola");
        Touch("opt", "dymola-2025-x86_64", "bin64", "dymola");
        var newest = Touch("opt", "dymola-2025x-x86_64", "bin64", "dymola");
        Touch("opt", "dymola2023x-x86_64", "bin64", "dymola");   // the guide's own spelling, no hyphen

        Assert.Equal(newest, FindOnLinux());
    }

    [Fact]
    public void OnLinux_ARefreshIsPreferredToTheReleaseItRefreshes()
    {
        Touch("opt", "dymola-2025x-x86_64", "bin64", "dymola");
        var refresh = Touch("opt", "dymola-2025x-refresh1-x86_64", "bin64", "dymola");

        Assert.Equal(refresh, FindOnLinux());
    }

    [Fact]
    public void OnLinux_ALauncherIsPreferredToTheProgramItLaunches()
    {
        // The launcher sets the environment the program needs to find its libraries.
        Touch("opt", "dymola-2025x-x86_64", "bin64", "dymola");
        var versioned = Touch("usr-local-bin", "dymola-2025x-x86_64");

        Assert.Equal(versioned, FindOnLinux());

        var plain = Touch("usr-local-bin", "dymola");
        Assert.Equal(plain, FindOnLinux());
    }

    [Fact]
    public void OnLinux_DymolaIsFoundOnThePath_AndNothingWindowsIsAskedAbout()
    {
        var onPath = Touch("tools", "dymola");
        Touch("tools", "dymola.exe");

        Assert.Equal(onPath, FindOnLinux(pathVariable:
            $"{Path.Combine(_programFiles, "empty")}{Path.PathSeparator}{Path.GetDirectoryName(onPath)}"));
        Assert.DoesNotContain(
            DymolaSettings.CandidateDymolaPaths(false, "", Path.GetDirectoryName(onPath), 2027, Opt, LocalBin),
            c => c.EndsWith(".exe", StringComparison.Ordinal));
    }

    [Fact]
    public void OnLinux_OtherFilesNamedDymolaAreNotTakenForAnInstallation()
    {
        Touch("opt", "dymola-notes", "bin64", "dymola");
        Touch("usr-local-bin", "dymola-license-helper");

        Assert.Equal("", FindOnLinux());
    }

    [Fact]
    public void OnWindows_ProgramFilesComesBeforeThePath()
    {
        var installed = Install("Dymola 2025x");
        var onPath = Touch("tools", "dymola.exe");

        Assert.Equal(installed, DymolaSettings.FindInstalledDymola(windows: true, _programFiles,
            Path.GetDirectoryName(onPath), newestYear: 2027));
    }

    [Fact]
    public void OnWindows_DymolaExeIsFoundOnThePathWhenNothingIsInstalled()
    {
        var onPath = Touch("tools", "dymola.exe");

        Assert.Equal(onPath, DymolaSettings.FindInstalledDymola(windows: true, _programFiles,
            Path.GetDirectoryName(onPath), newestYear: 2027));
    }

    [Fact]
    public void BrowsingToTheInstallationFolderPointsAtItsBin64_OrToBin64Itself()
    {
        Assert.Equal(Path.Combine(_programFiles, "bin64", "dymola.exe"), DymolaSettings.DymolaUnder(_programFiles));

        var exe = Install("Dymola 2025x");
        Assert.Equal(exe, DymolaSettings.DymolaUnder(Path.GetDirectoryName(exe)!));
    }

    [Fact]
    public void TheExecutableIsNamedForThePlatform()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? "dymola.exe" : "dymola", DymolaSettings.DymolaExecutableName);
    }

    // ---- A blank saved path is looked for again ------------------------------------------------
    //
    // The constructor searched, then the saved "" overwrote what it found, so blanking the field
    // switched detection off for good.

    [Fact]
    public void ASavedBlankPathIsLookedForAgainOnLoad()
    {
        var installed = DymolaSettings.FindInstalledDymola();
        if (installed.Length == 0)
            Assert.Skip("No Dymola on this machine, so a blank path found again cannot be told from one left blank.");

        var loaded = System.Text.Json.JsonSerializer.Deserialize<DymolaSettings>("""{"DymolaPath":""}""")!;

        Assert.Equal(installed, loaded.DymolaPath);
    }

    [Fact]
    public void ASavedPathIsKeptOnLoad_EvenOneThatDoesNotExist()
    {
        // The user's choice stands; the warning under the field is what says it is missing.
        var saved = Path.Combine(_programFiles, "nowhere", DymolaSettings.DymolaExecutableName);
        var json = System.Text.Json.JsonSerializer.Serialize(new DymolaSettings { DymolaPath = saved });

        var loaded = System.Text.Json.JsonSerializer.Deserialize<DymolaSettings>(json)!;

        Assert.Equal(saved, loaded.DymolaPath);
    }
}
