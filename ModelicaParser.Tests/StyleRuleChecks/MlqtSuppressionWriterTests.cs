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
    public void AddsMlqtToMultiLineAnnotation_OnItsOwnLine()
    {
        // A multi-line annotation: __MLQT should go on its own line, matching the existing indentation,
        // rather than sharing a line with the first existing argument.
        var code = "model Foo\n  Real x;\n  annotation (\n    Documentation(info=\"<html></html>\"));\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "Doc.ClassDocumentationRevisions", null, out var outCode, out _));

        Assert.True(Parses(outCode));
        Assert.Contains("__MLQT(suppress=\"Doc.ClassDocumentationRevisions\"),\n    Documentation(info=", outCode);
    }

    [Fact]
    public void AddsMlqtToInlineAnnotation_StaysInline()
    {
        var code = "model Foo\n  Real x;\n  annotation(Documentation(info=\"<html></html>\"));\nend Foo;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "Doc.ClassDocumentationRevisions", null, out var outCode, out _));

        Assert.True(Parses(outCode));
        Assert.Contains("__MLQT(suppress=\"Doc.ClassDocumentationRevisions\"), Documentation(info=", outCode);
        Assert.DoesNotContain("\n", outCode.Split("annotation(")[1].Split("Documentation")[0]); // no newline inserted inline
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

    [Fact]
    public void ShortClass_CreatesAnnotation_ThatSuppresses()
    {
        // A `type` is a short class specifier with no composition — the annotation goes in its comment.
        var code = "type Len = Real(unit=\"m\") \"a length\";";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "MLQT.Units.MissingUnit", null, out var outCode, out _));

        Assert.Contains("annotation(__MLQT(suppress=\"MLQT.Units.MissingUnit\"))", outCode);
        Assert.True(Parses(outCode));
        Assert.True(Suppresses(outCode, "Len", null, "MLQT.Units.MissingUnit"));
    }

    [Fact]
    public void ShortClass_NoComment_CreatesAnnotation()
    {
        var code = "type Len = Real;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, null, "MLQT.Units.MissingUnit", null, out var outCode, out _));

        Assert.Contains("__MLQT(suppress=\"MLQT.Units.MissingUnit\")", outCode);
        Assert.True(Parses(outCode));
        Assert.True(Suppresses(outCode, "Len", null, "MLQT.Units.MissingUnit"));
    }

    [Fact]
    public void NestedShortClass_ViaClassPath_TargetsThatClass()
    {
        var code = "package P\n  type Len = Real(unit=\"m\");\n  model M\n    Real y;\n  end M;\nend P;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, new[] { "Len" }, null, "MLQT.Units.MissingUnit", null, out var outCode, out _));

        Assert.True(Parses(outCode));
        var lenLine = outCode.Split('\n').Single(l => l.Contains("type Len"));
        Assert.Contains("__MLQT(suppress=\"MLQT.Units.MissingUnit\")", lenLine); // waiver on Len, not the package
        Assert.DoesNotContain("__MLQT", outCode.Split('\n').Single(l => l.Contains("model M")));
    }

    [Fact]
    public void NestedLongClass_ViaClassPath_TargetsThatClass()
    {
        // The Modelica.Blocks.Continuous.Integrator case: a class nested in a package.mo file.
        var code = "package P\n  model Inner\n    Real x;\n  end Inner;\nend P;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, new[] { "Inner" }, null, "MLQT.Doc.ClassDescription", null, out var outCode, out _));

        Assert.True(Parses(outCode));
        var innerStart = outCode.IndexOf("model Inner", StringComparison.Ordinal);
        var innerEnd = outCode.IndexOf("end Inner", StringComparison.Ordinal);
        var mlqtAt = outCode.IndexOf("__MLQT", StringComparison.Ordinal);
        Assert.InRange(mlqtAt, innerStart, innerEnd); // inside Inner, before its 'end'
    }

    [Fact]
    public void ComponentInNestedClass_ViaClassPath()
    {
        var code = "package P\n  model Inner\n    Real x;\n  end Inner;\nend P;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, new[] { "Inner" }, "x", "MLQT.Units.MissingUnit", null, out var outCode, out _));

        Assert.True(Parses(outCode));
        var xLine = outCode.Split('\n').Single(l => l.Contains("Real x"));
        Assert.Contains("annotation(__MLQT(suppress=\"MLQT.Units.MissingUnit\"))", xLine);
    }

    [Fact]
    public void DeeplyNestedShortClass_ViaClassPath()
    {
        // Mirrors Modelica.Units.SI.Molarity: type nested two packages deep, with sibling packages.
        var code = "package Units \"u\"\n" +
                   "  package UsersGuide \"g\"\n    class G end G;\n  end UsersGuide;\n" +
                   "  package SI \"s\"\n    type Angle = Real(final unit=\"rad\");\n" +
                   "    type Molarity = Real(final quantity=\"Molarity\", final unit=\"mol/m3\", min=0);\n" +
                   "  end SI;\nend Units;";
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(code, new[] { "SI", "Molarity" }, null, "MLQT.Units.MissingUnit", null, out var outCode, out var err), err);

        Assert.True(Parses(outCode));
        var molarityLine = outCode.Split('\n').Single(l => l.Contains("type Molarity"));
        Assert.Contains("__MLQT(suppress=\"MLQT.Units.MissingUnit\")", molarityLine);
        Assert.DoesNotContain("__MLQT", outCode.Split('\n').Single(l => l.Contains("type Angle")));
    }

    [Fact]
    public void ClassPath_NotFound_ReturnsError()
    {
        var code = "package P\n  model Inner\n    Real x;\n  end Inner;\nend P;";
        Assert.False(MlqtSuppressionWriter.TryAddSuppression(code, new[] { "Missing" }, null, "R", null, out _, out var error));
        Assert.Contains("could not locate the class 'Missing'", error);
    }
}
