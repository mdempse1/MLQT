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
    public void ToFile_PreservesCrlfLineEndings_AndOnlyAddsTheAnnotation()
    {
        var code = "within Foo;\r\nmodel Bar\r\n  Real x;\r\nend Bar;\r\n";
        Assert.True(MlqtSuppressionWriter.TryAddSuppressionToFile(code, null, null, "MLQT.Doc.ClassDescription", null, out var outCode, out _));

        Assert.Contains("__MLQT(suppress=\"MLQT.Doc.ClassDescription\")", outCode);
        Assert.True(Parses(outCode));
        // Every line ending stays CRLF: after removing CRLF pairs there is no stray LF.
        Assert.DoesNotContain("\n", outCode.Replace("\r\n", ""));
        // Original lines are untouched (no whole-file rewrite).
        Assert.Contains("within Foo;\r\n", outCode);
        Assert.Contains("  Real x;\r\n", outCode);
        Assert.Contains("end Bar;\r\n", outCode);
    }

    [Fact]
    public void ToFile_PreservesLfLineEndings()
    {
        var code = "within Foo;\nmodel Bar\n  Real x;\nend Bar;\n";
        Assert.True(MlqtSuppressionWriter.TryAddSuppressionToFile(code, null, null, "R", null, out var outCode, out _));

        Assert.DoesNotContain("\r", outCode); // no CRLF introduced into an LF file
        Assert.Contains("__MLQT", outCode);
        Assert.True(Parses(outCode));
    }

    [Fact]
    public void ClassPath_NotFound_ReturnsError()
    {
        var code = "package P\n  model Inner\n    Real x;\n  end Inner;\nend P;";
        Assert.False(MlqtSuppressionWriter.TryAddSuppression(code, new[] { "Missing" }, null, "R", null, out _, out var error));
        Assert.Contains("could not locate the class 'Missing'", error);
    }

    // ── writing into a file rather than a class ──

    [Fact]
    public void WritingIntoACrlfFile_KeepsItACrlfFile()
    {
        // The splice works in LF. Writing that back would rewrite every line of the file, burying a
        // one-line annotation in a whole-file diff.
        var file = "model Foo\r\n  Real x;\r\nend Foo;\r\n";

        Assert.True(MlqtSuppressionWriter.TryAddSuppressionToFile(
            file, null, null, "MLQT.Class.Description", "legacy", out var written, out _));

        Assert.DoesNotContain("\n", written.Replace("\r\n", ""));
        Assert.True(Parses(written.Replace("\r\n", "\n")));
    }

    [Fact]
    public void WritingIntoAnLfFile_LeavesItWithLfEndings()
    {
        Assert.True(MlqtSuppressionWriter.TryAddSuppressionToFile(
            "model Foo\n  Real x;\nend Foo;\n", null, null, "MLQT.Class.Description", null,
            out var written, out _));

        Assert.DoesNotContain("\r", written);
    }

    [Fact]
    public void AFileWhoseClassCannotBeFound_IsReturnedUntouchedWithAReason()
    {
        // MCP and the UI both offer to suppress against a class named by the caller. If the name does
        // not match what is in the file, saying so beats writing the annotation onto another class.
        const string file = "model Foo\r\n  Real x;\r\nend Foo;\r\n";

        var added = MlqtSuppressionWriter.TryAddSuppressionToFile(
            file, ["NoSuchNested"], null, "MLQT.Class.Description", null, out var written, out var error);

        Assert.False(added);
        Assert.Equal(file, written);
        Assert.Contains("NoSuchNested", error);
    }

    [Fact]
    public void ANestedClass_IsLocatedByItsPath()
    {
        // Each nested class is checked as its own model, so the annotation has to land inside that
        // class rather than on the package that holds it.
        const string code =
            "package P\n  model Inner\n    Real x;\n  end Inner;\nend P;";

        Assert.True(MlqtSuppressionWriter.TryAddSuppression(
            code, ["Inner"], null, "MLQT.Class.Description", null, out var outCode, out _));

        Assert.True(Parses(outCode));
        var inner = outCode[outCode.IndexOf("model Inner", StringComparison.Ordinal)..
                            outCode.IndexOf("end Inner;", StringComparison.Ordinal)];
        Assert.Contains("__MLQT(suppress=\"MLQT.Class.Description\")", inner);
    }

    // ── the directive forms the extractor has to read ──

    [Fact]
    public void AShortClassDefinition_CarriesItsDirectiveInTheTrailingComment()
    {
        // `type Length = Real(unit="m")` has no composition to hold an annotation, so a suppression
        // on it can only live in the trailing comment. Missing it means the rule fires anyway and
        // the author has no way to silence it.
        const string code =
            "type Gain = Real annotation(__MLQT(suppress=\"MLQT.Class.Description\"));";

        Assert.True(Suppresses(code, "Gain", null, "MLQT.Class.Description"));
    }

    [Fact]
    public void ADerivativeClassDefinition_CarriesItsDirectiveTheSameWay()
    {
        const string code =
            "function df = der(f, x) annotation(__MLQT(suppress=\"MLQT.Class.Description\"));";

        Assert.True(Suppresses(code, "df", null, "MLQT.Class.Description"));
    }

    [Fact]
    public void AnMlqtAnnotationWithNothingInIt_SuppressesNothing()
    {
        const string code = "model Foo\n  annotation(__MLQT);\nend Foo;";

        Assert.True(Parses(code));
        Assert.False(Suppresses(code, "Foo", null, "MLQT.Class.Description"));
    }

    [Fact]
    public void AnotherVendorsAnnotation_IsNotReadAsADirective()
    {
        const string code =
            "model Foo\n  annotation(__Dymola_experimentFlags(suppress=\"MLQT.Class.Description\"));\nend Foo;";

        Assert.False(Suppresses(code, "Foo", null, "MLQT.Class.Description"));
    }

    // ── opting out of formatting ──

    private static SuppressionSet SetFor(string code)
    {
        var extractor = new MlqtSuppressionExtractor();
        extractor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return extractor.Build();
    }

    [Theory]
    [InlineData("preserveOrder=true")]
    [InlineData("format=false")]
    public void EitherWayOfSayingDoNotFormatThis_IsHonoured(string directive)
    {
        // Both spellings are in the field. A formatter that honoured only one would reorder a class
        // whose author had explicitly asked it not to, and the diff would be the whole class.
        var set = SetFor($"model Foo\n  annotation(__MLQT({directive}));\nend Foo;");

        Assert.True(set.HasFormattingOptOut);
        Assert.True(set.PreservesFormatting("Foo"));
    }

    [Theory]
    [InlineData("preserveOrder=false")]
    [InlineData("format=true")]
    public void SayingTheOppositeIsNotAnOptOut(string directive)
    {
        Assert.False(SetFor($"model Foo\n  annotation(__MLQT({directive}));\nend Foo;").HasFormattingOptOut);
    }

    [Fact]
    public void AComponentCannotOptTheClassOutOfFormatting()
    {
        // Formatting is a whole-class operation, so a component-level directive has nothing to act
        // on; honouring it would silently exempt the class on the strength of one declaration.
        var set = SetFor("model Foo\n  Real x annotation(__MLQT(preserveOrder=true));\nend Foo;");

        Assert.False(set.HasFormattingOptOut);
    }

    [Fact]
    public void ADerivativeClassDefinition_CanBeSuppressedOnToo()
    {
        // It is a class of the library like any other and the rules fire on it, so there has to be
        // a way to waive one — even though it has no body to put an annotation in.
        Assert.True(MlqtSuppressionWriter.TryAddSuppression(
            "function df = der(f, x);", null, null, "MLQT.Class.Description", null,
            out var outCode, out var error));

        Assert.Null(error);
        Assert.True(Parses(outCode));
        Assert.True(Suppresses(outCode, "df", null, "MLQT.Class.Description"));
    }
}
