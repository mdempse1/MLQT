using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Tests for <see cref="ExternalStubBuilder"/> — turning a class recovered from vendor
/// documentation into Modelica that parses and into a graph node that no write path will touch.
/// </summary>
public class ExternalStubBuilderTests
{
    private static DocumentedClass Documented(
        string fullName,
        string? description = null,
        IReadOnlyList<string>? extends = null,
        bool? hasIcon = null,
        string kind = DocumentedClass.KindUnknown,
        IReadOnlyList<string>? children = null) =>
        new(fullName, description, extends, hasIcon, null, kind,
            children ?? [], [], [], [], [], []);

    #region Synthesized source

    [Fact]
    public void SynthesizeSource_ProducesParseableModelica()
    {
        var source = ExternalStubBuilder.SynthesizeSource(Documented(
            "Battery.BMS.Interfaces.BMS", "Interface model for BMS",
            ["Modelica.Blocks.Icons.Block", "DymolaModels.Icons.Elements.Microcontroller"],
            hasIcon: true, kind: DocumentedClass.KindModel));

        var (tree, errors) = ModelicaParserHelper.ParseWithErrors(source);

        Assert.NotNull(tree);
        Assert.Empty(errors);
    }

    [Fact]
    public void SynthesizeSource_CarriesNameDescriptionAndBaseClasses()
    {
        var source = ExternalStubBuilder.SynthesizeSource(Documented(
            "Battery.BMS.Interfaces.BMS", "Interface model for BMS",
            ["Battery.Common.Icons.Boxed"], kind: DocumentedClass.KindModel));

        Assert.Contains("within Battery.BMS.Interfaces;", source);
        Assert.Contains("model BMS \"Interface model for BMS\"", source);
        Assert.Contains("extends Battery.Common.Icons.Boxed;", source);
        Assert.Contains("end BMS;", source);
    }

    [Fact]
    public void SynthesizeSource_TopLevelClass_HasNoWithinClause()
    {
        var source = ExternalStubBuilder.SynthesizeSource(
            Documented("Battery", "Battery library", kind: DocumentedClass.KindPackage));

        Assert.DoesNotContain("within", source);
        Assert.Empty(ModelicaParserHelper.ParseWithErrors(source).Item2);
    }

    [Fact]
    public void SynthesizeSource_EscapesQuotesAndBackslashesInDescriptions()
    {
        var source = ExternalStubBuilder.SynthesizeSource(
            Documented("Lib.Thing", "A \"quoted\" thing with a \\ backslash"));

        Assert.Empty(ModelicaParserHelper.ParseWithErrors(source).Item2);
    }

    [Fact]
    public void SynthesizeSource_QuotedIdentifierName_Parses()
    {
        // Operator overloads are named with quoted identifiers.
        var source = ExternalStubBuilder.SynthesizeSource(
            Documented("Testing.Utilities.Time.DateTime.'<='", "Less or equal",
                kind: DocumentedClass.KindFunction));

        Assert.Contains("function '<=' \"Less or equal\"", source);
        Assert.Empty(ModelicaParserHelper.ParseWithErrors(source).Item2);
    }

    [Fact]
    public void SynthesizeSource_SaysWhatItIs()
    {
        // A stub reads as ordinary Modelica that happens to be nearly empty, which invites a worse
        // conclusion than the truth — that the vendor's class has no parameters, or that MLQT lost
        // them. The header travels with the text, so it survives being copied out of the viewer.
        var source = ExternalStubBuilder.SynthesizeSource(
            Documented("Lib.Thing", "A thing", kind: DocumentedClass.KindModel));

        Assert.StartsWith("//", source);
        Assert.Contains("NOT the vendor's source", source);
        Assert.Contains("encrypted", source);

        // And it must still be a comment, not something the parser trips over.
        var (tree, errors) = ModelicaParserHelper.ParseWithErrors(source);
        Assert.NotNull(tree);
        Assert.Empty(errors);
    }

    #endregion

    #region Icon asymmetry

    [Fact]
    public void SynthesizeSource_DocumentedIcon_EmitsAnIconAnnotation()
    {
        var source = ExternalStubBuilder.SynthesizeSource(Documented("Lib.Thing", hasIcon: true));

        Assert.Contains("Icon(", source);
    }

    [Fact]
    public void SynthesizeSource_DocumentedAsHavingNoIcon_EmitsNone()
    {
        var source = ExternalStubBuilder.SynthesizeSource(Documented("Lib.Thing", hasIcon: false));

        Assert.DoesNotContain("Icon(", source);
    }

    [Fact]
    public void SynthesizeSource_UnknownIconState_EmitsAnIcon()
    {
        // Guessing "no icon" from missing information would make every user class extending this
        // one fail the icon rule — a finding invented out of an absent input. Guessing "has icon"
        // can only suppress one, which is the safe direction for a library we cannot read.
        var source = ExternalStubBuilder.SynthesizeSource(Documented("Lib.Thing", hasIcon: null));

        Assert.Contains("Icon(", source);
    }

    [Fact]
    public void SynthesizedIcon_IsVisibleToTheIconExtractor()
    {
        // The synthesized annotation has to be the real thing, not merely present: the icon rule
        // reaches it through the extractor, and a stub the extractor cannot read is inert.
        var source = ExternalStubBuilder.SynthesizeSource(Documented("Lib.Thing", hasIcon: true));
        var parsed = ModelicaParserHelper.ParseWithErrors(source).Item1;

        Assert.NotNull(parsed);
        Assert.NotNull(IconExtractor.ExtractIconWithInheritance(parsed!)?.Icon);
    }

    #endregion

    #region Graph nodes

    [Fact]
    public void AddDocumentedClasses_MarksEveryNodeAsAnExternalStub()
    {
        var graph = new DirectedGraph();

        ExternalStubBuilder.AddDocumentedClasses(graph,
            [Documented("Lib", kind: DocumentedClass.KindPackage), Documented("Lib.Thing")],
            @"C:\libs\Lib 1.0\package.moe", "1.0");

        Assert.All(graph.ModelNodes, node => Assert.True(node.IsExternalStub));
    }

    [Fact]
    public void AddDocumentedClasses_StampsVersionOnTheRootOnly()
    {
        var graph = new DirectedGraph();

        ExternalStubBuilder.AddDocumentedClasses(graph,
            [Documented("Lib", kind: DocumentedClass.KindPackage), Documented("Lib.Thing")],
            @"C:\libs\Lib 1.0\package.moe", "2.9.0");

        Assert.Equal("2.9.0", graph.GetNode<ModelNode>("Lib")!.Version);
        Assert.Null(graph.GetNode<ModelNode>("Lib.Thing")!.Version);
    }

    [Fact]
    public void AddDocumentedClasses_PointsTheFileNodeAtTheEncryptedPackage()
    {
        var graph = new DirectedGraph();

        ExternalStubBuilder.AddDocumentedClasses(graph,
            [Documented("Lib", kind: DocumentedClass.KindPackage)],
            @"C:\libs\Lib 1.0\package.moe");

        Assert.Equal(@"C:\libs\Lib 1.0\package.moe", Assert.Single(graph.FileNodes).FilePath);
    }

    [Fact]
    public void AddDocumentedClasses_NeverOffersAStubAsStandaloneStorable()
    {
        var graph = new DirectedGraph();

        ExternalStubBuilder.AddDocumentedClasses(graph,
            [Documented("Lib.Thing")], @"C:\libs\Lib 1.0\package.moe");

        Assert.False(graph.GetNode<ModelNode>("Lib.Thing")!.CanBeStoredStandalone);
    }

    [Fact]
    public void AddDocumentedClasses_RecordsChildOrderFromTheDocumentation()
    {
        var graph = new DirectedGraph();

        ExternalStubBuilder.AddDocumentedClasses(graph,
            [Documented("Lib", kind: DocumentedClass.KindPackage, children: ["Lib.B", "Lib.A"])],
            @"C:\libs\Lib 1.0\package.moe");

        Assert.Equal(["B", "A"], graph.GetNode<ModelNode>("Lib")!.PackageOrder!);
    }

    [Fact]
    public void AddDocumentedClasses_NoClasses_AddsNothingAtAll()
    {
        var graph = new DirectedGraph();

        var ids = ExternalStubBuilder.AddDocumentedClasses(graph, [], @"C:\libs\Lib\package.moe");

        Assert.Empty(ids);
        Assert.Empty(graph.ModelNodes);
        Assert.Empty(graph.FileNodes);
    }

    #endregion

    #region Write-path guards

    [Fact]
    public void TrimStandaloneChildren_LeavesStubsAlone()
    {
        // The trimmer rewrites a package's stored source to remove children that live in their own
        // files. A stub's source is our own reconstruction, with nothing inline to remove.
        var graph = new DirectedGraph();
        ExternalStubBuilder.AddDocumentedClasses(graph,
            [
                Documented("Lib", kind: DocumentedClass.KindPackage, children: ["Lib.Thing"]),
                Documented("Lib.Thing")
            ],
            @"C:\libs\Lib 1.0\package.moe");

        var before = graph.GetNode<ModelNode>("Lib")!.Definition.ModelicaCode;
        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.Equal(before, graph.GetNode<ModelNode>("Lib")!.Definition.ModelicaCode);
        Assert.False(graph.GetNode<ModelNode>("Lib")!.ChildrenTrimmed);
    }

    [Fact]
    public void MetricsCalculator_ExcludesStubsFromTheCounts()
    {
        // A vendor's library is neither the user's achievement nor the user's debt.
        var graph = new DirectedGraph();
        ExternalStubBuilder.AddDocumentedClasses(graph,
            [Documented("Lib", kind: DocumentedClass.KindPackage), Documented("Lib.Thing")],
            @"C:\libs\Lib 1.0\package.moe");

        var metrics = Analysis.MetricsCalculator.Compute(graph, graph.ModelNodes.ToList());

        Assert.Equal(0, metrics.TotalClasses);
    }

    #endregion
}
