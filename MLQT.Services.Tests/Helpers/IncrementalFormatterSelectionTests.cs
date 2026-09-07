using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="IncrementalFormatter.SelectFilesToFormat"/> — which changed files the formatter will
/// actually rewrite.
///
/// <para>This is the path that runs at startup and after every VCS operation, so it is the formatter
/// most users meet. Every rule here is a reason not to touch a file, and losing one means a file
/// rewritten that should not have been — which is what <b>B65</b> was.</para>
/// </summary>
public class IncrementalFormatterSelectionTests
{
    private const string PackagePath = @"C:\lib\package.mo";

    private static StyleCheckingSettings Formatting(bool on = true) => new() { ApplyFormattingRules = on };

    /// <summary>A graph holding one file with a top-level package and one class nested in it.</summary>
    private static DirectedGraph OnePackageFile(out ModelNode package, out ModelNode nested)
    {
        var graph = new DirectedGraph();
        var fileId = GraphBuilder.GenerateFileId(PackagePath);
        graph.AddNode(new FileNode(fileId, PackagePath));

        package = new ModelNode("Lib", "Lib", "package Lib end Lib;");
        nested = new ModelNode("Lib.A", "A", "model A end A;") { ParentModelName = "Lib" };
        graph.AddNode(package);
        graph.AddNode(nested);
        graph.AddFileContainsModel(fileId, "Lib");
        graph.AddFileContainsModel(fileId, "Lib.A");
        return graph;
    }

    private static List<FileToFormat> Select(
        DirectedGraph graph, StyleCheckingSettings settings, params string[] paths) =>
        IncrementalFormatter.SelectFilesToFormat(graph, paths, settings, _ => true);

    [Fact]
    public void AChangedFileWithClassesInIt_IsSelected()
    {
        var graph = OnePackageFile(out _, out _);

        var selected = Select(graph, Formatting(), PackagePath);

        Assert.Equal(PackagePath, Assert.Single(selected).FilePath);
    }

    [Fact]
    public void WithFormattingSwitchedOff_NothingIsSelected()
    {
        var graph = OnePackageFile(out _, out _);

        Assert.Empty(Select(graph, Formatting(on: false), PackagePath));
    }

    [Fact]
    public void AFileThatIsNoLongerThere_IsSkipped()
    {
        var graph = OnePackageFile(out _, out _);

        var selected = IncrementalFormatter.SelectFilesToFormat(
            graph, [PackagePath], Formatting(), _ => false);

        Assert.Empty(selected);
    }

    [Fact]
    public void AFileInAHiddenDirectory_IsSkipped()
    {
        // .git and .svn hold Modelica-looking files of their own, and rewriting one corrupts the
        // working copy.
        var hidden = @"C:\lib\.git\package.mo";
        var graph = new DirectedGraph();
        var fileId = GraphBuilder.GenerateFileId(hidden);
        graph.AddNode(new FileNode(fileId, hidden));
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib end Lib;"));
        graph.AddFileContainsModel(fileId, "Lib");

        Assert.Empty(Select(graph, Formatting(), hidden));
    }

    [Fact]
    public void AFileTheGraphDoesNotKnow_IsSkipped()
    {
        var graph = OnePackageFile(out _, out _);

        Assert.Empty(Select(graph, Formatting(), @"C:\lib\Unknown.mo"));
    }

    [Fact]
    public void AFileHoldingAnExcludedClass_IsSkippedEntirely()
    {
        // A file is reformatted whole or not at all: the renderer works on the file's parse tree, so
        // there is no way to reformat some classes and leave a nested sibling alone. Excluding the
        // whole file is the safe reading of an exclusion.
        var graph = OnePackageFile(out _, out var nested);
        var settings = Formatting();
        settings.FormattingExcludedModels.Add(nested.Id);

        Assert.Empty(Select(graph, settings, PackagePath));
    }

    [Fact]
    public void AnExclusionOnTheTopLevelClass_AlsoSkipsTheFile()
    {
        var graph = OnePackageFile(out var package, out _);
        var settings = Formatting();
        settings.FormattingExcludedModels.Add(package.Id);

        Assert.Empty(Select(graph, settings, PackagePath));
    }

    [Fact]
    public void AnMlqtFormatFalseAnnotation_IsHonouredHereToo()
    {
        // B65. This path asked the name list alone, so __MLQT(format=false) - the rename-safe
        // successor the documentation steers people to - was honoured by Format All Files and
        // ignored here, which is the path that runs at startup and after every VCS operation. The
        // class it was written on was reordered in the working copy on the next pull.
        var graph = new DirectedGraph();
        var fileId = GraphBuilder.GenerateFileId(PackagePath);
        graph.AddNode(new FileNode(fileId, PackagePath));
        graph.AddNode(new ModelNode("Lib", "Lib",
            "package Lib annotation(__MLQT(format=false)); end Lib;"));
        graph.AddFileContainsModel(fileId, "Lib");

        var selected = Select(graph, Formatting(), PackagePath);

        Assert.Empty(selected);
    }

    [Fact]
    public void TheOwnerIsTheTopmostClassInTheFile()
    {
        // Only the owner's within clause describes the file, so picking a nested class instead
        // writes the wrong one.
        var graph = OnePackageFile(out var package, out _);

        var selected = Assert.Single(Select(graph, Formatting(), PackagePath));

        Assert.Equal(package.Id, selected.Owner.Id);
    }

    [Fact]
    public void EveryClassInTheFileIsCarried_NotJustTheOwner()
    {
        // They all have their stored source refreshed after the render, so the code viewer and the
        // style check see the formatted text without waiting for a reload.
        var graph = OnePackageFile(out _, out _);

        var selected = Assert.Single(Select(graph, Formatting(), PackagePath));

        Assert.Equal(2, selected.Models.Count);
    }

    [Fact]
    public void AClassWhoseParentIsInAnotherFile_IsAnOwner()
    {
        // A standalone class in its own file: its parent package exists, but elsewhere.
        var standalone = @"C:\lib\A.mo";
        var graph = new DirectedGraph();

        var packageFileId = GraphBuilder.GenerateFileId(PackagePath);
        graph.AddNode(new FileNode(packageFileId, PackagePath));
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib end Lib;"));
        graph.AddFileContainsModel(packageFileId, "Lib");

        var standaloneFileId = GraphBuilder.GenerateFileId(standalone);
        graph.AddNode(new FileNode(standaloneFileId, standalone));
        graph.AddNode(new ModelNode("Lib.A", "A", "model A end A;") { ParentModelName = "Lib" });
        graph.AddFileContainsModel(standaloneFileId, "Lib.A");

        var selected = Assert.Single(Select(graph, Formatting(), standalone));

        Assert.Equal("Lib.A", selected.Owner.Id);
    }

    [Fact]
    public void SeveralChangedFiles_AreAllSelected()
    {
        var second = @"C:\lib\B.mo";
        var graph = OnePackageFile(out _, out _);
        var fileId = GraphBuilder.GenerateFileId(second);
        graph.AddNode(new FileNode(fileId, second));
        graph.AddNode(new ModelNode("Lib.B", "B", "model B end B;"));
        graph.AddFileContainsModel(fileId, "Lib.B");

        Assert.Equal(2, Select(graph, Formatting(), PackagePath, second).Count);
    }
}
