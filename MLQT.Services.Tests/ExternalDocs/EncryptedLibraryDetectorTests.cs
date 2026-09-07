using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// Tests for <see cref="EncryptedLibraryDetector"/> — recognising an encrypted library and
/// establishing which version of it is on the machine.
/// </summary>
public class EncryptedLibraryDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-encrypted-tests", Guid.NewGuid().ToString("N"));

    public EncryptedLibraryDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private string MakeLibrary(string directoryName, string? libraryInfo = null, bool withHelp = false)
    {
        var path = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.moe"), "encrypted");

        if (libraryInfo is not null)
            File.WriteAllText(Path.Combine(path, "libraryinfo.mos"), libraryInfo);

        if (withHelp)
        {
            var help = Path.Combine(path, "help");
            Directory.CreateDirectory(help);
            File.WriteAllText(Path.Combine(help, "Lib.html"), "<html></html>");
        }

        return path;
    }

    #region Recognition

    [Fact]
    public void IsEncryptedLibraryRoot_WithoutEncryptedPackage_IsFalse()
    {
        var path = Path.Combine(_root, "Plain");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.mo"), "package Plain end Plain;");

        Assert.False(EncryptedLibraryDetector.IsEncryptedLibraryRoot(path));
        Assert.Null(EncryptedLibraryDetector.Detect(path));
    }

    [Fact]
    public void Detect_FindsEncryptedPackageAndHelp()
    {
        var path = MakeLibrary("Battery 2.9.0", withHelp: true);

        var detected = EncryptedLibraryDetector.Detect(path);

        Assert.NotNull(detected);
        Assert.Equal("Battery", detected!.Name);
        Assert.EndsWith("package.moe", detected.EncryptedPackagePath);
        Assert.True(detected.HasDocumentation);
    }

    [Fact]
    public void Detect_HelpDirectoryWithNoHtml_CountsAsNoDocumentation()
    {
        var path = MakeLibrary("Opaque 1.0.0");
        Directory.CreateDirectory(Path.Combine(path, "help"));

        Assert.False(EncryptedLibraryDetector.Detect(path)!.HasDocumentation);
    }

    #endregion

    #region Version resolution

    [Fact]
    public void Detect_VersionedDirectoryName_WinsOverLibraryInfo()
    {
        // The directory name is what a tool resolves against when choosing which copy to load,
        // so it states which version is actually present — the question the dependency-version
        // check asks. libraryinfo.mos can lag a rebuild.
        var path = MakeLibrary("Battery 2.9.0", libraryInfo: "LibraryInfoMenuCommand(version=\"1.0.0\")");

        Assert.Equal("2.9.0", EncryptedLibraryDetector.Detect(path)!.Version);
    }

    [Fact]
    public void Detect_NoVersionSuffix_FallsBackToLibraryInfo()
    {
        var path = MakeLibrary("CATIAMultiBody", libraryInfo: "LibraryInfoMenuCommand(version=\"1.15.1\")");

        var detected = EncryptedLibraryDetector.Detect(path)!;
        Assert.Equal("1.15.1", detected.Version);
        Assert.Equal("CATIAMultiBody", detected.Name);
    }

    [Fact]
    public void Detect_VersionSuffixButNoLibraryInfo_StillResolves()
    {
        // Roughly a third of the shipped libraries carry no libraryinfo.mos at all, so the
        // directory name is the only source for them.
        var path = MakeLibrary("VeSyMA 2026.1");

        Assert.Equal("2026.1", EncryptedLibraryDetector.Detect(path)!.Version);
    }

    [Fact]
    public void Detect_NameWithASpaceButNoVersion_IsNotMistakenForOne()
    {
        // Without the version-shaped guard the last word wins, and — because the directory name
        // is consulted first — that nonsense would beat the correct value in libraryinfo.mos.
        var path = MakeLibrary("My Library", libraryInfo: "LibraryInfoMenuCommand(version=\"3.1\")");

        var detected = EncryptedLibraryDetector.Detect(path)!;
        Assert.Equal("3.1", detected.Version);
        Assert.Equal("My Library", detected.Name);
    }

    [Fact]
    public void Detect_NoVersionAnywhere_LeavesVersionNull()
    {
        var path = MakeLibrary("Mystery");

        Assert.Null(EncryptedLibraryDetector.Detect(path)!.Version);
    }

    [Fact]
    public void Detect_ModelicaVersionEntry_IsNotReadAsTheLibraryVersion()
    {
        // libraryinfo.mos carries both `version` and `ModelicaVersion`; reading the wrong one
        // reports the library as being at the version of its dependency.
        var path = MakeLibrary("Thing", libraryInfo:
            "LibraryInfoMenuCommand(\n  ModelicaVersion=\">=4.1.0\",\n  version=\"7.2\")");

        Assert.Equal("7.2", EncryptedLibraryDetector.Detect(path)!.Version);
    }

    [Theory]
    [InlineData("Battery 2.9.0", "Battery", "2.9.0")]
    [InlineData("Visa2Steam 1.20", "Visa2Steam", "1.20")]
    [InlineData("TIL3_AddOn_NTU 2026.1", "TIL3_AddOn_NTU", "2026.1")]
    [InlineData("Modelica 4.0.0+maint.om", "Modelica", "4.0.0+maint.om")]
    [InlineData("CATIAMultiBody", "CATIAMultiBody", null)]
    [InlineData("LinearAnalysis", "LinearAnalysis", null)]
    public void SplitVersionedDirectoryName_HandlesRealNames(string directory, string name, string? version)
    {
        var (actualName, actualVersion) = EncryptedLibraryDetector.SplitVersionedDirectoryName(directory);

        Assert.Equal(name, actualName);
        Assert.Equal(version, actualVersion);
    }

    #endregion

    #region Against the installed libraries

    [Fact]
    public void Detect_EveryInstalledEncryptedLibrary_IsRecognised()
    {
        var libraries = DymolaInstall.EncryptedLibraries();
        if (libraries.Count == 0)
            return;   // No Dymola on this machine.

        var withoutDocumentation = new List<string>();
        foreach (var path in libraries)
        {
            var detected = EncryptedLibraryDetector.Detect(path);
            Assert.NotNull(detected);
            Assert.False(string.IsNullOrWhiteSpace(detected!.Name), $"no name for {path}");
            Assert.True(File.Exists(detected.EncryptedPackagePath));

            if (!detected.HasDocumentation)
                withoutDocumentation.Add(detected.Name);
        }

        // Documentation is the whole basis of this feature, so a sharp drop in how many libraries
        // ship it is something a future Dymola could introduce and we would want to know about.
        Assert.True(
            withoutDocumentation.Count <= libraries.Count / 10,
            $"{withoutDocumentation.Count} of {libraries.Count} installed encrypted libraries ship no " +
            $"documentation: {string.Join(", ", withoutDocumentation)}");
    }

    #endregion
}
