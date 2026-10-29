using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>MLQT.Resource.Missing</c> and <c>MLQT.Resource.AbsolutePath</c>, through the graph build that
/// records the references and <see cref="ResourceProblems"/>, which decides them for the rule and the
/// External Resources tab alike.
/// </summary>
public sealed class ExternalResourceAnalyzerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ExtResAnalyzer_" + Guid.NewGuid().ToString("N"));

    public ExternalResourceAnalyzerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static StyleCheckingSettings BothRulesOn() =>
        new() { CheckMissingResources = true, CheckAbsoluteResourcePaths = true };

    /// <summary>A library called TestLib at <paramref name="libraryRoot"/> holding one class.</summary>
    private static async Task<DirectedGraph> BuildAsync(string libraryRoot, string classSource, params string[] otherLibraries)
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(libraryRoot, "package.mo"), classSource);

        var libraries = new List<LibraryInfo> { new("TestLib", libraryRoot) };
        libraries.AddRange(otherLibraries.Select(name => new LibraryInfo(name, Path.Combine(libraryRoot, "..", name))));

        await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);
        return graph;
    }

    private static List<Finding> Analyze(DirectedGraph graph, StyleCheckingSettings settings) =>
        new ExternalResourceAnalyzer()
            .Analyze(new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList(), dependenciesAnalyzed: true))
            .ToList();

    private static string LoadsTable(string uri) => $$"""
        package TestLib
          model Reader
            parameter String file = Modelica.Utilities.Files.loadResource("{{uri}}");
          end Reader;
        end TestLib;
        """;

    [Fact]
    public async Task AFileTheLibraryDoesNotHave_IsMissing_AtTheLineThatNamesIt()
    {
        var graph = await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/Data/table.mat"));

        var finding = Assert.Single(Analyze(graph, BothRulesOn()));

        Assert.Equal(RuleIds.MissingResource, finding.RuleId);
        Assert.Equal("TestLib.Reader", finding.ModelId);
        Assert.Equal("modelica://TestLib/Resources/Data/table.mat", finding.ElementPath);
        Assert.Equal("loadResource", finding.Discriminator);
        Assert.Equal(2, finding.LineNumber);   // Reader's own line 2, its parameter
        Assert.Contains("table.mat", finding.Message);
    }

    [Fact]
    public async Task AFileThatIsThere_IsNotReported()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Resources", "Data"));
        File.WriteAllText(Path.Combine(_root, "Resources", "Data", "table.mat"), "");

        var graph = await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/Data/table.mat"));

        Assert.Empty(Analyze(graph, BothRulesOn()));
    }

    [Fact]
    public async Task AReferenceIntoALibraryThatIsNotLoaded_IsNeverReported()
    {
        // The three-state rule: MLQT cannot resolve it, so whether the file exists says nothing about
        // the library being checked.
        var graph = await BuildAsync(_root, LoadsTable("modelica://OtherLib/Resources/table.mat"));

        Assert.Empty(Analyze(graph, BothRulesOn()));
    }

    [Fact]
    public async Task WithTheRulesOff_NothingIsReported_EvenThroughTheRunner()
    {
        // Off by default until the release decides, like most of the catalogue.
        var graph = await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/Data/table.mat"));

        Assert.Empty(Analyze(graph, new StyleCheckingSettings()));
        Assert.DoesNotContain(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(graph, new StyleCheckingSettings(), graph.ModelNodes.ToList(), dependenciesAnalyzed: true)),
            f => f.RuleId.StartsWith("MLQT.Resource.", StringComparison.Ordinal));
        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.MissingResource));
        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.AbsoluteResourcePath));
    }

    [Fact]
    public async Task TheRunnerReportsIt_AtTheConfiguredSeverity()
    {
        var graph = await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/Data/table.mat"));
        var settings = BothRulesOn();

        var finding = Assert.Single(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList(), dependenciesAnalyzed: true)),
            f => f.RuleId == RuleIds.MissingResource);

        Assert.Equal(RuleSeverity.Error, finding.Severity);
    }

    [Fact]
    public async Task OnlyTheRuleThatIsOn_Reports()
    {
        var path = Path.Combine(_root, "nowhere", "table.mat").Replace('\\', '/');
        var graph = await BuildAsync(_root, LoadsTable(path));

        Assert.Equal([RuleIds.MissingResource],
            Analyze(graph, new StyleCheckingSettings { CheckMissingResources = true }).Select(f => f.RuleId));
        Assert.Equal([RuleIds.AbsoluteResourcePath],
            Analyze(graph, new StyleCheckingSettings { CheckAbsoluteResourcePaths = true }).Select(f => f.RuleId));
    }

    [Fact]
    public async Task AnAbsolutePath_IsNotPortable_AndMissingIfItIsNotThere()
    {
        var path = Path.Combine(_root, "nowhere", "table.mat").Replace('\\', '/');
        var graph = await BuildAsync(_root, LoadsTable(path));

        var rules = Analyze(graph, BothRulesOn()).Select(f => f.RuleId).Order().ToList();

        Assert.Equal([RuleIds.AbsoluteResourcePath, RuleIds.MissingResource], rules);
    }

    [Fact]
    public async Task AnAbsolutePathThatIsThere_IsStillNotPortable()
    {
        var file = Path.Combine(_root, "table.mat");
        File.WriteAllText(file, "");
        var graph = await BuildAsync(_root, LoadsTable(file.Replace('\\', '/')));

        var finding = Assert.Single(Analyze(graph, BothRulesOn()));

        Assert.Equal(RuleIds.AbsoluteResourcePath, finding.RuleId);
    }

    [Fact]
    public async Task ADirectoryAnnotationNamingNothing_IsRecordedAndMissing()
    {
        // Until phase 2 the graph build dropped a directory annotation whose directory did not exist,
        // so neither the tab nor any rule could say so.
        var graph = await BuildAsync(_root, """
            package TestLib
              function f
                input Real x;
                output Real y;
                external "C" y = f(x)
                  annotation(IncludeDirectory="modelica://TestLib/Resources/C-Sources");
              end f;
            end TestLib;
            """);

        var node = Assert.Single(graph.ResourceDirectoryNodes);
        Assert.False(node.DirectoryExists);

        var finding = Assert.Single(Analyze(graph, BothRulesOn()));
        Assert.Equal(RuleIds.MissingResource, finding.RuleId);
        Assert.Equal("includeDirectory", finding.Discriminator);
        Assert.Contains("does not exist", finding.Message);
    }

    [Fact]
    public async Task AHeaderNoIncludeDirectoryHolds_IsMissing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Resources", "Include"));
        var graph = await BuildAsync(_root, """
            package TestLib
              function f
                input Real x;
                output Real y;
                external "C" y = f(x)
                  annotation(Include="#include \"mylib.h\"");
              end f;
            end TestLib;
            """);

        var finding = Assert.Single(Analyze(graph, BothRulesOn()));

        Assert.Equal("include", finding.Discriminator);
        Assert.Contains("mylib.h", finding.Message);
    }

    [Fact]
    public async Task AHeaderTheCompilerSupplies_IsNotReported()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Resources", "Include"));
        var graph = await BuildAsync(_root, """
            package TestLib
              function f
                input Real x;
                output Real y;
                external "C" y = f(x)
                  annotation(Include="#include <math.h>");
              end f;
            end TestLib;
            """);

        Assert.Empty(Analyze(graph, BothRulesOn()));
    }

    [Fact]
    public async Task ALibraryWithNoBinary_IsNotReported()
    {
        // A tool links the system's libraries too, so a Library with nothing under Resources/Library
        // is often normal - "cannot tell", as for a header the compiler supplies.
        Directory.CreateDirectory(Path.Combine(_root, "Resources", "Library"));
        var graph = await BuildAsync(_root, """
            package TestLib
              function f
                input Real x;
                output Real y;
                external "C" y = f(x) annotation(Library="m");
              end f;
            end TestLib;
            """);

        Assert.Empty(Analyze(graph, BothRulesOn()));
    }

    [Fact]
    public async Task AReferenceInANestedClass_IsReportedOnThatClass_NotOnEveryEnclosingOne()
    {
        // A package stored in one file carries its nested classes' source, and the graph build reads
        // resources from all of it - so the package has an edge for Inner.Reader's file, and so does
        // Inner. Only the class that writes the reference has the problem.
        var graph = await BuildAsync(_root, """
            package TestLib
              package Inner
                model Reader
                  parameter String file = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
                end Reader;
              end Inner;
            end TestLib;
            """);
        Assert.True(graph.ResourceEdges.Count() > 1, "the premise: enclosing classes record the reference too");

        var finding = Assert.Single(Analyze(graph, BothRulesOn()));

        Assert.Equal("TestLib.Inner.Reader", finding.ModelId);
    }

    [Fact]
    public async Task APackageThatWritesTheSameReferenceAsItsNestedClass_IsReportedForItsOwn()
    {
        // Matched on the path alone, the package's own loadResource was taken for Reader's and
        // dropped. Its text writes it twice, and Reader accounts for one.
        var graph = await BuildAsync(_root, """
            package TestLib
              constant String table = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
              model Reader
                parameter String file = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
              end Reader;
            end TestLib;
            """);

        var findings = Analyze(graph, BothRulesOn());

        Assert.Equal([("TestLib", 2), ("TestLib.Reader", 2)],
            findings.Select(f => (f.ModelId, f.LineNumber)).OrderBy(p => p.ModelId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AReferenceInANestedClassStoredInItsOwnFile_IsNotThePackages()
    {
        // The package's text cannot hold a class stored in a file of its own, so when both make
        // the reference each is reporting its own.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(_root, "package.mo"), """
            package TestLib
              constant String table = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
            end TestLib;
            """);
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(_root, "Reader.mo"), """
            within TestLib;
            model Reader
              parameter String file = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
            end Reader;
            """);
        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("TestLib", _root)]);

        Assert.Equal(["TestLib", "TestLib.Reader"],
            Analyze(graph, BothRulesOn()).Select(f => f.ModelId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheSameReferenceTwice_IsOneFinding_BecauseTheGraphKeepsOneEdge()
    {
        // Nothing downstream de-duplicates: the graph build records one edge per class, resource and
        // kind of reference, which is what makes this one finding.
        var graph = await BuildAsync(_root, """
            package TestLib
              model Reader
                parameter String a = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
                parameter String b = Modelica.Utilities.Files.loadResource("modelica://TestLib/Resources/t.mat");
              end Reader;
            end TestLib;
            """);

        Assert.Single(graph.GetResourceEdgesForModel("TestLib.Reader"));
        Assert.Single(Analyze(graph, BothRulesOn()), f => f.ModelId == "TestLib.Reader");
    }

    [Fact]
    public async Task AFileThatComesBack_ClearsItsFinding_WithoutRebuildingTheGraph()
    {
        // B521: a node kept its answer from when it was created, so a restored file still read as
        // missing until the project was reloaded. The answer is read from disk when it is asked.
        var graph = await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/t.mat"));
        Assert.Single(Analyze(graph, BothRulesOn()));

        Directory.CreateDirectory(Path.Combine(_root, "Resources"));
        File.WriteAllText(Path.Combine(_root, "Resources", "t.mat"), "");

        Assert.Empty(Analyze(graph, BothRulesOn()));
        Assert.True(Assert.Single(graph.ResourceFileNodes).FileExists);
    }

    [Fact]
    public async Task TheFingerprint_DoesNotDependOnWhereTheLibraryIsCheckedOut()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);

        var here = Assert.Single(Analyze(await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/t.mat")), BothRulesOn()));
        var there = Assert.Single(Analyze(await BuildAsync(elsewhere, LoadsTable("modelica://TestLib/Resources/t.mat")), BothRulesOn()));

        Assert.NotEqual(here.Message, there.Message);    // the message names this machine's path
        Assert.Equal(here.Fingerprint, there.Fingerprint);
    }

    [Fact]
    public async Task ALibraryOpenedFromASecondCheckout_HasOnlyThatCheckoutsReferences()
    {
        // B522: removing a class left its resource edges, so the same class loaded again from another
        // checkout also had the first checkout's - reported missing once that folder was gone. Found
        // by the journey, run after other journeys had loaded a library of the same name.
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        foreach (var checkout in new[] { first, second })
        {
            Directory.CreateDirectory(Path.Combine(checkout, "Resources", "Data"));
            File.WriteAllText(Path.Combine(checkout, "Resources", "Data", "table.mat"), "");
        }

        var graph = await BuildAsync(first, LoadsTable("modelica://TestLib/Resources/Data/table.mat"));
        graph.RemoveNodes(graph.ModelNodes.Select(m => m.Id).ToList());
        Directory.Delete(first, true);

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(second, "package.mo"), LoadsTable("modelica://TestLib/Resources/Data/table.mat"));
        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("TestLib", second)]);

        var edge = Assert.Single(graph.GetResourceEdgesForModel("TestLib.Reader"));
        Assert.StartsWith(second, graph.GetNode<ResourceFileNode>(edge.ResourceNodeId)!.ResolvedPath);
        Assert.Empty(Analyze(graph, BothRulesOn()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingAClass_ForgetsItsResourceReferences(bool oneAtATime)
    {
        var graph = await BuildAsync(_root, LoadsTable("modelica://TestLib/Resources/Data/table.mat"));
        var file = Assert.Single(graph.ResourceFileNodes);
        Assert.Contains("TestLib.Reader", file.ReferencedByModelIds);

        if (oneAtATime)
            graph.RemoveNode("TestLib.Reader");
        else
            graph.RemoveNodes(["TestLib.Reader", "TestLib"]);

        Assert.Empty(graph.GetResourceEdgesForModel("TestLib.Reader"));
        Assert.DoesNotContain("TestLib.Reader", file.ReferencedByModelIds);
    }

    [Fact]
    public void EveryReferenceKind_HasItsOwnWrittenOutName()
    {
        // The discriminator goes into the fingerprint, so it is spelt out rather than taken from the
        // enum, which an obfuscated build renames. A kind added to the enum and not here would fall
        // to "other", and two kinds would share a fingerprint.
        var names = Enum.GetValues<ResourceReferenceType>().Select(ExternalResourceAnalyzer.KindOf).ToList();

        Assert.DoesNotContain("other", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData("a\nb\nc \"x\"", "\"x\"", 3)]
    [InlineData("\"x\"", "\"x\"", 1)]
    [InlineData("a\nb", "absent", 1)]
    [InlineData("", "x", 1)]
    public void TheLine_IsWhereTheReferenceIsFirstWritten(string source, string raw, int expected)
    {
        Assert.Equal(expected, ExternalResourceAnalyzer.LineOf(source, raw));
    }
}
