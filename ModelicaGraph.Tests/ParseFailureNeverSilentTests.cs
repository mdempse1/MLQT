using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That a file which fails to parse leaves something behind — a node, and a diagnostic on it.
///
/// <para><b>What broke (B201).</b> <c>LoadModelicaFile</c> made a placeholder only when
/// <c>hasFatal &amp;&amp; models.Count == 0</c>. A file whose syntax the grammar rejects without the
/// visitor crashing records <see cref="ParserErrorSeverity.RecoveredSyntax"/> errors and extracts
/// zero models, so <c>hasFatal</c> was false, no placeholder was made, and the errors collected in
/// <c>fileParserErrors</c> were dropped because there was no node to attach them to. A real library
/// with one malformed file lost those classes and nobody was told.</para>
///
/// <para><b>Why the existing tests did not catch it.</b>
/// <c>GraphBuilderTests.LoadModelicaFile_UnparseableContent_CreatesPlaceholderWithFullSource</c>
/// wrapped its assertions in <c>if (placeholder != null)</c> and recorded that producing nothing
/// "is still acceptable — no crash reached the caller". That is exactly the B201 outcome, excused by
/// the test written to cover the machinery that prevents it. The guard is gone and the assertions in
/// this file are unconditional.</para>
///
/// <para>The invariant is the one <c>LoadModelicaFile</c>'s own catch block already claims: "no file
/// ever disappears silently from the library tree". It is asserted here for every way a file can
/// fail, not only for the way that throws.</para>
/// </summary>
public class ParseFailureNeverSilentTests
{
    private static string Path_(string name) =>
        Path.Combine(Path.GetTempPath(), "mlqt-parse-failure", name);

    /// <summary>
    /// The shape found in ModelicaTools: a top-level <c>import</c> between the <c>within</c> clause
    /// and the class. <c>stored_definition</c> (modelica.g4:43) admits only <c>within</c> followed by
    /// class definitions, so there is no valid parse and no recovery alternative — yet nothing throws.
    /// </summary>
    private const string ImportAtFileScope = """
        within MyLib.Sub;
        import MyLib.Types.Temperature;

        model Affected
          Real x;
        end Affected;
        """;

    [Fact]
    public void AFileWithATopLevelImport_StillYieldsANode()
    {
        var graph = new DirectedGraph();

        var modelIds = GraphBuilder.LoadModelicaFile(graph, Path_("Affected.mo"), ImportAtFileScope);

        Assert.NotEmpty(modelIds);
        Assert.NotEmpty(graph.ModelNodes);
    }

    [Fact]
    public void AFileWithATopLevelImport_IsReportedRatherThanDropped()
    {
        // The half that matters most: a node with no diagnostic on it is a class that looks fine.
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path_("Affected.mo"), ImportAtFileScope);

        var placeholder = Assert.Single(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.NotEmpty(placeholder.Definition.ParserErrors);
    }

    [Fact]
    public void AFileWithATopLevelImport_KeepsItsSourceSoItCanBeFixed()
    {
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path_("Affected.mo"), ImportAtFileScope);

        var placeholder = Assert.Single(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.Contains("import MyLib.Types.Temperature;", placeholder.Definition.ModelicaCode);
        Assert.Contains("model Affected", placeholder.Definition.ModelicaCode);
    }

    [Fact]
    public void AFileWithATopLevelImport_TakesItsIdFromTheWithinClause()
    {
        // The within clause survives even when the class body does not, and the placeholder id is
        // what makes the library tree, the Issues table and "only this model" line up.
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path_("Affected.mo"), ImportAtFileScope);

        var placeholder = Assert.Single(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.Equal("MyLib.Sub.Affected", placeholder.Id);
    }

    [Fact]
    public void OutrightGarbage_StillYieldsAReportedNode()
    {
        // The case the old test covered conditionally. Asserted unconditionally here.
        var graph = new DirectedGraph();
        const string garbage = "this is not modelica at all @@@ {{ ::::";

        GraphBuilder.LoadModelicaFile(graph, Path_("Garbage.mo"), garbage);

        var placeholder = Assert.Single(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.NotEmpty(placeholder.Definition.ParserErrors);
        Assert.Equal(garbage, placeholder.Definition.ModelicaCode);
    }

    [Theory]
    // A stray closing keyword with no opening.
    [InlineData("within A.B;\n\nend Nothing;")]
    // A within clause and then something that is not a class at all.
    [InlineData("within A.B;\n\n@@@")]
    public void AnythingThatParsesToNoClasses_IsReported(string content)
    {
        // The rule, rather than a list of shapes: errors recorded and no classes extracted means the
        // file is unusable, whatever severity the parser chose to record.
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path_("Various.mo"), content);

        var placeholder = Assert.Single(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.NotEmpty(placeholder.Definition.ParserErrors);
    }

    [Fact]
    public void AValidFileGetsNoPlaceholder()
    {
        // The counterpart that keeps the fix from being "always make a placeholder".
        var graph = new DirectedGraph();
        const string valid = """
            within MyLib.Sub;

            model Fine
              Real x;
            end Fine;
            """;

        GraphBuilder.LoadModelicaFile(graph, Path_("Fine.mo"), valid);

        Assert.DoesNotContain(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.Single(graph.ModelNodes);
    }

    [Fact]
    public void AClassTheParserSalvaged_KeepsItsOwnNodeAndItsDiagnostic()
    {
        // The boundary the fix draws, and the case RecoveredSyntax was actually named for: the
        // parser recovered, a class came out, and the diagnostic belongs on that class. A
        // placeholder here would be a regression — it would replace a class the user can navigate to
        // with an opaque stand-in for the whole file.
        //
        // `model Half` is truncated before its `end`, and the extractor salvages it. This is the
        // input that showed the first draft of these tests was asserting the wrong thing: it was
        // written expecting a placeholder, and the code was right.
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path_("Half.mo"), "within A.B;\n\nmodel Half\n  Real x");

        Assert.DoesNotContain(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        var salvaged = Assert.Single(graph.ModelNodes, m => m.Name == "Half");
        Assert.Equal("A.B.Half", salvaged.Id);
        Assert.NotEmpty(salvaged.Definition.ParserErrors);
    }

    [Fact]
    public void AFileWhoseClassesParsedIsNotReplacedByAPlaceholder()
    {
        var graph = new DirectedGraph();
        const string good = """
            within MyLib.Sub;

            model Good
              Real x;
            end Good;
            """;

        GraphBuilder.LoadModelicaFile(graph, Path_("Mixed.mo"), good);

        Assert.DoesNotContain(graph.ModelNodes, m => m.IsParseFailurePlaceholder);
        Assert.Contains(graph.ModelNodes, m => m.Name == "Good");
    }
}
