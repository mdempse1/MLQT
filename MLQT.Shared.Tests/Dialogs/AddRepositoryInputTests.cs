using MLQT.Shared.Dialogs;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// What the Add Repository dialog refuses, and what it says.
/// </summary>
/// <remarks>
/// Real directories rather than an abstraction over the filesystem: the rule is "does this folder
/// exist on this machine", and a fake that answers yes proves nothing about the question actually
/// being asked.
/// </remarks>
public class AddRepositoryInputTests : IDisposable
{
    private readonly string _existingDirectory =
        Path.Combine(Path.GetTempPath(), "mlqt-addrepo-" + Guid.NewGuid().ToString("N"));

    public AddRepositoryInputTests() => Directory.CreateDirectory(_existingDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_existingDirectory))
            Directory.Delete(_existingDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ---- the local-folder tab --------------------------------------------------------------

    [Fact]
    public void AnExistingFolder_IsAccepted()
    {
        Assert.Null(AddRepositoryInput.Validate(true, _existingDirectory, "", ""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFolderChosen_IsRefused(string? path)
    {
        var error = AddRepositoryInput.Validate(true, path, "", "");

        Assert.Equal("You need to select a directory before you can add a repository", error);
    }

    [Fact]
    public void AFolderThatIsNotThere_IsRefused_AndTheMessageNamesIt()
    {
        // The message quotes the path back. A user who has pasted a UNC path or a drive letter that
        // is not mapped needs to see which one the application looked for.
        var missing = Path.Combine(_existingDirectory, "no-such-folder");

        var error = AddRepositoryInput.Validate(true, missing, "", "");

        Assert.NotNull(error);
        Assert.Contains(missing, error);
    }

    [Fact]
    public void TheLocalTab_IgnoresTheUrlFields()
    {
        // The two tabs share one method and one set of backing fields. Validating the checkout URL
        // while the user is on the folder tab would refuse a perfectly good folder over an empty box
        // on a tab they never opened.
        Assert.Null(AddRepositoryInput.Validate(true, _existingDirectory, url: null, checkoutPath: null));
    }

    // ---- the checkout tab ------------------------------------------------------------------

    [Fact]
    public void AUrlAndACheckoutFolder_AreAccepted()
    {
        Assert.Null(AddRepositoryInput.Validate(false, "", "https://example.com/svn/trunk", _existingDirectory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoUrl_IsRefused(string? url)
    {
        var error = AddRepositoryInput.Validate(false, "", url, _existingDirectory);

        Assert.Equal("You need to specify a url to checkout the repository from", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoCheckoutFolder_IsRefused(string? checkoutPath)
    {
        var error = AddRepositoryInput.Validate(false, "", "https://example.com/svn/trunk", checkoutPath);

        Assert.Equal("You need to specify a directory that the repository can be checked out to", error);
    }

    [Fact]
    public void TheUrlIsReportedBeforeTheCheckoutFolder()
    {
        // Both are empty on a freshly opened tab. Naming the first field the user would fill in is
        // the difference between a message that guides and one that reads as arbitrary.
        var error = AddRepositoryInput.Validate(false, "", "", "");

        Assert.Equal("You need to specify a url to checkout the repository from", error);
    }

    [Fact]
    public void ACheckoutFolderThatDoesNotExistYet_IsAccepted()
    {
        // Unlike the local tab. Checking out creates the folder, so demanding it already exists would
        // force the user to make an empty directory first for no reason.
        var notYetThere = Path.Combine(_existingDirectory, "will-be-created");

        Assert.Null(AddRepositoryInput.Validate(false, "", "https://example.com/svn/trunk", notYetThere));
    }

    [Fact]
    public void TheCheckoutTab_IgnoresTheFolderField()
    {
        Assert.Null(AddRepositoryInput.Validate(false, path: null, "https://example.com/svn/trunk", _existingDirectory));
    }

    // ---- the two tabs really do differ -----------------------------------------------------

    [Fact]
    public void TheSameEmptyInput_IsRefusedDifferentlyPerTab()
    {
        // The guard against the tab flag being ignored: if it were, one of these would be wrong and
        // every other test here would still pass, because each one only ever uses one tab.
        Assert.Equal(
            "You need to select a directory before you can add a repository",
            AddRepositoryInput.Validate(true, "", "", ""));

        Assert.Equal(
            "You need to specify a url to checkout the repository from",
            AddRepositoryInput.Validate(false, "", "", ""));
    }
}
