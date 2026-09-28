using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

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
/// <para><b>Why here.</b> These lived in <c>MLQT.Services.Tests</c> until phase 7a-2, because
/// <c>MLQT.Shared</c> had no test project and a suite that runs beat a suite that does not. This is
/// the project they always said they belonged in.</para>
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

    /// <summary>
    /// Every component file, markup and code-behind alike.
    ///
    /// <para>Both halves are enumerated because phase 7a-1 moved component logic out of
    /// <c>@code</c> blocks into <c>.razor.cs</c> partial classes. Scanning only <c>*.razor</c> — which
    /// is what this did when it was written, when that was the whole of the C# — leaves these checks
    /// reading markup files with no code in them: five tests passing over nothing. Each file is a unit
    /// in its own right, so no pairing is needed: a converted component keeps its subscriptions and
    /// its interface declaration together in the code-behind, and an unconverted one keeps both in the
    /// markup.</para>
    /// </summary>
    private static IEnumerable<(string Path, string Text)> Components()
    {
        var root = SharedDirectory();
        if (root is null)
            yield break;

        foreach (var pattern in new[] { "*.razor", "*.razor.cs" })
        foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
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

        // And that it can see the C#, not just the markup. Counting files is not enough: after the
        // 7a-1 sweep moved every @code block into a .razor.cs, an enumeration of *.razor alone still
        // returned 39 files and every check below still passed — over markup with no code in it.
        // Assert on the material the checks actually consume.
        Assert.True(Components().Count(c => Subscribe.IsMatch(c.Text)) > 10,
            "The sweep is reading files but finding no event subscriptions in any of them, which "
            + "means it is looking at the wrong half of the components.");
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

            // Both spellings: the @implements directive in an unconverted component, and the
            // interface on the partial class declaration in a converted one.
            var declares = text.Contains("@implements IDisposable")
                           || text.Contains("@implements IAsyncDisposable")
                           || Regex.IsMatch(text, @"partial class \w+\s*:.*Disposable");
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

    /// <summary>
    /// <c>MainLayout</c> never loads repository settings without naming the project to load.
    /// </summary>
    /// <remarks>
    /// <para><b>B192, twice.</b> The startup path called <c>LoadRepositorySettingsAsync()</c> with no
    /// argument purely to get the saved project list into memory, so that a newly created project
    /// could be appended to it. That overload loads the <i>currently active</i> project: it opens
    /// every repository in it and loads their libraries, and nothing afterwards unloads them — the
    /// method never clears the loaded repositories or the graph, and only <c>SwitchProjectAsync</c>
    /// does. So creating a project on the startup screen came up holding the previous session's
    /// repositories.</para>
    ///
    /// <para>It reads as a harmless "make sure settings are loaded", which is why it survived a first
    /// fix of this item, and it compiles wherever it is written. Every call from here names a project;
    /// the no-argument form belongs to callers that really do mean "open whatever was open last".</para>
    /// </remarks>
    [Fact]
    public void MainLayoutAlwaysNamesTheProjectItLoads()
    {
        var shared = SharedDirectory();
        if (shared is null)
            return;

        var source = File.ReadAllText(Path.Combine(shared, "Layout", "MainLayout.razor.cs"));

        // Strip comments first, so the explanation above the fixed call is not mistaken for the call.
        var code = Regex.Replace(source, @"//.*", string.Empty);

        var bare = Regex.Matches(code, @"LoadRepositorySettingsAsync\s*\(\s*\)").Count;

        Assert.True(bare == 0,
            $"MainLayout calls LoadRepositorySettingsAsync() with no project {bare} time(s). That overload "
            + "loads the previously active project's repositories and libraries, and nothing unloads them "
            + "afterwards - which is B192. Name the project to load, or use CreateAndSelectProjectAsync "
            + "when the point is only to add a project to the saved settings.");
    }

    /// <summary>
    /// Every screen that names a project shows why a name is refused, and will not let it be
    /// confirmed.
    /// </summary>
    /// <remarks>
    /// <para>Three places can name a project: the startup selector, and creating or renaming one in
    /// Settings - Manage Repositories. The rule itself lives once, in <c>ProjectNameRules</c>, and the
    /// service refuses a name that reaches it regardless. What this holds is the half the user meets,
    /// which is markup and therefore compiles whether or not it was written: a field bound to the
    /// error and a confirm button disabled by it.</para>
    ///
    /// <para>Checked by reading, because the message itself cannot be asserted from a rendered test:
    /// MudTextField emits <c>ErrorText</c> on the render after the value changes, so a single
    /// synthetic keystroke shows the disabled button and not yet the reason.</para>
    /// </remarks>
    [Theory]
    [InlineData("Dialogs/ProjectSelectionDialog.razor", "NewProjectNameError")]
    [InlineData("Components/SettingsRepositories.razor", "NewProjectNameError")]
    [InlineData("Components/SettingsRepositories.razor", "RenameError")]
    public void EveryPlaceThatNamesAProjectShowsWhyAndBlocksConfirm(string markupFile, string errorProperty)
    {
        var shared = SharedDirectory();
        if (shared is null)
            return;

        var markup = File.ReadAllText(Path.Combine(shared, markupFile));

        Assert.True(
            markup.Contains($"ErrorText=\"@{errorProperty}\"", StringComparison.Ordinal),
            $"{markupFile} does not show {errorProperty} to the user. Bind the field's ErrorText to it, "
            + "or a refused name is refused with no reason given.");

        Assert.True(
            markup.Contains($"Disabled=\"@({errorProperty} is not null)\"", StringComparison.Ordinal),
            $"{markupFile} does not disable its confirm button on {errorProperty}. Without it the name "
            + "can be confirmed and the service throws instead, which reaches the user as a crash.");
    }
}
