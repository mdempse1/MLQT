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
    public async Task AddComponent_Protected_CreatesProtectedSection()
    {
        using var host = new TestHost();
        // P.A has only public 'Real x;' — a protected component must create a new protected section.
        ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "Real", "helper", visibility: "protected"));
        var src = Source(host, "P.A");
        Assert.Contains("protected", src);
        Assert.Contains("Real helper;", src);
        // The protected section (and its element) come after the public 'Real x;'.
        Assert.True(src.IndexOf("Real x;", StringComparison.Ordinal) < src.IndexOf("protected", StringComparison.Ordinal));
        Assert.True(src.IndexOf("protected", StringComparison.Ordinal) < src.IndexOf("Real helper;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddComponent_Protected_AppendsToExistingSection()
    {
        const string pkg = """
            within;
            package Q "q"
              model A "a"
                Real x;
              protected
                Real p1;
              end A;
            end Q;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new StructureEditTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("Q.A", "Real", "p2", visibility: "protected"));
        var src = host.Libraries.GetModelById("Q.A")!.Definition.ModelicaCode!;
        // Only one protected keyword — p2 joined the existing section after p1.
        Assert.Single(Regex.Matches(src, @"\bprotected\b"));
        Assert.True(src.IndexOf("Real p1;", StringComparison.Ordinal) < src.IndexOf("Real p2;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddComponent_Prefix_ParameterAndReplaceable()
    {
        using var host = new TestHost();
        var tools = Load(host);
        ToolAssert.Ok<StructureEditResult>(
            await tools.AddComponent("P.A", "Real", "gain", modifier: "= 2", prefix: "parameter", description: "the gain"));
        Assert.Contains("parameter Real gain = 2 \"the gain\";", Source(host, "P.A"));

        ToolAssert.Ok<StructureEditResult>(
            await tools.AddComponent("P.A", "P.M", "blk", prefix: "replaceable parameter"));
        Assert.Contains("replaceable parameter P.M blk;", Source(host, "P.A"));
    }

    [Fact]
    public async Task AddComponent_Replaceable_WithConstrainingClause()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).AddComponent(
            "P.A", "P.M", "medium", prefix: "replaceable", constrainedBy: "P.M", description: "the medium"));
        Assert.Contains("replaceable P.M medium constrainedby P.M \"the medium\";", Source(host, "P.A"));
    }

    [Fact]
    public async Task AddComponent_ConditionalComponent()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "Real", "port", condition: "useHeatPort"));
        Assert.Contains("Real port if useHeatPort;", Source(host, "P.A"));
    }

    [Fact]
    public async Task AddComponent_InvalidPrefix_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).AddComponent("P.A", "Real", "y", prefix: "Modelica.Blocks.Sine"));
        Assert.Contains("not a valid component prefix", err.Error);
    }

    [Fact]
    public async Task AddComponent_ConstrainedByWithoutReplaceable_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(
            await Load(host).AddComponent("P.A", "P.M", "m", prefix: "parameter", constrainedBy: "P.M"));
        Assert.Contains("replaceable", err.Error);
    }

    [Fact]
    public async Task AddComponent_InvalidVisibility_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).AddComponent("P.A", "Real", "y", visibility: "private"));
        Assert.Contains("public", err.Error);
    }

    // --- Restricted-class legality checks for the add_* tools ---

    private const string KindsPackage = """
        within;
        package K "kinds"
          package Sub "a subpackage"
            constant Real g = 9.81;
          end Sub;
          connector Flange "an acausal connector"
            Real s;
            flow Real f;
          end Flange;
          connector Causal "a causal connector"
            input Real a;
            output Real b;
          end Causal;
          record Data "a record"
            Real value;
          end Data;
          function f "a function"
            input Real u;
            output Real y;
          algorithm
            y := u;
          end f;
          model M "a model"
            Real x;
          end M;
          block B "a block"
            Real x;
          end B;
        end K;
        """;

    private static StructureEditTools LoadKinds(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = KindsPackage });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new StructureEditTools(h.Libraries, h.Resources, h.Session);
    }

    [Fact]
    public async Task AddEquation_ToPackage_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await LoadKinds(host).AddEquation("K.Sub", "g = 9.81"));
        Assert.Contains("package cannot contain an equation", err.Error);
    }

    [Fact]
    public async Task AddEquation_ToConnector_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await LoadKinds(host).AddEquation("K.Flange", "s = 0"));
        Assert.Contains("cannot contain an equation", err.Error);
    }

    [Fact]
    public async Task AddEquation_ToModel_Allowed()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await LoadKinds(host).AddEquation("K.M", "x = 1"));
    }

    [Fact]
    public async Task AddStatement_ToPackage_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await LoadKinds(host).AddStatement("K.Sub", "g := 9.81"));
        Assert.Contains("cannot contain an algorithm", err.Error);
    }

    [Fact]
    public async Task AddStatement_ToFunction_Allowed()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await LoadKinds(host).AddStatement("K.f", "y := 2*u"));
    }

    [Fact]
    public async Task AddStatement_ToRecord_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await LoadKinds(host).AddStatement("K.Data", "value := 0"));
        Assert.Contains("cannot contain an algorithm", err.Error);
    }

    [Fact]
    public async Task AddConnection_ToPackage_Rejected()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await LoadKinds(host).AddConnection("K.Sub", "a", "b"));
        Assert.Contains("cannot contain a connection", err.Error);
    }

    [Fact]
    public async Task AddComponent_ToPackage_RequiresConstant()
    {
        using var host = new TestHost();
        var tools = LoadKinds(host);
        // A plain component is refused...
        var err = ToolAssert.Error(await tools.AddComponent("K.Sub", "Real", "h", modifier: "= 1"));
        Assert.Contains("must be a constant", err.Error);
        // ...but a constant is allowed.
        ToolAssert.Ok<StructureEditResult>(
            await tools.AddComponent("K.Sub", "Real", "h", modifier: "= 1", prefix: "constant"));
        Assert.Contains("constant Real h = 1;", host.Libraries.GetModelById("K.Sub")!.Definition.ModelicaCode!);
    }

    [Fact]
    public async Task AddComponent_ToConnectorAndRecord_Allowed()
    {
        using var host = new TestHost();
        var tools = LoadKinds(host);
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("K.Flange", "Real", "e", prefix: "flow"));
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("K.Data", "Real", "extra"));
    }

    // --- Semantic restricted-class rules (causality / visibility of components) ---

    [Fact]
    public async Task AddComponent_ToFunction_PublicMustBeCausal()
    {
        using var host = new TestHost();
        var tools = LoadKinds(host);
        // A plain public component is refused...
        var err = ToolAssert.Error(await tools.AddComponent("K.f", "Real", "z"));
        Assert.Contains("input or an output", err.Error);
        // ...an input/output is fine...
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("K.f", "Real", "z", prefix: "output"));
        // ...and a protected local is fine.
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("K.f", "Real", "tmp", visibility: "protected"));
    }

    [Fact]
    public async Task AddComponent_ToRecord_NoProtectedOrConnectorPrefix()
    {
        using var host = new TestHost();
        var tools = LoadKinds(host);
        var protErr = ToolAssert.Error(await tools.AddComponent("K.Data", "Real", "p", visibility: "protected"));
        Assert.Contains("no protected section", protErr.Error);
        var flowErr = ToolAssert.Error(await tools.AddComponent("K.Data", "Real", "q", prefix: "flow"));
        Assert.Contains("flow", flowErr.Error);
    }

    [Fact]
    public async Task AddComponent_ToBlock_AcausalConnectorRejected()
    {
        using var host = new TestHost();
        // K.Flange is acausal (has 'Real s;' / 'flow Real f;') — illegal as a connector in a block.
        var err = ToolAssert.Error(await LoadKinds(host).AddComponent("K.B", "K.Flange", "port"));
        Assert.Contains("must be causal", err.Error);
    }

    [Fact]
    public async Task AddComponent_ToBlock_CausalConnectorAndPlainVariableAllowed()
    {
        using var host = new TestHost();
        var tools = LoadKinds(host);
        // A causal connector is fine...
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("K.B", "K.Causal", "port"));
        // ...and a plain (non-connector) variable is unrestricted in a block.
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("K.B", "Real", "gain"));
    }

    [Fact]
    public async Task AddComponent_ToBlock_UnresolvedConnectorType_NotBlocked()
    {
        using var host = new TestHost();
        // The type is unknown (MSL not loaded), so the causality rule cannot be applied — the component is
        // still added, with a note about the unresolved type rather than a spurious causality refusal.
        var res = ToolAssert.Ok<StructureEditResult>(
            await LoadKinds(host).AddComponent("K.B", "Modelica.Blocks.Interfaces.RealInput", "u"));
        Assert.Contains("does not resolve", res.Note);
    }

    [Fact]
    public async Task AddComponent_ToPackage_Batch_Rejected()
    {
        using var host = new TestHost();
        var tools = LoadKinds(host);
        var ops = new List<BatchOperation>
        {
            new() { Op = "add_component", ClassId = "K.Sub", Type = "Real", Name = "bad", Modifier = "= 1" },
        };
        var err = ToolAssert.Error(await tools.BatchEdit(ops));
        Assert.Contains("must be a constant", err.Error);
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
    public async Task AddComponent_WithComment_AddsCommentLine()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(
            await Load(host).AddComponent("P.A", "Real", "y", description: "out", comment: "the output signal"));
        var src = Source(host, "P.A");
        Assert.Contains("// the output signal", src);
        Assert.Contains("Real y \"out\";", src);
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
    public async Task AddComponent_ModifierList_WrappedInParens()
    {
        using var host = new TestHost();
        var tools = Load(host);

        // A single bare modifier is wrapped: k=2 -> m2(k=2)
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("P.A", "P.M", "m2", modifier: "k=2"));
        Assert.Contains("P.M m2(k=2);", Source(host, "P.A"));

        // A comma-separated list is wrapped: k=2, r=34 -> m3(k=2, r=34)
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("P.A", "P.M", "m3", modifier: "k=2, r=34"));
        Assert.Contains("P.M m3(k=2, r=34);", Source(host, "P.A"));

        // A lone value is still a binding.
        ToolAssert.Ok<StructureEditResult>(await tools.AddComponent("P.A", "Real", "v", modifier: "5"));
        Assert.Contains("Real v = 5;", Source(host, "P.A"));
    }

    [Fact]
    public async Task SetComponentModifier_List_WrapsInParens()
    {
        using var host = new TestHost();
        // M has 'parameter Real k = 1' — set a modifier list on it via a component that has a modifier slot.
        await Load(host).AddComponent("P.A", "P.M", "inst");
        ToolAssert.Ok<StructureEditResult>(
            await new StructureEditTools(host.Libraries, host.Resources, host.Session)
                .SetComponentModifier("P.A", "inst", "k=2, r=34"));
        Assert.Contains("P.M inst(k=2, r=34);", Source(host, "P.A"));
    }

    [Fact]
    public async Task AddExtends_ModifierList_WrappedInParens()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(await Load(host).AddExtends("P.A", "P.M", modifier: "k=4, r=5"));
        Assert.Contains("extends P.M(k=4, r=5);", Source(host, "P.A"));
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
    public async Task Connection_DrawsAndRefreshesLine_WhenComponentsPositioned()
    {
        using var host = new TestHost();
        var edit = LoadConn(host);
        var diagram = new DiagramTools(host.Libraries, host.Resources, host.Session);

        // Connect before positioning: no line can be drawn yet.
        ToolAssert.Ok<StructureEditResult>(await edit.AddConnection("C.Sys", "source1.y", "sink1.u"));
        Assert.DoesNotContain("Line(", host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!);

        // Positioning the first component alone still can't draw the line (the other has no placement)...
        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "source1", -10, -10, 10, 10));
        Assert.DoesNotContain("Line(", host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!);

        // ...positioning the second draws it: straight centre-to-centre, coloured as a signal line.
        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "sink1", 40, -10, 60, 10));
        var src = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;
        Assert.Contains("connect(source1.y, sink1.u) annotation (Line(points={{0,0},{50,0}}, color={0,0,127}))", src);

        // Moving a component refreshes the (2-point) line to track its new centre.
        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "source1", 90, -10, 110, 10));
        src = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;
        Assert.Contains("Line(points={{100,0},{50,0}}, color={0,0,127})", src);
    }

    [Fact]
    public async Task AddConnection_BothPositioned_DrawsLineImmediately()
    {
        using var host = new TestHost();
        var edit = LoadConn(host);
        var diagram = new DiagramTools(host.Libraries, host.Resources, host.Session);
        await diagram.SetComponentPlacement("C.Sys", "source1", -10, -10, 10, 10);
        await diagram.SetComponentPlacement("C.Sys", "sink1", 40, -10, 60, 10);

        ToolAssert.Ok<StructureEditResult>(await edit.AddConnection("C.Sys", "source1.y", "sink1.u"));
        Assert.Contains("annotation (Line(points={{0,0},{50,0}}, color={0,0,127}))",
            host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!);
    }

    [Fact]
    public async Task Connection_LeavesHandRoutedMultiPointLineAlone()
    {
        // A connect with a routed 3-point line must not be flattened when a component is repositioned.
        const string pkg = """
            within;
            package C "c"
              connector RealInput = input Real;
              connector RealOutput = output Real;
              block Source RealOutput y; end Source;
              block Sink RealInput u; end Sink;
              model Sys
                Source source1 annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
                Sink sink1 annotation (Placement(transformation(extent={{40,-10},{60,10}})));
              equation
                connect(source1.y, sink1.u) annotation (Line(points={{10,0},{25,20},{40,0}}, color={0,0,127}));
              end Sys;
            end C;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var diagram = new DiagramTools(host.Libraries, host.Resources, host.Session);

        await diagram.SetComponentPlacement("C.Sys", "source1", 90, -10, 110, 10);
        var src = host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;
        Assert.Contains("points={{10,0},{25,20},{40,0}}", src); // the routed line is preserved
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

    // Regression: a class with a trailing class annotation and no equation section. The new equation
    // section must be inserted BEFORE the annotation (the grammar requires it to be last), otherwise the
    // result does not parse. Also verifies the unresolved-type note is actionable (mentions loading a library).
    [Fact]
    public async Task AddConnection_NoEquationSection_BeforeTrailingAnnotation_Parses()
    {
        const string pkg =
            """
            within;
            package MyLib "test library"
              model Class1 "test class"
              Modelica.Blocks.Continuous.Integrator int(k=2) "an integrator";
              Modelica.Blocks.Interfaces.RealInput u;
            annotation (Documentation(info="does something"));
             end Class1;
            end MyLib;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new StructureEditTools(host.Libraries, host.Resources, host.Session);

        // MSL is not loaded, so the port types cannot be resolved — the connection is still added.
        var res = ToolAssert.Ok<StructureEditResult>(await tools.AddConnection("MyLib.Class1", "u", "int.u"));
        Assert.True(res.Changed);

        var src = host.Libraries.GetModelById("MyLib.Class1")!.Definition.ModelicaCode!;
        Assert.Contains("equation", src);
        Assert.Contains("connect(u, int.u);", src);
        // The equation section precedes the class annotation (which stays last).
        Assert.True(src.IndexOf("connect(u, int.u);", StringComparison.Ordinal)
                    < src.IndexOf("annotation (Documentation", StringComparison.Ordinal));
        // The note tells the LLM what to do: load the library that defines the unresolved type.
        Assert.NotNull(res.Note);
        Assert.Contains("load its library", res.Note);
        Assert.Contains("Modelica.Blocks.Continuous.Integrator", res.Note);
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
