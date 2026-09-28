namespace OpenModelicaInterface.Tests;

/// <summary>
/// The parts of <see cref="OpenModelicaSettings"/> that need no omc: the time limits as spans, the
/// validity check, the installation folder and auto-detection's answer.
/// </summary>
/// <remarks>
/// Written when the assembly joined the coverage ratchet (B438), where this class measured 73% from
/// the tests needing no tool - below the bar for want of tests rather than of a tool.
/// </remarks>
public class OpenModelicaSettingsTests
{
    [Fact]
    public void ZeroMeansNoLimit()
    {
        var settings = new OpenModelicaSettings("x") { CommandTimeoutMs = 0, StartupTimeoutMs = 0 };

        Assert.Equal(Timeout.InfiniteTimeSpan, settings.CommandTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, settings.StartupTimeout);
    }

    [Fact]
    public void ANegativeLimitFallsBackToTheDefault()
    {
        // The dialog does not allow one; a hand-edited settings file might.
        var settings = new OpenModelicaSettings("x") { CommandTimeoutMs = -1, StartupTimeoutMs = -1 };

        Assert.Equal(TimeSpan.FromMilliseconds(60000), settings.CommandTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), settings.StartupTimeout);
    }

    [Fact]
    public void APositiveLimitIsTakenAsMilliseconds()
    {
        var settings = new OpenModelicaSettings("x") { CommandTimeoutMs = 1234, StartupTimeoutMs = 4321 };

        Assert.Equal(TimeSpan.FromMilliseconds(1234), settings.CommandTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(4321), settings.StartupTimeout);
    }

    [Fact]
    public void TheDefaultStartupLimitIsFiveSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new OpenModelicaSettings("x").StartupTimeout);
    }

    [Fact]
    public void IsValid_OnlyForAPathThatExists()
    {
        var file = Path.Combine(Path.GetTempPath(), $"omc-{Guid.NewGuid():N}");
        File.WriteAllText(file, "");
        try
        {
            Assert.True(new OpenModelicaSettings(file).IsValid());
            Assert.False(new OpenModelicaSettings(file + ".missing").IsValid());
            Assert.False(new OpenModelicaSettings("").IsValid());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TheInstallationDirectoryIsTheFolderAboveBin()
    {
        var install = Path.Combine(Path.GetTempPath(), "OpenModelica1.26.0-64bit");
        var settings = new OpenModelicaSettings(Path.Combine(install, "bin", OpenModelicaSettings.OmcExecutableName));

        Assert.Equal(install, settings.GetInstallationDirectory());
    }

    [Fact]
    public void TheInstallationDirectoryOfNoPathIsEmpty()
    {
        Assert.Equal("", new OpenModelicaSettings("").GetInstallationDirectory());
    }

    [Fact]
    public void TheCommonWindowsPathsAreOmcUnderAVersionedFolder()
    {
        var paths = OpenModelicaSettings.CommonWindowsPaths;

        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.Matches(@"\\OpenModelica1\.\d+\.\d+-64bit\\bin\\omc\.exe$", p));
    }

    [Fact]
    public void AutoDetectAnswersWhatDetectionFinds()
    {
        // Whatever this machine has: an omc that detection finds, or nothing at all.
        var found = OpenModelicaSettings.FindInstalledOmc();
        var detected = OpenModelicaSettings.TryAutoDetect();

        if (found.Length == 0)
            Assert.Null(detected);
        else
            Assert.Equal(found, detected!.OmcPath);
    }
}
