using MLQT.Services;
using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Which library a top-level class in the tree is attributed to, when two of them claim it.
/// </summary>
/// <remarks>
/// <para><b>It has to be the answer <see cref="LibraryOwnership.Owner"/> gives</b>, because a
/// repository's browser filters the tree by exactly that attribution — the library id is stamped
/// onto the shared node as it is prepared for display, and a node stamped with a library the
/// repository does not list is simply not drawn.</para>
///
/// <para><b>It was not, 2026-11-04.</b> <c>GetTopLevelModelsAsync</c> had a tie-break of its own,
/// "the last claimant of the node's kind wins", against <c>Owner</c>'s first. Two readable copies of
/// one library are the same kind, so every claimant won in turn and the last one took it — and a
/// readable reference library at a tool's library folder loads after the user's repositories. Six of
/// the eight libraries in a project at <c>C:\Projects\Modelica\MSL</c> disappeared from its browser
/// while the log reported all eight loaded and no errors. The <i>children</i> of those same packages
/// were attributed through <c>Owner</c> and had been the user's all along, so the two halves of one
/// tree disagreed about who owned a package.</para>
///
/// <para>The reference copy of each is no longer loaded at all (see
/// <see cref="ReferenceLibraryCandidateNameTests"/>) — but two readable copies of one library are
/// still ordinary, from two checkouts of it in different repositories, so this is the half that has
/// to hold whatever is loaded.</para>
/// </remarks>
public class TopLevelClassOwnershipTests
{
    private const string LibrarySource = """
        within ;
        package Lib "a library two places have a copy of"

          model M "a class in it"
            Real x;
          end M;

        end Lib;
        """;

    [Fact]
    public async Task TheFirstReadableClaimantOwnsTheClassInTheTree()
    {
        var service = new LibraryDataService();

        var checkout = await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("checkout"), "Lib.mo"), LibrarySource);
        var reference = await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("tool", "Library"), "Lib.mo"), LibrarySource);
        reference.IsReferenceOnly = true;

        // Both are readable source and neither outranks the other, so both really are loaded and
        // both really claim the class. If either ever stops being true this test is no longer about
        // anything, and says so here rather than passing on a single claimant — which is how it was
        // first written, and it passed against the defect.
        Assert.Equal(2, service.Libraries.Count);
        Assert.Contains("Lib", checkout.TopLevelModelIds);
        Assert.Contains("Lib", reference.TopLevelModelIds);

        var top = await service.GetTopLevelModelsAsync();
        var lib = Assert.Single(top, m => m.Id == "Lib");

        Assert.Equal(checkout.Id, lib.LibraryId);
    }

    [Fact]
    public async Task TheTreeAgreesWithGetOwningLibrary()
    {
        // The promise in MLQT.Services/CLAUDE.md, asserted rather than described: GetOwningLibrary
        // is the one way to ask which library a class belongs to. A second answer somewhere else is
        // the defect, whichever of the two is right.
        var service = new LibraryDataService();

        await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("checkout"), "Lib.mo"), LibrarySource);
        await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("tool", "Library"), "Lib.mo"), LibrarySource);

        var top = await service.GetTopLevelModelsAsync();
        var lib = Assert.Single(top, m => m.Id == "Lib");

        Assert.Equal(service.GetOwningLibrary("Lib")?.Id, lib.LibraryId);
    }

    [Fact]
    public async Task APackageAndItsChildrenAreOwnedByTheSameLibrary()
    {
        // The two halves of one tree. The children were already right; the package was not, so
        // expanding it moved ownership from one library to another halfway down.
        var service = new LibraryDataService();

        await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("checkout"), "Lib.mo"), LibrarySource);
        await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("tool", "Library"), "Lib.mo"), LibrarySource);

        var top = await service.GetTopLevelModelsAsync();
        var lib = Assert.Single(top, m => m.Id == "Lib");

        var children = await service.GetChildModelsAsync(lib);
        var child = Assert.Single(children, m => m.Id == "Lib.M");

        Assert.Equal(lib.LibraryId, child.LibraryId);
    }

    [Fact]
    public async Task OneClaimantIsStillTheOwner()
    {
        // The overwhelmingly common case, which the tie-break must not disturb.
        var service = new LibraryDataService();

        var only = await service.AddLibraryFromFileAsync(
            Path.Combine(TestPaths.Rooted("checkout"), "Lib.mo"), LibrarySource);

        var top = await service.GetTopLevelModelsAsync();
        var lib = Assert.Single(top, m => m.Id == "Lib");

        Assert.Equal(only.Id, lib.LibraryId);
    }
}
