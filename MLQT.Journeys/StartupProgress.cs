using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Journeys;

/// <summary>
/// The two dialogs that say a project is loading, told apart, and a project for startup to load.
/// </summary>
/// <remarks>
/// <para>Both are titled "Loading project repositories, please wait". The <b>six-step</b> dialog is
/// the one a layout shows for a run it is doing itself, and lists the steps. The <b>earlier-run</b>
/// dialog is the one a reloaded layout shows for a run an earlier layout began and is still doing
/// (B407, B423): it has only the step that run is on, since the step is all it can see of it.</para>
/// </remarks>
internal static class StartupProgress
{
    private const string Title = "Loading project repositories";

    private static ILocator Dialogs(IPage page) =>
        page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { HasTextString = Title });

    /// <summary>The dialog for a run this window is doing itself.</summary>
    internal static ILocator SixStep(IPage page) =>
        Dialogs(page).Filter(new LocatorFilterOptions { Has = page.Locator(".mud-list") });

    /// <summary>The dialog for a run an earlier window began.</summary>
    internal static ILocator EarlierRun(IPage page) =>
        Dialogs(page).Filter(new LocatorFilterOptions { HasNot = page.Locator(".mud-list") });

    /// <summary>
    /// Saves a project holding <paramref name="library"/>'s working copy as a repository, for the next
    /// startup or switch to load.
    /// </summary>
    /// <remarks>
    /// Written as settings rather than added through the service, because startup only runs for a
    /// window that finds no repository loaded - it is the saved project it opens.
    /// </remarks>
    internal static ProjectProfile ProjectFor(LibraryFixture library, string name) => new()
    {
        Name = name,
        Repositories =
        [
            new RepositorySettingsEntry
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                LocalPath = library.RepositoryPath,
                VcsRootPath = library.RepositoryPath,
                VcsType = "Git",
                AutoLoad = true,
            },
        ],
    };

    /// <summary>Saves <paramref name="projects"/>, the first of them active.</summary>
    internal static Task SaveProjectsAsync(TestHostFixture host, params ProjectProfile[] projects) =>
        host.Services.GetRequiredService<ISettingsService>().SetAsync("Repositories", new RepositorySettingsCollection
        {
            Projects = [.. projects],
            ActiveProjectId = projects[0].Id,
        });
}
