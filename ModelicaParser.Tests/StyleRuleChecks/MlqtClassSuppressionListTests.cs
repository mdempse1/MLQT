using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// <see cref="MlqtSuppressionWriter.TrySetClassSuppressionsToFile"/> and what
/// <see cref="SuppressionSet"/> reads back: a class's <c>suppress</c> list set as a whole, for the
/// Code Review dialog that turns rules on and off. Adding one entry at a time, which is all the
/// writer did before, cannot turn one off.
/// </summary>
public class MlqtClassSuppressionListTests
{
    private static SuppressionSet SetFor(string code)
    {
        var extractor = new MlqtSuppressionExtractor();
        extractor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return extractor.Build();
    }

    private static string Set(string code, string[]? classPath, string[] entries, string? reason = null)
    {
        Assert.True(MlqtSuppressionWriter.TrySetClassSuppressionsToFile(
            code, classPath, entries, reason, out var result, out var error), error);
        Assert.NotNull(ModelicaParserHelper.Parse(result));
        return result;
    }

    [Fact]
    public void OnAClassWithNoAnnotation_WritesTheListAndReason()
    {
        var result = Set("model Foo\n  Real x;\nend Foo;", null, ["MLQT.Doc.ClassDescription", "*"], "generated");

        var set = SetFor(result);
        Assert.Equal(["*", "MLQT.Doc.ClassDescription"], set.SuppressListOf("Foo").Order(StringComparer.Ordinal));
        Assert.Equal("generated", set.ReasonFor("Foo"));
    }

    [Fact]
    public void AnExistingList_IsReplaced_NotAppendedTo()
    {
        const string code = "model Foo\n  annotation(__MLQT(suppress=\"A,B\"));\nend Foo;";

        var result = Set(code, null, ["B", "C"]);

        Assert.Equal(["B", "C"], SetFor(result).SuppressListOf("Foo").Order());
    }

    [Fact]
    public void AnEmptyList_RemovesTheAnnotationItWasAllOf()
    {
        const string code = "model Foo\n  Real x;\n  annotation(__MLQT(suppress=\"A\"));\nend Foo;";

        var result = Set(code, null, []);

        Assert.Equal("model Foo\n  Real x;\nend Foo;", result);
    }

    [Fact]
    public void AnEmptyList_TakesALoneReasonWithIt_ButLeavesAnythingElse()
    {
        // A reason with nothing to be the reason for is not something anyone would write.
        const string alone = "model Foo\n  annotation(Icon(), __MLQT(suppress=\"A\", reason=\"why\"));\nend Foo;";
        Assert.Equal("model Foo\n  annotation(Icon());\nend Foo;", Set(alone, null, []));

        // Shared with format=false, it stays: it may be that directive's reason.
        const string shared = "model Foo\n  annotation(__MLQT(format=false, suppress=\"A\", reason=\"why\"));\nend Foo;";
        var result = Set(shared, null, []);
        Assert.Contains("format=false", result);
        Assert.Contains("reason=\"why\"", result);
        Assert.Empty(SetFor(result).SuppressListOf("Foo"));
    }

    [Fact]
    public void AReason_ReplacesTheOldOne_AndABlankOneLeavesItAlone()
    {
        const string code = "model Foo\n  annotation(__MLQT(suppress=\"A\", reason=\"old\"));\nend Foo;";

        Assert.Equal("new", SetFor(Set(code, null, ["A"], "new")).ReasonFor("Foo"));
        Assert.Equal("old", SetFor(Set(code, null, ["A", "B"], "  ")).ReasonFor("Foo"));
    }

    [Fact]
    public void AReasonWithAQuoteInIt_IsWrittenSoTheFileStillParses()
    {
        var result = Set("model Foo\nend Foo;", null, ["A"], "Dymola's \"FMU\" import");

        Assert.Equal("Dymola's \"FMU\" import", SetFor(result).ReasonFor("Foo"));
    }

    [Theory]
    [InlineData(@"C:\Temp\")]      // a trailing backslash would escape the closing quote
    [InlineData(@"a \"" b")]       // a backslash before a quote
    public void AReasonWithABackslashInIt_RoundTrips(string reason)
    {
        var result = Set("model Foo\nend Foo;", null, ["A"], reason);

        Assert.Equal(reason, SetFor(result).ReasonFor("Foo"));
    }

    [Fact]
    public void AFileThatDoesNotParse_IsReported()
    {
        Assert.False(MlqtSuppressionWriter.TrySetClassSuppressionsToFile(
            "model Foo this is not Modelica", null, ["A"], null, out _, out var error));
        Assert.Equal("could not parse the source", error);
    }

    [Fact]
    public void ANestedClass_IsTheOneEdited()
    {
        const string code = """
            package P
              model Inner
              end Inner;
              annotation(__MLQT(suppress="X"));
            end P;
            """;

        var result = Set(code.Replace("\r\n", "\n"), ["Inner"], ["Y"]);

        // The extractor skips a standalone nested class, so read it on its own.
        var inner = result[result.IndexOf("model Inner", StringComparison.Ordinal)..(result.IndexOf("end Inner;", StringComparison.Ordinal) + "end Inner;".Length)];
        Assert.Equal(["Y"], SetFor(inner).SuppressListOf("Inner"));
        Assert.Equal(["X"], SetFor(result).SuppressListOf("P"));
    }

    [Fact]
    public void AComponentLevelList_IsNotTheClassList()
    {
        const string code = "model Foo\n  Real x annotation(__MLQT(suppress=\"A\"));\nend Foo;";

        var result = Set(code, null, ["B"]);

        Assert.Contains("Real x annotation(__MLQT(suppress=\"A\"))", result);
        Assert.Equal(["B"], SetFor(result).SuppressListOf("Foo"));
    }

    [Fact]
    public void CrlfLineEndings_AreKept()
    {
        var result = Set("model Foo\r\n  Real x;\r\nend Foo;\r\n", null, ["A"]);

        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A,B")]
    [InlineData("say \"hi\"")]
    [InlineData(@"A\")]
    public void AnEntryTheListCannotHold_IsRefused(string entry)
    {
        const string code = "model Foo\nend Foo;";

        Assert.False(MlqtSuppressionWriter.TrySetClassSuppressionsToFile(
            code, null, [entry], null, out var result, out var error));
        Assert.Equal(code, result);
        Assert.NotNull(error);
    }

    [Fact]
    public void AClassThatIsNotThere_IsReported()
    {
        Assert.False(MlqtSuppressionWriter.TrySetClassSuppressionsToFile(
            "package P\nend P;", ["Missing"], ["A"], null, out _, out var error));
        Assert.Contains("Missing", error);
    }

    [Fact]
    public void TheListRead_IsWhatWasWritten_NotTheRulesFormatFalseWaives()
    {
        var set = SetFor("model Foo\n  annotation(__MLQT(format=false));\nend Foo;");

        Assert.Empty(set.SuppressListOf("Foo"));
        Assert.Null(set.ReasonFor("Foo"));
    }

    [Theory]
    [InlineData("MLQT.Doc.ClassDescription", true)]
    [InlineData("Doc.ClassDescription", true)]
    [InlineData("*", true)]
    [InlineData("Doc.ParameterDescription", false)]
    [InlineData("ClassDescription", false)]
    public void AnEntryNamesARule_AsTheCheckerMatchesIt(string token, bool names)
    {
        Assert.Equal(names, SuppressionSet.Names(token, "MLQT.Doc.ClassDescription"));
    }
}
