using System.Text.RegularExpressions;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Tests;

/// <summary>
/// That the graph build finds external resources by the same rules as
/// <see cref="ExternalResourceExtractor"/> (B511).
///
/// <para><b>Why this is a test.</b> <c>ModelAnalyzer</c> - the visitor the graph build runs - held
/// its own copy of the extractor's rules, and only tests constructed the extractor. B209 was fixed
/// in the extractor, its tests went green, and the graph build went on using the copy. The copy is
/// gone: <c>ModelAnalyzer</c> derives from the extractor. These tests hold that in place, and run
/// every case the extractor's own tests pin through the production visitor.</para>
/// </summary>
public class ResourceRuleParityTests
{
    private static List<ExternalResourceInfo> ByExtractor(string code)
    {
        var extractor = new ExternalResourceExtractor();
        extractor.Visit(ModelicaParserHelper.Parse(code));
        return extractor.Resources;
    }

    /// <summary>
    /// What the graph build finds, as <c>GraphBuilder</c> constructs the visitor. loadSelector
    /// defaults are left out: they need the graph, so the extractor does not know that rule, and
    /// none of the cases compared here has one.
    /// </summary>
    private static List<ExternalResourceInfo> ByGraphBuild(string code, string modelId = "TestModel")
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode(modelId, modelId, code));
        var analyzer = new ModelAnalyzer(modelId, graph);
        analyzer.Visit(ModelicaParserHelper.Parse(code));
        return analyzer.Resources.Where(r => r.ReferenceType != ResourceReferenceType.LoadSelector).ToList();
    }

    private static List<string> Describe(IEnumerable<ExternalResourceInfo> resources)
        => resources.Select(r => $"{r.ReferenceType}|{r.RawPath}|{r.ParameterName}").ToList();

    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void TheGraphBuildsVisitor_IsTheExtractor()
    {
        Assert.True(typeof(ExternalResourceExtractor).IsAssignableFrom(typeof(ModelAnalyzer)));
    }

    [Fact]
    public void ModelAnalyzer_HoldsNoCopyOfTheResourceRules()
    {
        var root = RepositoryRoot();
        if (root is null)
            return;

        var source = File.ReadAllText(Path.Combine(root, "ModelicaGraph", "ParserVisitors", "ModelAnalyzer.cs"));
        var code = Regex.Replace(source, @"//.*", string.Empty);

        // Each is a rule the extractor owns: the external-clause keys, the loadResource capture and
        // the modelica:// scan.
        Assert.DoesNotContain("override object? VisitComposition", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceReferenceType.ExternalLibrary", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceReferenceType.LoadResource", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceReferenceType.UriReference", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SoleStringLiteral", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelicaUriScanner", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every Modelica snippet in <c>ExternalResourceExtractorTests</c>, read from its source so a case
    /// added there is compared here without a second edit.
    /// </summary>
    [Fact]
    public void EveryExtractorTestCase_IsFoundAlikeByTheGraphBuild()
    {
        var root = RepositoryRoot();
        if (root is null)
            return;

        var source = File.ReadAllText(Path.Combine(root, "ModelicaParser.Tests", "ExternalResourceExtractorTests.cs"));
        var snippets = Regex.Matches(source, @"var code = @""((?:[^""]|"""")*)"";")
            .Select(m => m.Groups[1].Value.Replace("\"\"", "\""))
            .ToList();

        // A regex that stopped matching would make this pass over nothing.
        Assert.True(snippets.Count >= 30, $"Read only {snippets.Count} cases from the extractor's tests");

        foreach (var snippet in snippets)
        {
            var modelId = snippet.Contains("package Tests", StringComparison.Ordinal) ? "Tests" : "TestModel";
            Assert.Equal(Describe(ByExtractor(snippet)), Describe(ByGraphBuild(snippet, modelId)));
        }
    }

    public static TheoryData<string> PinnedCases => new()
    {
        // B209: an entity-quoted image source stops at the file name.
        """model TestModel annotation(Documentation(info="&lt;img src=&quot;modelica://Lib/Resources/Images/foo.png&quot;&gt;")); end TestModel;""",
        // B210: a composed loadResource argument is no path.
        """model TestModel parameter String f = Modelica.Utilities.Files.loadResource("modelica://" + "Lib" + "/package.mo"); end TestModel;""",
        // B446: a leading class annotation does not hide the clause's Library.
        """function TestModel annotation(Inline=true); input Real x; external "C" g(x) annotation(Library="lib"); end TestModel;""",
        // The short form is not captured as a resource by either.
        """model TestModel parameter String f = loadResource("modelica://Lib/Resources/a.txt"); end TestModel;""",
    };

    [Theory]
    [MemberData(nameof(PinnedCases))]
    public void PinnedCases_AreFoundAlike(string code)
    {
        Assert.Equal(Describe(ByExtractor(code)), Describe(ByGraphBuild(code)));
    }

    [Fact]
    public void TheGraphBuild_TreatsAnEntityQuotedUriAsText()
    {
        // HTML shown as text, not an image: see ModelicaUriScanner.IsShownAsText.
        var found = ByGraphBuild(
            """model TestModel annotation(Documentation(info="&lt;img src=&quot;modelica://Lib/Resources/Images/foo.png&quot;&gt;")); end TestModel;""");

        Assert.Empty(found);
    }

    [Fact]
    public void TheGraphBuild_CapturesALoneLoadResourceArgumentOnce()
    {
        var found = ByGraphBuild(
            """model TestModel parameter String f = Modelica.Utilities.Files.loadResource("modelica://Lib/Resources/a.mat"); end TestModel;""");

        var resource = Assert.Single(found);
        Assert.Equal(ResourceReferenceType.LoadResource, resource.ReferenceType);
        Assert.Equal("modelica://Lib/Resources/a.mat", resource.RawPath);
    }

    [Fact]
    public void TheShortForm_StillMarksALoadResourceParameter()
    {
        // The flag the loadSelector pass 2 relies on, set by ModelAnalyzer itself.
        const string code = """model TestModel parameter String f = loadResource("modelica://Lib/Resources/a.txt"); end TestModel;""";
        var graph = new DirectedGraph();
        var node = new ModelNode("TestModel", "TestModel", code);
        graph.AddNode(node);

        new ModelAnalyzer("TestModel", graph).Visit(ModelicaParserHelper.Parse(code));

        Assert.Contains("f", node.LoadResourceParameters);
    }

    [Fact]
    public void TheFullForm_StillMarksALoadResourceParameter()
    {
        const string code = """model TestModel parameter String f = ModelicaServices.ExternalReferences.loadResource("modelica://Lib/Resources/a.txt"); end TestModel;""";
        var graph = new DirectedGraph();
        var node = new ModelNode("TestModel", "TestModel", code);
        graph.AddNode(node);

        new ModelAnalyzer("TestModel", graph).Visit(ModelicaParserHelper.Parse(code));

        Assert.Contains("f", node.LoadResourceParameters);
    }

    /// <summary>
    /// End to end, through <c>GraphBuilder.AnalyzeDependenciesAsync</c>: an image written with the
    /// documentation's own quotes resolves to the file on disk, and the same image shown as text, with
    /// entity quotes, is no reference at all. B209 took the second for the first; MSL writes 1,261
    /// images the first way and five the second, all of them examples in UsersGuide.Conventions.
    /// </summary>
    [Fact]
    public async Task AnalyzeDependencies_ResolvesAnImage_AndLeavesTheSameImageShownAsTextAlone()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ResParity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var imageDir = Path.Combine(tempDir, "Resources", "Images");
            Directory.CreateDirectory(imageDir);
            File.WriteAllText(Path.Combine(imageDir, "foo.png"), "png");

            const string content = """
                model Lib "A model"
                  annotation (Documentation(info="<img src=\"modelica://Lib/Resources/Images/foo.png\">
                    Written like this: &lt;img src=&quot;modelica://Lib/Resources/Images/example.png&quot;&gt;"));
                end Lib;
                """;

            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, Path.Combine(tempDir, "Lib.mo"), content);

            await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("Lib", tempDir)]);

            var edge = Assert.Single(graph.GetResourceEdgesForModel("Lib"));
            Assert.Equal("modelica://Lib/Resources/Images/foo.png", edge.RawPath);
            var node = Assert.Single(graph.ResourceFileNodes);
            Assert.True(node.FileExists);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
