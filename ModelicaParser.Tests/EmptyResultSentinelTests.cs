using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.ExternalDocs;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// What each extractor hands back when there is nothing to extract.
///
/// <para>Every one of these is the answer to a question asked about most classes in a library — most
/// classes declare no equations, most have no suppressions, most edits are not asked to locate a
/// class that is missing. Returning a shared empty value rather than null is what lets callers read
/// the result without a null check on every line, so the emptiness has to be genuinely empty and the
/// derived answers have to agree with it.</para>
/// </summary>
public class EmptyResultSentinelTests
{
    [Fact]
    public void AClassWithNoBehaviour_ReportsNoneRatherThanNull()
    {
        var behavior = BehaviorExtractor.ExtractFromCode("model M\n  Real x;\nend M;");

        Assert.False(behavior.HasAny);
        Assert.Empty(behavior.Equations);
        Assert.Empty(behavior.Connections);
        Assert.Empty(behavior.Statements);
        Assert.False(behavior.HasEquationSection);
        Assert.False(behavior.HasAlgorithmSection);
    }

    [Fact]
    public void TheEmptyBehaviour_AgreesWithItself()
    {
        // Handed out to callers that then ask HasAny; a sentinel that claimed to have something would
        // put an empty section into a rendered class.
        Assert.False(ClassBehavior.Empty.HasAny);
        Assert.Empty(ClassBehavior.Empty.Equations);
        Assert.Empty(ClassBehavior.Empty.Connections);
        Assert.Empty(ClassBehavior.Empty.Statements);
    }

    [Fact]
    public void AnEmptyEquationSection_CountsAsPresentWithoutAnyBehaviour()
    {
        // The distinction matters to the formatter: the section is there and should stay there, but
        // there is nothing in it to report on.
        var behavior = BehaviorExtractor.ExtractFromCode("model M\n  Real x;\nequation\nend M;");

        Assert.True(behavior.HasEquationSection);
        Assert.False(behavior.HasAny);
    }

    [Fact]
    public void SourceThatIsNotAClass_LocatesNoBody()
    {
        var layout = ClassBodyLocator.Analyze("this is not Modelica");

        Assert.False(layout.Found);
        Assert.Empty(layout.Components);
        Assert.Empty(layout.Connections);
    }

    [Fact]
    public void TheNotFoundLayout_PromisesNothingAnEditCouldActOn()
    {
        // An edit that took this for a real layout would splice text at offset zero.
        Assert.False(ClassBodyLayout.NotFound.Found);
        Assert.Empty(ClassBodyLayout.NotFound.Components);
        Assert.Empty(ClassBodyLayout.NotFound.Connections);
    }

    [Fact]
    public void TheEmptySuppressionSet_SuppressesNothing()
    {
        // Used for every class in a library that has no __MLQT annotations at all, which is nearly
        // all of them: if it claimed a suppression, findings would go missing everywhere.
        Assert.False(SuppressionSet.Empty.HasFormattingOptOut);
        Assert.False(SuppressionSet.Empty.PreservesFormatting("Lib.M"));
    }

    private static SuppressionSet SuppressionsIn(string code)
    {
        var visitor = new MlqtSuppressionExtractor();
        visitor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return visitor.Build();
    }

    [Fact]
    public void AClassThatOptsOutOfFormatting_IsReportedByTheSet()
    {
        var set = SuppressionsIn(
            "model M\n  annotation(__MLQT(preserveOrder=true));\nend M;");

        Assert.True(set.HasFormattingOptOut);
        Assert.True(set.PreservesFormatting("M"));
    }

    [Fact]
    public void AClassWithNoAnnotations_OptsOutOfNothing()
    {
        var set = SuppressionsIn("model M\n  Real x;\nend M;");

        Assert.False(set.HasFormattingOptOut);
        Assert.False(set.PreservesFormatting("M"));
    }

    [Fact]
    public void AQuotedIdentifierWithAnEscapedQuote_IsOneNameNotTwo()
    {
        // Modelica allows almost anything inside single quotes, escapes included. Splitting on a dot
        // inside one would invent a class that does not exist — and these names come from a vendor's
        // documentation, where nothing else can correct the mistake.
        Assert.Equal("'a\\'b.c'", DocumentedClass.SimpleNameOf("Lib.'a\\'b.c'"));
    }

    [Fact]
    public void APlainQualifiedName_SplitsOnItsLastDot()
    {
        Assert.Equal("C", DocumentedClass.SimpleNameOf("Lib.Sub.C"));
    }

    [Fact]
    public void ANameWithNoDots_IsAlreadySimple()
    {
        Assert.Equal("Lib", DocumentedClass.SimpleNameOf("Lib"));
    }
}
