using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Where a class sits on disk, for a tool that reports a class-relative line and wants to give the
/// file line beside it - mlqt_list_findings and mlqt_validate_class_references.
/// </summary>
internal static class ClassFileLocation
{
    /// <summary>
    /// Where a class sits on disk and how its lines map to the file's, or null when that is unknown
    /// (a class with no file, a snippet). Built per class rather than through
    /// <c>ClassLocation.ForGraph</c>, which walks every file in the graph - too much work when a
    /// reference set of tens of thousands of classes is loaded and a handful have findings.
    /// </summary>
    public static ClassLocation? Of(DirectedGraph graph, ModelNode? node)
    {
        if (node?.ContainingFileId is null)
            return null;

        var file = graph.GetNode<FileNode>(node.ContainingFileId);
        return string.IsNullOrEmpty(file?.FilePath)
            ? null
            : new ClassLocation(file.FilePath, node.StartLine, node.SourceMatchesFile, node.TrimElision);
    }
}
