using Moq;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Tests for <see cref="DictionaryScope"/> — the one answer to "whose word list applies to this
/// class?".
///
/// <para>Accepted spellings live in the repository that owns the code, so every surface that reads or
/// writes one has to agree on which repository that is. If the desktop app wrote a word into the
/// repository the user was browsing while a check read it from the repository the class came from,
/// the word would appear to have no effect — the same invisible disagreement that keeping the list on
/// one machine used to cause.</para>
/// </summary>
public class DictionaryScopeTests
{
    private static LoadedLibrary Library(
        string name, string? repositoryId, LibrarySourceType sourceType = LibrarySourceType.Directory,
        params string[] modelIds) =>
        new()
        {
            Name = name,
            RepositoryId = repositoryId,
            SourceType = sourceType,
            ModelIds = [.. modelIds],
        };

    private static (ILibraryDataService libraries, IRepositoryService repositories) Fake(
        IEnumerable<LoadedLibrary> libraries, params Repository[] repositories)
    {
        var libraryService = new Mock<ILibraryDataService>();
        libraryService.SetupGet(l => l.Libraries).Returns(libraries.ToList());

        var repositoryService = new Mock<IRepositoryService>();
        foreach (var repository in repositories)
            repositoryService.Setup(r => r.GetRepository(repository.Id)).Returns(repository);

        return (libraryService.Object, repositoryService.Object);
    }

    [Fact]
    public void AClassIsScopedToTheRepositoryItsLibraryCameFrom()
    {
        var alpha = new Repository { Name = "Alpha", LocalPath = @"C:\repos\Alpha" };
        var beta = new Repository { Name = "Beta", LocalPath = @"C:\repos\Beta" };
        var (libraries, repositories) = Fake(
            [Library("A", alpha.Id, modelIds: "A.Model"), Library("B", beta.Id, modelIds: "B.Model")],
            alpha, beta);

        Assert.Equal(alpha.LocalPath, DictionaryScope.RootForModel(libraries, repositories, "A.Model"));
        Assert.Equal(beta.LocalPath, DictionaryScope.RootForModel(libraries, repositories, "B.Model"));
    }

    [Fact]
    public void LibrariesSharingACheckoutShareOneWordList()
    {
        // The list is committed with the working copy, so it covers everything in that working copy —
        // a repository holding several libraries has one list, not one per library.
        var repository = new Repository { Name = "R", LocalPath = @"C:\repos\R" };
        var (libraries, repositories) = Fake(
            [Library("A", repository.Id, modelIds: "A.Model"),
             Library("B", repository.Id, modelIds: "B.Model")],
            repository);

        Assert.Equal(repository.LocalPath, DictionaryScope.RootForModel(libraries, repositories, "A.Model"));
        Assert.Equal(repository.LocalPath, DictionaryScope.RootForModel(libraries, repositories, "B.Model"));
    }

    [Fact]
    public void ALibraryLoadedOutsideARepository_HasNoWordList()
    {
        // There is nowhere to put a word that a check would ever read back, so the answer is "none"
        // rather than borrowing whichever repository happens to be open.
        var (libraries, repositories) = Fake([Library("Loose", repositoryId: null, modelIds: "Loose.Model")]);

        Assert.Null(DictionaryScope.RootForModel(libraries, repositories, "Loose.Model"));
    }

    [Fact]
    public void AnEncryptedLibrary_HasNoWordList()
    {
        // Its classes are reconstructed from the vendor's documentation and never spell-checked.
        var repository = new Repository { Name = "R", LocalPath = @"C:\repos\R" };
        var (libraries, repositories) = Fake(
            [Library("Vendor", repository.Id, LibrarySourceType.EncryptedDirectory, "Vendor.Model")],
            repository);

        Assert.Null(DictionaryScope.RootForModel(libraries, repositories, "Vendor.Model"));
    }

    [Fact]
    public void ARepositoryWithNoWorkingCopy_HasNoWordList()
    {
        var repository = new Repository { Name = "R", LocalPath = "" };
        var (libraries, repositories) = Fake([Library("A", repository.Id, modelIds: "A.Model")], repository);

        Assert.Null(DictionaryScope.RootForModel(libraries, repositories, "A.Model"));
    }

    [Fact]
    public void AClassBelongingToNoLoadedLibrary_HasNoWordList()
    {
        var (libraries, repositories) = Fake([]);

        Assert.Null(DictionaryScope.RootForModel(libraries, repositories, "Nowhere.Model"));
    }

    [Fact]
    public void RootForLibrary_AgreesWithRootForModel()
    {
        // The settings page scopes by library and the review page by class; both have to land on the
        // same file or a word added from one would be invisible to the other.
        var repository = new Repository { Name = "R", LocalPath = @"C:\repos\R" };
        var library = Library("A", repository.Id, modelIds: "A.Model");
        var (libraries, repositories) = Fake([library], repository);

        Assert.Equal(
            DictionaryScope.RootForModel(libraries, repositories, "A.Model"),
            DictionaryScope.RootForLibrary(repositories, library));
    }
}
