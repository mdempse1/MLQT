using System.Runtime.InteropServices;

namespace RevisionControl.Tests;

/// <summary>
/// Tests for <see cref="SvnToolLocator"/>. The resolution result is cached in a
/// process-wide <c>Lazy</c> (and depends on the host's environment / PATH), so
/// these assert the deterministic, environment-independent behaviour: the public
/// constants, the bundled-directory probe relative to the app base directory, and
/// the stability/idempotence of the cached executable path.
/// </summary>
public class SvnToolLocatorTests
{
    [Fact]
    public void OverrideEnvVar_IsTheDocumentedVariableName()
    {
        Assert.Equal("MLQT_SVN_PATH", SvnToolLocator.OverrideEnvVar);
    }

    [Fact]
    public void BundledSubdirectory_IsSvn()
    {
        Assert.Equal("svn", SvnToolLocator.BundledSubdirectory);
    }

    [Fact]
    public void BundledDirectory_ReflectsPresenceOfBundledExecutable()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "svn.exe" : "svn";
        var expectedDir = Path.Combine(AppContext.BaseDirectory, SvnToolLocator.BundledSubdirectory);
        var bundledExeExists = File.Exists(Path.Combine(expectedDir, exeName));

        if (bundledExeExists)
            Assert.Equal(expectedDir, SvnToolLocator.BundledDirectory);
        else
            Assert.Null(SvnToolLocator.BundledDirectory);
    }

    [Fact]
    public void SvnExecutablePath_IsCached_ReturnsSameValueOnRepeatedAccess()
    {
        var first = SvnToolLocator.SvnExecutablePath;
        var second = SvnToolLocator.SvnExecutablePath;

        Assert.Equal(first, second);
    }

    [Fact]
    public void SvnExecutablePath_WhenBundledClientPresent_ResolvesToABundledOrOverridePath()
    {
        // When a bundled client exists, resolution must yield a non-null path.
        // (It may point at the override or the bundled copy; either way it is set.)
        if (SvnToolLocator.BundledDirectory != null)
            Assert.NotNull(SvnToolLocator.SvnExecutablePath);
    }
}
