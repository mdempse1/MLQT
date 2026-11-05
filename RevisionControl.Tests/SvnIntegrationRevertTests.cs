namespace RevisionControl.Tests;

/// <summary>
/// <see cref="SvnRevisionControlSystem.RevertFiles"/> over many files and every kind of change at
/// once, against a repository of this run's own. It ran two svn processes per file, so a revert of
/// 17,000 files was 34,000 of them; it now runs one status per chunk of paths and one revert.
/// </summary>
/// <remarks>
/// Named <c>SvnIntegration*</c> so the run without svn on PATH filters it out with the other classes
/// that need the client (<c>build/run-all-tests.ps1</c>).
/// </remarks>
public class SvnIntegrationRevertTests : IClassFixture<SvnTestRepository>
{
    private readonly SvnTestRepository _repository;
    private readonly SvnRevisionControlSystem _svn = new();

    public SvnIntegrationRevertTests(SvnTestRepository repository) => _repository = repository;

    private static string StatusOf(string workingCopy) =>
        SvnCli.Run("status", workingCopy).EnsureSuccess("status").StdOut;

    [Fact]
    public void EveryKindOfChange_IsRevertedInOneCall()
    {
        var wc = _repository.CheckOut();

        var readme = Path.Combine(wc, "README.txt");
        var original = File.ReadAllText(readme);
        File.AppendAllText(readme, "A local change\n");

        var added = Path.Combine(wc, "Added.mo");
        File.WriteAllText(added, "model Added end Added;\n");
        SvnCli.Run("add", added).EnsureSuccess("add");

        var deleted = Path.Combine(wc, "Models", "SimpleModel.mo");
        var deletedText = File.ReadAllText(deleted);
        SvnCli.Run("delete", deleted).EnsureSuccess("delete");

        var unversioned = Path.Combine(wc, "Unversioned.mo");
        File.WriteAllText(unversioned, "model Unversioned end Unversioned;\n");

        var newFolder = Path.Combine(wc, "NewFolder");
        var inNewFolder = Path.Combine(newFolder, "Inside.mo");
        Directory.CreateDirectory(newFolder);
        File.WriteAllText(inNewFolder, "model Inside end Inside;\n");

        var result = _svn.RevertFiles(wc,
        [
            "README.txt", "Added.mo", Path.Combine("Models", "SimpleModel.mo"),
            "Unversioned.mo", Path.Combine("NewFolder", "Inside.mo"), "NewFolder",
        ]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(original, File.ReadAllText(readme));
        Assert.Equal(deletedText, File.ReadAllText(deleted));
        Assert.False(File.Exists(unversioned), "an unversioned file is deleted by a revert");
        Assert.False(Directory.Exists(newFolder), "an unversioned folder is deleted by a revert");

        // A scheduled addition is unscheduled and left on disk, as svn revert leaves it.
        var status = StatusOf(wc);
        Assert.DoesNotContain("README.txt", status);
        Assert.DoesNotContain("SimpleModel.mo", status);
        Assert.Contains("?       " + added, status);
    }

    [Fact]
    public void ManyFiles_AreAllReverted()
    {
        // Enough files for the status to run in several chunks of the command line.
        var wc = _repository.CheckOut();
        var folder = Path.Combine(wc, "Many");
        Directory.CreateDirectory(folder);
        var names = Enumerable.Range(0, 300).Select(i => $"Class{i:D3}WithALongEnoughNameToFillTheCommandLine.mo").ToList();
        foreach (var name in names)
            File.WriteAllText(Path.Combine(folder, name), "model M end M;\n");
        SvnCli.Run("add", folder).EnsureSuccess("add");
        SvnCli.Run("commit", folder, "-m", "Many files").EnsureSuccess("commit");
        foreach (var name in names)
            File.AppendAllText(Path.Combine(folder, name), "// changed\n");

        var result = _svn.RevertFiles(wc, names.Select(n => Path.Combine("Many", n)));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain("Many", StatusOf(wc));
    }
}
