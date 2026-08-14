using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class MlqtSuppressionWriterTests
{
    // Round-trip: does the extractor consider (modelFqn, element, ruleId) suppressed in this code?
    private static bool Suppresses(string code, string modelFqn, string? element, string ruleId)
    {
        var extractor = new MlqtSuppressionExtractor();
        extractor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return extractor.Build().IsSuppressed(
            new Finding { RuleId = ruleId, ModelId = modelFqn, ElementPath = element, Message = "m" });
    }

    private static bool Parses(string code) => ModelicaParserHelper.Parse(code) is not null;

    [Fact]
    public void ClassLevel_CreatesAnnotation_ThatSuppresses()
    {
        var code = "model Foo\n  Real x;\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "MLQT.Naming.Convention", "legacy", out var outCode, out _));

        Assert.Contains("__MLQT(suppress=\"MLQT.Naming.Convention\"", outCode);
        Assert.Contains("reason=\"legacy\"", outCode);
        Assert.True(Parses(outCode));
        Assert.True(Suppresses(outCode, "Foo", null, "MLQT.Naming.Convention"));
    }

    [Fact]
    public void ComponentLevel_CreatesAnnotation_ThatSuppresses()
    {
        var code = "model Foo\n  Real R;\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, "R", "Naming.Convention", null, out var outCode, out _));

        Assert.Contains("annotation(__MLQT(suppress=\"Naming.Convention\"))", outCode);
        Assert.True(Parses(outCode));
        Assert.True(Suppresses(outCode, "Foo", "R", "MLQT.Naming.Convention")); // short token matches full id
    }

    [Fact]
    public void MergesIntoExistingSuppressList()
    {
        var code = "model Foo\n  Real x;\n  annotation(__MLQT(suppress=\"A\"));\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "B", null, out var outCode, out _));

        Assert.Contains("suppress=\"A,B\"", outCode);
        Assert.True(Parses(outCode));
    }

    [Fact]
    public void AddsMlqtToExistingAnnotation_PreservingIt()
    {
        var code = "model Foo\n  Real x;\n  annotation(Documentation(info=\"<html></html>\"));\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "Doc.ClassDescription", null, out var outCode, out _));

        Assert.Contains("__MLQT(suppress=\"Doc.ClassDescription\")", outCode);
        Assert.Contains("Documentation(info=", outCode); // existing annotation preserved
        Assert.True(Parses(outCode));
        Assert.True(Suppresses(outCode, "Foo", null, "MLQT.Doc.ClassDescription"));
    }

    [Fact]
    public void AddsSuppressToExistingMlqtWithoutSuppress()
    {
        var code = "model Foo\n  Real x;\n  annotation(__MLQT(preserveOrder=true));\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "Doc.ClassDescription", null, out var outCode, out _));

        Assert.Contains("suppress=\"Doc.ClassDescription\"", outCode);
        Assert.Contains("preserveOrder=true", outCode); // preserved
        Assert.True(Parses(outCode));
    }

    [Fact]
    public void ComponentNotFound_ReturnsError()
    {
        var code = "model Foo\n  Real x;\nend Foo;";
        Assert.False(MlqtSuppressionWriter.TryAddSuppression(code, "Nonexistent", "R", null, out _, out var error));
        Assert.Contains("not found", error);
    }
}
