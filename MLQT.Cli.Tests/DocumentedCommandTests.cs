using System.Text.Json;
using System.Text.RegularExpressions;

namespace MLQT.Cli.Tests;

/// <summary>
/// Every flag combination the documentation prints is run by some test.
///
/// <para><b>Why this exists.</b> `--metrics --coverage-ratchet` is printed by both `cli.md` and
/// `ci-quality-gate.md` as the recommended CI recipe, and it could not fail: the run recorded its
/// point before the gate read the history, so the ratchet compared itself against itself
/// (backlog B100). Fifteen reviews missed it because every ratchet test ran the two flags in
/// *separate* invocations — the suite exercised a usage the documentation does not recommend and
/// never the one it does. The tests were not wrong; they were aimed at the wrong command.</para>
///
/// <para>So: when a document prints a command, that exact command is a test case. The guard below
/// enforces it mechanically, and the tests beneath it close the six combinations that were
/// uncovered when it was written.</para>
/// </summary>
public class DocumentedCommandTests
{
    // ---- the guard -------------------------------------------------------------------------

    /// <summary>The repository root, found by walking up from the test binary.</summary>
    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Documentation", "cli.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>The flag sets of the `mlqt …` commands the documentation prints inside fenced blocks.</summary>
    private static List<(string Where, string Command, HashSet<string> Flags)> DocumentedCommands(string root)
    {
        var found = new List<(string, string, HashSet<string>)>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Documentation"), "*.md"))
        {
            var lines = File.ReadAllLines(path);
            var inFence = false;

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (!inFence)
                    continue;

                var command = lines[i].Trim();
                if (!Regex.IsMatch(command, @"^mlqt\s+(check|baseline|compare|hook)\b"))
                    continue;

                // Join a continuation, then drop the trailing comment.
                var j = i;
                while (command.TrimEnd().EndsWith('\\') && j + 1 < lines.Length)
                    command = command.TrimEnd().TrimEnd('\\') + " " + lines[++j].Trim();
                command = command.Split('#')[0].Trim();
                i = j;

                // A syntax template ("mlqt check <library-path> [options]") is not an invocation.
                if (command.Contains('<') || command.Contains('['))
                    continue;

                var flags = Regex.Matches(command, @"(--[a-z][a-z-]*)")
                    .Select(m => m.Value)
                    .ToHashSet(StringComparer.Ordinal);
                if (flags.Count > 0)
                    found.Add(($"{Path.GetFileName(path)}:{i + 1}", command, flags));
            }
        }

        return found;
    }

    /// <summary>The flag sets every <c>Cli.Run</c> in this suite passes.</summary>
    private static List<HashSet<string>> TestedCommands(string root)
    {
        var runs = new List<HashSet<string>>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "MLQT.Cli.Tests"), "*.cs"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"Cli\.Run\((.*?)\);", RegexOptions.Singleline))
            {
                runs.Add(Regex.Matches(m.Groups[1].Value, @"""(--[a-z][a-z-]*)""")
                    .Select(f => f.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal));
            }
        }
        return runs;
    }

    [Fact]
    public void EveryFlagCombinationTheDocumentationPrintsIsRunBySomeTest()
    {
        var root = RepositoryRoot();
        Assert.NotNull(root);   // a silent skip here would reintroduce exactly what B100 was

        var documented = DocumentedCommands(root!);
        Assert.True(documented.Count > 15, $"only {documented.Count} documented commands found");

        var tested = TestedCommands(root!);
        Assert.True(tested.Count > 100, $"only {tested.Count} test invocations found");

        var uncovered = documented
            .Where(d => !tested.Any(t => d.Flags.IsSubsetOf(t)))
            .Select(d => $"{d.Where}  ({string.Join(" ", d.Flags.OrderBy(f => f, StringComparer.Ordinal))})")
            .Distinct()
            .ToList();

        Assert.True(uncovered.Count == 0,
            "The documentation prints these commands and no test runs those flags together. "
            + "A combination nobody runs is how --coverage-ratchet --metrics came to be incapable of "
            + "failing (B100). Add a test, or stop printing the command:\n  "
            + string.Join("\n  ", uncovered));
    }

    // ---- the combinations that were uncovered when the guard was written ---------------------

    private const string Library = """
        within ;
        package Doc "A library"
          model Described "Described"
            parameter Real gain = 1 "the gain";
          end Described;
          model Bare
          end Bare;
        end Doc;
        """;

    /// <summary>The same library with a class added, for the tests that need a working-copy change.</summary>
    private const string ChangedLibrary = """
        within ;
        package Doc "A library"
          model Described "Described"
            parameter Real gain = 1 "the gain";
          end Described;
          model Added
            parameter Real n = 2;
          end Added;
          model Bare
          end Bare;
        end Doc;
        """;

    private const string Order = """
        Described
        Bare
        """;

    private const string ChangedOrder = """
        Described
        Added
        Bare
        """;

    private const string Settings =
        """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Warning" } }""";

    /// <summary>A library with a baseline already covering everything it reports.</summary>
    private sealed class Baselined : IDisposable
    {
        private readonly TempWorkspace _workspace = new("mlqt-documented");

        public Baselined(bool git = false)
        {
            _workspace.WithSettings(Settings, under: "Doc");
            _workspace.Write(Path.Combine("Doc", "package.mo"), Library)
                      .Write(Path.Combine("Doc", "package.order"), Order);
            if (git)
            {
                _workspace.InitGit();     // commits everything, on branch main
                // A working-copy change, so --changed-from main has something to report on.
                _workspace.Write(Path.Combine("Doc", "package.mo"), ChangedLibrary)
                          .Write(Path.Combine("Doc", "package.order"), ChangedOrder);
            }
            Cli.Run("baseline", "create", LibraryPath);
        }

        public string LibraryPath => _workspace.PathTo("Doc");
        public string Out(string name) => _workspace.PathTo(name);
        public TempWorkspace Workspace => _workspace;
        public void Dispose() => _workspace.Dispose();
    }

    [Fact]
    public void BaselineWithJUnitToAFile_WritesASuiteWithNoFailures()
    {
        // ci-quality-gate.md:295. Everything is accepted debt, so the CI test report is green.
        using var lib = new Baselined();
        var outPath = lib.Out("mlqt-results.xml");

        var (code, _, _) = Cli.Run(
            "check", lib.LibraryPath, "--baseline", ".mlqt/baseline.json", "--fail-on", "warning",
            "--format", "junit", "--out", outPath);

        Assert.Equal(0, code);
        var xml = File.ReadAllText(outPath);
        Assert.Contains("<testsuites", xml);
        Assert.Contains("failures=\"0\"", xml);
    }

    [Fact]
    public void BaselineWithSarifRelativeToABase_WritesPathsFromThatBase()
    {
        // ci-quality-gate.md:350. The library is a subdirectory, so --sarif-base .. is the repository
        // root and the URIs have to carry the subdirectory or GitHub attaches them to nothing.
        using var lib = new Baselined();
        var outPath = lib.Out("mlqt.sarif");

        var (code, _, _) = Cli.Run(
            "check", lib.LibraryPath, "--fail-on", "warning",
            "--format", "sarif", "--sarif-base", "..", "--out", outPath, "--baseline", ".mlqt/baseline.json",
            "--sarif-include-accepted");

        Assert.Equal(0, code);
        using var document = JsonDocument.Parse(File.ReadAllText(outPath));
        var uris = document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("locations")[0].GetProperty("physicalLocation")
                          .GetProperty("artifactLocation").GetProperty("uri").GetString())
            .Distinct()
            .ToList();

        Assert.NotEmpty(uris);
        Assert.All(uris, u => Assert.StartsWith("Doc/", u!, StringComparison.Ordinal));
    }

    [Fact]
    public void SarifAndAnExtraMarkdownReport_WriteBothFiles()
    {
        // ci-quality-gate.md:368 — one run, two artefacts, so the library is not checked twice.
        using var lib = new Baselined();
        var sarif = lib.Out("mlqt.sarif");
        var markdown = lib.Out("mlqt.md");

        var (code, _, _) = Cli.Run(
            "check", lib.LibraryPath, "--baseline", ".mlqt/baseline.json", "--fail-on", "warning",
            "--format", "sarif", "--sarif-base", "..", "--out", sarif, "--report", $"markdown:{markdown}");

        Assert.Equal(0, code);
        Assert.True(File.Exists(sarif));
        Assert.Contains("MLQT check", File.ReadAllText(markdown));
    }

    [Fact]
    public void BaselineWithAReviewBody_WritesAPostableReview()
    {
        // ci-quality-gate.md:387 — the body `gh api --input` posts.
        using var lib = new Baselined(git: true);
        var outPath = lib.Out("review.json");

        var (code, _, _) = Cli.Run(
            "check", lib.LibraryPath, "--changed-from", "main", "--baseline", ".mlqt/baseline.json",
            "--fail-on", "warning", "--format", "review", "--out", outPath);

        Assert.Equal(0, code);
        using var document = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal("COMMENT", document.RootElement.GetProperty("event").GetString());
        Assert.Contains("MLQT check", document.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void BaselineAndMetricsTogether_PassTheGateAndRecordAPoint()
    {
        // ci-quality-gate.md:516 and cli.md:519. The structural sibling of B100: two features that
        // both touch persisted state in one run.
        using var lib = new Baselined();

        var (code, _, stderr) = Cli.Run(
            "check", lib.LibraryPath, "--baseline", ".mlqt/baseline.json", "--fail-on", "warning", "--metrics");

        Assert.Equal(0, code);
        Assert.Contains("recorded metrics", stderr);
        Assert.True(File.Exists(Path.Combine(lib.LibraryPath, ".mlqt", "metrics-history.json")));
    }

    [Fact]
    public void BaselineAndMetricsWithoutAThreshold_StillRecords()
    {
        // cli.md:519 exactly — no --fail-on.
        using var lib = new Baselined();

        var (code, _, stderr) = Cli.Run("check", lib.LibraryPath, "--baseline", ".mlqt/baseline.json", "--metrics");

        Assert.Equal(0, code);
        Assert.Contains("recorded metrics", stderr);
    }
}
