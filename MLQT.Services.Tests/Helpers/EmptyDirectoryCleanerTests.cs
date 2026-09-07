using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="EmptyDirectoryCleaner"/>, lifted out of <c>MainLayout</c> in phase 7a-4.
///
/// <para>Driven against real directories rather than a mocked file system, because what is being
/// checked is which directories get <em>deleted</em> from a user's working copy, and a fake that
/// agrees with the implementation proves nothing about that.</para>
/// </summary>
public sealed class EmptyDirectoryCleanerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mlqt-cleaner-" + Guid.NewGuid().ToString("N"));

    public EmptyDirectoryCleanerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private string FileIn(string directory, string name = "Thing.mo")
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "model Thing end Thing;");
        return path;
    }

    [Fact]
    public void AnEmptyDirectory_IsRemoved()
    {
        var empty = Dir("Empty");

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.False(Directory.Exists(empty));
    }

    [Fact]
    public void ADirectoryWithAFile_IsKept()
    {
        var kept = Dir("Kept");
        FileIn(kept);

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.True(Directory.Exists(kept));
        Assert.True(File.Exists(Path.Combine(kept, "Thing.mo")));
    }

    [Fact]
    public void ADirectoryWithContent_SurvivesEvenWithoutTheEmptinessCheck()
    {
        // The guarantee is not the emptiness check - that is a shortcut to avoid a thrown exception
        // per non-empty directory. It is that Directory.Delete is called without `recursive`, so a
        // directory with anything in it refuses to go rather than taking its contents with it.
        // Removing the check leaves this test, and the user's files, unaffected.
        var full = Dir("Full");
        FileIn(full, "A.mo");
        Dir("Full", "Nested");
        FileIn(Path.Combine(_root, "Full", "Nested"), "B.mo");

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.True(File.Exists(Path.Combine(full, "A.mo")));
        Assert.True(File.Exists(Path.Combine(full, "Nested", "B.mo")));
    }

    [Fact]
    public void AChainOfEmptyDirectories_GoesInOnePass()
    {
        // Deepest-first is what makes one pass enough: emptying the child leaves the parent empty,
        // and a shallow-first walk would leave the parent behind until the next save.
        var deep = Dir("A", "B", "C");

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.False(Directory.Exists(deep));
        Assert.False(Directory.Exists(Path.Combine(_root, "A", "B")));
        Assert.False(Directory.Exists(Path.Combine(_root, "A")));
    }

    [Fact]
    public void AParentHoldingAFile_SurvivesItsEmptyChild()
    {
        var parent = Dir("Parent");
        FileIn(parent);
        var child = Dir("Parent", "Child");

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.False(Directory.Exists(child));
        Assert.True(Directory.Exists(parent));
    }

    [Fact]
    public void AHiddenDirectory_IsNeverTouched()
    {
        // The rule that keeps this safe. A .git or .svn directory is full of empty ones, and
        // removing them corrupts the working copy this whole pipeline exists to look after.
        var git = Dir(".git");
        var inside = Dir(".git", "refs", "heads");

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.True(Directory.Exists(git));
        Assert.True(Directory.Exists(inside));
    }

    [Fact]
    public void TheRootItself_IsNeverRemoved()
    {
        // However empty it becomes: it is the library the user opened.
        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void WhatWasRemoved_IsReported()
    {
        Dir("Gone");
        var kept = Dir("Kept");
        FileIn(kept);

        var removed = EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.Equal([Path.Combine(_root, "Gone")], removed);
    }

    [Fact]
    public void APathThatDoesNotExist_IsNotAnError()
    {
        // Libraries come and go while the app is running; a save that races a deletion must not
        // throw out of the formatting pass.
        var removed = EmptyDirectoryCleaner.RemoveEmptyDirectories(
            Path.Combine(_root, "never-existed"));

        Assert.Empty(removed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoPathAtAll_IsNotAnError(string? path)
    {
        Assert.Empty(EmptyDirectoryCleaner.RemoveEmptyDirectories(path));
    }

    [Fact]
    public void ADirectoryHoldingOnlyASubdirectory_IsRemovedOnceThatGoes()
    {
        var outer = Dir("Outer");
        Dir("Outer", "Inner");

        EmptyDirectoryCleaner.RemoveEmptyDirectories(_root);

        Assert.False(Directory.Exists(outer));
    }
}
