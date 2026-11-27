using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A replace whose old text differs from the class only in white space - spaces around an operator, a
/// line the formatter broke - is found. Formatting reflows a class, so text copied from a view taken
/// before it was "not in the class" afterwards, and the agent had to search the raw file and try again.
/// The text as written is looked for first; strings keep their spaces.
/// </summary>
public class ReplaceToleranceTests
{
    private const string Package = """
        within;
        package T "t"
          model M "m"
            parameter Real k(
              min = 0,
              max = 10) = 1 "gain";
            Real x(start = 1);
            Real y;
            Real z "a  label with two spaces";
          equation
            x = k*y;
            y = 2;
            z = x;
          end M;
        end T;
        """;

    private static EditClassTools Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package.ReplaceLineEndings("\n") });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new(host.Libraries, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Resources, host.Session);
    }

    private static string Source(TestHost host) => host.Libraries.GetModelById("T.M")!.Definition.ModelicaCode!;

    private static ClassEditOperation Replace(string old, string @new, bool all = false) =>
        new() { Op = "replace", Old = old, New = @new, All = all };

    [Fact]
    public async Task TextCopiedBeforeAFormat_IsFoundThoughItsSpacingDiffers()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<EditClassResult>(await tools.EditClass("T.M", [Replace("x=k*y;", "x = 2*k*y;")]));

        Assert.Contains("x = 2*k*y;", Source(host));
        // Said on every replace after a format, the note was noise: the match was the one place old could mean.
        Assert.Empty(res.Notes);
    }

    private const string WithComment = """
        within;
        package C "c"
          model M "m"
            Real body(min = 0, // lower limit
              max = 10) "b";
          equation
            body = 1;
          end M;
        end C;
        """;

    [Fact]
    public async Task TextWithACommentInsideItInTheFile_IsRefused_NamingTheComment()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = WithComment.ReplaceLineEndings("\n") });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new EditClassTools(host.Libraries, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Resources, host.Session);

        var err = ToolAssert.Error(await tools.EditClass("C.M", [Replace("Real body(min = 0, max = 10)", "Real body(min = 0, max = 20)")]));

        Assert.Contains("with a comment inside it", err.Error);
        Assert.Contains("'// lower limit'", err.Error);
        Assert.DoesNotContain("max = 20", host.Libraries.GetModelById("C.M")!.Definition.ModelicaCode!);
    }

    [Fact]
    public async Task TextNotInTheClass_SaysWhereItStopsMatching()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("T.M", [Replace("Real x(start = 2);", "Real x(start = 3);")]));

        Assert.Contains("up to '='", err.Error);
        Assert.Contains("old goes on with '2', where the class has '1);", err.Error);
    }

    [Fact]
    public async Task ADeclarationTheFormatterBrokeOverLines_IsFoundOnOne()
    {
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("T.M",
            [Replace("parameter Real k(min = 0, max = 10) = 1 \"gain\";", "parameter Real k(min = 0, max = 20) = 1 \"gain\";")]));

        Assert.Contains("max = 20", Source(host));
        Assert.DoesNotContain("max = 10", Source(host));
    }

    [Fact]
    public async Task TheTextAsWritten_IsPreferred_AndNotNoted()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<EditClassResult>(await tools.EditClass("T.M", [Replace("y = 2;", "y = 3;")]));

        Assert.Contains("y = 3;", Source(host));
        Assert.Empty(res.Notes);
    }

    [Fact]
    public async Task AString_IsMatchedExactly_ItsSpacesBeingItsContent()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("T.M", [Replace("\"a label with two spaces\"", "\"x\"")]));

        Assert.Contains("even ignoring differences in white space", err.Error);
    }

    [Fact]
    public async Task TwoNames_DoNotRunTogether()
    {
        // `Real x` is two names: loose matching does not let it match `Realx`, nor `Real` with nothing after.
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("T.M", [Replace("Realy;", "Real w;")]));

        Assert.Contains("not in 'T.M'", err.Error);
    }

    [Fact]
    public async Task AnAmbiguousLooseMatch_IsRefused_AndAllReplacesEvery()
    {
        // `=2;` is not written anywhere; ignoring white space it is `= 2;` in two equations.
        using var host = new TestHost();
        var tools = Load(host);
        ToolAssert.Ok<EditClassResult>(await tools.EditClass("T.M", [Replace("z = x;", "z = 2;")]));

        var twice = ToolAssert.Error(await tools.EditClass("T.M", [Replace("=2;", "= 5;")]));
        var res = ToolAssert.Ok<EditClassResult>(await tools.EditClass("T.M", [Replace("=2;", "= 5;", all: true)]));

        Assert.Contains("occurs 2 times", twice.Error);
        Assert.Contains("Replaced 2 occurrences.", Assert.Single(res.Notes));
        Assert.Contains("y = 5;", Source(host));
        Assert.Contains("z = 5;", Source(host));
    }
}
