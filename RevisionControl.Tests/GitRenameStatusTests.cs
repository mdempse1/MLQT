using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// B350 — a staged rename carries the path its committed content is at.
/// </summary>
/// <remarks>
/// Git reports a <c>git mv</c> at its new path only. Asked for the committed version of that path,
/// it has none, so every class in the file read as added - the simulation marker, for a move.
/// </remarks>
public class GitRenameStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GitRenameStatus_" + Guid.NewGuid().ToString("N"));

    public GitRenameStatusTests()
    {
        Repository.Init(_root);
        using var repo = new Repository(_root);
        File.WriteAllText(Path.Combine(_root, "Old.mo"), "model Resistor\n  parameter Real R = 100;\nend Resistor;\n");
        File.WriteAllText(Path.Combine(_root, "Other.mo"), "model Other\nend Other;\n");
        Commands.Stage(repo, "*");
        var who = new Signature("MLQT", "mlqt@localhost", DateTimeOffset.Now);
        repo.Commit("initial", who, who);
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void AStagedRename_CarriesItsOldPath()
    {
        using (var repo = new Repository(_root))
            Commands.Move(repo, "Old.mo", "New.mo");

        var changes = new GitRevisionControlSystem().GetWorkingCopyChanges(_root);

        var renamed = Assert.Single(changes);
        Assert.Equal("New.mo", renamed.Path);
        Assert.Equal(VcsFileStatus.Renamed, renamed.Status);
        Assert.Equal("Old.mo", renamed.OldPath);
    }

    [Fact]
    public void AnOrdinaryChange_HasNoOldPath()
    {
        File.AppendAllText(Path.Combine(_root, "Other.mo"), "// edited\n");

        var changes = new GitRevisionControlSystem().GetWorkingCopyChanges(_root);

        var modified = Assert.Single(changes);
        Assert.Equal(VcsFileStatus.Modified, modified.Status);
        Assert.Null(modified.OldPath);
    }
}
