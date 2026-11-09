using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Two ways a hand rename went wrong without a refactoring tool: a replace-all of a short name that also
/// hit a longer one (<c>Ti</c> in the class <c>TipIn</c>), and a rename to a name already declared
/// (<c>rearLoad</c>), which parses and was found only when the model failed to build.
/// </summary>
public class EditNameSafetyTests
{
    private const string Package = """
        within;
        package S "s"
          model TipIn "a class whose name starts with Ti"
          end TipIn;

          model M "m"
            parameter Real Ti = 1;
            TipIn tip;
            Real frontLoad;
            Real rearLoad;
          equation
            frontLoad = Ti;
            rearLoad = 2*Ti;
          end M;

          model Base "b"
            parameter Real k = 1;
            replaceable Real r;
          end Base;

          model Derived "d"
            extends Base;
            Real x;
          end Derived;

          model Broken "already declares a name twice"
            Real a;
            Real a;
            Real b;
          end Broken;
        end S;
        """;

    private static EditClassTools Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package.ReplaceLineEndings("\n") });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new(host.Libraries, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Resources, host.Session);
    }

    private static string Source(TestHost host, string id) => host.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    private static ClassEditOperation Replace(string old, string @new, bool all = false, bool wholeWord = false) =>
        new() { Op = "replace", Old = old, New = @new, All = all, WholeWord = wholeWord };

    [Fact]
    public async Task AWholeWordReplace_LeavesALongerNameAlone()
    {
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("S.M", [Replace("Ti", "Tau", all: true, wholeWord: true)]));

        var m = Source(host, "S.M");
        Assert.Contains("parameter Real Tau = 1;", m);
        Assert.Contains("frontLoad = Tau;", m);
        Assert.Contains("rearLoad = 2*Tau;", m);
        Assert.Contains("TipIn tip;", m);   // the class's name is not the parameter's
    }

    [Fact]
    public async Task APlainReplace_StillMatchesInsideANameItIsPartOf()
    {
        // The default is unchanged: text as written, wherever it is.
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("S.M", [Replace("Ti", "Tau")]));

        Assert.Contains("occurs 4 times", err.Error);
        Assert.Contains("wholeWord", err.Error);
    }

    [Fact]
    public async Task AWholeWordReplace_ThatFindsNothing_SaysSo()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("S.M", [Replace("Tip", "X", wholeWord: true)]));

        Assert.Contains("as a whole word", err.Error);
    }

    [Fact]
    public async Task ARenameToANameAlreadyDeclared_IsRefused()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Source(host, "S.M");

        var err = ToolAssert.Error(await tools.EditClass("S.M", [Replace("frontLoad", "rearLoad", all: true, wholeWord: true)]));

        Assert.Contains("'rearLoad' twice", err.Error);
        Assert.Equal(before, Source(host, "S.M"));
    }

    [Fact]
    public async Task AddingAComponentOfANameAlreadyDeclared_IsRefused()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("S.M", [new() { Op = "add_component", Type = "Real", Name = "rearLoad" }]));

        Assert.Contains("rearLoad", err.Error);
    }

    [Fact]
    public async Task DeclaringANameTheClassInherits_IsRefused()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var err = ToolAssert.Error(await tools.EditClass("S.Derived", [Replace("Real x;", "Real x;\n  Real k;")]));

        Assert.Contains("'k' (declared in 'S.Base')", err.Error);
    }

    [Fact]
    public async Task ARedeclaration_OfAnInheritedName_IsNotASecondDeclaration()
    {
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("S.Derived", [Replace("Real x;", "Real x;\n  redeclare Real r = 2;")]));

        Assert.Contains("redeclare Real r = 2;", Source(host, "S.Derived"));
    }

    [Fact]
    public async Task AClassThatAlreadyDeclaresANameTwice_CanStillBeEdited()
    {
        // Or nothing could be done to fix it.
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<EditClassResult>(await tools.EditClass("S.Broken", [Replace("Real b;", "Real c;")]));

        Assert.Contains("Real c;", Source(host, "S.Broken"));
    }

    [Fact]
    public async Task WritingAWholeClass_ThatDeclaresANameTwice_IsRefused()
    {
        using var host = new TestHost();
        Load(host);
        var edit = new EditTools(host.Libraries, host.Resources, host.Session);

        var err = ToolAssert.Error(await edit.UpdateClassSource("S.Derived", "model Derived \"d\"\n  extends Base;\n  Real x;\n  Real x;\nend Derived;"));

        Assert.Contains("'x' twice", err.Error);
    }
}
