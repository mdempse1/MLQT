using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Puts the graph back in step with the disk after a save moved classes between files.
/// </summary>
/// <remarks>
/// <para>Formatting a library is not only a rewrite: a class that may be stored standalone is
/// written to its own file, and one that may not is folded back into its parent package. Either way
/// the class is now in a different file from the one the graph has it in, and every consumer that
/// asks "which file is this class in" — the code viewer, the diff, the finding line mapping — gets
/// the old answer until this runs.</para>
///
/// <para>Lifted out of <c>MainLayout</c> in phase 7a-4, where it could only be reached by running a
/// full library save.</para>
/// </remarks>
public static class FileNodeReconciler
{
    /// <summary>
    /// Moves each named class onto the file it was written to, and drops any file left holding
    /// nothing.
    /// </summary>
    /// <param name="graph">The loaded graph, updated in place.</param>
    /// <param name="modelIdToFilePath">Where each class ended up, as the saver reported it.</param>
    /// <returns>The classes that actually moved.</returns>
    public static IReadOnlyList<string> ReassignModels(
        DirectedGraph graph, IReadOnlyDictionary<string, string> modelIdToFilePath)
    {
        var moved = new List<string>();

        foreach (var (modelId, newFilePath) in modelIdToFilePath)
        {
            var newFileId = GraphBuilder.GenerateFileId(newFilePath);

            // A class the saver reported but the graph does not hold: it was removed while the save
            // was in flight, and there is nothing to move.
            if (graph.GetNode<ModelNode>(modelId) is not { } modelNode)
                continue;

            var oldFileId = modelNode.ContainingFileId;
            if (oldFileId == newFileId)
                continue;

            if (graph.GetNode<FileNode>(newFileId) is null)
                graph.AddNode(new FileNode(newFileId, newFilePath));

            // Detaching from the old file is deliberately not done here. AddFileContainsModel does
            // it — a class lives in one file, and that is DirectedGraph's rule to keep, stated in as
            // many words on the method and naming this very case. MainLayout carried its own copy of
            // the detach, which is the "one rule in two places" shape this repository keeps finding;
            // mutation testing found it here by removing the copy and changing nothing.
            graph.AddFileContainsModel(newFileId, modelId);
            moved.Add(modelId);
        }

        // Files that ended up holding nothing: the original single file whose classes were
        // restructured into a directory of their own. Left in place they show up as empty files in
        // the browser and as candidates in every walk over the graph.
        foreach (var emptyFile in graph.FileNodes.Where(f => f.ContainedModelIds.Count == 0).ToList())
            graph.RemoveNode(emptyFile.Id);

        return moved;
    }
}
