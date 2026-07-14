using ModelicaGraph.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Resolves the "file owner" model for a class — the topmost model stored in the class's containing
/// file, whose ModelicaCode is the complete file slice. Writing changes at the file-owner level and
/// re-rendering rewrites the whole .mo file, matching how the MLQT UI persists single-file edits.
/// </summary>
internal static class ModelFilePersistence
{
    public sealed record FileOwnerContext(ModelNode FileOwner, string FilePath);

    public static FileOwnerContext? ResolveFileOwner(ILibraryDataService libraries, string classId)
    {
        var node = libraries.GetModelById(classId);
        var fileId = node?.ContainingFileId;
        if (fileId is null)
            return null;

        var graph = libraries.CombinedGraph;
        var fileNode = graph.GetNode<FileNode>(fileId);
        if (fileNode is null || string.IsNullOrEmpty(fileNode.FilePath))
            return null;

        // The file owner is the topmost model in the file: it has no parent, or its parent lives in a
        // different file (i.e. this model heads a standalone .mo file within the package hierarchy).
        var fileOwner = graph.GetModelsInFile(fileId).FirstOrDefault(m =>
                string.IsNullOrEmpty(m.ParentModelName)
                || graph.GetNode<ModelNode>(m.ParentModelName)?.ContainingFileId != fileId)
            ?? node!;

        return new FileOwnerContext(fileOwner, fileNode.FilePath);
    }
}
