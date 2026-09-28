using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// B414 - Format All Files leaves a file with syntax errors exactly as it is, and says so.
/// </summary>
/// <remarks>
/// <para>The row's hand check, performed: Format All over a library with one broken file - the
/// warning names it, it stays until dismissed, and the file is unchanged on disk. The skipping and
/// the wording are unit-tested; that the host shows the list, from the button a user presses, was
/// not.</para>
///
/// <para>The broken file is laid out badly as well as broken, and another file in the library is
/// laid out badly and not broken: the first is what a formatter that ignored the error would
/// rewrite, and the second is the evidence that the formatter ran at all, without which "the file
/// is unchanged" would hold for a Format All that did nothing.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class FormatAllSkipsBrokenFilesJourney(TestHostFixture host) : IAsyncLifetime
{
    private const string RepositoryName = "Broken files";

    private readonly LibraryFixture _library = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await host.ResetRepositoriesAsync();
        _library.Dispose();
    }

    [Fact]
    public async Task AFileWithSyntaxErrors_IsLeftAsItIs_AndNamedInAWarningThatStays()
    {
        await host.ResetRepositoriesAsync();
        _library.EnableFormatting();
        var broken = _library.AddAClassWithASyntaxError();
        await RepositoryShell.AddAsync(host, _library, RepositoryName);
        var page = await RepositoryShell.OpenAsync(host);

        var brokenBefore = File.ReadAllBytes(broken);
        var goodBefore = File.ReadAllText(_library.ModifiedFile);

        await RepositoryShell.FormatAllFilesAsync(page, RepositoryName);
        await Assertions.Expect(page.GetByText("Formatting all files").First).ToBeHiddenAsync(new() { Timeout = 60_000 });

        var warning = page.Locator(".mud-snackbar").Filter(new LocatorFilterOptions { HasTextString = "syntax errors" });
        await Assertions.Expect(warning).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(warning).ToContainTextAsync("Broken.mo");

        // Formatting ran - the badly laid-out file that parses was rewritten - and left the broken
        // one byte for byte as it was.
        Assert.NotEqual(goodBefore, File.ReadAllText(_library.ModifiedFile));
        Assert.Equal(brokenBefore, File.ReadAllBytes(broken));

        // Still there well after an ordinary notification would have gone: the user is told which
        // files to fix, so it waits for them.
        await page.WaitForTimeoutAsync(8_000);
        await Assertions.Expect(warning).ToBeVisibleAsync();

        await host.WaitForIdleAsync();
    }
}
