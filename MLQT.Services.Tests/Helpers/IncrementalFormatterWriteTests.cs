using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="IncrementalFormatter.FormatAndWriteAsync"/> — the half that actually overwrites the
/// user's files, on the path that runs at startup and after every VCS operation.
///
/// <para><see cref="IncrementalFormatterSelectionTests"/> covers which files are chosen. This covers
/// what happens to a chosen file that turns out not to parse, which is a different decision made
/// later and after the file has been read. The guard carries a comment saying why it is there —
/// "reformatting invalid Modelica produces unreliable output, and this overwrites the file in
/// place" — and the whole-solution mutation audit found that deleting its <c>return</c> broke
/// nothing (B225). Nothing had ever handed this method a file it could not parse.</para>
/// </summary>
public class IncrementalFormatterWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-incremental-format", Guid.NewGuid().ToString("N"));

    public IncrementalFormatterWriteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Writes a one-class file and registers it in a graph, as a load would.</summary>
    private (DirectedGraph Graph, string Path) FileHolding(string className, string source)
    {
        var path = Path.Combine(_dir, $"{className}.mo");
        File.WriteAllText(path, source);

        var graph = new DirectedGraph();
        var fileId = GraphBuilder.GenerateFileId(path);
        graph.AddNode(new FileNode(fileId, path));
        graph.AddNode(new ModelNode(className, className, source));
        graph.AddFileContainsModel(fileId, className);
        return (graph, path);
    }

    private static StyleCheckingSettings Formatting() => new() { ApplyFormattingRules = true };

    [Fact]
    public async Task AFileThatDoesNotParse_IsLeftExactlyAsTheUserWroteIt()
    {
        // Badly indented on purpose: the formatter would certainly rewrite this file if it could
        // read it, so "unchanged" cannot pass by the formatter having nothing to do.
        const string Broken = "model Broken\r\n      this is not Modelica\r\n   Real x;\r\nend Broken;\r\n";
        var (graph, path) = FileHolding("Broken", Broken);

        var written = await IncrementalFormatter.FormatAndWriteAsync(graph, new[] { path }, Formatting());

        Assert.Empty(written);
        Assert.Equal(Broken, File.ReadAllText(path));
    }

    [Fact]
    public async Task AFileThatParses_IsFormattedAndWritten()
    {
        // The positive control. Without it, "the file is unchanged" would pass against a formatter
        // that never wrote anything at all — which is exactly how a guard like this comes to be
        // believed without being tested.
        const string Untidy = "model Tidy\r\n      Real x;\r\nend Tidy;\r\n";
        var (graph, path) = FileHolding("Tidy", Untidy);

        var written = await IncrementalFormatter.FormatAndWriteAsync(graph, new[] { path }, Formatting());

        Assert.Single(written);
        Assert.NotEqual(Untidy, File.ReadAllText(path));
        Assert.Contains("Real x;", File.ReadAllText(path));
    }

    [Fact]
    public async Task OneUnparseableFileDoesNotStopTheOthersBeingFormatted()
    {
        // The guard returns out of one file's work, not the whole run. A library where somebody is
        // mid-edit on one file must still get the rest formatted, or a single syntax error silently
        // switches formatting off across the repository.
        const string Broken = "model Broken\r\n   this is not Modelica\r\nend Broken;\r\n";
        const string Untidy = "model Tidy\r\n      Real x;\r\nend Tidy;\r\n";

        var (brokenGraph, brokenPath) = FileHolding("Broken", Broken);
        var tidyPath = Path.Combine(_dir, "Tidy.mo");
        File.WriteAllText(tidyPath, Untidy);
        var tidyId = GraphBuilder.GenerateFileId(tidyPath);
        brokenGraph.AddNode(new FileNode(tidyId, tidyPath));
        brokenGraph.AddNode(new ModelNode("Tidy", "Tidy", Untidy));
        brokenGraph.AddFileContainsModel(tidyId, "Tidy");

        var written = await IncrementalFormatter.FormatAndWriteAsync(
            brokenGraph, new[] { brokenPath, tidyPath }, Formatting());

        Assert.Equal(Broken, File.ReadAllText(brokenPath));
        Assert.Contains(tidyPath, written.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
