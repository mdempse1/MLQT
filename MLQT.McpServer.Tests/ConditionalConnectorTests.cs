using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A port that is switched off, and a label that says what a parameter is (B277, B278).
///
/// <para>Both were found putting <c>get_diagram_image</c>'s render of
/// <c>Modelica.Blocks.Examples.PID_Controller</c> beside Dymola's: four of its nine components drew a
/// connector Dymola leaves off — each declared <c>if</c> some parameter that is false by default —
/// and every parameter label read <c>J=%J</c> rather than <c>J=1</c>.</para>
///
/// <para>Both are the same question underneath: <b>what is this parameter, for this instance</b>,
/// answered by the modification the declaration gave it and otherwise by the type's own default.</para>
/// </summary>
public class ConditionalConnectorTests
{
    /// <summary>MSL's shape: a conditional port governed by a parameter declared in a base class.</summary>
    private static readonly string Package = PackageSource.Replace("\r\n", "\n");

    private const string PackageSource = """
        within;
        package Lib "l"
          connector Flange "mechanical"
            annotation (Icon(graphics={Ellipse(extent={{-100,-100},{100,100}}, fillColor={95,95,95},
              fillPattern=FillPattern.Solid)}));
          end Flange;

          connector HeatPort "thermal"
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, lineColor={191,0,0},
              fillColor={191,0,0}, fillPattern=FillPattern.Solid)}));
          end HeatPort;

          partial model PartialHeat "declares the switch, as MSL does"
            parameter Boolean useHeatPort = false "= true, to expose the heat port";
            HeatPort heatPort if useHeatPort "conditional"
              annotation (Placement(transformation(extent={{-110,-110},{-90,-90}})));
          end PartialHeat;

          model Damper "a damper with an optional heat port"
            extends PartialHeat;
            parameter Real d = 1 "damping";
            Flange flange annotation (Placement(transformation(extent={{90,-10},{110,10}})));
            annotation (Icon(graphics={
              Rectangle(extent={{-100,-100},{100,100}}),
              Text(extent={{-150,-160},{150,-120}}, textString="d=%d")}));
          end Damper;

          model Plant "two dampers, one of which wants its heat port"
            Damper cold annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Damper hot(useHeatPort=true, d=250) annotation (Placement(transformation(extent={{20,-10},{40,10}})));
          end Plant;
        end Lib;
        """;

    private static TestHost Load(string? packageMo = null)
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = packageMo ?? Package,
            ["package.order"] = "Flange\nHeatPort\nPartialHeat\nDamper\nPlant\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static string Render(TestHost host, string classId = "Lib.Plant") =>
        DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById(classId)!, 800)!;

    /// <summary>
    /// The heat port's fill, which nothing else in the fixture uses. The fill and not the colour on
    /// its own: the port is drawn in one colour throughout, so counting the colour counts each port
    /// twice and every expectation below would have been written to match.
    /// </summary>
    private const string HeatPortColour = "fill=\"#BF0000\"";

    [Fact]
    public void AConnectorSwitchedOffIsNotDrawn()
    {
        using var host = Load();
        var svg = Render(host);

        // `hot` enabled it and `cold` did not, so exactly one heat port is on the picture — and the
        // flange, which is unconditional, is on both.
        Assert.Equal(1, Regex(svg, HeatPortColour));
        Assert.Equal(2, Regex(svg, "fill=\"#5F5F5F\""));
    }

    [Fact]
    public void ADefaultOfTrueIsEnoughOnItsOwn()
    {
        // The switch is a parameter like any other: nothing has to modify it for the port to exist.
        using var host = Load(Package.Replace(
            "parameter Boolean useHeatPort = false", "parameter Boolean useHeatPort = true",
            StringComparison.Ordinal));

        Assert.Equal(2, Regex(Render(host), HeatPortColour));
    }

    [Fact]
    public void AConditionThatCannotBeWorkedOutIsDrawn()
    {
        // Showing a port that is switched off is a smaller lie than hiding one that is switched on,
        // and a connection into it is real either way.
        using var host = Load(Package.Replace(
            "HeatPort heatPort if useHeatPort", "HeatPort heatPort if d > 0",
            StringComparison.Ordinal));

        Assert.Equal(2, Regex(Render(host), HeatPortColour));
    }

    [Fact]
    public void TheClassesOwnDiagramStillShowsWhatItDeclares()
    {
        // On its own diagram a conditional connector is a declaration to be seen and edited, which
        // is what Dymola shows there too — the rule is about an instance's icon.
        using var host = Load();

        Assert.Contains(HeatPortColour, Render(host, "Lib.PartialHeat"));
    }

    // --- B278 -------------------------------------------------------------------------------------

    [Fact]
    public void AnIconLabelSaysWhatTheParameterIs()
    {
        using var host = Load();
        var svg = Render(host);

        Assert.Contains(">d=250<", svg);   // what `hot` was given
        Assert.Contains(">d=1<", svg);     // what `cold` inherits from the type
        Assert.DoesNotContain("%d", svg);
    }

    // --- what an agent is told --------------------------------------------------------------------

    [Fact]
    public void TheInterfaceSaysAConnectorIsConditional()
    {
        // Listing it without saying so tells an agent it can connect to a port that may not be there.
        using var host = Load();

        var view = ToolAssert.Ok<ClassInterfaceView>(
            new ViewTools(host.Libraries).GetClassInterface("Lib.Damper"));

        Assert.Equal("useHeatPort", Assert.Single(view.Connectors, c => c.Name == "heatPort").Condition);
        Assert.Null(Assert.Single(view.Connectors, c => c.Name == "flange").Condition);

        var elements = ToolAssert.Ok<ClassElementsResult>(
            new ViewTools(host.Libraries).ListClassElements("Lib.Damper"));
        Assert.Equal("useHeatPort", Assert.Single(elements.Elements, e => e.Name == "heatPort").Condition);
    }

    private static int Regex(string text, string pattern) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(pattern)).Count;
}
