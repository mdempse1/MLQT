using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_edit_class</c>: the replace operation, the element operations it hands to the tools that do
/// them, and the promise that a failed operation leaves the file as it was.
/// </summary>
public class EditClassToolsTests
{
    private const string Package = """
        within;
        package P "p"
          block Lag "a first order lag"
            parameter Real T = 0.1 "time constant";
            input Real u;
            output Real y;
          equation
            T*der(y) = u - y;
          end Lag;
          model M "m"
            parameter Real k = 1 "gain";
            Real a;
          equation
            a = k;
          end M;
        end P;
        """;

    private static EditClassTools Load(TestHost h, string? source = null)
    {
        // A raw string literal carries the .cs file's own line endings, which differ between checkouts;
        // the tests that care about line endings build the ones they want from this.
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = source ?? Lf(Package) });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return Tools(h);
    }

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static EditClassTools Tools(TestHost h) =>
        new(h.Libraries, h.Repositories, h.CustomDictionary, h.DictionaryManager, h.Resources, h.Session);

    private static string Source(TestHost h, string id) => h.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    private static string FileText(TestHost h, string id) =>
        File.ReadAllText(h.Libraries.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(
            h.Libraries.GetModelById(id)!.ContainingFileId!)!.FilePath);

    // Every write goes through ModelicaFileEncoding, which ends a file with a newline, so a file restored
    // by a rollback is the text as the funnel writes it - which for a real .mo file is the text itself.
    private static string Written(string before) => ModelicaParser.Helpers.ModelicaFileEncoding.EnsureFinalNewline(before);

    private static ClassEditOperation Replace(string old, string @new) => new() { Op = "replace", Old = old, New = @new };

    [Fact]
    public async Task Replace_ChangesTheOneOccurrence_AndKeepsTheRest()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<EditClassResult>(await Load(host).EditClass("P.Lag",
            [Replace("T*der(y) = u - y;", "T*der(y) = k*u - y;"),
             Replace("parameter Real T = 0.1 \"time constant\";",
                     "parameter Real T = 0.25 \"time constant\";\n    parameter Real k = 1 \"gain applied to the input\";")]));

        Assert.Equal(2, res.OperationsApplied);
        Assert.False(res.PreviewOnly);
        var src = Source(host, "P.Lag");
        Assert.Contains("T*der(y) = k*u - y;", src);
        Assert.Contains("parameter Real T = 0.25", src);
        Assert.Contains("parameter Real k = 1 \"gain applied to the input\";", src);
        Assert.Contains("a = k;", Source(host, "P.M")); // the other class in the file is untouched
    }

    [Fact]
    public async Task Replace_MatchesWhicheverLineEndingsTheCallerUsed()
    {
        // An agent's text has line feeds; the file may have carriage returns too. Both must match the
        // same source, or a correct patch fails depending on how the library was checked out.
        using var host = new TestHost();
        var crlf = Package.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var tools = Load(host, crlf);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("P.Lag",
            [Replace("    output Real y;\n  equation", "    output Real y;\n    Real e;\n  equation")]));

        var text = FileText(host, "P.Lag");
        Assert.Contains("Real e;\r\n", text);
        Assert.DoesNotMatch("(?<!\r)\n", text); // no bare line feed slipped in
    }

    [Fact]
    public async Task Replace_RefusesTextThatIsNotThere_AndSaysHowToCopyIt()
    {
        using var host = new TestHost();
        var before = FileText2(host);
        var err = Assert.IsType<ToolError>(await Tools(host).EditClass("P.Lag", [Replace("T*der(y)=u-y;", "x")]));

        Assert.Contains("not in 'P.Lag'", err.Error);
        Assert.Contains("includeAnnotations=true", err.Error);
        Assert.Contains("Nothing was changed", err.Error);
        Assert.Equal(before, FileText(host, "P.Lag"));

        string FileText2(TestHost h) { Load(h); return FileText(h, "P.Lag"); }
    }

    [Fact]
    public async Task Replace_RefusesTextThatOccursMoreThanOnce()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.Lag", [Replace("Real", "Integer")]));
        Assert.Contains("occurs 3 times", err.Error);
        Assert.Contains("all=true", err.Error);
    }

    // ---- all=true: every occurrence ------------------------------------------------------------
    //
    // A name used throughout a class was renamed with a shell sed in the Dymola scenario, because
    // replace refused text that occurs twice.

    private static ClassEditOperation ReplaceAll(string old, string @new) =>
        new() { Op = "replace", Old = old, New = @new, All = true };

    [Fact]
    public async Task ReplaceAll_ChangesEveryOccurrence_AndSaysHowMany()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<EditClassResult>(await Load(host).EditClass("P.Lag", [ReplaceAll("T", "Tau")]));

        var src = Source(host, "P.Lag");
        Assert.Contains("parameter Real Tau = 0.1", src);
        Assert.Contains("Tau*der(y) = u - y;", src);
        Assert.DoesNotContain("T*der", src);
        Assert.Equal("Operation 0: Replaced 2 occurrences.", Assert.Single(res.Notes));
        Assert.Contains("parameter Real k = 1", Source(host, "P.M"));   // only the class named
    }

    [Fact]
    public async Task ReplaceAll_OfTextThatOccursOnce_IsAnOrdinaryReplace()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<EditClassResult>(await Load(host).EditClass("P.Lag", [ReplaceAll("u - y", "u - 2*y")]));

        Assert.Contains("T*der(y) = u - 2*y;", Source(host, "P.Lag"));
        Assert.Equal("Operation 0: Replaced 1 occurrence.", Assert.Single(res.Notes));
    }

    [Fact]
    public async Task ReplaceAll_StillRefusesTextThatIsNotThere()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.Lag", [ReplaceAll("Boolean", "Integer")]));

        Assert.Contains("not in 'P.Lag'", err.Error);
    }

    [Fact]
    public async Task ReplaceAll_IsParseChecked_AndWritesNothingThatWouldNotParse()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = FileText(host, "P.Lag");

        var err = Assert.IsType<ToolError>(await tools.EditClass("P.Lag", [ReplaceAll("Real", "Real(")]));

        Assert.Contains("Nothing was changed", err.Error);
        Assert.Equal(before, FileText(host, "P.Lag"));
    }

    [Fact]
    public async Task ReplaceAll_StillRefusesToRenameTheClass()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.Lag", [ReplaceAll("Lag", "Delay")]));

        Assert.Contains("mlqt_rename_class", err.Error);
    }

    [Fact]
    public async Task Replace_RefusesToRenameTheClass()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.Lag",
            [Replace("block Lag \"a first order lag\"", "block Delay \"a first order lag\""),]));
        Assert.Contains("mlqt_rename_class", err.Error);
        Assert.Contains("block Lag", Source(host, "P.Lag"));
    }

    [Fact]
    public async Task Replace_RefusesAResultThatDoesNotParse()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = FileText(host, "P.Lag");

        var err = Assert.IsType<ToolError>(await tools.EditClass("P.Lag", [Replace("u - y;", "u - ;")]));

        Assert.Contains("unparseable", err.Error);
        Assert.Equal(before, FileText(host, "P.Lag"));
    }

    [Fact]
    public async Task ElementOperations_AreDoneByTheToolsOfTheSameName()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<EditClassResult>(await Load(host).EditClass("P.M",
        [
            new() { Op = "add_component", Type = "Real", Name = "b", Modifier = "= 2", Description = "bee" },
            new() { Op = "set_component_modifier", Name = "k", Modifier = "3" },
            new() { Op = "add_equation", Equation = "b = 2*a" },
            new() { Op = "set_class_description", Description = "the model" },
            new() { Op = "mlqt_set_component_description", Name = "a", Description = "the a" },
        ]));

        Assert.Equal(5, res.OperationsApplied);
        var src = Source(host, "P.M");
        Assert.Contains("model M \"the model\"", src);
        Assert.Contains("Real b = 2 \"bee\";", src);
        Assert.Contains("parameter Real k = 3", src);
        Assert.Contains("b = 2*a;", src);
        Assert.Contains("Real a \"the a\";", src);
    }

    [Fact]
    public async Task AFailedOperation_LeavesTheFileAsItWas()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = FileText(host, "P.M");

        var err = Assert.IsType<ToolError>(await tools.EditClass("P.M",
        [
            Replace("a = k;", "a = 2*k;"),
            new() { Op = "remove_component", Name = "nothere" },
        ]));

        Assert.Contains("Operation 1 (remove_component) failed", err.Error);
        Assert.Equal(Written(before), FileText(host, "P.M"));
        Assert.Contains("a = k;", Source(host, "P.M"));
    }

    [Fact]
    public async Task Preview_ReturnsTheFile_AndKeepsNothing()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = FileText(host, "P.M");

        var res = ToolAssert.Ok<EditClassResult>(await tools.EditClass("P.M", [Replace("a = k;", "a = 2*k;")], preview: true));

        Assert.True(res.PreviewOnly);
        Assert.Contains("a = 2*k;", res.NewFileContent);
        Assert.Equal(Written(before), FileText(host, "P.M"));
    }

    [Fact]
    public async Task AMissingField_IsReportedBeforeAnythingIsWritten_NamingWhatTheOpTakes()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = FileText(host, "P.M");

        var err = Assert.IsType<ToolError>(await tools.EditClass("P.M",
        [
            Replace("a = k;", "a = 2*k;"),
            new() { Op = "add_connection", PortA = "x.y" },
        ]));

        Assert.Contains("Operation 1 (add_connection) needs portB", err.Error);
        Assert.Contains("It takes: portA, portB", err.Error);
        Assert.Equal(before, FileText(host, "P.M"));
    }

    [Fact]
    public async Task AnUnknownOperation_IsRefused_ListingTheValidOnes()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.M", [new() { Op = "rename" }]));
        Assert.Contains("unknown op 'rename'", err.Error);
        Assert.Contains("replace", err.Error);
    }

    public static TheoryData<string> AllOperations => [.. EditClassTools.Operations.Keys];

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task EveryOperation_GivenNoFields_SaysWhichItNeeds(string op)
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.M", [new() { Op = op }]));
        Assert.Contains($"Operation 0 ({op}) needs ", err.Error);
        Assert.Contains("It takes: " + string.Join(", ", EditClassTools.Operations[op]), err.Error);
    }

    [Fact]
    public async Task DocumentationSuppressionAndSpellingOperations_AreDoneByTheirTools()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<EditClassResult>(await Load(host).EditClass("P.M",
        [
            new() { Op = "set_class_documentation", Info = "<html><p>The modell.</p></html>" },
            new() { Op = "correct_spelling", Word = "modell", NewWord = "model" },
            new() { Op = "accept_spelling", Word = "Zorbit", Reason = "a name" },
            new() { Op = "suppress_rule", RuleId = "MLQT.Doc.ParameterDescription", Component = "k", Reason = "test" },
            new() { Op = "add_import", Import = "SI = Modelica.Units.SI" },
            new() { Op = "add_statement", Statement = "a := k" },
        ], preview: true));

        Assert.Equal(6, res.OperationsApplied);
        var text = res.NewFileContent!;
        Assert.Contains("<p>The model.</p>", text);
        Assert.Contains("spelling=\"Zorbit\"", text);
        Assert.Contains("MLQT.Doc.ParameterDescription", text);
        Assert.Contains("import SI = Modelica.Units.SI;", text);
        Assert.Contains("a := k;", text);
    }

    [Fact]
    public async Task ConnectionAndPlacementOperations_AreDoneByTheirTools()
    {
        const string wired = """
            within;
            package W "w"
              connector Pin "a pin"
                Real v;
                flow Real i;
              end Pin;
              model Part "a part"
                Pin p;
                Pin n;
              equation
                p.i + n.i = 0;
              end Part;
              model Sys "a system"
                Part a;
                Part b;
              equation
                connect(a.n, b.p);
              end Sys;
            end W;
            """;
        using var host = new TestHost();
        var res = ToolAssert.Ok<EditClassResult>(await Load(host, Lf(wired)).EditClass("W.Sys",
        [
            new() { Op = "remove_connection", PortA = "a.n", PortB = "b.p" },
            new() { Op = "add_connection", PortA = "b.n", PortB = "a.p" },
            new() { Op = "set_component_placement", Name = "a", X1 = -40, Y1 = -10, X2 = -20, Y2 = 10 },
            new() { Op = "add_extends", BaseType = "W.Part" },
        ], preview: true));

        var text = res.NewFileContent!;
        Assert.DoesNotContain("connect(a.n, b.p)", text);
        Assert.Contains("connect(b.n, a.p)", text);
        Assert.Contains("extent={{-40,-10},{-20,10}}", text.Replace(" ", ""));
        Assert.Contains("extends W.Part;", text);
    }

    [Fact]
    public async Task AReadOnlyOrMissingClass_IsRefusedBeforeTheOperationsAreRead()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.Nope", [Replace("a", "b")]));
        Assert.Contains("P.Nope", err.Error);

        var empty = Assert.IsType<ToolError>(await Tools(host).EditClass("P.M", []));
        Assert.Contains("non-empty", empty.Error);
    }

    [Fact]
    public async Task Replace_WithNothingToChange_IsRefused()
    {
        using var host = new TestHost();
        var err = Assert.IsType<ToolError>(await Load(host).EditClass("P.M", [Replace("a = k;", "a = k;")]));
        Assert.Contains("the same", err.Error);
    }

    [Fact]
    public void EveryOperationIsHandledByTheDispatch()
    {
        // The list the errors quote and the switch that does the work are two lists of one thing; an
        // operation in the first and not the second is accepted and then fails as unknown.
        var source = File.ReadAllText(Path.Combine(ToolCountTests.RepositoryRoot(), "MLQT.McpServer", "Tools", "EditClassTools.cs"));
        var dispatch = source[source.IndexOf("private Task<object> Dispatch", StringComparison.Ordinal)..];
        Assert.All(EditClassTools.Operations.Keys, op => Assert.Contains($"\"{op}\" =>", dispatch));
    }
}
