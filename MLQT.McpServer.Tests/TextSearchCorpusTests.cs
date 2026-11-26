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

            var prefilter = new TextSearchPrefilter(query.Trim(), classes);
            var kept = classes.Count(prefilter.MayMatch);
            output.WriteLine($"'{query}': {a.Total} hits, {kept} classes parsed; first {first.Elapsed.TotalSeconds:F2} s, " +
                             $"repeat {repeat.Elapsed.TotalSeconds:F2} s, every class parsed {reference.Elapsed.TotalSeconds:F2} s");

            Assert.Equal(expected.Total, a.Total);
            Assert.Equal(expected.Items, a.Items);
        }
    }
}
