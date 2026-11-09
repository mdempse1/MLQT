using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A diagram's lines are re-routed only where an edit touches them: add_connection draws the connect
/// it adds, set_component_placement re-routes the connects ending on the component it moves, and
/// remove_connection takes only its own. Each used to route every connect in the class, so one edit
/// to a hand-drawn library - or to a model an agent had already tidied - replaced every line with the
/// default route and dropped its colour and thickness.
/// </summary>
public class ConnectionLineOwnershipTests
{
    // source1 -> sink2 is drawn by hand: a route of its own, a colour the router would not pick, a
    // thickness. Every component is placed, so the router could route any connect in the class.
    private const string HandDrawn =
        "connect(source1.y, sink2.u) annotation (Line(\n" +
        "        points={{10,0},{20,0},{20,-40},{40,-40}},\n" +
        "        color={10,20,30},\n" +
        "        thickness=0.5));";

    private const string Package = """
        within;
        package C "c"
          connector RealInput = input Real;
          connector RealOutput = output Real;
          block Source "src"
            RealOutput y;
          end Source;
          block Sink "snk"
            RealInput u;
          end Sink;
          model Sys
            Source source1 annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            Sink sink1 annotation (Placement(transformation(extent={{40,30},{60,50}})));
            Sink sink2 annotation (Placement(transformation(extent={{40,-50},{60,-30}})));
            Sink sink3 annotation (Placement(transformation(extent={{40,70},{60,90}})));
          equation
            HAND_DRAWN
            connect(source1.y, sink3.u);
          end Sys;
        end C;
        """;

    private static (StructureEditTools Edit, DiagramTools Diagram) Load(TestHost host)
    {
        // A raw literal carries the checkout's line endings; the hand-drawn line is compared as stored.
        var package = Package.ReplaceLineEndings("\n").Replace("HAND_DRAWN", HandDrawn);
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (new StructureEditTools(host.Libraries, host.Resources, host.Session),
                new DiagramTools(host.Libraries, host.Resources, host.Session));
    }

    private static string Source(TestHost host) => host.Libraries.GetModelById("C.Sys")!.Definition.ModelicaCode!;

    private static int Count(string text, string what) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(what)).Count;

    [Fact]
    public async Task AddingAConnection_DrawsIt_AndLeavesEveryOtherConnectAsWritten()
    {
        using var host = new TestHost();
        var (edit, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await edit.AddConnection("C.Sys", "source1.y", "sink1.u"));

        var src = Source(host);
        Assert.Contains("connect(source1.y, sink1.u) annotation (Line(points=", src);
        Assert.Contains(HandDrawn, src);
        Assert.Contains("connect(source1.y, sink3.u);", src); // unannotated, and not this edit's
    }

    [Fact]
    public async Task AddingADuplicateConnection_DrawsTheNewOne_AndLeavesTheOneThereAsWritten()
    {
        using var host = new TestHost();
        var (edit, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await edit.AddConnection("C.Sys", "source1.y", "sink2.u"));

        var src = Source(host);
        Assert.Contains(HandDrawn, src);
        Assert.Equal(2, Count(src, "connect(source1.y, sink2.u) annotation (Line("));
    }

    [Fact]
    public async Task MovingAComponent_LeavesConnectsThatDoNotEndOnIt_AsWritten()
    {
        using var host = new TestHost();
        var (_, diagram) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "sink1", 40, 0, 60, 20));

        Assert.Contains(HandDrawn, Source(host));
        Assert.Contains("connect(source1.y, sink3.u);", Source(host));
    }

    [Fact]
    public async Task MovingAComponent_ReRoutesItsOwnConnects_KeepingTheirColourAndThickness()
    {
        using var host = new TestHost();
        var (_, diagram) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "sink2", 40, -90, 60, -70));

        var src = Source(host);
        Assert.DoesNotContain("{20,-40},{40,-40}", src);  // the route follows sink2
        Assert.Contains("{40,-80}}", src);                 // ...to its input, on its left edge
        Assert.Contains("color={10,20,30}", src);          // the author's colour
        Assert.Contains("thickness=0.5", src);             // and thickness, kept
        Assert.Contains("connect(source1.y, sink3.u);", src);
    }

    [Fact]
    public async Task MovingAComponent_DrawsItsUnannotatedConnects()
    {
        using var host = new TestHost();
        var (_, diagram) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "sink3", 40, 60, 60, 80));

        var src = Source(host);
        Assert.Contains("connect(source1.y, sink3.u) annotation (Line(points=", src);
        Assert.Contains(HandDrawn, src);
    }

    [Fact]
    public async Task RemovingAConnection_LeavesEveryOtherConnectAsWritten()
    {
        using var host = new TestHost();
        var (edit, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await edit.RemoveConnection("C.Sys", "source1.y", "sink3.u"));

        var src = Source(host);
        Assert.DoesNotContain("sink3.u", src);
        Assert.Contains(HandDrawn, src);
    }
}
