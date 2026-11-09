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
        Assert.Contains("white space", Assert.Single(res.Notes));   // the agent learns its copy is stale
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
