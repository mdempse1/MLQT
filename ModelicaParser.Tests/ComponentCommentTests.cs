using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// The description and annotation of a component after <c>constrainedby</c> are the element's, not the
/// declaration's - how Dymola and OMEdit write every constrained replaceable component.
/// </summary>
public class ComponentCommentTests
{
    private static modelicaParser.Component_declarationContext Declaration(string code, string name)
    {
        var composition = ModelicaParserHelper.Parse(code).class_definition()[0]
            .class_specifier().long_class_specifier().composition();
        return composition.element_list()
            .SelectMany(l => l.element())
            .SelectMany(e => e.component_clause()?.component_list().component_declaration() ?? [])
            .Single(d => d.declaration().IDENT().GetText() == name);
    }

    private static readonly string Constrained = """
        model M
          replaceable Engine engine constrainedby Base "The engine" annotation(Placement(transformation(extent={{-10,-10},{10,10}})));
          Real x "Plain" annotation(HideResult=true);
        end M;
        """.Replace("\r\n", "\n");

    [Fact]
    public void AfterConstrainedBy_TheElementsCommentIsTheComponents()
    {
        var engine = Declaration(Constrained, "engine");

        Assert.StartsWith("annotation(Placement", ComponentComment.Annotation(engine)!.GetText());
        Assert.Equal("\"The engine\"", ComponentComment.Description(engine)!.GetText());
    }

    [Fact]
    public void WithoutAConstraint_TheDeclarationsOwnCommentIs()
    {
        var x = Declaration(Constrained, "x");

        Assert.Equal("annotation(HideResult=true)", ComponentComment.Annotation(x)!.GetText());
        Assert.Equal("\"Plain\"", ComponentComment.Description(x)!.GetText());
    }

    [Fact]
    public void ADescriptionWrittenBeforeConstrainedBy_IsStillRead()
    {
        // Hand-written text with the description on the declaration and the annotation after the clause.
        var code = "model M\n  replaceable Engine engine \"Mine\" constrainedby Base annotation(Placement());\nend M;";
        var engine = Declaration(code, "engine");

        Assert.Equal("\"Mine\"", ComponentComment.Description(engine)!.GetText());
        Assert.Equal("annotation(Placement())", ComponentComment.Annotation(engine)!.GetText());
    }

    [Fact]
    public void NoCommentAnywhere_IsNull()
    {
        var engine = Declaration("model M\n  replaceable Engine engine constrainedby Base;\n  Real y;\nend M;", "engine");
        var y = Declaration("model M\n  Real y;\nend M;", "y");

        Assert.Null(ComponentComment.Annotation(engine));
        Assert.Null(ComponentComment.Description(engine));
        Assert.Null(ComponentComment.Annotation(y));
        Assert.Null(ComponentComment.Description(y));
    }

    [Theory]
    [InlineData("model M\n  replaceable Engine engine constrainedby Base(k=1);\nend M;", "constrainedby Base(k=1)")]
    [InlineData("model M\n  replaceable Engine engine constrainedby Base \"d\";\nend M;", "constrainedby Base \"d\"")]
    [InlineData("model M\n  Real y \"d\";\nend M;", "Real y \"d\"")]
    public void ANewAnnotation_GoesAfterTheDescription_AndAnyConstraint(string code, string before)
    {
        var at = ComponentComment.AnnotationInsertOffset(Declaration(code, code.Contains("engine") ? "engine" : "y"));

        Assert.EndsWith(before, code[..at]);
        Assert.Equal(';', code[at]);
    }

    [Theory]
    [InlineData("model M\n  replaceable Engine engine constrainedby Base annotation();\nend M;", "engine", "constrainedby Base")]
    [InlineData("model M\n  Real y(start=1) annotation();\nend M;", "y", "Real y(start=1)")]
    [InlineData("model M\n  parameter Boolean b;\n  Real y if b annotation();\nend M;", "y", "Real y if b")]
    public void ANewDescription_GoesBeforeAnyAnnotation_AfterAnyConstraintOrCondition(string code, string name, string before)
    {
        var at = ComponentComment.DescriptionInsertOffset(Declaration(code, name));

        Assert.EndsWith(before, code[..at]);
    }

    // ---- what reads it ---------------------------------------------------------------------------

    [Fact]
    public void TheClassBody_GivesAConstrainedComponentItsAnnotationsSpan()
    {
        var engine = ClassBodyLocator.Analyze(Constrained).Components.Single(c => c.Name == "engine");

        var (start, stop) = engine.AnnotationSpan!.Value;
        Assert.StartsWith("annotation(Placement", Constrained[start..(stop + 1)]);
        Assert.Null(ClassBodyLocator.Analyze("model M\n  Real y;\nend M;").Components.Single().AnnotationSpan);
    }

    [Fact]
    public void TheInterface_GivesAConstrainedComponentItsDescription()
    {
        var engine = ClassInterfaceExtractor.Extract(ModelicaParserHelper.Parse(Constrained))
            .Elements.Single(e => e.Name == "engine");

        Assert.Equal("The engine", engine.Description);
    }

    [Fact]
    public void ADescriptionAfterConstrainedBy_IsSpellChecked_Once()
    {
        // Two components in one element share the description after its constraining clause.
        var code = "model M\n  replaceable Engine e1, e2 constrainedby Base \"The enjine\";\nend M;";

        var visitor = new SpellCheckDescriptions(ModelicaParser.SpellChecking.SpellChecker.Create());
        visitor.Visit(ModelicaParserHelper.Parse(code));

        Assert.Single(visitor.RuleFindings, f => f.Summary.Contains("enjine"));
    }
}
