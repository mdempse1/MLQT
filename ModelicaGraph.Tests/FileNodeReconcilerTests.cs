using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="FileNodeReconciler"/>, lifted out of <c>MainLayout</c> in phase 7a-4.
///
/// <para>Formatting moves classes between files — standalone classes out into their own, non-standalone
/// ones back into their parent package — and until this runs, every consumer that asks which file a
/// class is in gets the answer from before the save: the code viewer, the diff, and the mapping from
/// a finding's class-relative line to a line in a file.</para>
/// </summary>
public class FileNodeReconcilerTests
{
    private static DirectedGraph GraphWith(params (string FileId, string FilePath, string[] ModelIds)[] files)
    {
        var graph = new DirectedGraph();
        foreach (var (fileId, filePath, modelIds) in files)
        {
            graph.AddNode(new FileNode(fileId, filePath));
            foreach (var modelId in modelIds)
            {
                graph.AddNode(new ModelNode(modelId, modelId, $"model {modelId} end {modelId};"));
                graph.AddFileContainsModel(fileId, modelId);
            }
        }
        return graph;
    }

    private static string FileOf(DirectedGraph graph, string modelId) =>
        graph.GetNode<ModelNode>(modelId)?.ContainingFileId ?? "";

    [Fact]
    public void AClassWrittenToANewFile_MovesToIt()
    {
        var oldId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var graph = GraphWith((oldId, "Lib/package.mo", ["Lib.A", "Lib.B"]));

        FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string>
        {
            ["Lib.A"] = "Lib/A.mo",
        });

        Assert.Equal(GraphBuilder.GenerateFileId("Lib/A.mo"), FileOf(graph, "Lib.A"));
    }

    [Fact]
    public void TheNewFileIsCreatedIfItIsNotThereYet()
    {
        var oldId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var graph = GraphWith((oldId, "Lib/package.mo", ["Lib.A", "Lib.B"]));

        FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string> { ["Lib.A"] = "Lib/A.mo" });

        var newFile = graph.GetNode<FileNode>(GraphBuilder.GenerateFileId("Lib/A.mo"));
        Assert.NotNull(newFile);
        Assert.Contains("Lib.A", newFile!.ContainedModelIds);
    }

    [Fact]
    public void TheOldFileNoLongerClaimsTheClass()
    {
        // Both halves matter: the file's own list and the edge. A file that still lists a class it
        // no longer holds makes that class appear in two files at once — which is how an encrypted
        // package came to claim classes whose real source had been loaded over it.
        //
        // The detach itself belongs to DirectedGraph.AddFileContainsModel, not here. This asserts
        // the invariant rather than the mechanism, which is why it holds either way.
        var oldId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var graph = GraphWith((oldId, "Lib/package.mo", ["Lib.A", "Lib.B"]));

        FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string> { ["Lib.A"] = "Lib/A.mo" });

        var oldFile = graph.GetNode<FileNode>(oldId);
        Assert.NotNull(oldFile);
        Assert.DoesNotContain("Lib.A", oldFile!.ContainedModelIds);
        Assert.DoesNotContain("Lib.A", graph.GetModelsInFile(oldId).Select(m => m.Id));
    }

    [Fact]
    public void TheOldFileKeepsTheClassesThatDidNotMove()
    {
        var oldId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var graph = GraphWith((oldId, "Lib/package.mo", ["Lib.A", "Lib.B"]));

        FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string> { ["Lib.A"] = "Lib/A.mo" });

        Assert.Equal(oldId, FileOf(graph, "Lib.B"));
    }

    [Fact]
    public void AClassWrittenBackToTheSameFile_IsLeftAlone()
    {
        var fileId = GraphBuilder.GenerateFileId("Lib/A.mo");
        var graph = GraphWith((fileId, "Lib/A.mo", ["Lib.A"]));

        var moved = FileNodeReconciler.ReassignModels(
            graph, new Dictionary<string, string> { ["Lib.A"] = "Lib/A.mo" });

        Assert.Empty(moved);
        Assert.Equal(fileId, FileOf(graph, "Lib.A"));
    }

    [Fact]
    public void AClassTheGraphDoesNotHold_IsSkipped()
    {
        // Removed while the save was in flight. There is nothing to move and nothing to fail over.
        var fileId = GraphBuilder.GenerateFileId("Lib/A.mo");
        var graph = GraphWith((fileId, "Lib/A.mo", ["Lib.A"]));

        var moved = FileNodeReconciler.ReassignModels(
            graph, new Dictionary<string, string> { ["Lib.Gone"] = "Lib/Gone.mo" });

        Assert.Empty(moved);
    }

    [Fact]
    public void AFileLeftHoldingNothing_IsRemoved()
    {
        // The original single file whose classes were restructured into a directory of their own.
        // Left in place it shows up as an empty file in the browser and as a candidate in every
        // walk over the graph.
        var oldId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var graph = GraphWith((oldId, "Lib/package.mo", ["Lib.A"]));

        FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string> { ["Lib.A"] = "Lib/A.mo" });

        Assert.Null(graph.GetNode<FileNode>(oldId));
    }

    [Fact]
    public void EveryClassOfAFile_CanMoveToItsOwn()
    {
        // A package split entirely into standalone files: every class leaves, and the package file
        // goes with them.
        var oldId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var graph = GraphWith((oldId, "Lib/package.mo", ["Lib.A", "Lib.B"]));

        var moved = FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string>
        {
            ["Lib.A"] = "Lib/A.mo",
            ["Lib.B"] = "Lib/B.mo",
        });

        Assert.Equal(["Lib.A", "Lib.B"], moved.OrderBy(x => x));
        Assert.Null(graph.GetNode<FileNode>(oldId));
        Assert.Equal(GraphBuilder.GenerateFileId("Lib/A.mo"), FileOf(graph, "Lib.A"));
        Assert.Equal(GraphBuilder.GenerateFileId("Lib/B.mo"), FileOf(graph, "Lib.B"));
    }

    [Fact]
    public void ClassesCanBeFoldedBackIntoOneFile()
    {
        // The other direction: a class that may no longer be stored standalone goes back into its
        // parent package, and its own file disappears.
        var packageId = GraphBuilder.GenerateFileId("Lib/package.mo");
        var standaloneId = GraphBuilder.GenerateFileId("Lib/A.mo");
        var graph = GraphWith(
            (packageId, "Lib/package.mo", ["Lib"]),
            (standaloneId, "Lib/A.mo", ["Lib.A"]));

        FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string>
        {
            ["Lib.A"] = "Lib/package.mo",
        });

        Assert.Equal(packageId, FileOf(graph, "Lib.A"));
        Assert.Null(graph.GetNode<FileNode>(standaloneId));
    }

    [Fact]
    public void NothingToDo_ChangesNothing()
    {
        var fileId = GraphBuilder.GenerateFileId("Lib/A.mo");
        var graph = GraphWith((fileId, "Lib/A.mo", ["Lib.A"]));

        var moved = FileNodeReconciler.ReassignModels(graph, new Dictionary<string, string>());

        Assert.Empty(moved);
        Assert.NotNull(graph.GetNode<FileNode>(fileId));
        Assert.Equal(fileId, FileOf(graph, "Lib.A"));
    }
}
