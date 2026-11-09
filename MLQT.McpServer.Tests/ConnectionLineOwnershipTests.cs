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
        "        points={{10,0},{20.5,0},{20.5,-40},{40,-40}},\n" +
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
    public async Task MovingAComponent_MovesOnlyTheEndOfItsLines_KeepingTheRouteColourAndThickness()
    {
        // sink2 moves 40 down. Its input goes with it, the bend before it follows, and the rest of the
        // hand-drawn route - its 20.5 included, which the router would have rounded - stays.
        using var host = new TestHost();
        var (_, diagram) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("C.Sys", "sink2", 40, -90, 60, -70));

        var src = Source(host);
        Assert.Contains("points={{10,0},{20.5,0},{20.5,-80},{40,-80}}", src);
        Assert.Contains("color={10,20,30}", src);
        Assert.Contains("thickness=0.5", src);
        Assert.Contains("connect(source1.y, sink3.u);", src);
    }

    [Fact]
    public async Task MovingAComponent_MovesItsLines_EvenWhereItsConnectorCannotBeLocated()
    {
        // The type is not loaded, so the router cannot place the connector, and nothing used to move
        // the line - an agent deleted the connection and added it again. A pure move carries the
        // connector by the component's displacement, which needs no type.
        const string pkg = """
            within;
            package U "u"
              connector RealOutput = output Real;
              block Source "src"
                RealOutput y;
              end Source;
              model Sys
                Source source1 annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
                Missing.Sink sink1 annotation (Placement(transformation(extent={{40,-10},{60,10}})));
              equation
                connect(source1.y, sink1.u) annotation (Line(points={{10,0},{40,0}}, color={0,0,127}));
              end Sys;
            end U;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var diagram = new DiagramTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("U.Sys", "sink1", 40, 10, 60, 30));

        Assert.Contains("points={{10,0},{25,0},{25,20},{40,20}}, color={0,0,127}",
            host.Libraries.GetModelById("U.Sys")!.Definition.ModelicaCode!);
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

    // A source, one sink and an array of sinks, all placed; HAND_DRAWN is the connect under test.
    private const string Small = """
        within;
        package D "d"
          connector RealInput = input Real;
          connector RealOutput = output Real;
          block Source "src"
            RealOutput y annotation (Placement(transformation(extent={{100,-10},{120,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Source;
          block Sink "snk"
            RealInput u annotation (Placement(transformation(extent={{-120,-10},{-100,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Sink;
          model Sys
            parameter Integer n = 2;
            Source source1 annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            Sink sink2 annotation (Placement(transformation(extent={{40,-50},{60,-30}})));
            Sink sinks[n] annotation (Placement(transformation(extent={{40,30},{60,50}})));
          equation
            HAND_DRAWN
          end Sys;
        end D;
        """;

    private static (StructureEditTools Edit, DiagramTools Diagram) LoadSmall(TestHost host, string connect)
    {
        var package = Small.ReplaceLineEndings("\n").Replace("HAND_DRAWN", connect);
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (new StructureEditTools(host.Libraries, host.Resources, host.Session),
                new DiagramTools(host.Libraries, host.Resources, host.Session));
    }

    private static string SmallSource(TestHost host) => host.Libraries.GetModelById("D.Sys")!.Definition.ModelicaCode!;

    [Fact]
    public async Task AddingAConnection_WithSpacesInAPort_StillDrawsIt()
    {
        // The clause's ports are read without white space, so `sinks[1 + 1].u` as written was not found
        // among them and the new connect got no line.
        using var host = new TestHost();
        var (edit, _) = LoadSmall(host, "");

        ToolAssert.Ok<StructureEditResult>(await edit.AddConnection("D.Sys", "source1.y", "sinks[1 + 1].u"));

        Assert.Contains("connect(source1.y, sinks[1 + 1].u) annotation (Line(points=", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_KeepsTheTextOfEveryNumberItDidNotChange()
    {
        // Written with .0 and spaces: the two points that do not move stay exactly as written, and of
        // the two that do, the coordinate that does not change keeps its text.
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(points={{11.0, 0.0}, {20.0, 0.0}, {20.0, -40.0}, {39.0, -40.0}}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -90, 60, -70));

        Assert.Contains("points={{11.0, 0.0}, {20.0, 0.0}, {20.0,-80}, {39.0,-80}}", SmallSource(host));
    }

    [Fact]
    public async Task PlacingAComponentWhereItAlreadyIs_ChangesNoLine()
    {
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(points = {{11.0, 0.0}, {39.0, -40.0}}));");
        var before = SmallSource(host);

        await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -50, 60, -30);

        Assert.Contains("Line(points = {{11.0, 0.0}, {39.0, -40.0}})", SmallSource(host));
        Assert.Equal(before.Length, SmallSource(host).Length);
    }

    [Fact]
    public async Task MovingAComponent_LeavesALineWhosePointsAreNotLiteral_AsWritten()
    {
        // The author's expression is not a route to replace with the router's default.
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(points={{x0,0},{39,-40}}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -90, 60, -70));

        Assert.Contains("Line(points={{x0,0},{39,-40}})", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_ReadsEveryModelicaNumberForm()
    {
        // `11.` and `.5e1` are numbers: the line is literal, and its end follows the move.
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(points={{11.,0},{.5e1,0},{5,-40},{39,-40}}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -90, 60, -70));

        Assert.Contains("points={{11.,0},{.5e1,0},{5,-80},{39,-80}}", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_BringsTheLinesOfItsLoopConnects()
    {
        // A loop's connect carries one line for every index, drawn by the author; a pure move carries its
        // end with the component. It was skipped, and left behind.
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "for i in 1:n loop\n      connect(source1.y, sinks[i].u) annotation (Line(points={{11,0},{20,0},{20,40},{39,40}}));\n    end for;");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sinks", 40, 60, 60, 80));

        Assert.Contains("points={{11,0},{20,0},{20,70},{39,70}}", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_StillAddsNoLineToALoopConnectThatHasNone()
    {
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host, "for i in 1:n loop\n      connect(source1.y, sinks[i].u);\n    end for;");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sinks", 40, 60, 60, 80));

        Assert.Contains("connect(source1.y, sinks[i].u);", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_PutsADoglegIntoAStraightLine_BetweenThePointsItKeeps()
    {
        // The far end did not move, so the straight line gets a dogleg at its middle, written in
        // between the points as the line separates them.
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(points={{11,-40},{39,-40}}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -90, 60, -70));

        Assert.Contains("points={{11,-40},{25,-40},{25,-80},{39,-80}}", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_GivesPointsToALineThatHasNone_KeepingWhatItSays()
    {
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(color={10,20,30}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -90, 60, -70));

        Assert.Contains("annotation (Line(points={{", SmallSource(host));
        Assert.Contains("color={10,20,30}", SmallSource(host));
    }

    [Fact]
    public async Task TurningAComponent_PutsTheEndWhereTheRouterFindsTheConnector()
    {
        // A turn moves the connector relative to the component, so the move's displacement cannot say
        // where it went: the router does. sink2 turned half round has its input on its right edge.
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(source1.y, sink2.u) annotation (Line(points={{11,0},{20,0},{20,-40},{39,-40}}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sink2", 40, -50, 60, -30, rotation: 180));

        Assert.Contains("{61,-40}}", SmallSource(host));
        Assert.Contains("{11,0},{20,0}", SmallSource(host));
    }

    [Fact]
    public async Task MovingAComponent_MovesALineWithBothEndsOnIt_Whole()
    {
        using var host = new TestHost();
        var (_, diagram) = LoadSmall(host,
            "connect(sinks[1].u, sinks[2].u) annotation (Line(points={{39,40},{30,40},{30,45},{39,45}}));");

        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("D.Sys", "sinks", 40, 60, 60, 80));

        Assert.Contains("points={{39,70},{30,70},{30,75},{39,75}}", SmallSource(host));
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
