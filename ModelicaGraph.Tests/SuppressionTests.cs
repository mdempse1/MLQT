using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>Phase 5a: __MLQT vendor-annotation suppression.</summary>
public class SuppressionTests
{
    private static List<Finding> Check(string code, StyleCheckingSettings settings, bool honor = true)
        => StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", code), settings, "TestModel", honorSuppressions: honor);

    private static StyleCheckingSettings ParamRule => new() { ParameterHasDescription = true };

    [Fact]
    public void ClassLevelSuppress_RemovesThatRuleForTheClass()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="Doc.ParameterDescription", reason="legacy"));
            end TestModel;
            """;
        Assert.Empty(Check(code, ParamRule));
    }

    [Fact]
    public void FullRuleId_AlsoMatches()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="MLQT.Doc.ParameterDescription"));
            end TestModel;
            """;
        Assert.Empty(Check(code, ParamRule));
    }

    [Fact]
    public void Wildcard_SuppressesEverythingForTheClass()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="*"));
            end TestModel;
            """;
        var settings = new StyleCheckingSettings { ParameterHasDescription = true, ClassHasDescription = true };
        Assert.Empty(Check(code, settings));
    }

    [Fact]
    public void ComponentLevelSuppress_AppliesToThatComponentOnly()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0 annotation(__MLQT(suppress="Doc.ParameterDescription"));
              parameter Real y = 2.0;
            end TestModel;
            """;
        var findings = Check(code, ParamRule);
        Assert.Single(findings);
        Assert.Equal("y", findings[0].ElementPath); // x suppressed, y still flagged
    }

    [Fact]
    public void UnrelatedRule_IsNotSuppressed()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="Naming.Convention"));
            end TestModel;
            """;
        Assert.Single(Check(code, ParamRule)); // suppresses a different rule, so the param finding stands
    }

    [Fact]
    public void Suppression_IsLineIndependent()
    {
        // Suppression keys on rule + element, not position — so it survives reformatting/line shifts.
        var code = """
            model TestModel


              parameter Real x = 1.0;

              annotation(__MLQT(suppress="Doc.ParameterDescription"));
            end TestModel;
            """;
        Assert.Empty(Check(code, ParamRule));
    }

    [Fact]
    public void PreserveOrder_SuppressesOrderingRules()
    {
        // An import after a component would normally flag ImportStatementsFirst; preserveOrder waives it.
        var code = """
            model TestModel
              Real x;
              import Modelica.Units.SI;
              annotation(__MLQT(preserveOrder=true, reason="order affects the nonlinear system"));
            end TestModel;
            """;
        Assert.Empty(Check(code, new StyleCheckingSettings { ImportStatementsFirst = true }));
    }

    // ---- the two formatting exclusions waive the same rules (B72) -----------------------------

    /// <summary>A class that breaks every rule the checker puts behind <c>isExcludedFromFormatting</c>.</summary>
    private const string BreaksEveryLayoutRule = """
        model TestModel
          Real x;
          parameter Real p = 1;
          model Inner
          end Inner;
          Real y;
          import Modelica.Units.SI;
          extends Modelica.Icons.Example;
        equation
          connect(a.p, b.n);
          x = time;
        initial equation
          x = 0;
        algorithm
          x := x;
        equation
          y = x;
        end TestModel;
        """;

    private static StyleCheckingSettings EveryLayoutRule => new()
    {
        ImportStatementsFirst = true,
        OneOfEachSection = true,
        DontMixEquationAndAlgorithm = true,
        DontMixConnections = true,
        InitialEQAlgoFirst = true,
        InitialEQAlgoLast = true,
        ComponentsBeforeClasses = true,
        DeclarationOrder = true,
    };

    [Fact]
    public void TheTwoFormattingExclusionsWaiveTheSameRules()
    {
        // "Which rules are layout rules" is written in three places: the if (!isExcludedFromFormatting)
        // block in StyleChecking, MlqtSuppressionExtractor.FormattingRuleIds, and
        // CoverageDimension.Layout. CoverageDimensionsTests pins the first to the third. This pins the
        // first to the second, which nothing did — so an eighth layout rule added to the checker would
        // have been waived by the FormattingExcludedModels name list and not by __MLQT(format=false),
        // and every existing test would still have passed.
        var byNameList = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", BreaksEveryLayoutRule), EveryLayoutRule, "TestModel",
            isExcludedFromFormatting: true);

        var annotated = BreaksEveryLayoutRule.Replace(
            "end TestModel;", "annotation(__MLQT(format=false));\nend TestModel;");
        var byAnnotation = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", annotated), EveryLayoutRule, "TestModel");

        Assert.Equal(
            byNameList.Select(f => f.RuleId).OrderBy(id => id, StringComparer.Ordinal),
            byAnnotation.Select(f => f.RuleId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void WithNeitherExclusion_TheSameClassReportsLayoutFindings()
    {
        // The test above passes vacuously for any rule the fixture does not break, which is exactly
        // how it rotted: ComponentsBeforeClasses and DeclarationOrder arrived after it, the fixture
        // broke neither, and __MLQT(format=false) went on reporting both (B284). So the fixture is
        // held to every layout rule in the catalogue, not merely to reporting something.
        var reported = StyleChecking.RunStyleCheckingFindings(
                new ModelDefinition("M", BreaksEveryLayoutRule), EveryLayoutRule, "TestModel")
            .Select(f => f.RuleId)
            .ToHashSet(StringComparer.Ordinal);

        var layoutRules = RuleCatalog.BuiltIn.Values
            .Where(r => r.Category == "Ordering")
            .Select(r => r.Id)
            .ToList();
        Assert.NotEmpty(layoutRules);
        Assert.All(layoutRules, id => Assert.Contains(id, reported));
    }

    [Fact]
    public void NoSuppress_IgnoresAnnotations()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="*"));
            end TestModel;
            """;
        Assert.NotEmpty(Check(code, ParamRule, honor: false));
    }

    // ---- one read per class, shared by everything that wants it (B55) --------------------------

    private const string Waiving = """
        model TestModel
          parameter Real x = 1.0;
          annotation(__MLQT(suppress="Doc.ParameterDescription"));
        end TestModel;
        """;

    [Fact]
    public void TheDirectivesAreReadOnceAndKeptOnTheClass()
    {
        // Three passes want this answer about the same class in the same run — the checker, the
        // coverage measurer and the graph analyses, the last of which used to re-parse to get it.
        var definition = new ModelDefinition("M", Waiving);

        var first = ClassSuppressions.For(definition, "TestModel");
        var second = ClassSuppressions.For(definition, "TestModel");

        Assert.Same(first, second);
        Assert.Same(first, definition.Suppressions);
        Assert.False(first.IsEmpty);
    }

    [Fact]
    public void AClassCarryingNothing_KeepsTheOneSharedEmptySet()
    {
        // Nearly every class carries nothing, and a library holds tens of thousands of them, so the
        // kept answer has to cost a reference rather than a set.
        var a = new ModelDefinition("A", "model A end A;");
        var b = new ModelDefinition("B", "model B end B;");

        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(a, "A"));
        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(b, "B"));
    }

    [Fact]
    public void EditingTheSourceDropsWhatWasReadFromTheOldOne()
    {
        var definition = new ModelDefinition("M", Waiving);
        Assert.False(ClassSuppressions.For(definition, "TestModel").IsEmpty);

        definition.ModelicaCode = "model TestModel\n  parameter Real x = 1.0;\nend TestModel;";

        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(definition, "TestModel"));
    }

    [Fact]
    public void AClassThatWillNotParse_CarriesNoDirectives()
    {
        // The safe direction: a broken file loses its waivers rather than silently gaining every one
        // of them. Its parse error is reported on its own account.
        var definition = new ModelDefinition("M", "model TestModel this is not Modelica");

        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(definition, "TestModel"));
    }

    [Fact]
    public void ReadingTheDirectivesDoesNotTakeATreeTheCallerWasHolding()
    {
        var definition = new ModelDefinition("M", Waiving);
        var tree = definition.EnsureParsed();

        ClassSuppressions.For(definition, "TestModel");

        Assert.Same(tree, definition.ParsedCode);
    }

    [Fact]
    public void ReadingThemForSomeoneElsesClassHandsTheTreeBack()
    {
        var definition = new ModelDefinition("M", Waiving);

        ClassSuppressions.For(definition, "TestModel");

        Assert.Null(definition.ParsedCode);
    }

    // ---- a class-level waiver reaches the classes nested in it ---------------------------------
    // What makes a sub-package of generated code (Dymola's _fmu import models) one annotation rather
    // than one per class.

    private static Finding FindingOn(string modelId, string ruleId = RuleIds.ParameterDescription,
        string? element = null, string message = "m") =>
        new() { RuleId = ruleId, ModelId = modelId, ElementPath = element, Message = message };

    private static SuppressionSet SetOn(string packageId, string annotation) =>
        ClassSuppressions.For(
            new ModelDefinition(packageId, $"package {packageId}\n  annotation({annotation});\nend {packageId};"),
            packageId);

    [Fact]
    public void AClassLevelSuppress_ReachesEveryClassNestedInIt()
    {
        var set = SetOn("P", "__MLQT(suppress=\"Doc.ParameterDescription\")");

        Assert.True(set.IsSuppressed(FindingOn("P")));
        Assert.True(set.IsSuppressed(FindingOn("P.Inner")));
        Assert.True(set.IsSuppressed(FindingOn("P.Sub.Deep.Inner", element: "x")));
    }

    [Fact]
    public void AClassLevelSuppress_WaivesOnlyTheRulesItNames_InNestedClassesToo()
    {
        var set = SetOn("P", "__MLQT(suppress=\"Doc.ParameterDescription\")");

        Assert.False(set.IsSuppressed(FindingOn("P.Inner", RuleIds.ClassDescription)));
    }

    [Fact]
    public void AClassLevelSuppress_DoesNotReachANamesake()
    {
        // PX is not inside P, however the names line up.
        var set = SetOn("P", "__MLQT(suppress=\"*\")");

        Assert.False(set.IsSuppressed(FindingOn("PX.Inner")));
        Assert.False(set.IsSuppressed(FindingOn("Q.P")));
    }

    [Fact]
    public void AComponentLevelSuppress_StaysWithItsComponent()
    {
        var set = ClassSuppressions.For(new ModelDefinition("P", """
            package P
              constant Real x = 1 annotation(__MLQT(suppress="*"));
            end P;
            """), "P");

        Assert.True(set.IsSuppressed(FindingOn("P", element: "x")));
        Assert.False(set.IsSuppressed(FindingOn("P.Inner", element: "x")));
    }

    [Fact]
    public void AFormattingOptOut_StaysWithItsClass()
    {
        // format=false is how the formatter writes that class. A nested class in a file of its own is
        // still formatted, so waiving its layout rules would stop reporting what the formatter changes.
        var set = SetOn("P", "__MLQT(format=false)");

        Assert.True(set.IsSuppressed(FindingOn("P", RuleIds.ImportStatementsFirst)));
        Assert.False(set.IsSuppressed(FindingOn("P.Inner", RuleIds.ImportStatementsFirst)));
        Assert.False(set.PreservesFormatting("P.Inner"));
    }

    [Fact]
    public void AnAcceptedSpelling_ReachesEveryClassNestedInIt()
    {
        var set = SetOn("P", "__MLQT(spelling=\"Fmu\")");
        Finding Misspelt(string modelId, string word) =>
            FindingOn(modelId, RuleIds.SpellingDescription, message: SpellingMessage.For(word, "the description"));

        Assert.True(set.IsSuppressed(Misspelt("P.Sub.Inner", "Fmu")));
        Assert.True(set.IsSuppressed(Misspelt("P.Sub.Inner", "Fmu's")));
        Assert.False(set.IsSuppressed(Misspelt("P.Sub.Inner", "Fmi")));
        Assert.False(set.IsSuppressed(Misspelt("Q.Inner", "Fmu")));
    }

    [Fact]
    public void ANestedClassInTheSameTree_IsWaivedByItsParentsAnnotation()
    {
        // A replaceable class is checked inside its parent's tree, so the parent's own set is the one
        // asked about it: the nesting has to be recognised there, not only across the graph.
        var findings = Check("""
            model TestModel
              replaceable model Inner
                parameter Real k = 1;
              end Inner;
              annotation(__MLQT(suppress="Doc.ParameterDescription"));
            end TestModel;
            """, ParamRule);

        Assert.DoesNotContain(findings, f => f.ModelId == "TestModel.Inner");
    }

    // ---- ...across the graph, to a class in a node of its own ---------------------------------

    private static DirectedGraph Graph(params (string Id, string Code)[] classes)
    {
        var graph = new DirectedGraph();
        foreach (var (id, code) in classes)
            graph.AddNode(new ModelNode(id, ModelicaName.LeafOf(id), code));
        return graph;
    }

    [Fact]
    public void Enclosing_IsEveryAnnotatedClassAClassIsNestedIn_InnermostFirst()
    {
        var graph = Graph(
            ("P", "package P\n  annotation(__MLQT(suppress=\"A\"));\nend P;"),
            ("P.Sub", "package Sub\nend Sub;"),
            ("P.Sub.Deeper", "package Deeper\n  annotation(__MLQT(suppress=\"B\"));\nend Deeper;"),
            ("P.Sub.Deeper.M", "model M\nend M;"));

        var enclosing = ClassSuppressions.Enclosing(graph, "P.Sub.Deeper.M");

        Assert.Equal(2, enclosing.Count);
        Assert.True(enclosing[0].IsSuppressed(FindingOn("P.Sub.Deeper.M", "B")));
        Assert.True(enclosing[1].IsSuppressed(FindingOn("P.Sub.Deeper.M", "A")));
    }

    [Fact]
    public void Enclosing_SkipsAPackageTheGraphDoesNotHold_AndLeavesOutTheClassItself()
    {
        var graph = Graph(
            ("P", "package P\n  annotation(__MLQT(suppress=\"*\"));\nend P;"),
            ("P.Missing.M", "model M\n  annotation(__MLQT(suppress=\"*\"));\nend M;"));

        var enclosing = Assert.Single(ClassSuppressions.Enclosing(graph, "P.Missing.M"));
        Assert.Same(ClassSuppressions.For(graph.GetNode<ModelNode>("P")!.Definition, "P"), enclosing);
        Assert.Empty(ClassSuppressions.Enclosing(graph, "P"));
    }

    [Fact]
    public void TheChecker_HonoursAnEnclosingPackagesWaiver_WhenGivenThem()
    {
        var graph = Graph(("P", "package P\n  annotation(__MLQT(suppress=\"Doc.ParameterDescription\"));\nend P;"));
        var inner = new ModelDefinition("M", "model M\n  parameter Real k = 1;\nend M;");

        Assert.Empty(StyleChecking.RunStyleCheckingFindings(inner, ParamRule, "P.M",
            enclosingSuppressions: id => ClassSuppressions.Enclosing(graph, id)));
        Assert.Single(StyleChecking.RunStyleCheckingFindings(inner, ParamRule, "Q.M",
            enclosingSuppressions: id => ClassSuppressions.Enclosing(graph, id)));
        Assert.Single(StyleChecking.RunStyleCheckingFindings(inner, ParamRule, "P.M", honorSuppressions: false,
            enclosingSuppressions: id => ClassSuppressions.Enclosing(graph, id)));
    }
}
