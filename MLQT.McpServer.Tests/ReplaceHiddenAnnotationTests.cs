using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A replace whose old text was copied from mlqt_get_class's source view, which hides annotations by
/// default: it is found in the file with the annotations stepped over, and each is kept where it was.
/// The view's text did not match the file, so an edit copied from it failed until the agent read the
/// class again with annotations shown.
/// </summary>
public class ReplaceHiddenAnnotationTests
{
    private const string Package = """
        within;
        package H "h"
          model M "m"
            parameter Real k = 1 "gain" annotation (Dialog(group = "Main"));
            Real x annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            Real y;
          equation
            connect(a, b) annotation (Line(points={{0,0},{10,0}}, color={0,0,127}));
            y = k*x;
            annotation (Documentation(info="<html>(a) note</html>"));
          end M;
        end H;
        """;

    private static EditClassTools Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package.ReplaceLineEndings("\n") });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new(host.Libraries, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Resources, host.Session);
    }

    private static string Source(TestHost host) => host.Libraries.GetModelById("H.M")!.Definition.ModelicaCode!;

    private static ClassEditOperation Replace(string old, string @new, bool all = false) =>
        new() { Op = "replace", Old = old, New = @new, All = all };

    [Fact]
    public async Task ADeclarationCopiedFromTheView_IsChanged_AndItsAnnotationKept()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = ToolAssert.Ok<EditClassResult>(await tools.EditClass("H.M",
            [Replace("parameter Real k = 1 \"gain\";", "parameter Real k = 2 \"gain\";")]));

        Assert.Contains("parameter Real k = 2 \"gain\" annotation (Dialog(group = \"Main\"));", Source(host));
        Assert.Contains("kept", Assert.Single(res.Notes));
    }

    [Fact]
    public async Task APlacedComponent_KeepsItsPlacement()
    {
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("H.M", [Replace("Real x;", "Real x(start = 1);")]));

        Assert.Contains("Real x(start = 1) annotation (Placement(transformation(extent={{-10,-10},{10,10}})));", Source(host));
    }

    [Fact]
    public async Task AConnectCopiedFromTheView_KeepsItsLine()
    {
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("H.M", [Replace("connect(a, b);", "connect(a, c);")]));

        Assert.Contains("connect(a, c) annotation (Line(points={{0,0},{10,0}}, color={0,0,127}));", Source(host));
    }

    [Fact]
    public async Task TextAcrossTheClassAnnotation_KeepsIt_BracketsInItsStringsAndAll()
    {
        // The class annotation owns its `;`, and its documentation holds a bracket in a string.
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("H.M", [Replace("y = k*x;\nend M;", "y = 2*k*x;\nend M;")]));

        var source = Source(host);
        Assert.Contains("y = 2*k*x;", source);
        // On a line of its own before `end M;`, at the indentation new gave `end M;`.
        Assert.Contains("y = 2*k*x;\nannotation (Documentation(info=\"<html>(a) note</html>\"));\nend M;", source.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task ADeclarationAddedAfterTheMatch_DoesNotTakeTheAnnotation()
    {
        // Both anchors fit - new still ends with `;` - and the declaration before the annotation is the
        // longer, so k keeps its Dialog rather than k2 getting it.
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("H.M",
            [Replace("parameter Real k = 1 \"gain\";", "parameter Real k = 1 \"gain\";\n  parameter Real k2 = 2;")]));

        var source = Source(host);
        Assert.Contains("parameter Real k = 1 \"gain\" annotation (Dialog(group = \"Main\"));", source);
        Assert.Contains("parameter Real k2 = 2;", source);
    }

    [Fact]
    public async Task ANewTextThatChangesBothSidesOfAnAnnotation_IsRefused_RatherThanGuessed()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Source(host);

        var err = ToolAssert.Error(await tools.EditClass("H.M",
            [Replace("parameter Real k = 1 \"gain\";", "parameter Real k = 2")]));

        Assert.Contains("includeAnnotations=true", err.Error);
        Assert.Contains("Dialog", err.Error);
        Assert.Equal(before, Source(host));
    }

    [Fact]
    public async Task TextThatWritesAnAnnotation_IsNotMatchedAroundOne()
    {
        // Copied with annotations shown: it has to match as written (or ignoring white space), never by
        // stepping over another annotation.
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("H.M",
            [Replace("Real y annotation (Dialog(group = \"Main\"));", "Real w;")]));

        Assert.Contains("not in 'H.M'", err.Error);
    }
}
