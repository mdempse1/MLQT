using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The shell with a repository open in it, and the repository's own controls.
/// </summary>
/// <remarks>
/// For the journeys that act on a repository rather than a bare library - version control, Format
/// All Files. Each of them adds the fixture through <see cref="IRepositoryService"/>, since the Add
/// Repository dialog opens a native folder picker, which is the one thing this host fakes; and each
/// of them takes it out again through <see cref="TestHostFixture.ResetRepositoriesAsync"/>.
/// </remarks>
internal static class RepositoryShell
{
    /// <summary>Adds <paramref name="library"/>'s working copy as a repository and loads its libraries.</summary>
    internal static async Task<string> AddAsync(TestHostFixture host, LibraryFixture library, string name)
    {
        var repositories = host.Services.GetRequiredService<IRepositoryService>();
        var added = await repositories.AddRepositoryAsync(library.RepositoryPath, name: name, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        await repositories.LoadLibrariesAsync(added.Repository!.Id);
        await host.WaitForIdleAsync();
        return added.Repository.Id;
    }

    /// <summary>Opens the shell and waits until nothing is in front of it.</summary>
    internal static async Task<IPage> OpenAsync(TestHostFixture host)
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        return page;
    }

    /// <summary>
    /// A button found by the icon it draws.
    /// </summary>
    /// <remarks>
    /// For the buttons that carry a tooltip and no accessible name - MudTooltip text is not in the
    /// markup, so the icon's own path is the one thing that names them (see skill-gui-testing.md).
    /// </remarks>
    internal static ILocator ButtonWithIcon(IPage page, string icon) => ButtonWithIcon(page, page.Locator("body"), icon);

    /// <summary>A button inside <paramref name="scope"/>, found by the icon it draws.</summary>
    internal static ILocator ButtonWithIcon(IPage page, ILocator scope, string icon)
    {
        var path = Regex.Matches(icon, "d=\"([^\"]+)\"")
                        .Select(m => m.Groups[1].Value)
                        .First(d => d != "M0 0h24v24H0z" && d != "M0 0h24v24H0V0z");
        return scope.Locator("button").Filter(new LocatorFilterOptions { Has = page.Locator($"path[d='{path}']") });
    }

    /// <summary>
    /// Settings, Manage Repositories, the repository's row, and Format All Files in the dialog it opens.
    /// </summary>
    internal static async Task FormatAllFilesAsync(IPage page, string repositoryName)
    {
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(4).ClickAsync();

        var manage = page.GetByRole(AriaRole.Tab, new() { Name = "Manage Repositories" });
        await manage.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        await manage.ClickAsync();

        await page.Locator("tr").Filter(new LocatorFilterOptions { HasTextString = repositoryName }).Last.ClickAsync();

        var dialog = page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { HasTextString = "Edit Repository Details" });
        var formatAll = dialog.GetByRole(AriaRole.Button, new() { Name = "Format All Files" });
        await Assertions.Expect(formatAll).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await formatAll.ClickAsync();
    }
}
