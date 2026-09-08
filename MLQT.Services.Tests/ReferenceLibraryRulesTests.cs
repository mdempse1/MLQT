using MLQT.Services;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Not loading the same reference library twice.
/// </summary>
/// <remarks>
/// <para>Phase 7b-5, from a real configuration. MLQT has two ways to say "load this for reference" -
/// a repository marked reference-only, and a path in the reference-library settings - and a user had
/// used both for the same three directories. The result was <b>159 loads of 97 distinct
/// libraries</b>: the standard library, ExternData and sixty others parsed and indexed twice.</para>
///
/// <para>It went unnoticed because the graph is keyed on class id, so the duplicates collapsed there.
/// The counts that are per-library did not: two entries in the browser tree, and a model total of
/// 118,612 against a true 77,860 - the number the deferred-analysis threshold is compared against.</para>
/// </remarks>
public class ReferenceLibraryRulesTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\libs" : "/libs";

    private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    private static LoadedLibrary Loaded(string name, string path,
        LibrarySourceType type = LibrarySourceType.Directory) =>
        new() { Name = name, SourcePath = path, SourceType = type };

    private static string? Skip(string path, IReadOnlyList<LoadedLibrary> loaded,
        bool isEncrypted = false, string? name = null, bool useEncrypted = true) =>
        ReferenceLibraryRules.ReasonToSkip(path, isEncrypted, name ?? Path.GetFileName(path), loaded, useEncrypted);

    // ---- nothing loaded yet --------------------------------------------------------------------

    [Fact]
    public void AnUnseenLibraryIsLoaded()
    {
        Assert.Null(Skip(At("Modelica"), []));
    }

    [Fact]
    public void AnUnseenLibraryIsLoadedEvenWhenOthersAre()
    {
        Assert.Null(Skip(At("Modelica"), [Loaded("ExternData", At("ExternData"))]));
    }

    // ---- the same directory --------------------------------------------------------------------

    [Fact]
    public void TheSameDirectoryIsNotLoadedAgain()
    {
        // The case that actually happened, three times over in one configuration: the folder is
        // already in the graph because a reference-only repository pointed at it.
        var reason = Skip(At("Dymola", "Library"), [Loaded("Library", At("Dymola", "Library"))]);

        // The path wording, not the name wording. The two libraries here share a name as well, so a
        // looser assertion passes with the path rule deleted - which is how it was first written.
        Assert.Contains("already loaded as", reason);
    }

    [Fact]
    public void TheSameDirectorySpeltDifferentlyIsNotLoadedAgain()
    {
        // A trailing separator and a "." segment are the same folder. Configured by hand and copied
        // from a file dialog, the two spellings differ far more often than they match.
        var loaded = new[] { Loaded("Library", At("Dymola", "Library")) };

        Assert.NotNull(Skip(At("Dymola", "Library") + Path.DirectorySeparatorChar, loaded));
        Assert.NotNull(Skip(At("Dymola", ".", "Library"), loaded));
    }

    [Fact]
    public void ASiblingDirectoryIsStillLoaded()
    {
        // The path check has to be an equality, not a prefix: Library and LibraryExtras are two
        // libraries, and a StartsWith would silently drop the second.
        Assert.Null(Skip(At("Dymola", "LibraryExtras"), [Loaded("Library", At("Dymola", "Library"))]));
    }

    [Fact]
    public void ANestedDirectoryIsStillLoaded()
    {
        // Configuring both a folder and a library inside it is how the duplicates arose - but the
        // right answer for the *inner* path is to load it if discovery has not already produced it,
        // and the outer path being loaded says nothing about that.
        Assert.Null(Skip(At("Dymola", "Library", "Modelica"), [Loaded("Dymola", At("Dymola"))]));
    }

    // ---- the same library from somewhere else --------------------------------------------------

    [Fact]
    public void TheSameNameFromAnotherPlaceIsNotLoadedAgain()
    {
        // Two copies of one library cannot both be in the graph: they share every class id, and which
        // one wins is decided class by class in AddNode rather than by anything a user chose.
        var reason = Skip(At("Dymola", "Library", "Modelica"),
                          [Loaded("Modelica", At("checkout", "MSL"))]);

        Assert.Contains("already loaded from", reason);
    }

    [Fact]
    public void AReadableCopyIsLoadedOverAnEncryptedOne()
    {
        // The preference this rule has always had, and the reason it cannot simply be "skip on a name
        // match". Real source beats classes reconstructed from vendor documentation, in whichever
        // order the two arrive; AddNode replaces the stubs when it lands.
        Assert.Null(Skip(At("checkout", "Claytex"),
                         [Loaded("Claytex", At("Dymola", "Library", "Claytex 2026.1"),
                                 LibrarySourceType.EncryptedDirectory)],
                         isEncrypted: false, name: "Claytex"));
    }

    [Fact]
    public void AnEncryptedCopyIsNotLoadedOverReadableSource()
    {
        // The half that was already implemented, kept: a tool ships the encrypted build of a library
        // the user has checked out, and loading it would add thousands of stubs to be discarded.
        var reason = Skip(At("Dymola", "Library", "Claytex 2026.1"),
                          [Loaded("Claytex", At("checkout", "Claytex"))],
                          isEncrypted: true, name: "Claytex");

        Assert.Contains("already loaded from", reason);
    }

    [Fact]
    public void AnEncryptedCopyIsNotLoadedOverAnotherEncryptedCopy()
    {
        // Two tool installations both shipping the same encrypted library. Neither is better, so the
        // first one wins - what matters is that the second is not loaded on top of it.
        var reason = Skip(At("Dymola 2026x", "Library", "VeSyMA 2026.1"),
                          [Loaded("VeSyMA", At("Dymola 2025x", "Library", "VeSyMA 2025.2"),
                                  LibrarySourceType.EncryptedDirectory)],
                          isEncrypted: true, name: "VeSyMA");

        Assert.NotNull(reason);
    }

    [Fact]
    public void AnEncryptedLibraryWithNoNameIsLoadedAndLetToFail()
    {
        // Its documentation gave no name, so there is nothing to compare against. Loading it and
        // finding it contributes no classes is the existing behaviour, and better than guessing.
        // Called directly rather than through the helper, which fills a missing name in from the
        // folder - which is right for every other case here and wrong for this one.
        var reason = ReferenceLibraryRules.ReasonToSkip(
            At("Dymola", "Library", "Mystery"),
            isEncrypted: true,
            encryptedName: null,
            [Loaded("Mystery", At("elsewhere", "Mystery"))],
            useEncryptedDocumentation: true);

        Assert.Null(reason);
    }

    // ---- the user's own setting ----------------------------------------------------------------

    [Fact]
    public void EncryptedLibrariesAreSkippedWhenTheUserTurnsThemOff()
    {
        var reason = Skip(At("Dymola", "Library", "VeSyMA 2026.1"), [],
                          isEncrypted: true, name: "VeSyMA", useEncrypted: false);

        Assert.Contains("turned off", reason);
    }

    [Fact]
    public void ThatSettingDoesNotAffectReadableLibraries()
    {
        Assert.Null(Skip(At("checkout", "MyLib"), [], useEncrypted: false));
    }
}
