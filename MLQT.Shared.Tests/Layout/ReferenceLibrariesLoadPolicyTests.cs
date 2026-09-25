using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests.Layout;

/// <summary>
/// Every way MainLayout opens a project also loads the Reference Libraries setting (B280, B355).
/// </summary>
/// <remarks>
/// <para>The setting belongs to no project, so nothing that loads a project's repositories brings
/// it with it. Project switching did not load it until B280; the two startup paths for an empty or
/// new project returned before the main path reached it, so such a project resolved nothing against
/// its reference libraries all session (B355). Neither fix had a test.</para>
///
/// <para>Read as text because MainLayout's startup cannot be rendered without every service in the
/// application behind it. The rule: each <c>LoadRepositorySettingsAsync</c> call is followed by
/// <c>LoadReferenceLibrariesAsync</c> before its path returns or its method ends, and the project
/// switch handler loads them before its first return.</para>
/// </remarks>
public class ReferenceLibrariesLoadPolicyTests
{
    private static readonly Regex MethodStart =
        new(@"^    (private|internal|public|protected)\b", RegexOptions.Compiled);

    private static string[] MainLayoutLines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared", "Layout", "MainLayout.razor.cs");
            if (File.Exists(candidate))
                return File.ReadAllLines(candidate);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("MainLayout.razor.cs not found from " + AppContext.BaseDirectory);
    }

    /// <summary>Whether the reference libraries are loaded after line <paramref name="from"/> before
    /// the path returns or the method ends.</summary>
    private static bool LoadsReferencesBeforeLeaving(string[] lines, int from)
    {
        for (var i = from + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.Contains("LoadReferenceLibrariesAsync()", StringComparison.Ordinal))
                return true;
            if (line.Contains("return;", StringComparison.Ordinal) || MethodStart.IsMatch(line))
                return false;
        }
        return false;
    }

    [Fact]
    public void EveryProjectLoad_IsFollowedByTheReferenceLibraries()
    {
        var lines = MainLayoutLines();
        var loads = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("RepositoryService.LoadRepositorySettingsAsync(", StringComparison.Ordinal)
                        && !lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToList();

        // A guard over nothing proves nothing: startup does load projects in this file.
        Assert.NotEmpty(loads);

        var offenders = loads.Where(i => !LoadsReferencesBeforeLeaving(lines, i)).Select(i => $"line {i + 1}").ToList();
        Assert.True(offenders.Count == 0,
            "These load a project and leave without LoadReferenceLibrariesAsync, so the project resolves "
            + "nothing against the Reference Libraries setting until a restart (B355): " + string.Join(", ", offenders));
    }

    [Fact]
    public void AProjectSwitch_LoadsTheReferenceLibraries()
    {
        var lines = MainLayoutLines();
        var handler = Array.FindIndex(lines, l => l.Contains("private async void OnProjectChanged(string projectId)", StringComparison.Ordinal));
        Assert.True(handler >= 0, "OnProjectChanged not found - if it was renamed, point this guard at its successor");

        Assert.True(LoadsReferencesBeforeLeaving(lines, handler),
            "A project switch clears the graph, the reference libraries included, and has to load them again (B280)");
    }
}
