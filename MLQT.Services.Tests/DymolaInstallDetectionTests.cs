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
}
