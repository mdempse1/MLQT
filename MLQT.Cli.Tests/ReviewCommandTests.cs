using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// <c>--format review</c> end to end, over a real branch in a real repository: the diff, the check
/// and the payload together. The formatter's own tests fix the rules; these prove the line numbers
/// arriving from git are the ones the findings are judged against, which no unit test can show.
/// </summary>
public class ReviewCommandTests
{
    private sealed class TempRepo(bool initGit = true) : IDisposable
    {
        private readonly TempWorkspace _workspace = Build(initGit);

        private static TempWorkspace Build(bool initGit)
        {
            var workspace = new TempWorkspace("mlqt-review").WithSettings(Settings);
            WriteLibrary(workspace, BaseLibrary, "Good\nOld\n");
            return initGit ? workspace.InitGit() : workspace;
        }

        private static void WriteLibrary(TempWorkspace workspace, string packageMo, string order)
            => workspace
                .Write(Path.Combine("Lib", "package.mo"), packageMo)
                .Write(Path.Combine("Lib", "package.order"), order);

        public string Root => _workspace.Root;
        public string LibraryPath => _workspace.PathTo("Lib");

        public void Write(string packageMo, string order) => WriteLibrary(_workspace, packageMo, order);

        public (int Code, string Output) Git(string arguments) => _workspace.Git(arguments);

        public void Dispose() => _workspace.Dispose();
    }

    private const string Settings =
        """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Error" } }""";

    // Lib.Old has no description and is there from the start: pre-existing, on an untouched line.
    private const string BaseLibrary = """
        within ;
        package Lib "A library"
          model Good "Described"
          end Good;
          model Old
          end Old;
        end Lib;
        """;

    // Lib.Fresh is added by the branch, so its finding sits on a line the change wrote.
    private const string BranchLibrary = """
        within ;
        package Lib "A library"
          model Good "Described"
          end Good;
          model Old
          end Old;
          model Fresh
          end Fresh;
        end Lib;
        """;

    /// <summary>A repository whose branch adds one undescribed class on top of one already there.</summary>
    private static TempRepo BranchWithOneNewClass()
    {
        var repo = new TempRepo();
        repo.Git("checkout -qb feature");
        repo.Write(BranchLibrary, "Good\nOld\nFresh\n");
        repo.Git("add -A");
        repo.Git("commit -qm \"add Fresh\"");
        return repo;
    }

    [Fact]
    public void OnlyTheFindingOnTheChangedLineIsCommentedOn()
    {
        using var repo = BranchWithOneNewClass();

        var (_, stdout, _) = Cli.Run("check", repo.LibraryPath, "--changed-from", "main", "--format", "review");

        var review = JsonDocument.Parse(stdout).RootElement;
        var comment = Assert.Single(review.GetProperty("comments").EnumerateArray().ToArray());

        Assert.Equal("Lib/package.mo", comment.GetProperty("path").GetString());
        Assert.Equal(7, comment.GetProperty("line").GetInt32());   // "  model Fresh", the added line

        // Lib.Old is just as undescribed, but on a line this branch never wrote.
        var body = review.GetProperty("body").GetString()!;
        Assert.Contains("Lib.Old", body);
        Assert.Contains("not on a changed line", body);
    }

    [Fact]
    public void TheGateStillDecidesTheExitCode()
    {
        using var repo = BranchWithOneNewClass();

        var (code, _, _) = Cli.Run("check", repo.LibraryPath, "--changed-from", "main", "--format", "review");

        Assert.Equal(1, code);   // two errors; the review itself is only ever a comment
    }

    [Fact]
    public void WithoutChangedFrom_ItSaysSoBeforeCheckingAnything()
    {
        using var repo = BranchWithOneNewClass();

        var (code, _, stderr) = Cli.Run("check", repo.LibraryPath, "--format", "review");

        Assert.Equal(2, code);
        Assert.Contains("--changed-from", stderr);
        Assert.DoesNotContain("settings from", stderr);   // refused before the library was loaded
    }

    [Fact]
    public void OutsideGit_ItSaysWhyRatherThanProducingAnEmptyReview()
    {
        using var repo = new TempRepo(initGit: false);

        var (code, _, stderr) = Cli.Run("check", repo.LibraryPath, "--changed-from", "main", "--format", "review");

        Assert.Equal(2, code);
        Assert.Contains("not inside a Git working copy", stderr);
        Assert.DoesNotContain("settings from", stderr);   // refused before the library was loaded
    }

    [Fact]
    public void AnUnreachableBaseRefNamesTheUsualCause()
    {
        // A shallow CI checkout is the common way to get here, and the message has to say so or
        // this reads as a defect in the diff.
        using var repo = BranchWithOneNewClass();

        var (code, _, stderr) = Cli.Run(
            "check", repo.LibraryPath, "--changed-from", "no-such-branch", "--format", "review");

        Assert.Equal(2, code);
        Assert.Contains("fetch-depth", stderr);
    }

    [Fact]
    public void ReviewCanBeAskedForAsASecondReport()
    {
        using var repo = BranchWithOneNewClass();
        var path = System.IO.Path.Combine(repo.Root, "review.json");

        var (_, stdout, _) = Cli.Run(
            "check", repo.LibraryPath, "--changed-from", "main", "--no-color", "--report", $"review:{path}");

        Assert.Contains("Lib.Fresh", stdout);   // the console output is unchanged
        var review = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Single(review.GetProperty("comments").EnumerateArray().ToArray());
    }
}
