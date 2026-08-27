using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class ClassBodyLocatorTests
{
    [Fact]
    public void Components_Captured_WithTypeAndModification()
    {
        const string code = "model M\n  parameter Real k = 2 \"gain\";\n  Real x;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.True(layout.Found);
        Assert.Equal(2, layout.Components.Count);

        var k = layout.Components.Single(c => c.Name == "k");
        Assert.Equal("Real", k.TypeText);
        Assert.True(k.SoleInClause);
        Assert.NotNull(k.ModStart);
        // The modification span covers "= 2".
        Assert.Equal("= 2", code.Substring(k.ModStart!.Value, k.ModStop!.Value - k.ModStart.Value + 1));
    }

    [Fact]
    public void PublicAppendOffset_IsAfterLastElement()
    {
        const string code = "model M\n  Real x;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);
        // Inserting at the append offset should place new text right after "Real x;".
        var edited = code.Insert(layout.PublicAppendOffset, "\n  Real y;");
        Assert.Contains("Real x;\n  Real y;", edited);
    }

    [Fact]
    public void MultipleComponentsInClause_NotSole()
    {
        var layout = ClassBodyLocator.Analyze("model M\n  Real a, b, c;\nend M;");
        Assert.Equal(3, layout.Components.Count);
        Assert.All(layout.Components, c => Assert.False(c.SoleInClause));
    }

    [Fact]
    public void EquationSection_Offset_AndConnections()
    {
        const string code =
            "model M\n  RealInput u;\n  RealOutput y;\nequation\n  connect(u, y);\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.NotNull(layout.EquationAppendOffset);
        var conn = Assert.Single(layout.Connections);
        Assert.Equal("u", conn.PortA);
        Assert.Equal("y", conn.PortB);
        Assert.Equal("connect(u, y)", code.Substring(conn.Start, conn.Stop - conn.Start + 1));
    }

    [Fact]
    public void NoEquationSection_OffsetNull()
    {
        var layout = ClassBodyLocator.Analyze("model M\n  Real x;\nend M;");
        Assert.Null(layout.EquationAppendOffset);
        Assert.Null(layout.AlgorithmAppendOffset);
    }

    [Fact]
    public void AlgorithmSection_OffsetCaptured()
    {
        var layout = ClassBodyLocator.Analyze("function f\n  input Real x;\n  output Real y;\nalgorithm\n  y := x;\nend f;");
        Assert.NotNull(layout.AlgorithmAppendOffset);
    }

    // ── the offsets a surgical edit splices at ──

    [Fact]
    public void AProtectedSection_HasItsOwnAppendOffset()
    {
        // Appending a protected component at the public offset would make it public, which changes
        // the class's interface — the whole point of keeping the two offsets apart.
        const string code =
            "model M\n  Real pub;\nprotected\n  Real hidden;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.NotNull(layout.ProtectedAppendOffset);
        var edited = code.Insert(layout.ProtectedAppendOffset!.Value, "\n  Real alsoHidden;");

        Assert.Contains("Real hidden;\n  Real alsoHidden;", edited);
        Assert.EndsWith("Real alsoHidden;\nend M;", edited);
    }

    [Fact]
    public void AnExplicitPublicSectionAfterAProtectedOne_AppendsBackInThePublicOne()
    {
        const string code =
            "model M\n  Real a;\nprotected\n  Real h;\npublic\n  Real b;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        var edited = code.Insert(layout.PublicAppendOffset, "\n  Real c;");

        Assert.Contains("Real b;\n  Real c;", edited);
    }

    [Fact]
    public void AClassWithNoProtectedSection_OffersNoProtectedOffset()
    {
        Assert.Null(ClassBodyLocator.Analyze("model M\n  Real x;\nend M;").ProtectedAppendOffset);
    }

    [Fact]
    public void TheFirstPublicElement_IsWhereATopInsertGoes()
    {
        // An extends or import clause has to go above the components, not after them.
        const string code = "model M\n  Real x;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        var edited = code.Insert(layout.FirstPublicElementOffset!.Value, "extends Base;\n  ");

        Assert.Contains("model M\n  extends Base;\n  Real x;", edited);
    }

    [Fact]
    public void AnEmptyClass_HasNoFirstElementAndAppendsAtTheEnd()
    {
        const string code = "model M\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.True(layout.Found);
        Assert.Null(layout.FirstPublicElementOffset);
        Assert.Equal(layout.BodyEndOffset, layout.PublicAppendOffset);
        Assert.Contains("model M\n  Real x;\nend M;",
            code.Insert(layout.PublicAppendOffset, "  Real x;\n"));
    }

    [Fact]
    public void ATrailingClassAnnotation_StaysLast()
    {
        // The grammar requires the class annotation to be the last thing in the composition, so an
        // element spliced after it does not parse. The insertion boundary is the annotation's start.
        const string code =
            "model M\n  Real x;\n  annotation(Icon());\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        var edited = code.Insert(layout.BodyEndOffset, "Real y;\n  ");

        Assert.Contains("Real y;\n  annotation(Icon());", edited);
        Assert.True(ClassBodyLocator.Analyze(edited).Found, "the edited class should still parse");
    }

    [Fact]
    public void AnAnnotationOnAComponent_IsNotTheClassAnnotation()
    {
        // It is nested inside the element rather than a direct child of the composition; treating it
        // as the class annotation would put every later insert above the first component.
        const string code = "model M\n  Real x annotation(Dialog());\n  Real y;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.Contains("Real y;\n  Real z;", code.Insert(layout.PublicAppendOffset, "\n  Real z;"));
    }

    [Fact]
    public void TheDetectedIndent_FollowsTheFirstComponent()
    {
        Assert.Equal("    ", ClassBodyLocator.Analyze("model M\n    Real x;\nend M;").Indent);
        Assert.Equal("  ", ClassBodyLocator.Analyze("model M\nend M;").Indent);
    }

    // ── elements that are not components ──

    [Fact]
    public void ExtendsAndImportClauses_AreNotComponents()
    {
        var layout = ClassBodyLocator.Analyze(
            "model M\n  import Modelica.Units.SI;\n  extends Base;\n  Real x;\nend M;");

        var component = Assert.Single(layout.Components);
        Assert.Equal("x", component.Name);
    }

    [Fact]
    public void ANestedClass_IsNotAComponentAndIsNotDescendedInto()
    {
        // The nested class has its own body and is analysed separately; its components must not be
        // attributed to the outer class, or an edit would be spliced into the wrong scope.
        var layout = ClassBodyLocator.Analyze(
            "model M\n  model Inner\n    Real hidden;\n  end Inner;\n  Real own;\nend M;");

        Assert.Equal("own", Assert.Single(layout.Components).Name);
    }

    [Fact]
    public void SourceThatDoesNotCloseItsClass_StillReportsWhatItCan()
    {
        // Half-typed source reaches the locator from the editor; it has to degrade rather than throw.
        var layout = ClassBodyLocator.Analyze("model M\n  Real x;\n");

        Assert.True(layout.PublicAppendOffset >= 0);
    }

    // ── the spans a component edit needs ──

    [Fact]
    public void AComponentInAMultipleDeclarationClause_KnowsItsOwnSpanAndItsClauses()
    {
        // Removing b means cutting the declaration span; removing all three means cutting the clause.
        const string code = "model M\n  parameter Real a = 1, b = 2, c = 3;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);
        var b = layout.Components.Single(c => c.Name == "b");

        Assert.Equal("b = 2", code[b.DeclStart..(b.DeclStop + 1)]);
        Assert.Equal("parameter Real a = 1, b = 2, c = 3", code[b.ClauseStart..(b.ClauseStop + 1)]);
        Assert.False(b.SoleInClause);
    }

    [Fact]
    public void AComponentWithNoModifier_SaysWhereOneWouldGo()
    {
        const string code = "model M\n  Real x;\nend M;";
        var x = Assert.Single(ClassBodyLocator.Analyze(code).Components);

        Assert.Null(x.ModStart);
        Assert.Equal("model M\n  Real x(start = 0);\nend M;",
            code.Insert(x.BindingInsertOffset, "(start = 0)"));
    }

    [Fact]
    public void AnArrayComponent_KeepsItsSubscriptsAheadOfANewModifier()
    {
        // Inserting at the name rather than after the subscripts would produce Real x(…)[3], which
        // does not parse.
        const string code = "model M\n  Real x[3];\nend M;";
        var x = Assert.Single(ClassBodyLocator.Analyze(code).Components);

        Assert.Equal("model M\n  Real x[3](each start = 0);\nend M;",
            code.Insert(x.BindingInsertOffset, "(each start = 0)"));
    }
}
