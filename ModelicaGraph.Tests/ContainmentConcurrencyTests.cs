using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Files and their classes are wired up from parallel loading threads. A class that moves between
/// files touches the set belonging to the file it leaves — a set another thread may be adding to for
/// a file of its own — so these changes have to be serialized. Two threads inside one HashSet do not
/// merely race for an outcome: they corrupt it, and a corrupted set can spin forever on the next
/// lookup, which presents as a load that never finishes rather than as an error.
/// </summary>
public class ContainmentConcurrencyTests
{
    [Fact]
    public void ClassesMovingBetweenFilesFromManyThreads_LeaveTheGraphConsistent()
    {
        var graph = new DirectedGraph();
        const int files = 8;
        const int classes = 250;

        for (var f = 0; f < files; f++)
            graph.AddNode(new FileNode($"f{f}", $"P{f}.mo"));

        for (var c = 0; c < classes; c++)
        {
            graph.AddNode(new ModelNode($"P.C{c}", $"C{c}", $"model C{c}\nend C{c};"));
            graph.AddFileContainsModel("f0", $"P.C{c}");
        }

        // Every class is claimed by every file in turn, from every thread at once.
        Parallel.For(0, files, f =>
        {
            for (var c = 0; c < classes; c++)
                graph.AddFileContainsModel($"f{f}", $"P.C{c}");
        });

        // Whichever file won each class, the graph agrees with itself: a class is in exactly one
        // file's list, and that is the file the class names.
        for (var c = 0; c < classes; c++)
        {
            var id = $"P.C{c}";
            var owner = graph.GetNode<ModelNode>(id)!.ContainingFileId;
            Assert.NotNull(owner);

            var claiming = Enumerable.Range(0, files)
                .Select(f => graph.GetNode<FileNode>($"f{f}")!)
                .Where(file => file.ContainedModelIds.Contains(id))
                .Select(file => file.Id)
                .ToList();

            Assert.Equal([owner], claiming);
        }
    }

    [Fact]
    public void ReadingAFilesClassesWhileTheyMove_DoesNotThrow()
    {
        // The read used to hand back a query over the live set, so the caller enumerated it whenever
        // it liked — including while a loading thread was still moving classes about.
        var graph = new DirectedGraph();
        graph.AddNode(new FileNode("f0", "P0.mo"));
        graph.AddNode(new FileNode("f1", "P1.mo"));

        for (var c = 0; c < 200; c++)
        {
            graph.AddNode(new ModelNode($"P.C{c}", $"C{c}", $"model C{c}\nend C{c};"));
            graph.AddFileContainsModel("f0", $"P.C{c}");
        }

        var mover = Task.Run(() =>
        {
            for (var round = 0; round < 20; round++)
                for (var c = 0; c < 200; c++)
                    graph.AddFileContainsModel(round % 2 == 0 ? "f1" : "f0", $"P.C{c}");
        });

        while (!mover.IsCompleted)
            _ = graph.GetModelsInFile("f0").ToList();

        mover.GetAwaiter().GetResult();   // rethrows anything the mover hit
    }
}
