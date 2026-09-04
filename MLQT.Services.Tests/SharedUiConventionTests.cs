using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Conventions that hold over the <c>MLQT.Shared</c> source itself, checked by reading it.
///
/// <para><b>Why a test and not a review.</b> Two of these were written as throwaway scripts during a
/// review, found real defects (backlog B88 and B91), and then existed only in the review transcript —
/// so the next occurrence would wait for somebody to think of running them again. B91 in particular
/// had been read past by nine reviews: the unsubscribe method was there and looked right, and only
/// asking "who calls it" showed it was dead. That is precisely the kind of question a machine should
/// be asking on every build.</para>
///
/// <para><b>Why here.</b> <c>MLQT.Shared</c> has no test project of its own until phase 7a builds the
/// GUI harness (see <c>design-phase7-gui-tests.md</c>), and these need no rendering — only the source
/// text. They belong in <c>MLQT.Shared.Tests</c> the day it exists; until then they live in a suite
/// that runs, which is the same reasoning <c>RuleDocumentationTests</c> uses for reading
/// <c>Documentation/</c> from a CLI test.</para>
/// </summary>
public class SharedUiConventionTests
{
    /// <summary>
    /// The <c>MLQT.Shared</c> source, found by walking up from the test binary. Null when the tests
    /// run from somewhere the sources are not, which is not a failure — there is simply nothing to
    /// read.
    /// </summary>
    private static string? SharedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared");
            if (File.Exists(Path.Combine(candidate, "_Imports.razor")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static IEnumerable<(string Path, string Text)> Components()
    {
        var root = SharedDirectory();
        if (root is null)
            yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            yield return (Path.GetFileName(path), File.ReadAllText(path));
        }
    }

    /// <summary>An event subscription on something other than a local field.</summary>
    private static readonly Regex Subscribe = new(@"^\s*([\w\.]+\.\w+)\s*\+=\s*\w", RegexOptions.Multiline);

    private static bool IsServiceEvent(string target) =>
        target.Contains('.') &&
        (target.Contains("On") || target.Contains("Changed") || target.Contains("Found")
         || target.Contains("Progress") || target.Contains("Complete"));

    [Fact]
    public void TheSweepCanSeeTheSource()
    {
        // Guard against every test below passing on an empty enumeration, which is the only way a
        // check of this shape fails silently.
        //
        // Asserted rather than skipped, which is the opposite of what RuleDocumentationTests does with
        // the same problem — deliberately. That one reads Documentation/ to check a link, and a run
        // from somewhere without the docs is a run that simply cannot answer. These check for a defect
        // that nine reviews walked past precisely because nothing was asking, so a silent no-op here
        // would reintroduce the failure the file exists to prevent. The suites always run from the
        // tree: CI checks out and builds in place, and so does build/check-coverage.ps1.
        Assert.NotNull(SharedDirectory());
        Assert.True(Components().Count() > 20);
    }

    [Fact]
    public void EverySubscriptionInAComponentIsMatchedByAnUnsubscribe()
    {
        var unbalanced = new List<string>();

        foreach (var (name, text) in Components())
        {
            foreach (var target in Subscribe.Matches(text).Select(m => m.Groups[1].Value)
                                            .Where(IsServiceEvent).Distinct())
            {
                if (!Regex.IsMatch(text, Regex.Escape(target) + @"\s*-="))
                    unbalanced.Add($"{name}: {target}");
            }
        }

        Assert.True(unbalanced.Count == 0,
            "These components subscribe to a singleton's event and never unsubscribe, so the "
            + "singleton holds the component alive and calls it after it is gone: "
            + string.Join("; ", unbalanced));
    }

    [Fact]
    public void EveryComponentThatSubscribesIsDisposable()
    {
        // The reason the check above is not enough on its own: three components had the unsubscribe
        // method and none of them declared the interface, so Blazor never called it (B91).
        var notDisposable = new List<string>();

        foreach (var (name, text) in Components())
        {
            var subscribes = Subscribe.Matches(text).Select(m => m.Groups[1].Value).Any(IsServiceEvent);
            if (!subscribes)
                continue;

            var declares = text.Contains("@implements IDisposable")
                           || text.Contains("@implements IAsyncDisposable");
            if (!declares)
                notDisposable.Add(name);
        }

        Assert.True(notDisposable.Count == 0,
            "These components subscribe to a singleton's event but declare neither IDisposable nor "
            + "IAsyncDisposable, so nothing ever calls their cleanup: " + string.Join(", ", notDisposable));
    }

    [Fact]
    public void NoComponentHasACleanupMethodNothingCalls()
    {
        // The shape of B91 stated directly: a method named OnDispose, left over from before the
        // component declared the interface, that reads as the guard and is dead code.
        var orphaned = Components()
            .Where(c => c.Text.Contains("OnDispose()") && !Regex.IsMatch(c.Text, @"[^d]OnDispose\(\);"))
            .Select(c => c.Path)
            .ToList();

        Assert.True(orphaned.Count == 0,
            "These declare an OnDispose() that nothing invokes: " + string.Join(", ", orphaned));
    }

    [Fact]
    public void NoDocCommentIsStrandedAboveAnother()
    {
        // B88: a rewritten summary left above its replacement. Two of the seven found this way stated
        // the opposite of the summary beneath them, and one had been introduced two commits earlier
        // by another fix.
        var stacked = new List<string>();

        foreach (var (name, text) in Components())
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim() == "/// </summary>" && lines[i + 1].Trim() == "/// <summary>")
                    stacked.Add($"{name}:{i + 1}");
            }
        }

        Assert.True(stacked.Count == 0,
            "A <summary> directly follows a </summary>, which means one doc comment was left above "
            + "the member's real one: " + string.Join(", ", stacked));
    }
}
