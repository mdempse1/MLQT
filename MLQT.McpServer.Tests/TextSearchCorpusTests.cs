using System.Diagnostics;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// How long a text search takes over real libraries - the MSL above all, whose search took 31 s while
/// generated libraries of the same size took 2 s - and whether skipping the classes
/// <see cref="TextSearchPrefilter"/> rules out changes any result.
///
/// <para><b>Opt-in</b>, as the fidelity, resolution and supplied-load corpora are: point
/// <c>MLQT_TEXT_SEARCH_CORPUS</c> at one or more libraries (a directory holding a <c>package.mo</c>,
/// or a single <c>.mo</c> file), separated by the platform's path separator (<c>;</c> on Windows).
/// With it unset the test returns at once. It reports the first and the repeat call's times rather
/// than asserting them, and asserts that the search gives exactly what a search parsing every class
/// gives: the same total, the same items, the same snippets.</para>
/// </summary>
public class TextSearchCorpusTests(ITestOutputHelper output)
{
    private const string Variable = "MLQT_TEXT_SEARCH_CORPUS";

    private static readonly string[] Queries =
    [
        "Mat File Format", "table", "PID controller", "heat transfer", "&", "<b>", "Modelica.Blocks",
        "e.g.", "ä", "it's", "x", "file format", "See also",
    ];

    [Fact]
    public async Task ATextSearch_OverRealLibraries()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } list)
            return;

        using var host = new TestHost();
        var load = Stopwatch.StartNew();
        foreach (var path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await host.Libraries.AddLibraryFromPathAsync(path);
        load.Stop();
        var classes = host.Libraries.GetAllModels().ToList();
        output.WriteLine($"Loaded {classes.Count} classes in {load.Elapsed.TotalSeconds:F1} s");

        // How often a package parsed without its inline classes has to be parsed whole after all.
        var all = new TextSearchPrefilter("x", classes);
        var reduced = classes.Select(all.ReducedSource).Where(s => s is not null).ToList();
        var failing = reduced.Count(s => ModelicaParser.Helpers.ModelicaParserHelper.ParseWithErrors(s!).errors.Count > 0);
        output.WriteLine($"{reduced.Count} classes have inline classes; {failing} of them fail to parse without them");

        var search = new SearchTools(host.Libraries);
        var everyClass = new SearchTools(host.Libraries) { UsePrefilter = false };
        foreach (var query in Queries)
        {
            var first = Stopwatch.StartNew();
            var a = ToolAssert.Ok<TextSearchResult>(search.SearchText(query, 200));
            first.Stop();
            var repeat = Stopwatch.StartNew();
            ToolAssert.Ok<TextSearchResult>(search.SearchText(query, 200));
            repeat.Stop();

            var reference = Stopwatch.StartNew();
            var expected = ToolAssert.Ok<TextSearchResult>(everyClass.SearchText(query, 200));
            reference.Stop();

            var filtering = Stopwatch.StartNew();
            var prefilter = new TextSearchPrefilter(query.Trim(), classes);
            var kept = classes.Count(prefilter.MayMatch);
            filtering.Stop();
            output.WriteLine($"'{query}': {a.Total} hits, {kept} classes parsed (prefilter {filtering.Elapsed.TotalSeconds:F2} s); " +
                             $"first {first.Elapsed.TotalSeconds:F2} s, " +
                             $"repeat {repeat.Elapsed.TotalSeconds:F2} s, every class parsed {reference.Elapsed.TotalSeconds:F2} s");

            Assert.Equal(expected.Total, a.Total);
            Assert.Equal(expected.Items, a.Items);
        }
    }

    /// <summary>
    /// The interface search over the same libraries: timed unfiltered, by connectors and by parameters,
    /// and every class's counts held to the count as it was made before the search shared its caches
    /// and ran in parallel - one class at a time, nothing remembered between them.
    /// </summary>
    [Fact]
    public async Task AnInterfaceSearch_OverRealLibraries()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } list)
            return;

        using var host = new TestHost();
        foreach (var path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await host.Libraries.AddLibraryFromPathAsync(path);
        var search = new SearchTools(host.Libraries);

        foreach (var (what, run) in new (string, Func<object>)[]
                 {
                     ("unfiltered", () => search.SearchByInterface(limit: 10)),
                     ("minConnectors 2", () => search.SearchByInterface(minConnectors: 2, limit: 10)),
                     ("models, minParameters 5", () => search.SearchByInterface(classType: "model", minParameters: 5, limit: 10)),
                 })
        {
            var clock = Stopwatch.StartNew();
            var res = ToolAssert.Ok<InterfaceSearchResult>(run());
            clock.Stop();
            output.WriteLine($"interface, {what}: {res.Total} classes in {clock.Elapsed.TotalSeconds:F2} s");
        }

        var all = ToolAssert.Ok<InterfaceSearchResult>(search.SearchByInterface(minConnectors: 0, minParameters: 1, limit: 200));
        var graph = host.Libraries.CombinedGraph;
        var reference = Stopwatch.StartNew();
        var expected = host.Libraries.GetAllModels()
            .Where(m => !m.IsParseFailurePlaceholder)
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => (m.Id, Counts: CountAsBefore(graph, m)))
            .Where(c => c.Counts.Parameters >= 1)
            .ToList();
        reference.Stop();
        output.WriteLine($"counted as before: {reference.Elapsed.TotalSeconds:F2} s");

        Assert.Equal(expected.Count, all.Total);
        Assert.Equal(
            expected.Take(200).Select(e => (e.Id, e.Counts.Parameters, e.Counts.Connectors)),
            all.Items.Select(i => (i.Id, i.ParameterCount, i.ConnectorCount)));
    }

    private static (int Parameters, int Connectors) CountAsBefore(ModelicaGraph.DirectedGraph graph, ModelicaGraph.DataTypes.ModelNode node)
    {
        var parameters = 0;
        var connectors = 0;
        foreach (var m in ModelicaGraph.Analysis.ClassElementResolver.Collect(graph, node, includeProtected: false, includeInherited: true))
        {
            if (m.Element.Kind != ModelicaParser.DataTypes.ClassElementKind.Component)
                continue;
            var typeNode = ModelicaGraph.Analysis.TypeResolver.Resolve(graph, m.OwnerId, m.Element.Type, m.OwnerImports);
            if (m.Element.Causality is not null || typeNode?.ClassType == "connector")
                connectors++;
            else if (m.Element.Variability is "parameter" or "constant")
                parameters++;
        }
        return (parameters, connectors);
    }
}
