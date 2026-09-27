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

    // A package file holding two nested classes, where formatting the first one changes how many
    // lines it takes, so everything after it in the file moves (B444). Normalised here because a raw
    // string carries this .cs file's line endings.
    private const string PackageWithTwoClasses = """
        package P
          model A Real x; Real y; end A;
          model B
                Real zeta = 1;
          end B;
                constant Real kappa = 2;
        end P;
        """;

    /// <summary>Writes a <c>package.mo</c> and loads it the way the application does.</summary>
    private (DirectedGraph Graph, string Path) LoadPackage(string source)
    {
        var dir = Path.Combine(_dir, "P");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "package.mo");
        File.WriteAllText(path, source.Replace("\r\n", "\n"));

        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, path, File.ReadAllText(path));
        return (graph, path);
    }

    /// <summary>
    /// The file line a finding on <paramref name="text"/> in a class's stored source is reported at,
    /// through <see cref="MLQT.Services.Checking.ClassLocation"/> - the mapping Findings, SARIF and the
    /// CLI share - and the line the file as written actually has it on.
    /// </summary>
    private static (int Reported, int Actual) Locate(DirectedGraph graph, string path, string modelId, string text)
    {
        var model = graph.GetNode<ModelNode>(modelId)!;
        var lineInClass = model.Definition.ModelicaCode.Replace("\r\n", "\n").Split('\n')
            .Select((line, i) => (line, i)).Single(l => l.line.Contains(text)).i + 1;
        var reported = MLQT.Services.Checking.ClassLocation.ForGraph(graph)[modelId].FileLine(lineInClass);

        var actual = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n')
            .Select((line, i) => (line, i)).Single(l => l.line.Contains(text)).i + 1;
        return (reported, actual);
    }

    [Fact]
    public async Task AClassBelowOneTheFormatChangedTheLengthOf_IsMappedToWhereTheFileNowHasIt()
    {
        // B444. The formatter stored each class's re-rendered code but left its StartLine where the
        // load found it, so a finding in B was reported at a line of the old file.
        var (graph, path) = LoadPackage(PackageWithTwoClasses);

        var written = await IncrementalFormatter.FormatAndWriteAsync(graph, new[] { path }, Formatting());

        Assert.Single(written);
        Assert.NotEqual(PackageWithTwoClasses.Replace("\r\n", "\n"), File.ReadAllText(path).Replace("\r\n", "\n"));
        var (reported, actual) = Locate(graph, path, "P.B", "Real zeta");
        Assert.Equal(actual, reported);
    }

    [Fact]
    public async Task ATrimmedPackage_IsMappedToWhereTheFileNowHasIt_AndStaysTrimmed()
    {
        // The same for a package whose inline children were trimmed out of its stored source: its
        // elision described the file before the format (B444).
        var (graph, path) = LoadPackage(PackageWithTwoClasses);
        PackageCodeTrimmer.TrimStandaloneChildren(graph);
        var package = graph.GetNode<ModelNode>("P")!;
        Assert.NotNull(package.TrimElision);

        await IncrementalFormatter.FormatAndWriteAsync(graph, new[] { path }, Formatting());

        // Still the representation every surface checks: the children are their own nodes.
        Assert.DoesNotContain("model A", package.Definition.ModelicaCode);
        var (reported, actual) = Locate(graph, path, "P", "constant Real kappa");
        Assert.Equal(actual, reported);

        // ...and the Code Review page shows the whole package as the file now has it, sliced at
        // offsets that describe the written file rather than falling back to the trimmed text.
        var shown = ClassSource.For(package, graph).Replace("\r\n", "\n");
        Assert.Contains("model A", shown);
        Assert.Contains(shown, File.ReadAllText(path).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task AFormattedClass_StoresWhatItsFileNowHolds()
    {
        // Every class carries the text a fresh load of the written file would give it, so the Code
        // Review page, the checker and a later reload all agree.
        var (graph, path) = LoadPackage(PackageWithTwoClasses);

        await IncrementalFormatter.FormatAndWriteAsync(graph, new[] { path }, Formatting());

        var reloaded = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(reloaded, path, File.ReadAllText(path));
        foreach (var id in new[] { "P", "P.A", "P.B" })
        {
            var node = graph.GetNode<ModelNode>(id)!;
            var fresh = reloaded.GetNode<ModelNode>(id)!;
            Assert.Equal(fresh.Definition.ModelicaCode, node.Definition.ModelicaCode);
            Assert.Equal(fresh.StartLine, node.StartLine);
            Assert.Equal(fresh.StartIndex, node.StartIndex);
            Assert.True(node.SourceMatchesFile);
        }
    }

    [Fact]
    public async Task AClassWhoseFileCouldNotBeWritten_KeepsTheCodeOnDisk()
    {
        // The B374 shape on this path: the stored code was replaced before the write was attempted,
        // so a file that could not be written left the graph checking code that is on no disk.
        var (graph, path) = LoadPackage(PackageWithTwoClasses);
        var b = graph.GetNode<ModelNode>("P.B")!;
        var before = b.Definition.ModelicaCode;
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var written = await IncrementalFormatter.FormatAndWriteAsync(graph, new[] { path }, Formatting());

            Assert.Empty(written);
            Assert.Equal(before, b.Definition.ModelicaCode);
            var (reported, actual) = Locate(graph, path, "P.B", "Real zeta");
            Assert.Equal(actual, reported);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
