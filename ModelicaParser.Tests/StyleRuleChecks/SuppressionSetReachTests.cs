using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// <see cref="SuppressionSet.ReachesNestedClassesAs"/>: whether two readings of a package waive the
/// same things for the classes nested in it. A reload asks it to decide whether the classes below a
/// package, in files of their own, have to be checked again (B499) - so a difference it misses leaves
/// stale findings on screen, and one it invents re-checks a whole sub-library for an edited
/// description.
/// </summary>
public class SuppressionSetReachTests
{
    private static SuppressionSet SetFor(string code)
    {
        var extractor = new MlqtSuppressionExtractor();
        extractor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return extractor.Build();
    }

    private static bool Alike(string before, string after) =>
        SetFor(before).ReachesNestedClassesAs(SetFor(after));

    [Fact]
    public void TheSameWaivers_ReachAlike()
    {
        const string code = "package P\n  annotation(__MLQT(suppress={\"Doc.ClassDescription\"}, spelling=\"Hx\"));\nend P;";

        Assert.True(Alike(code, code));
    }

    [Fact]
    public void NoWaiversOnEitherSide_ReachAlike()
    {
        Assert.True(Alike("package P\nend P;", "package P \"Described\"\nend P;"));
        Assert.True(SuppressionSet.Empty.ReachesNestedClassesAs(SetFor("package P\nend P;")));
    }

    [Fact]
    public void ASuppressAdded_ReachesDifferently()
    {
        Assert.False(Alike("package P\nend P;", "package P\n  annotation(__MLQT(suppress=\"*\"));\nend P;"));
    }

    [Fact]
    public void ASuppressRemoved_ReachesDifferently()
    {
        Assert.False(Alike("package P\n  annotation(__MLQT(suppress=\"*\"));\nend P;", "package P\nend P;"));
    }

    [Fact]
    public void ADifferentRuleOfTheSameCount_ReachesDifferently()
    {
        // Same number of entries, so a count alone would call these alike.
        Assert.False(Alike(
            "package P\n  annotation(__MLQT(suppress=\"Doc.ClassDescription\"));\nend P;",
            "package P\n  annotation(__MLQT(suppress=\"Naming.Convention\"));\nend P;"));
    }

    [Fact]
    public void TheSameRulesOnADifferentClass_ReachDifferently()
    {
        // Moving a waiver from the package to a class nested in it narrows what it reaches.
        Assert.False(Alike(
            "package P\n  package Q\n  end Q;\n  annotation(__MLQT(suppress=\"*\"));\nend P;",
            "package P\n  package Q\n    annotation(__MLQT(suppress=\"*\"));\n  end Q;\nend P;"));
    }

    [Fact]
    public void ASpellingWordAddedOrChanged_ReachesDifferently()
    {
        Assert.False(Alike("package P\nend P;", "package P\n  annotation(__MLQT(spelling=\"Hx\"));\nend P;"));
        Assert.False(Alike(
            "package P\n  annotation(__MLQT(spelling=\"Hx\"));\nend P;",
            "package P\n  annotation(__MLQT(spelling=\"Nx\"));\nend P;"));
    }

    [Fact]
    public void WaiversThatStayWithTheirClass_DoNotCount()
    {
        // A reason waives nothing; format=false and a component's waiver are about the package alone.
        // None of them changes a finding below it, so none of them may re-check the sub-library.
        const string plain = "package P\n  Real x;\n  annotation(__MLQT(suppress=\"*\"));\nend P;";

        Assert.True(Alike(plain, "package P\n  Real x;\n  annotation(__MLQT(suppress=\"*\", reason=\"generated\"));\nend P;"));
        Assert.True(Alike(plain, "package P\n  Real x;\n  annotation(__MLQT(suppress=\"*\", format=false));\nend P;"));
        Assert.True(Alike(plain,
            "package P\n  Real x annotation(__MLQT(suppress=\"Doc.ParameterDescription\"));\n  annotation(__MLQT(suppress=\"*\"));\nend P;"));
    }
}
