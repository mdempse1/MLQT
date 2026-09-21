using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// Which library owns a class when two of them claim it — the state a project is in whenever a
/// library is both checked out as source and shipped, encrypted, in a tool's library folder.
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> Reported against
/// <c>Suspensions.HalfCar.Steering.Experiments.RackAndPinionKinematics</c>: the library browser
/// marked it as a cosmetic change, and the Code Review page opened it with all three diff views
/// disabled. The browser goes from the changed <i>file</i> to the models in it; Code Review went
/// from the model to <c>Libraries.FirstOrDefault(l =&gt; l.ModelIds.Contains(id))</c>. Both the
/// user's checkout and Dymola's encrypted copy of the same library listed the id, the encrypted one
/// had been added first, and its repository is a read-only folder under <c>Program Files</c> — so
/// the class resolved to a repository with no version control and the diff was withheld.</para>
///
/// <para><b>Why only some classes.</b> The two loads run in parallel against one graph. The stub
/// builder skips a class the source has already put there, so which library ends up claiming which
/// class is a race — 737 stubbed and 650 superseded in the reported session. That is why it
/// affected a handful of classes rather than a library, and why it moved between runs.</para>
/// </remarks>
public class LibraryOwnershipTests
{
    private const string Shared = "Suspensions.HalfCar.Steering.Experiments.RackAndPinionKinematics";

    private readonly List<LoadedLibrary> _libraries = [];
    private ModelNode? _node;

    /// <summary>
    /// The graph after both loads: one node for the class, and <c>AddNode</c> has already decided
    /// that readable source beats a class reconstructed from documentation.
    /// </summary>
    private void ArrangeGraph(bool sourceWon) =>
        _node = new ModelNode(Shared, "RackAndPinionKinematics", "model RackAndPinionKinematics end RackAndPinionKinematics;")
        {
            IsExternalStub = !sourceWon,
        };

    private LoadedLibrary Add(string name, string repositoryId, LibrarySourceType sourceType, params string[] modelIds)
    {
        var library = new LoadedLibrary
        {
            Name = name,
            RepositoryId = repositoryId,
            SourceType = sourceType,
            ModelIds = new HashSet<string>(modelIds, StringComparer.Ordinal),
        };

        _libraries.Add(library);
        return library;
    }

    private LoadedLibrary? Owner(string modelId) =>
        LibraryOwnership.Owner(_libraries, modelId, id => id == _node?.Id ? _node : null);

    /// <summary>
    /// The reported case: the vendor's encrypted copy was added first and claims the class, but the
    /// source is what is in the graph, so the source library owns it.
    /// </summary>
    [Fact]
    public void SourceOwnsAClassTheEncryptedCopyAlsoClaims()
    {
        ArrangeGraph(sourceWon: true);
        Add("Suspensions", "dymola-repo", LibrarySourceType.EncryptedDirectory, Shared);
        var source = Add("Suspensions", "working-copy", LibrarySourceType.Directory, Shared);

        Assert.Same(source, Owner(Shared));
    }

    /// <summary>The order the two were loaded in must not decide it.</summary>
    [Fact]
    public void TheAnswerDoesNotDependOnWhichLoadFinishedFirst()
    {
        ArrangeGraph(sourceWon: true);
        var source = Add("Suspensions", "working-copy", LibrarySourceType.Directory, Shared);
        Add("Suspensions", "dymola-repo", LibrarySourceType.EncryptedDirectory, Shared);

        Assert.Same(source, Owner(Shared));
    }

    /// <summary>
    /// The other way round, which is the ordinary case for a class the user has no source for: the
    /// stub is what is in the graph, so the encrypted library owns it.
    /// </summary>
    [Fact]
    public void TheEncryptedLibraryOwnsAClassThatIsOnlyAStub()
    {
        ArrangeGraph(sourceWon: false);
        var encrypted = Add("Suspensions", "dymola-repo", LibrarySourceType.EncryptedDirectory, Shared);
        Add("Suspensions", "working-copy", LibrarySourceType.Directory, Shared);

        Assert.Same(encrypted, Owner(Shared));
    }

    [Fact]
    public void OneClaimantIsTheAnswerWithoutConsultingTheGraph()
    {
        var only = Add("Suspensions", "working-copy", LibrarySourceType.Directory, Shared);

        Assert.Same(only, Owner(Shared));
    }

    [Fact]
    public void AClassNoLibraryClaimsIsOwnedByNone()
    {
        Add("Suspensions", "working-copy", LibrarySourceType.Directory, Shared);

        Assert.Null(Owner("Somewhere.Else"));
    }

    /// <summary>
    /// A class claimed twice but missing from the graph still resolves to something, rather than
    /// falling through to null and taking the class out of its repository altogether.
    /// </summary>
    [Fact]
    public void AClaimedClassWithNoNodeStillResolves()
    {
        Add("Suspensions", "dymola-repo", LibrarySourceType.EncryptedDirectory, Shared);
        var source = Add("Suspensions", "working-copy", LibrarySourceType.Directory, Shared);

        // No node, so "not a stub" is the reading, and the source library is the one that supplies
        // those. Either answer beats none.
        Assert.Same(source, Owner(Shared));
    }
}
