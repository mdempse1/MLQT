using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class SuppressionToolsTests
{
    private const string Package = """
        within;
        package P "p"
          model M "m"
            parameter Real k = 1 "gain";
            Real x;
          end M;
        end P;
        """;

    private static SuppressionTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new SuppressionTools(h.Libraries, h.Resources, h.Session);
    }

    private static string Source(TestHost h, string id) => h.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    [Fact]
    public async Task SuppressRule_ClassLevel_WritesAnnotation()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "MLQT.Doc.ClassDescription", reason: "legacy model"));

        Assert.True(res.Changed);
        var src = Source(host, "P.M");
        Assert.Contains("__MLQT(suppress=\"MLQT.Doc.ClassDescription\"", src);
        Assert.Contains("reason=\"legacy model\"", src);
    }

    [Fact]
    public async Task SuppressRule_ComponentLevel_ScopesToComponent()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "Doc.ParameterDescription", component: "k"));

        var src = Source(host, "P.M");
        Assert.Contains("__MLQT(suppress=\"Doc.ParameterDescription\")", src);
        // The waiver is on the parameter line, not the model.
        var kLine = src.Split('\n').Single(l => l.Contains("parameter Real k"));
        Assert.Contains("__MLQT", kLine);
    }

    [Theory]
    [InlineData("MLQT.Parse.SyntaxError")]
    [InlineData("MLQT.Parse.Failure")]
    [InlineData("MLQT.Check.Failed")]
    [InlineData("Parse.SyntaxError")]
    [InlineData("Check.Failed")]
    public async Task SuppressRule_ADiagnostic_IsRefused(string ruleId)
    {
        // "Always reported, never configurable, never baselined" - and never suppressible, because
        // writing the annotation would edit the user's source and change nothing, leaving them to
        // conclude the finding was waived. Nothing tested the refusal: the mutation audit could turn
        // the `||` into `&&`, and empty the "MLQT." prefix, without a single test objecting (B221).
        // Both forms are here because both are what the guard is for - the caller may name a rule
        // with or without the prefix, and only one of the two was ever reaching it.
        using var host = new TestHost();
        var tools = Load(host);
        var before = Source(host, "P.M");

        var err = ToolAssert.Error(await tools.SuppressRule("P.M", ruleId));

        Assert.Contains("diagnostic", err.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Source(host, "P.M"));   // and the source is untouched
    }

    [Fact]
    public async Task SuppressRule_AnOrdinaryRule_IsStillAccepted()
    {
        // The positive control for the theory above: the refusal must not have swallowed everything.
        using var host = new TestHost();

        ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "MLQT.Doc.ClassDescription", reason: "legacy"));
    }

    [Fact]
    public async Task SuppressRule_UnknownComponent_Errors()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).SuppressRule("P.M", "MLQT.Doc.ParameterDescription", component: "nope"));
        Assert.Contains("'nope'", err.Error);
        Assert.Contains("not found", err.Error);
    }

    [Fact]
    public async Task SuppressRule_ShortClassType_WritesAnnotation()
    {
        const string pkg = """
            within;
            package P "p"
              type Len = Real(unit="m");
              model M "m"
                Real x;
              end M;
            end P;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new SuppressionTools(host.Libraries, host.Resources, host.Session);

        // A `type` is a short class definition with no body — this used to fail to locate a class body.
        var res = ToolAssert.Ok<StructureEditResult>(
            await tools.SuppressRule("P.Len", "MLQT.Units.MissingUnit", preview: true));
        Assert.Contains("type Len = Real(unit=\"m\") annotation(__MLQT(suppress=\"MLQT.Units.MissingUnit\"))", res.NewFileContent);
    }

    [Fact]
    public async Task SuppressRule_NestedClass_WritesOntoThatClass()
    {
        const string pkg = """
            within;
            package P "p"
              model Inner "inner"
                Real x;
              end Inner;
            end P;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new SuppressionTools(host.Libraries, host.Resources, host.Session);

        var res = ToolAssert.Ok<StructureEditResult>(
            await tools.SuppressRule("P.Inner", "MLQT.Doc.ClassDescription", preview: true));
        var content = res.NewFileContent!;
        var innerStart = content.IndexOf("model Inner", StringComparison.Ordinal);
        var innerEnd = content.IndexOf("end Inner", StringComparison.Ordinal);
        var mlqtAt = content.IndexOf("__MLQT", StringComparison.Ordinal);
        Assert.InRange(mlqtAt, innerStart, innerEnd); // on Inner, not the package
    }

    /// <summary>
    /// A suppression matches a rule id exactly, and every finding carries a catalog id, so one naming no
    /// rule waives nothing. It was written anyway, with a note: an agent that guessed
    /// MLQT.Style.NamingConvention for MLQT.Naming.Convention left a waiver that did nothing, and a
    /// later clean check read as though it had worked. These tests' own first versions made the same
    /// guess - MLQT.Documentation.ClassDescription for MLQT.Doc.ClassDescription - and passed.
    /// </summary>
    [Theory]
    [InlineData("MLQT.Style.NamingConvention", "MLQT.Naming.Convention")]
    [InlineData("MLQT.Documentation.ClassDescription", "MLQT.Doc.ClassDescription")]
    [InlineData("Documentation.ParameterDescription", "MLQT.Doc.ParameterDescription")]
    public async Task SuppressRule_AnIdThatIsNoRule_IsRefused_WithTheNearestIds(string guessed, string meant)
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Source(host, "P.M");

        var err = ToolAssert.Error(await tools.SuppressRule("P.M", guessed));

        Assert.Contains("not a rule id", err.Error);
        Assert.Contains(meant, err.Error);
        Assert.Contains("ruleId from the finding", err.Error);
        Assert.Equal(before, Source(host, "P.M"));
    }

    [Theory]
    [InlineData("MLQT.Naming.Convention")]
    [InlineData("Naming.Convention")]
    [InlineData("*")]
    public async Task SuppressRule_ARuleId_WithOrWithoutItsPrefix_OrTheWildcard_IsWritten(string ruleId)
    {
        using var host = new TestHost();

        var res = ToolAssert.Ok<StructureEditResult>(await Load(host).SuppressRule("P.M", ruleId, preview: true));

        Assert.Contains($"suppress=\"{ruleId}\"", res.NewFileContent);
    }

    [Fact]
    public async Task ASuppressionWrittenByTheTool_WaivesTheFinding()
    {
        // The whole point, end to end: the id as the finding gives it, written by the tool, read by the
        // checker, and the finding gone.
        using var host = new TestHost();
        var tools = Load(host);
        var settings = new ModelicaGraph.StyleCheckingSettings();
        const string rule = ModelicaParser.StyleRules.RuleIds.ClassDocumentationInfo;   // M has no Documentation(info)
        settings.SetRuleEnabled(rule, true);
        var finding = Assert.Single(Check(host, settings), f => f.RuleId == rule);

        ToolAssert.Ok<StructureEditResult>(await tools.SuppressRule("P.M", finding.RuleId!));

        Assert.DoesNotContain(Check(host, settings), f => f.RuleId == rule);
    }

    private static List<ModelicaParser.DataTypes.LogMessage> Check(TestHost host, ModelicaGraph.StyleCheckingSettings settings)
    {
        var node = host.Libraries.GetModelById("P.M")!;
        var context = MLQT.Services.Checking.StyleCheckContext.Build(
            settings, host.Libraries.CombinedGraph, host.CustomDictionary, host.DictionaryManager, null);
        return MLQT.Services.Checking.StyleCheckRunner.Run(node, settings, context);
    }

    [Fact]
    public async Task SuppressRule_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "MLQT.Doc.ClassDescription", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.False(res.Changed);
        Assert.NotNull(res.NewFileContent);
        Assert.Contains("__MLQT", res.NewFileContent!);
        Assert.DoesNotContain("__MLQT", Source(host, "P.M")); // original untouched
    }

    [Fact]
    public async Task AcceptSpellingInClass_WritesAnnotation()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AcceptSpellingInClass("P.M", "wibbler", reason: "vendor term"));

        Assert.True(res.Changed);
        var src = Source(host, "P.M");
        Assert.Contains("__MLQT(spelling=\"wibbler\"", src);
        Assert.Contains("reason=\"vendor term\"", src);
    }

    [Fact]
    public async Task AcceptSpellingInClass_MergesASecondWord()
    {
        using var host = new TestHost();
        var tools = Load(host);
        ToolAssert.Ok<StructureEditResult>(await tools.AcceptSpellingInClass("P.M", "wibbler"));
        ToolAssert.Ok<StructureEditResult>(await tools.AcceptSpellingInClass("P.M", "frimbo"));

        Assert.Contains("spelling=\"wibbler,frimbo\"", Source(host, "P.M"));
    }

    [Fact]
    public async Task AcceptSpellingInClass_EmptyWord_Errors()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).AcceptSpellingInClass("P.M", "  "));
        Assert.Contains("word is required", err.Error);
    }

    [Fact]
    public async Task AcceptSpellingInClass_UnknownClass_Errors()
    {
        using var host = new TestHost();
        ToolAssert.Error(await Load(host).AcceptSpellingInClass("P.Nope", "wibbler"));
    }

    [Fact]
    public async Task AcceptSpellingInClass_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AcceptSpellingInClass("P.M", "wibbler", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.False(res.Changed);
        Assert.Contains("spelling=\"wibbler\"", res.NewFileContent!);
        Assert.DoesNotContain("__MLQT", Source(host, "P.M"));
    }
}
