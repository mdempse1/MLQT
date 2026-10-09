using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Puts a read-only library's classes into a graph. The only route in for either kind of
/// <see cref="IReadOnlyClassSource"/>, so every class from one arrives marked
/// <see cref="ModelNode.IsExternalStub"/>, opening with its source's provenance note, and in a file
/// <see cref="ReadOnlySources.IsReadOnlyPath"/> refuses to write.
/// </summary>
public static class ReadOnlySourceLoader
{
    /// <summary>Files parsed between collection hints, as <see cref="GraphBuilder.LoadModelicaFiles"/> does.</summary>
    private const int BatchSize = 200;

    /// <summary>
    /// Adds the classes in <paramref name="content"/> to <paramref name="graph"/>.
    /// </summary>
    /// <param name="graph">Graph to populate.</param>
    /// <param name="source">The source the content was read from: its name, version, location and note.</param>
    /// <param name="content">What <see cref="IReadOnlyClassSource.Read"/> returned.</param>
    /// <param name="cancellationToken">Observed once, before any class is added, and not after: a load
    /// that has started finishes. Stopping half way would leave classes in the graph that no library
    /// owns - and taking them out again is not enough, because a supplied class may already have
    /// replaced a recovered one, which would then be gone from the library it belongs to.</param>
    /// <returns>The ids of the classes added, and how many were left to a copy that outranks them.</returns>
    /// <exception cref="ArgumentException">The content does not match the source's kind, or the
    /// source names a location that could be written: a recovered source's must be a read-only path
    /// such as its <c>package.moe</c>, and a supplied source's, when it names one, must be in memory.</exception>
    public static ReadOnlySourceLoad Load(
        DirectedGraph graph,
        IReadOnlyClassSource source,
        ReadOnlySourceContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        return source.Kind switch
        {
            ReadOnlySourceKind.RecoveredFromDocumentation => LoadRecovered(graph, source, content),
            ReadOnlySourceKind.Supplied => LoadSupplied(graph, source, content),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source.Kind, "Unknown read-only source kind.")
        };
    }

    private static ReadOnlySourceLoad LoadRecovered(
        DirectedGraph graph, IReadOnlyClassSource source, ReadOnlySourceContent content)
    {
        if (content.Texts.Count > 0)
            throw new ArgumentException("A source recovered from documentation supplies no Modelica text.", nameof(content));
        if (source.Location is null)
            throw new ArgumentException(
                "A source recovered from documentation names the encrypted package its classes stand for.",
                nameof(source));
        if (!ReadOnlySources.IsReadOnlyPath(source.Location))
            throw new ArgumentException(
                $"A read-only source cannot live at '{source.Location}': every write path takes a class's " +
                "file path at face value, so it must be a path MLQT refuses to write.", nameof(source));

        var ids = ExternalStubBuilder.AddDocumentedClasses(
            graph, content.Documented, source.Location, out var superseded, source.LibraryVersion,
            source.ProvenanceNote);
        return new ReadOnlySourceLoad(ids, superseded);
    }

    private static ReadOnlySourceLoad LoadSupplied(
        DirectedGraph graph, IReadOnlyClassSource source, ReadOnlySourceContent content)
    {
        if (content.Documented.Count > 0)
            throw new ArgumentException("A supplied source supplies Modelica text, not documentation.", nameof(content));
        if (source.Location is { } location && !ReadOnlySources.IsInMemoryPath(location))
            throw new ArgumentException(
                $"A supplied source's files are held in memory, so its location must start with " +
                $"'{ReadOnlySources.InMemoryPathPrefix}', not be '{location}'.", nameof(source));

        var fileLoad = new ReadOnlyFileLoad(
            ReadOnlySources.Banner(source.ProvenanceNote), source.LibraryName, new ConcurrentBag<string>());
        var root = source.Location ?? ReadOnlySources.InMemoryRoot(source.LibraryName);
        var classTexts = content.Texts.Where(t => !IsPackageOrder(t.RelativePath)).ToList();
        var ids = new ConcurrentBag<string>();
        var superseded = 0;

        for (var start = 0; start < classTexts.Count; start += BatchSize)
        {
            var batch = classTexts.Skip(start).Take(BatchSize);
            Parallel.ForEach(batch, text =>
            {
                var loaded = GraphBuilder.LoadModelicaFile(graph, PathOf(root, text.RelativePath), text.Text, fileLoad);
                foreach (var id in loaded)
                {
                    // Left in the graph only where nothing outranked it: AddNode keeps readable source
                    // over a supplied class, and that class is not this library's to list or remove.
                    if (graph.GetNode<ModelNode>(id) is { IsExternalStub: true, RecoveredFromDocumentation: null })
                        ids.Add(id);
                    else
                        Interlocked.Increment(ref superseded);
                }
            });
        }

        // Package order travels as package.order text beside the package.mo it orders, as on disk.
        foreach (var order in content.Texts.Where(t => IsPackageOrder(t.RelativePath)))
            ApplyPackageOrder(graph, root, order);

        return new ReadOnlySourceLoad(ids.Distinct().ToList(), superseded)
        {
            Refused = fileLoad.Refused.Distinct().Order(StringComparer.Ordinal).ToList()
        };
    }

    private static string PathOf(string root, string relativePath) =>
        root + "/" + relativePath.Replace('\\', '/').TrimStart('/');

    private static bool IsPackageOrder(string relativePath) =>
        relativePath.Replace('\\', '/').Split('/')[^1].Equals("package.order", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stores a <c>package.order</c> on the package its sibling <c>package.mo</c> defines: the class
    /// in that file whose parent is not in it. Applies to that package only, as on disk.
    /// </summary>
    private static void ApplyPackageOrder(DirectedGraph graph, string root, SuppliedText order)
    {
        var relative = order.RelativePath.Replace('\\', '/');
        var packageMo = relative[..(relative.LastIndexOf('/') + 1)] + "package.mo";
        var fileId = GraphBuilder.GenerateFileId(PathOf(root, packageMo));
        var inFile = graph.GetModelsInFile(fileId).ToList();
        var package = inFile.FirstOrDefault(m =>
            m.ClassType == "package" && !inFile.Any(other => other.Id == m.ParentModelName));
        if (package is null)
            return;

        package.PackageOrder = order.Text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
