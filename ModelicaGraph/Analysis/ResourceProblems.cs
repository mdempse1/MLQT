using ModelicaGraph.DataTypes;
using ModelicaGraph.Interfaces;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>What is wrong with one reference to an external resource.</summary>
public enum ResourceProblemKind
{
    /// <summary>The reference resolved, and nothing is there.</summary>
    Missing,

    /// <summary>The reference is an absolute file-system path, which only one machine has.</summary>
    AbsolutePath,
}

/// <summary>One problem with one reference: which class makes it, as written, and where it led.</summary>
/// <param name="ResolvedPath">Where the reference resolved to on disk.</param>
public sealed record ResourceProblem(
    ResourceProblemKind Kind,
    string ModelId,
    string RawPath,
    string ResolvedPath,
    ResourceReferenceType ReferenceType);

/// <summary>
/// The one answer to "is this resource reference a problem, and which": read by
/// <see cref="ExternalResourceAnalyzer"/> for the <c>MLQT.Resource.*</c> findings, and by
/// <c>ExternalResourceService</c> for the External Resources tab's warnings.
/// </summary>
/// <remarks>
/// <para><b>One answer because there are two readers.</b> A rule and a page that each decided for
/// themselves which references were missing would come to disagree, and the place a user looks
/// would contradict the place a build fails.</para>
///
/// <para><b>Existence is read from the disk when it is asked</b>, and written back onto the node so
/// every reader sees the same answer. A node records whether its file existed when the node was
/// created, and the graph hands back that node for as long as anything references the path. So in a
/// session where a file was restored, the stored answer said "missing" until the project was
/// reloaded (B521). Reading it again costs one <c>File.Exists</c> per reference in the checked set.</para>
///
/// <para><b>A reference that did not resolve is not here at all</b>: the graph build makes no node
/// for a <c>modelica://</c> URI into a library that is not loaded. That is the "cannot tell" of the
/// roadmap's three-state rule, and it must never become a finding.</para>
/// </remarks>
public static class ResourceProblems
{
    /// <summary>
    /// Whether the resource is there now, read from disk and recorded on the node.
    /// </summary>
    public static bool Exists(IGraphNode node)
    {
        switch (node)
        {
            case ResourceFileNode file:
                return file.FileExists = File.Exists(file.ResolvedPath);
            case ResourceDirectoryNode directory:
                return directory.DirectoryExists = Directory.Exists(directory.ResolvedPath);
            default:
                return false;
        }
    }

    /// <summary>
    /// The problems with one reference: none, a missing resource, an absolute path, or both.
    /// </summary>
    public static IEnumerable<ResourceProblem> Of(DirectedGraph graph, ResourceEdge edge)
    {
        if (IsMadeByANestedClass(graph, edge))
            yield break;

        var node = graph.GetNode(edge.ResourceNodeId);
        var resolved = node switch
        {
            ResourceFileNode file => file.ResolvedPath,
            ResourceDirectoryNode directory => directory.ResolvedPath,
            _ => null,
        };
        if (node is null || resolved is null)
            yield break;

        if (!Exists(node))
            yield return new ResourceProblem(ResourceProblemKind.Missing, edge.ModelId, edge.RawPath, resolved, edge.ReferenceType);

        if (edge.IsAbsolutePath)
            yield return new ResourceProblem(ResourceProblemKind.AbsolutePath, edge.ModelId, edge.RawPath, resolved, edge.ReferenceType);
    }

    /// <summary>
    /// True when a class nested inside the edge's class makes the same reference, which then belongs
    /// to that class and not to this one.
    /// </summary>
    /// <remarks>
    /// <para>A package stored in one file carries the source of the classes nested in it, and the
    /// graph build reads resources from the whole of it, deliberately (<c>ModelAnalyzer</c> descends
    /// into nested classes so extraction keeps running there). Each nested class records its own
    /// references too. So a reference written in <c>Lib.Reader</c> is an edge from <c>Lib</c> as well,
    /// and the package would be reported for a file it never names, once for every level it is
    /// nested in. The innermost class that makes the reference is the one that has the problem.</para>
    ///
    /// <para>Asked of the edges to the same resource, which are few, rather than of the class's
    /// nested classes, which the graph does not index.</para>
    /// </remarks>
    internal static bool IsMadeByANestedClass(DirectedGraph graph, ResourceEdge edge)
    {
        var prefix = edge.ModelId + ".";
        return graph.GetModelEdgesToResource(edge.ResourceNodeId).Any(other =>
            other.ModelId.StartsWith(prefix, StringComparison.Ordinal)
            && other.ReferenceType == edge.ReferenceType
            && string.Equals(other.RawPath, edge.RawPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every problem with the references one class makes, each reported once: a class that names
    /// the same file twice in the same way has one problem with it, not two.
    /// </summary>
    public static IReadOnlyList<ResourceProblem> For(DirectedGraph graph, string modelId) =>
        graph.GetResourceEdgesForModel(modelId)
             .SelectMany(edge => Of(graph, edge))
             .DistinctBy(p => (p.Kind, p.RawPath, p.ReferenceType))
             .ToList();
}
