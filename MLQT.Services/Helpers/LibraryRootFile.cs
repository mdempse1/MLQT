using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// The file an external tool has to open before it can see a class.
/// </summary>
/// <remarks>
/// <para><b>Not the class's own file.</b> A class stored at
/// <c>MSL\Modelica\Blocks\Continuous\Integrator.mo</c> is only <c>Modelica.Blocks.Continuous.
/// Integrator</c> because of the package structure above it, and a tool handed that file alone has
/// no way to know: it sees a class called <c>Integrator</c>, with nothing to resolve its
/// <c>within</c> clause against. OpenModelica refuses to load it. Dymola accepts it and then
/// discovers the enclosing package itself, which is why the same code worked for one tool and not
/// the other (B170).</para>
///
/// <para>The answer is the library's own <c>package.mo</c> — for that example,
/// <c>MSL\Modelica\package.mo</c> — after which the tool is asked about the class by its full name.
/// </para>
/// </remarks>
public static class LibraryRootFile
{
    /// <summary>
    /// The file to open so <paramref name="model"/> can be checked, or null when nothing in the
    /// graph says where it lives.
    /// </summary>
    /// <remarks>
    /// Resolved through the graph rather than by walking directories: the graph already knows which
    /// file each class came from, and the top-level package is just the first segment of the class's
    /// own name. A library that is one file, or a class with no enclosing package, resolves to its
    /// own file — which is the right answer for both.
    /// </remarks>
    public static string? For(DirectedGraph graph, ModelNode model)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);

        var ownFile = FileOf(graph, model.Id);

        var dot = model.Id.IndexOf('.');
        if (dot <= 0)
            return ownFile;   // already top level

        var rootId = model.Id[..dot];
        return FileOf(graph, rootId) ?? ownFile;
    }

    private static string? FileOf(DirectedGraph graph, string modelId)
    {
        var node = graph.GetNode<ModelNode>(modelId);
        if (node?.ContainingFileId is not { } fileId)
            return null;

        var path = graph.GetNode<FileNode>(fileId)?.FilePath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
