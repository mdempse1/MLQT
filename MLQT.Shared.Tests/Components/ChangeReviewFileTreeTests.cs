using MLQT.Shared.Components;
using MudBlazor;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="ChangeReview.BuildFileTree"/> — the folder tree a user reads before committing.
///
/// <para>It is the last thing seen before a commit, so a file in the wrong place, or missing, is a
/// change committed without being looked at.</para>
/// </summary>
public class ChangeReviewFileTreeTests
{
    private static VcsWorkingCopyFile File(string path, VcsFileStatus status = VcsFileStatus.Modified) =>
        new() { Path = path, Status = status };

    private static List<TreeItemData<ChangeReview.FileTreeNode>> Tree(params VcsWorkingCopyFile[] files) =>
        ChangeReview.BuildFileTree(files.ToList());

    private static ChangeReview.FileTreeNode Node(TreeItemData<ChangeReview.FileTreeNode> item) =>
        item.Value ?? throw new InvalidOperationException("tree item has no node");

    private static IEnumerable<string> Names(IEnumerable<TreeItemData<ChangeReview.FileTreeNode>> items) =>
        items.Select(i => Node(i).Name);

    [Fact]
    public void ASingleFileAtTheRoot_IsOneLeaf()
    {
        var tree = Tree(File("package.mo"));

        var only = Assert.Single(tree);
        Assert.Equal("package.mo", Node(only).Name);
        Assert.True(Node(only).IsFile);
    }

    [Fact]
    public void ANestedPath_BuildsAFolderForEachLevel()
    {
        var tree = Tree(File("Lib/Sub/Thing.mo"));

        var lib = Assert.Single(tree);
        Assert.Equal("Lib", Node(lib).Name);
        Assert.False(Node(lib).IsFile);

        var sub = Assert.Single(Node(lib).Children);
        Assert.Equal("Sub", sub.Name);

        var thing = Assert.Single(sub.Children);
        Assert.Equal("Thing.mo", thing.Name);
        Assert.True(thing.IsFile);
    }

    [Fact]
    public void GitAndSvnSeparators_ProduceTheSameTree()
    {
        // The reason the normalisation is there: Git reports "Lib/Thing.mo" and SVN on Windows
        // reports "Lib\Thing.mo". A tree that treats them differently shows one repository's
        // changes as a tree and the other's as a flat list of long names.
        var git = Tree(File("Lib/Sub/Thing.mo"));
        var svn = Tree(File(@"Lib\Sub\Thing.mo"));

        Assert.Equal(Names(git), Names(svn));
        Assert.Equal(Node(git[0]).Children.Single().Name, Node(svn[0]).Children.Single().Name);
    }

    [Fact]
    public void MixedSeparatorsInOnePath_AreStillOneTree()
    {
        var tree = Tree(File(@"Lib/Sub\Thing.mo"));

        var lib = Assert.Single(tree);
        var sub = Assert.Single(Node(lib).Children);
        Assert.Equal("Thing.mo", Assert.Single(sub.Children).Name);
    }

    [Fact]
    public void TwoFilesInTheSameFolder_ShareOneFolderNode()
    {
        var tree = Tree(File("Lib/A.mo"), File("Lib/B.mo"));

        var lib = Assert.Single(tree);
        Assert.Equal(["A.mo", "B.mo"], Node(lib).Children.Select(c => c.Name));
    }

    [Fact]
    public void FoldersComeBeforeFiles()
    {
        // Alphabetically "zzz" sorts after "aaa.mo", but a folder is still listed first: the tree
        // reads as structure, not as a sorted path list.
        var tree = Tree(File("aaa.mo"), File("zzz/inner.mo"));

        Assert.Equal(["zzz", "aaa.mo"], Names(tree));
    }

    [Fact]
    public void SiblingsAreAlphabeticalIgnoringCase()
    {
        var tree = Tree(File("Lib/b.mo"), File("Lib/A.mo"), File("Lib/C.mo"));

        Assert.Equal(["A.mo", "b.mo", "C.mo"], Node(Assert.Single(tree)).Children.Select(c => c.Name));
    }

    [Fact]
    public void SortingReachesEveryLevel()
    {
        // The input is walked in path order, so siblings mostly arrive sorted already and a tree
        // built without the recursive sort still looks right. These two do not: "aaa.mo" sorts
        // before "zzz/x.mo" by path, so the file is created before the folder, and only the sort
        // at that level puts the folder first.
        var tree = Tree(File("Lib/Deep/aaa.mo"), File("Lib/Deep/zzz/x.mo"));

        var deep = Node(Assert.Single(tree)).Children.Single();
        Assert.Equal(["zzz", "aaa.mo"], deep.Children.Select(c => c.Name));
    }

    [Fact]
    public void CaseOnlyDifferencesAreSortedAtDepthToo()
    {
        // Ordinal path order puts "B.mo" before "a.mo" because uppercase sorts first; the
        // case-insensitive sibling sort has to undo that, at every level and not just the root.
        var tree = Tree(File("Lib/Deep/B.mo"), File("Lib/Deep/a.mo"));

        var deep = Node(Assert.Single(tree)).Children.Single();
        Assert.Equal(["a.mo", "B.mo"], deep.Children.Select(c => c.Name));
    }

    [Fact]
    public void OnlyFilesCarryAStatus()
    {
        // A folder has no VCS status of its own; showing one would claim the directory was modified.
        var tree = Tree(File("Lib/Thing.mo", VcsFileStatus.Added));

        var lib = Node(Assert.Single(tree));
        Assert.Null(lib.Status);
        Assert.Equal(VcsFileStatus.Added, lib.Children.Single().Status);
    }

    [Fact]
    public void EachFilesFullPathIsKept()
    {
        // The full path is what selection and commit work from, so it has to survive the split.
        var tree = Tree(File("Lib/Sub/Thing.mo"));

        var leaf = Node(tree[0]).Children.Single().Children.Single();
        Assert.Equal("Lib/Sub/Thing.mo", leaf.FullPath);
    }

    [Theory]
    [InlineData("Lib/Sub/Thing.mo")]
    [InlineData(@"Lib\Sub\Thing.mo")]
    public void AFullPathCanReachTheFileOnDisk(string reportedByTheVcs)
    {
        // B137, and the assertion that was missing rather than wrong. EachFilesFullPathIsKept pinned
        // the *shape* of FullPath without asking whether that shape is usable, and the shape it
        // pinned was backslash-separated - which is a relative path on Windows and a single file name
        // containing backslashes on Linux. ChangeReview.SelectFile hands FullPath to Path.Combine and
        // File.Exists to read the working copy, so on Linux the commit dialog's diff showed the HEAD
        // side and nothing at all for the modified file.
        //
        // Written against a real file rather than against a string, because a string assertion is
        // what let this through: the only thing that settles it is whether the path opens.
        var root = Directory.CreateTempSubdirectory(nameof(AFullPathCanReachTheFileOnDisk));
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Sub"));
            System.IO.File.WriteAllText(Path.Combine(root.FullName, "Lib", "Sub", "Thing.mo"), "model M end M;");

            var tree = Tree(File(reportedByTheVcs));
            var leaf = Node(tree[0]).Children.Single().Children.Single();

            Assert.True(System.IO.File.Exists(Path.Combine(root.FullName, leaf.FullPath)),
                $"FullPath '{leaf.FullPath}' does not reach the file it names");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void NoChanges_IsAnEmptyTree()
    {
        Assert.Empty(ChangeReview.BuildFileTree([]));
    }

    [Fact]
    public void SeveralRoots_AreAllListed()
    {
        var tree = Tree(File("A/x.mo"), File("B/y.mo"), File("root.mo"));

        Assert.Equal(["A", "B", "root.mo"], Names(tree));
    }
}
