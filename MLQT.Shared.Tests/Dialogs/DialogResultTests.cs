using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Dialogs;
using Moq;
using MudBlazor;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// Layer 1b for the dialogs: opened, driven, and asked what they closed with.
/// </summary>
/// <remarks>
/// <para>The rest of a dialog's logic is tested as plain functions. This is the part that cannot be:
/// <c>MudDialog.Close(DialogResult.Ok(x))</c> reaches the cascaded dialog instance, which only exists
/// inside a rendered provider, so what a dialog hands back to its caller is invisible without one.
/// The caller acts on that value — <c>LibraryBrowser</c> re-reads the repository, fires
/// <c>VcsFilesChanged</c> and starts the formatting pipeline on the strength of it — so a dialog that
/// closes with <c>Cancel</c> where it meant <c>Ok</c>, or with the wrong payload, silently stops the
/// work that was supposed to follow.</para>
///
/// <para>One test per dialog per outcome, and no more: these are the slowest tests in the suite and
/// everything else about these dialogs is reachable without a renderer.</para>
/// </remarks>
public class DialogResultTests : MlqtComponentTestBase
{
    private readonly Mock<IRepositoryService> _repositories = new(MockBehavior.Loose);

    /// <summary>A folder that really is on disk — the local tab's validation asks the filesystem.</summary>
    private string ExistingFolder { get; } =
        Path.Combine(Path.GetTempPath(), "mlqt-dialog-" + Guid.NewGuid().ToString("N"));

    public DialogResultTests()
    {
        Directory.CreateDirectory(ExistingFolder);

        _repositories.Setup(r => r.GetRepository(It.IsAny<string>()))
                     .Returns(new Repository { Id = "repo", Name = "Repo", LocalPath = "C:\\repo", CurrentBranch = "main" });
        _repositories.Setup(r => r.GetBranches(It.IsAny<string>(), It.IsAny<bool>()))
                     .Returns([new VcsBranchInfo { Name = "main", IsCurrent = true }]);

        Services.AddSingleton(_repositories.Object);
        Services.AddSingleton(new Mock<IFilePickerService>().Object);
        Services.AddSingleton(new Mock<IStyleCheckingService>().Object);
    }

    private static DialogParameters ForRepository() => new() { { "RepositoryId", "repo" } };

    // ---- CreateBranchDialog ----------------------------------------------------------------

    [Fact]
    public async Task CreateBranch_ClosesWithTheBranchName()
    {
        // The payload matters, not just the success: LibraryBrowser switches the UI to the branch
        // this returns. Closing with Ok(true) would look like success and switch to nothing.
        _repositories.Setup(r => r.CreateBranchAsync("repo", "feature/x", It.IsAny<bool>()))
                     .ReturnsAsync(new VcsOperationResult { Success = true });

        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(ForRepository());

        await TypeBranchName(provider, "feature/x");
        await ClickButton(provider, "Create");

        var result = await dialog.Result;

        Assert.False(result!.Canceled);
        Assert.Equal("feature/x", result.Data);
    }

    [Fact]
    public async Task CreateBranch_WhenTheVcsRefuses_StaysOpen()
    {
        // A failed create must not close: the dialog is where the error message is shown, and
        // closing would report success to a caller that then re-reads a branch that was never made.
        _repositories.Setup(r => r.CreateBranchAsync("repo", "feature/x", It.IsAny<bool>()))
                     .ReturnsAsync(new VcsOperationResult { Success = false, ErrorMessage = "already exists" });

        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(ForRepository());

        await TypeBranchName(provider, "feature/x");
        await ClickButton(provider, "Create");

        Assert.False(dialog.Result.IsCompleted);
        Assert.Contains("already exists", provider.Markup);
    }

    [Fact]
    public async Task CreateBranch_Cancelled_ReportsCancelled()
    {
        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(ForRepository());

        await ClickButton(provider, "Cancel");

        var result = await dialog.Result;

        Assert.True(result!.Canceled);
        _repositories.Verify(
            r => r.CreateBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateBranch_WithAnInvalidName_DoesNotReachTheVcs()
    {
        // The validation tested directly in CreateBranchDialogTests, here proved to be wired to the
        // button. A rule that is right and not connected is the failure this layer exists to catch.
        var (provider, dialog) = await ShowDialogAsync<CreateBranchDialog>(ForRepository());

        await TypeBranchName(provider, "bad name");
        await ClickButton(provider, "Create");

        Assert.False(dialog.Result.IsCompleted);
        _repositories.Verify(
            r => r.CreateBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
    }

    // ---- AddRepositoryDialog ---------------------------------------------------------------

    [Fact]
    public async Task AddRepository_ClosesWithTheNewRepositoryId()
    {
        // MainLayout uses the returned id to find the new libraries and start dependency analysis on
        // exactly their models. A different payload means either no analysis or analysis of the wrong
        // set.
        var added = new Repository { Id = "new-repo", Name = "New", LocalPath = ExistingFolder };
        _repositories.Setup(r => r.AddRepositoryAsync(
                          ExistingFolder, It.IsAny<string?>(), It.IsAny<string?>(),
                          It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
                     .ReturnsAsync(new AddRepositoryResult { Success = true, Repository = added });
        _repositories.Setup(r => r.GetRepository("new-repo")).Returns(added);

        var (provider, dialog) = await ShowDialogAsync<AddRepositoryDialog>();

        await TypeInto(provider, 0, ExistingFolder);
        await ClickButton(provider, "Add Repository");

        var result = await dialog.Result;

        Assert.False(result!.Canceled);
        Assert.Equal("new-repo", result.Data);
    }

    [Fact]
    public async Task AddRepository_WhenTheAddFails_StaysOpenAndSaysWhy()
    {
        // B110. This used to fall through and close with DialogResult.Ok(null): the error was
        // computed and thrown away with the dialog that would have shown it, and MainLayout - seeing
        // a result that was not cancelled - switched the UI into repository mode for a repository
        // that did not exist. The user saw the dialog vanish and nothing else happen.
        _repositories.Setup(r => r.AddRepositoryAsync(
                          It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                          It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
                     .ReturnsAsync(new AddRepositoryResult { Success = false, ErrorMessage = "not a repository" });

        var (provider, dialog) = await ShowDialogAsync<AddRepositoryDialog>();

        await TypeInto(provider, 0, ExistingFolder);
        await ClickButton(provider, "Add Repository");

        Assert.False(dialog.Result.IsCompleted);
        Assert.Contains("not a repository", provider.Markup);
    }

    [Fact]
    public async Task AddRepository_WithAFolderThatIsNotThere_StaysUsable()
    {
        // B109. The validation returns early, and the only place the loading flag was cleared was
        // the finally of a try that started after those returns - so the dialog spun for the rest of
        // the session with its own error message behind the overlay. The assertion that matters is
        // the last one: the button is live again, so the user can correct the path and retry.
        var missing = Path.Combine(ExistingFolder, "no-such-folder");

        var (provider, dialog) = await ShowDialogAsync<AddRepositoryDialog>();

        await TypeInto(provider, 0, missing);
        await ClickButton(provider, "Add Repository");

        Assert.False(dialog.Result.IsCompleted);
        Assert.Contains("does not exist", provider.Markup);

        _repositories.Verify(r => r.AddRepositoryAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool?>()), Times.Never);

        Assert.DoesNotContain("Add Repository", DisabledButtonLabels(provider));
    }

    [Fact]
    public async Task AddRepository_CancelledAfterAPartialAdd_RemovesTheEmptyRepository()
    {
        // The repository is created before its libraries load, so a failure in between leaves one
        // registered with nothing in it. Cancelling has to clean that up or the user is left with a
        // phantom repository they did not add and cannot explain.
        var added = new Repository { Id = "half-added", Name = "Half", LocalPath = ExistingFolder };
        _repositories.Setup(r => r.AddRepositoryAsync(
                          It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                          It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
                     .ReturnsAsync(new AddRepositoryResult { Success = true, Repository = added });
        _repositories.Setup(r => r.GetRepository("half-added")).Returns(added);
        _repositories.Setup(r => r.LoadLibrariesAsync("half-added", It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new IOException("disk went away"));

        var (provider, dialog) = await ShowDialogAsync<AddRepositoryDialog>();

        await TypeInto(provider, 0, ExistingFolder);
        await ClickButton(provider, "Add Repository");

        Assert.False(dialog.Result.IsCompleted);   // the throw is caught, the dialog stays up

        await ClickButton(provider, "Cancel");

        Assert.True((await dialog.Result)!.Canceled);
        _repositories.Verify(r => r.RemoveRepository("half-added", false), Times.Once);
    }

    [Fact]
    public async Task AddRepository_CancelledBeforeAdding_RemovesNothing()
    {
        var (provider, dialog) = await ShowDialogAsync<AddRepositoryDialog>();

        await ClickButton(provider, "Cancel");

        Assert.True((await dialog.Result)!.Canceled);
        _repositories.Verify(r => r.RemoveRepository(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    // ---- helpers ---------------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(ExistingFolder))
            Directory.Delete(ExistingFolder, recursive: true);

        base.Dispose(disposing);
    }

    private static async Task TypeBranchName(IRenderedComponent<MudDialogProvider> provider, string name)
    {
        // Input, not Change. The field is Immediate, so MudBlazor wires oninput and never onchange -
        // and bUnit throws MissingEventHandlerException rather than silently doing nothing, which is
        // the one thing that made this quick to find.
        var input = provider.Find("input");
        await provider.InvokeAsync(() => input.Input(name));
    }

    private static async Task TypeInto(IRenderedComponent<MudDialogProvider> provider, int index, string text)
    {
        var input = provider.FindAll("input")[index];
        await provider.InvokeAsync(() => input.Input(text));
    }

    private static IEnumerable<string> DisabledButtonLabels(IRenderedComponent<MudDialogProvider> provider) =>
        provider.FindAll("button")
                .Where(b => b.HasAttribute("disabled"))
                .Select(b => b.TextContent.Trim());

    private static async Task ClickButton(IRenderedComponent<MudDialogProvider> provider, string label)
    {
        var button = provider.FindAll("button")
                             .FirstOrDefault(b => b.TextContent.Contains(label, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException(
                         $"no button labelled '{label}' in:\n{provider.Markup}");

        await provider.InvokeAsync(() => button.Click());
    }
}
