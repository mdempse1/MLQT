using MLQT.Services;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The name a discovered reference library is compared by — <see cref="ReferenceLibraryRules.CandidateName"/>.
/// </summary>
/// <remarks>
/// <para><b>From a real load, 2026-11-04.</b> A project pointing at <c>C:\Projects\Modelica\MSL</c>
/// showed <b>two</b> of its eight libraries in the browser while the log reported all eight loaded
/// and no errors. The reference-library path was a tool's library folder, which ships readable
/// source for most of the standard library, and the name handed to
/// <see cref="ReferenceLibraryRules.ReasonToSkip"/> was <c>Path.GetFileName</c> of the discovered
/// path. That folder names almost nothing after the package it holds: <c>Modelica 4.1.0</c> carries
/// the release, and <c>Complex.mo</c>, <c>ObsoleteModelica4.mo</c>,
/// <c>ModelicaTestConversion4.mo</c> and <c>ModelicaTestOverdetermined.mo</c> are single-file
/// libraries with an extension. Six of the eight therefore matched nothing and loaded a second time;
/// the two that survived were <c>ModelicaTest</c>, which the tool does not ship, and
/// <c>ModelicaReference</c> — whose folder happens to be named after its package, which is why the
/// one case anybody had ever tested was the one case that worked.</para>
///
/// <para>So these tests use <b>real directories and files named the way a tool's library folder
/// names them</b>. Asserting on a name passed in as a string is what
/// <see cref="ReferenceLibraryRulesTests"/> does, and it cannot see this: its helper fills a missing
/// name in from the path exactly as the caller did, so the defect was reproduced by the test
/// fixture rather than caught by it.</para>
/// </remarks>
public class ReferenceLibraryCandidateNameTests : IDisposable
{
    private readonly string _root;

    public ReferenceLibraryCandidateNameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-refname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A readable library as a directory of the given name, holding that package.mo.</summary>
    private string LibraryDirectory(string directoryName, string packageMo)
    {
        var path = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.mo"), packageMo);
        return path;
    }

    /// <summary>A readable library as a single file of the given name.</summary>
    private string LibraryFile(string fileName, string content)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static LoadedLibrary Checkout(string name) =>
        new() { Name = name, SourcePath = Path.Combine(TestPaths.Rooted("checkout"), name) };

    // ---- the name comes from the library, not the path -----------------------------------------

    [Fact]
    public void AVersionedDirectoryGivesThePackageItHolds()
    {
        var path = LibraryDirectory("Modelica 4.1.0", """
            within ;
            package Modelica "Modelica Standard Library"

              package Blocks "Library of basic input/output control blocks"
              end Blocks;

              annotation (version = "4.1.0");
            end Modelica;
            """);

        Assert.Equal("Modelica", ReferenceLibraryRules.CandidateName(path, isEncrypted: false));
    }

    [Fact]
    public void ASingleFileLibraryGivesTheClassInIt()
    {
        // Not "Complex.mo". The extension alone was enough to miss the match.
        var path = LibraryFile("Complex.mo", """
            within ;
            operator record Complex "Complex number"
              replaceable Real re "Real part";
              replaceable Real im "Imaginary part";
            end Complex;
            """);

        Assert.Equal("Complex", ReferenceLibraryRules.CandidateName(path, isEncrypted: false));
    }

    [Fact]
    public void AnEncryptedLibraryStillGivesTheNameItsFolderStates()
    {
        // The encrypted half was already right, and goes through the same call now: the detector
        // reads the versioned directory name, so the version does not become part of the name here
        // either.
        var path = Path.Combine(_root, "VendorLib 2.9.0");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.moe"), "encrypted");

        Assert.Equal("VendorLib", ReferenceLibraryRules.CandidateName(path, isEncrypted: true));
    }

    [Fact]
    public void AReadableLibraryWhoseNameCannotBeReadFallsBackToItsPath()
    {
        // All there was before, kept: a name from the path is better than no name, which matches
        // nothing and loads a second copy.
        var path = LibraryDirectory("Unreadable", "// no class in here at all");

        Assert.Equal("Unreadable", ReferenceLibraryRules.CandidateName(path, isEncrypted: false));
    }

    // ---- and that is what stops the second copy loading ----------------------------------------

    [Fact]
    public void AVersionedDirectoryIsNotLoadedBesideTheUsersCheckout()
    {
        // The whole defect, end to end. Six libraries reached this point and were loaded a second
        // time, and the browser then attributed the user's own class to the reference copy.
        var path = LibraryDirectory("Modelica 4.1.0", """
            within ;
            package Modelica "Modelica Standard Library"
            end Modelica;
            """);

        var reason = ReferenceLibraryRules.ReasonToSkip(
            path,
            isEncrypted: false,
            ReferenceLibraryRules.CandidateName(path, isEncrypted: false),
            [Checkout("Modelica")],
            useEncryptedDocumentation: true);

        Assert.Contains("already loaded from", reason);
    }

    [Fact]
    public void ASingleFileLibraryIsNotLoadedBesideTheUsersCheckout()
    {
        var path = LibraryFile("ObsoleteModelica4.mo", """
            within ;
            package ObsoleteModelica4 "Obsolete classes"
            end ObsoleteModelica4;
            """);

        var reason = ReferenceLibraryRules.ReasonToSkip(
            path,
            isEncrypted: false,
            ReferenceLibraryRules.CandidateName(path, isEncrypted: false),
            [Checkout("ObsoleteModelica4")],
            useEncryptedDocumentation: true);

        Assert.Contains("already loaded from", reason);
    }

    [Fact]
    public void ALibraryTheProjectDoesNotHaveIsStillLoaded()
    {
        // The control. ModelicaTest is not in the tool's library folder and ModelicaReference was
        // not in this project; neither must be skipped because something else was.
        var path = LibraryDirectory("VehicleInterfaces 2.0.2", """
            within ;
            package VehicleInterfaces "Interface definitions"
            end VehicleInterfaces;
            """);

        Assert.Null(ReferenceLibraryRules.ReasonToSkip(
            path,
            isEncrypted: false,
            ReferenceLibraryRules.CandidateName(path, isEncrypted: false),
            [Checkout("Modelica"), Checkout("ModelicaTest")],
            useEncryptedDocumentation: true));
    }
}
