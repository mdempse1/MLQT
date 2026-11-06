namespace RevisionControl.Tests;

/// <summary>
/// <see cref="SvnRevisionControlSystem.Commit"/>'s preparation over many files and every kind of
/// change at once, against a repository of this run's own. It asked svn about each path on its own -
/// several processes per file, each opening and locking the working copy's database - and now
/// classifies them a chunk at a time and deletes and adds them with one --targets list each.
/// </summary>
/// <remarks>
/// Named <c>SvnIntegration*</c> so the run without svn on PATH filters it out with the other classes
/// that need the client (<c>build/run-all-tests.ps1</c>).
/// </remarks>
public class SvnIntegrationCommitTests : IClassFixture<SvnTestRepository>
{
    private readonly SvnTestRepository _repository;
    private readonly SvnRevisionControlSystem _svn = new();

    public SvnIntegrationCommitTests(SvnTestRepository repository) => _repository = repository;

    private static string StatusOf(string workingCopy) =>
        SvnCli.Run("status", workingCopy).EnsureSuccess("status").StdOut;

    private bool ExistsAtHead(string branch, string relativePath) =>
        SvnCli.Run("info", $"{_repository.RootUrl}/{branch}/{relativePath}").Success;

    private string Branch(string wc)
    {
        // Every test commits, so each works on a branch of its own and leaves trunk as the other
        // tests expect it.
        var name = "commit-" + Guid.NewGuid().ToString("N")[..8];
        var created = _svn.CreateBranch(wc, name, switchToBranch: true, branchDirectories: null);
        Assert.True(created.Success, created.ErrorMessage);
        return "branches/" + name;
    }

    [Fact]
    public void EveryKindOfChange_IsCommittedInOneCall()
    {
        var wc = _repository.CheckOut();
        var branch = Branch(wc);

        File.AppendAllText(Path.Combine(wc, "README.txt"), "A change\n");

        // Deleted from disk, not through svn: "missing", which the commit schedules for deletion.
        File.Delete(Path.Combine(wc, "Models", "SimpleModel.mo"));

        // Two new folders deep: both folders are added with the file, and nothing else in them.
        var deep = Path.Combine(wc, "New", "Deeper");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "Deep.mo"), "model Deep end Deep;\n");
        File.WriteAllText(Path.Combine(wc, "New", "NotSelected.mo"), "model NotSelected end NotSelected;\n");

        // A new folder selected itself: added with everything in it.
        var whole = Path.Combine(wc, "Whole");
        Directory.CreateDirectory(whole);
        File.WriteAllText(Path.Combine(whole, "Inside.mo"), "model Inside end Inside;\n");

        var result = _svn.Commit(wc, "Every kind of change",
        [
            "README.txt",
            "Models/SimpleModel.mo",
            "New/Deeper/Deep.mo",
            "Whole",
        ]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.NewRevision);
        Assert.Empty(result.SkippedFiles);

        Assert.False(ExistsAtHead(branch, "Models/SimpleModel.mo"), "the missing file was not deleted");
        Assert.True(ExistsAtHead(branch, "New/Deeper/Deep.mo"));
        Assert.True(ExistsAtHead(branch, "Whole/Inside.mo"));
        Assert.False(ExistsAtHead(branch, "New/NotSelected.mo"), "a file nobody selected was committed with its folder");

        var status = StatusOf(wc);
        Assert.DoesNotContain("README.txt", status);
        Assert.DoesNotContain("Deep.mo", status);
        Assert.Contains("NotSelected.mo", status);
    }

    /// <summary>
    /// A selected new folder that is also above another selected new file is still added whole:
    /// a file selected only through the folder is committed with it.
    /// </summary>
    [Fact]
    public void ASelectedNewFolder_AboveAnotherSelectedFile_IsCommittedWhole()
    {
        var wc = _repository.CheckOut();
        var branch = Branch(wc);
        Directory.CreateDirectory(Path.Combine(wc, "Outer", "Sub"));
        File.WriteAllText(Path.Combine(wc, "Outer", "Sub", "A.mo"), "model A end A;\n");
        File.WriteAllText(Path.Combine(wc, "Outer", "B.mo"), "model B end B;\n");

        var result = _svn.Commit(wc, "A folder and a file in it", ["Outer/Sub/A.mo", "Outer"]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(ExistsAtHead(branch, "Outer/Sub/A.mo"));
        Assert.True(ExistsAtHead(branch, "Outer/B.mo"), "a file in the selected folder was left out");
    }

    [Fact]
    public void ManyNewFiles_AreAllCommitted()
    {
        // Enough files for the status to run in several chunks of the command line.
        var wc = _repository.CheckOut();
        var branch = Branch(wc);
        var folder = Path.Combine(wc, "Many");
        Directory.CreateDirectory(folder);
        var names = Enumerable.Range(0, 300).Select(i => $"Class{i:D3}WithALongEnoughNameToFillTheCommandLine.mo").ToList();
        foreach (var name in names)
            File.WriteAllText(Path.Combine(folder, name), "model M end M;\n");

        var result = _svn.Commit(wc, "Many new files", names.Select(n => Path.Combine("Many", n)));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain("Many", StatusOf(wc));
        Assert.True(ExistsAtHead(branch, $"Many/{names[^1]}"));
    }
}
