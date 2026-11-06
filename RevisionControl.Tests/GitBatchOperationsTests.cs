using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// <see cref="GitRevisionControlSystem.RevertFiles"/> and <see cref="GitRevisionControlSystem.Commit"/>
/// over every kind of change at once and over many files. Both went file by file - a status and a
/// checkout per file, a stage per file - and every LibGit2Sharp call that changes the index writes the
/// whole of it back, so the time grew faster than the files did. Each now makes one call of each kind.
/// </summary>
public sealed class GitBatchOperationsTests : IDisposable
{
    private readonly GitRevisionControlSystem _git = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-gitbatch-" + Guid.NewGuid().ToString("N"));
    private readonly Signature _signature = new("Test User", "test@example.com", DateTimeOffset.Now);

    public GitBatchOperationsTests()
    {
        Repository.Init(_root);
        using var repo = new Repository(_root);
        foreach (var (path, text) in new[]
                 {
                     ("README.md", "readme\n"),
                     ("Lib/package.mo", "package Lib end Lib;\n"),
                     ("Lib/A.mo", "within Lib; model A end A;\n"),
                     ("Lib/B.mo", "within Lib; model B end B;\n"),
                     ("Lib/C.mo", "within Lib; model C end C;\n"),
                 })
            Write(path, text);
        Commands.Stage(repo, "*");
        repo.Commit("Initial", _signature, _signature);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
            return;
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
        }
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Full(string relativePath) => Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// A file's text with its line endings made <c>\n</c>: a checkout applies the machine's
    /// <c>core.autocrlf</c>, as git does, so a reverted file may come back with CRLF.
    /// </summary>
    private string Read(string relativePath) => File.ReadAllText(Full(relativePath)).Replace("\r\n", "\n");

    private void Write(string relativePath, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(relativePath))!);
        File.WriteAllText(Full(relativePath), text);
    }

    private RepositoryStatus Status()
    {
        using var repo = new Repository(_root);
        return repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true, RecurseUntrackedDirs = true });
    }

    [Fact]
    public void EveryKindOfChange_IsRevertedInOneCall()
    {
        Write("README.md", "changed\n");                            // modified
        Write("Lib/A.mo", "within Lib; model A Real x; end A;\n");   // modified and staged
        File.Delete(Full("Lib/B.mo"));                              // deleted
        Write("Lib/New.mo", "within Lib; model New end New;\n");     // untracked
        Write("Lib/Staged.mo", "within Lib; model Staged end Staged;\n");          // added
        Write("Lib/StagedThenEdited.mo", "within Lib; model S end S;\n");          // added, then edited
        using (var repo = new Repository(_root))
            Commands.Stage(repo, ["Lib/A.mo", "Lib/Staged.mo", "Lib/StagedThenEdited.mo"]);
        File.AppendAllText(Full("Lib/StagedThenEdited.mo"), "// edited\n");

        var result = _git.RevertFiles(_root,
        [
            "README.md", "Lib\\A.mo", "Lib/B.mo", "Lib/New.mo", "Lib/Staged.mo", "Lib/StagedThenEdited.mo",
        ]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("readme\n", Read("README.md"));
        Assert.Equal("within Lib; model A end A;\n", Read("Lib/A.mo"));
        Assert.True(File.Exists(Full("Lib/B.mo")), "a deleted file is restored");
        Assert.False(File.Exists(Full("Lib/New.mo")), "an untracked file is deleted");
        Assert.False(File.Exists(Full("Lib/Staged.mo")), "an added file is deleted");
        Assert.False(File.Exists(Full("Lib/StagedThenEdited.mo")), "an added file edited since is deleted too");
        Assert.False(Status().IsDirty, "something was left changed or staged");
    }

    [Fact]
    public void OnlyTheSelectedFiles_AreReverted()
    {
        Write("Lib/A.mo", "within Lib; model A Real x; end A;\n");
        Write("Lib/C.mo", "within Lib; model C Real y; end C;\n");

        var result = _git.RevertFiles(_root, ["Lib/A.mo"]);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("within Lib; model A end A;\n", Read("Lib/A.mo"));
        Assert.Equal("within Lib; model C Real y; end C;\n", Read("Lib/C.mo"));
    }

    [Fact]
    public void RevertingNothing_Succeeds() =>
        Assert.True(_git.RevertFiles(_root, []).Success);

    [Fact]
    public void ManyFiles_AreAllRevertedAndCommitted()
    {
        var names = Enumerable.Range(0, 300).Select(i => $"Many/Class{i:D3}.mo").ToList();
        foreach (var name in names)
            Write(name, "model M end M;\n");

        var committed = _git.Commit(_root, "Many new files", names);
        Assert.True(committed.Success, committed.ErrorMessage);
        Assert.False(Status().IsDirty);

        foreach (var name in names)
            File.AppendAllText(Full(name), "// changed\n");

        var reverted = _git.RevertFiles(_root, names);

        Assert.True(reverted.Success, reverted.ErrorMessage);
        Assert.False(Status().IsDirty);
        Assert.Equal("model M end M;\n", Read(names[^1]));
    }

    [Fact]
    public void EveryKindOfChange_IsCommittedInOneCall_AndNothingUnselected()
    {
        Write("README.md", "changed\n");
        File.Delete(Full("Lib/B.mo"));
        Write("Lib/Sub/Deep.mo", "within Lib.Sub; model Deep end Deep;\n");
        Write("Lib/NotSelected.mo", "within Lib; model NotSelected end NotSelected;\n");
        Write("Lib/C.mo", "within Lib; model C Real y; end C;\n");

        var result = _git.Commit(_root, "Every kind of change", ["README.md", "Lib\\B.mo", "Lib/Sub/Deep.mo"]);

        Assert.True(result.Success, result.ErrorMessage);
        using var repo = new Repository(_root);
        var tip = repo.Head.Tip;
        Assert.Equal("changed\n", ((Blob)tip["README.md"].Target).GetContentText());
        Assert.Null(tip["Lib/B.mo"]);
        Assert.NotNull(tip["Lib/Sub/Deep.mo"]);
        Assert.Null(tip["Lib/NotSelected.mo"]);
        Assert.Equal("within Lib; model C end C;\n", ((Blob)tip["Lib/C.mo"].Target).GetContentText());
    }

    [Fact]
    public void CommittingEverything_StagesEveryChange()
    {
        Write("README.md", "changed\n");
        File.Delete(Full("Lib/B.mo"));
        Write("Lib/New.mo", "within Lib; model New end New;\n");

        var result = _git.Commit(_root, "Everything");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(Status().IsDirty);
    }
}
