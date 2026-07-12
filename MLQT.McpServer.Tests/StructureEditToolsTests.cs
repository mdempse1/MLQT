using System.Text.RegularExpressions;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class StructureEditToolsTests
{
    private const string Package = """
        within;
        package P "p"
          model A "a"
            Real x;
          end A;
          model M "m"
            parameter Real k = 1 "gain";
            Real a, b, c;
          end M;
        end P;
        """;

    private static StructureEditTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new StructureEditTools(h.Libraries, h.Resources, h.Session);
    }

    private static string Source(TestHost h, string id) => h.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    [Fact]
    public async Task AddComponent_InsertsIntoPublicSection()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "Real", "y", modifier: "= 3", description: "the y"));

        Assert.True(res.Changed);
        var src = Source(host, "P.A");
        Assert.Contains("Real y = 3 \"the y\";", src);
        Assert.Contains("Real x;", src); // existing kept
    }

    [Fact]
    public async Task AddComponent_DuplicateName_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).AddComponent("P.A", "Real", "x"));
        Assert.Contains("already has a component", err.Error);
    }

    [Fact]
    public async Task AddComponent_UnresolvedType_Notes()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "Nonexistent.Thing", "w"));
        Assert.Contains("does not resolve", res.Note);
    }

    [Fact]
    public async Task AddComponent_ParenModifier()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "P.M", "m1", modifier: "(k = 2)"));
        Assert.Contains("P.M m1(k = 2);", Source(host, "P.A"));
    }

    [Fact]
    public async Task RemoveComponent_SoleOnLine()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).RemoveComponent("P.A", "x"));
        var src = Source(host, "P.A");
        Assert.DoesNotContain("Real x", src);
        Assert.Contains("end A;", src);
    }

    [Fact]
    public async Task RemoveComponent_FromSharedClause()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).RemoveComponent("P.M", "b"));
        var src = Source(host, "P.M");
        Assert.Contains("Real a, c;", src);
        Assert.Contains("parameter Real k", src); // untouched
    }

    [Fact]
    public async Task RemoveComponent_FirstInSharedClause()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).RemoveComponent("P.M", "a"));
        Assert.Contains("Real b, c;", Source(host, "P.M"));
    }

    [Fact]
    public async Task RemoveComponent_Missing_Rejected()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(await Load(host).RemoveComponent("P.A", "nope"));
    }

    [Fact]
    public async Task SetComponentModifier_AddsAndReplacesAndClears()
    {
        using var host = new TestHost();
        var tools = Load(host);

        // x has no modifier -> add one.
        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentModifier("P.A", "x", "= 5"));
        Assert.Contains("Real x = 5;", Source(host, "P.A"));

        // k has '= 1' -> replace.
        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentModifier("P.M", "k", "= 9"));
        Assert.Contains("parameter Real k = 9", Source(host, "P.M"));

        // clear k's modifier.
        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentModifier("P.M", "k", ""));
        Assert.Contains("parameter Real k \"gain\";", Source(host, "P.M"));
    }

    [Fact]
    public async Task AddExtends_InsertsAtTop()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).AddExtends("P.A", "P.M", modifier: "(k = 4)"));
        var src = Source(host, "P.A");
        Assert.Contains("extends P.M(k = 4);", src);
        // extends precedes the existing component.
        Assert.True(src.IndexOf("extends P.M") < src.IndexOf("Real x"));
    }

    [Fact]
    public async Task AddImport_Inserts()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).AddImport("P.A", "SI = Modelica.Units.SI"));
        Assert.Contains("import SI = Modelica.Units.SI;", Source(host, "P.A"));
    }

    [Fact]
    public async Task AddEquation_CreatesSectionThenAppends()
    {
        using var host = new TestHost();
        var tools = Load(host);

        // A has no equation section — creates one.
        ToolAssert.Ok<StructureEditResult>(await tools.AddEquation("P.A", "x = 1"));
        var src = Source(host, "P.A");
        Assert.Contains("equation", src);
        Assert.Contains("x = 1;", src);

        // Second equation appends to the same section.
        ToolAssert.Ok<StructureEditResult>(await tools.AddEquation("P.A", "x = 2*time"));
        src = Source(host, "P.A");
        Assert.Contains("x = 1;", src);
        Assert.Contains("x = 2*time;", src);
        Assert.Single(Regex.Matches(src, @"\bequation\b")); // still one section
    }

    [Fact]
    public async Task AddStatement_CreatesAlgorithmSection()
    {
        using var host = new TestHost();
        // A function with inputs/outputs (in a fresh single-file library).
        var path = host.WriteMoFile("F.mo", "within;\nfunction F\n  input Real a;\n  output Real b;\nend F;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var tools = new StructureEditTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await tools.AddStatement("F", "b := 2*a"));
        var src = host.Libraries.GetModelById("F")!.Definition.ModelicaCode!;
        Assert.Contains("algorithm", src);
        Assert.Contains("b := 2*a;", src);
    }

    [Fact]
    public async Task ListAndRemoveConnection()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("C.mo",
            "within;\nmodel C\n  Real u, y;\nequation\n  connect(u, y);\n  u = 1;\nend C;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var tools = new StructureEditTools(host.Libraries, host.Resources, host.Session);

        var list = ToolAssert.Ok<ConnectionsResult>(tools.ListConnections("C"));
        Assert.Contains(list.Connections, c => c.PortA == "u" && c.PortB == "y");

        // Order-insensitive removal.
        ToolAssert.Ok<StructureEditResult>(await tools.RemoveConnection("C", "y", "u"));
        var src = host.Libraries.GetModelById("C")!.Definition.ModelicaCode!;
        Assert.DoesNotContain("connect(", src);
        Assert.Contains("u = 1;", src); // other equation kept
    }

    [Fact]
    public async Task RemoveConnection_Missing_Rejected()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(await Load(host).RemoveConnection("P.A", "a", "b"));
    }

    // A library with signal connectors (RealInput/RealOutput), a physical Pin, and a model wiring blocks.
    private const string ConnPackage = """
        within;
        package C "c"
          connector RealInput = input Real;
          connector RealOutput = output Real;
          connector Pin "physical"
            Real v;
            flow Real i;
          end Pin;
          block Source "src"
            RealOutput y;
          end Source;
          block Sink "snk"
            RealInput u;
          end Sink;
          model Ground
            Pin p;
          end Ground;
          model Sys
            Source source1;
            Sink sink1;
            Ground g1;
          end Sys;
        end C;
        """;

    private static StructureEditTools LoadConn(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = ConnPackage });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new StructureEditTools(h.Libraries, h.Resources, h.Session);
    }

    [Fact]
    public async Task AddConnection_OutputToInput_Allowed()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await LoadConn(host).AddConnection("C.Sys", "source1.y", "sink1.u"));
        Assert.True(res.Changed);
        Assert.Contains("connect(source1.y, sink1.u);", host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!);
    }

    [Fact]
    public async Task AddConnection_SignalToPhysical_Refused()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(
            await LoadConn(host).AddConnection("C.Sys", "source1.y", "g1.p"));
        Assert.Contains("Incompatible connectors", err.Error);
        Assert.DoesNotContain("connect(", host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!);
    }

    [Fact]
    public async Task AddConnection_MissingPort_Refused()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await LoadConn(host).AddConnection("C.Sys", "source1.nope", "sink1.u"));
        Assert.Contains("nope", err.Error);
    }

    [Fact]
    public async Task Batch_BuildsModel_Atomically()
    {
        using var host = new TestHost();
        var tools = LoadConn(host); // has Source (RealOutput y), Sink (RealInput u), empty-ish Sys

        var ops = new List<BatchOperation>
        {
            new() { Op = "add_component", ClassId = "C.Sys", Type = "C.Source", Name = "src2" },
            new() { Op = "add_component", ClassId = "C.Sys", Type = "C.Sink", Name = "snk2" },
            // Connect components added earlier in the SAME batch.
            new() { Op = "add_connection", ClassId = "C.Sys", PortA = "src2.y", PortB = "snk2.u" },
        };

        var res = ToolAssert.Ok<BatchEditResult>(await tools.BatchEdit(ops));
        Assert.Equal(3, res.OperationsApplied);
        var src = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;
        Assert.Contains("C.Source src2", src);
        Assert.Contains("connect(src2.y, snk2.u)", src);
    }

    [Fact]
    public async Task Batch_RollsBackOnFailure()
    {
        using var host = new TestHost();
        var tools = LoadConn(host);
        var before = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;

        var ops = new List<BatchOperation>
        {
            new() { Op = "add_component", ClassId = "C.Sys", Type = "C.Source", Name = "ok1" },
            // This fails: incompatible connectors -> whole batch must roll back.
            new() { Op = "add_connection", ClassId = "C.Sys", PortA = "ok1.y", PortB = "g1.p" },
        };

        var err = ToolAssert.Error(await tools.BatchEdit(ops));
        Assert.Contains("rolled back", err.Error);
        // Nothing survived — not even the first (valid) operation.
        var after = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;
        Assert.DoesNotContain("ok1", after);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Batch_UnknownOp_RejectedBeforeWriting()
    {
        using var host = new TestHost();
        var tools = LoadConn(host);
        var before = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;

        var ops = new List<BatchOperation>
        {
            new() { Op = "add_component", ClassId = "C.Sys", Type = "C.Source", Name = "z1" },
            new() { Op = "frobnicate", ClassId = "C.Sys" },
        };
        var err = ToolAssert.Error(await tools.BatchEdit(ops));
        Assert.Contains("unknown op", err.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!); // nothing written
    }

    [Fact]
    public async Task Batch_Preview_DoesNotKeepChanges()
    {
        using var host = new TestHost();
        var tools = LoadConn(host);

        var ops = new List<BatchOperation>
        {
            new() { Op = "add_component", ClassId = "C.Sys", Type = "C.Source", Name = "p1" },
        };
        var res = ToolAssert.Ok<BatchEditResult>(await tools.BatchEdit(ops, preview: true));
        Assert.True(res.PreviewOnly);
        Assert.Contains(res.Files, f => f.NewContent != null && f.NewContent.Contains("C.Source p1"));
        Assert.DoesNotContain("p1", host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!); // rolled back
    }

    [Fact]
    public async Task BuildOnEmptyModel_ComponentsThenConnection()
    {
        using var host = new TestHost();
        // C.Sys is non-empty; add an empty model would need its own class. Use the empty model path:
        var path = host.WriteMoFile("E.mo", "within;\nmodel E \"empty\"\nend E;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var tools = new StructureEditTools(host.Libraries, host.Resources, host.Session);

        // Add two components to a completely empty class, then an equation — exercises section creation
        // when 'end' initially shares its line with inserted content.
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("E", "Real", "x"));
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("E", "Real", "y"));
        ToolAssert.Ok<StructureEditResult>(await tools.AddEquation("E", "y = 2*x"));

        var src = host.Libraries.GetModelById("E")!.Definition.ModelicaCode!;
        Assert.Contains("Real x;", src);
        Assert.Contains("Real y;", src);
        Assert.Contains("equation", src);
        Assert.Contains("y = 2*x;", src);
    }

    [Fact]
    public async Task AddComponent_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "Real", "y", preview: true));
        Assert.True(res.PreviewOnly);
        Assert.Contains("Real y", res.NewFileContent!);
        Assert.DoesNotContain("Real y", Source(host, "P.A")); // graph unchanged
    }
}
