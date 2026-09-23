using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Where a component actually sits on a diagram, and what it is drawn as.
///
/// <para>Reported against the render of <c>Modelica.Blocks.Continuous.Integrator</c>, which came out
/// as two oversized triangles overlapping in the middle of an otherwise empty canvas where Dymola
/// draws four connectors around the edge. Three separate defects, all visible in that one picture
/// and none of them visible in a unit test that existed:</para>
///
/// <list type="number">
/// <item>the <c>origin</c> of a Placement was ignored, and a Modelica extent is stated relative to
/// it — so every component that uses one was drawn at the centre of the diagram;</item>
/// <item>only components the class <em>declares</em> were drawn, and most blocks in the Modelica
/// Standard Library get their <c>u</c> and <c>y</c> from a base class;</item>
/// <item>a connector was drawn with its icon layer, where Modelica gives it a separate diagram
/// layer for exactly this position — a different, smaller drawing that carries its name.</item>
/// </list>
///
/// <para>The fixture is the shape MSL's Integrator has: two ports inherited from a SISO-like base,
/// and two optional connectors placed with an origin and a rotation.</para>
/// </summary>
public class DiagramPlacementTests
{
    private static readonly string Package = PackageSource.Replace("\r\n", "\n");

    private const string PackageSource = """
        within;
        package Lib "l"
          connector RealInput "in"
            annotation (
              Icon(coordinateSystem(extent={{-100,-100},{100,100}}), graphics={Polygon(
                points={{-100,100},{100,0},{-100,-100}}, lineColor={0,0,127}, fillColor={0,0,127},
                fillPattern=FillPattern.Solid)}),
              Diagram(coordinateSystem(extent={{-100,-100},{100,100}}), graphics={
                Polygon(points={{0,50},{100,0},{0,-50},{0,50}}, lineColor={0,0,127},
                  fillColor={0,0,127}, fillPattern=FillPattern.Solid),
                Text(extent={{-10,60},{-10,85}}, textColor={0,0,127}, textString="%name")}));
          end RealInput;

          connector RealOutput "out"
            annotation (Icon(graphics={Polygon(points={{-100,60},{100,0},{-100,-60}},
              lineColor={0,0,127}, fillColor={255,255,255}, fillPattern=FillPattern.Solid)}));
          end RealOutput;

          partial block SISO "single in, single out"
            RealInput u "in" annotation (Placement(transformation(extent={{-140,-20},{-100,20}})));
            RealOutput y "out" annotation (Placement(transformation(extent={{100,-10},{120,10}})));
          end SISO;

          block Integrator "integrates"
            extends SISO;
            RealInput reset "optional reset" annotation (Placement(transformation(
              extent={{-20,-20},{20,20}}, rotation=90, origin={60,-120})));
            RealInput set "optional set" annotation (Placement(transformation(
              extent={{-20,-20},{20,20}}, rotation=270, origin={60,120})));
          end Integrator;

          block Framed "placed twice, for two layers"
            RealInput u "in" annotation (Placement(
              transformation(extent={{-140,-20},{-100,20}}),
              iconTransformation(extent={{0,0},{80,80}})));
          end Framed;
        end Lib;
        """;

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "RealInput\nRealOutput\nSISO\nIntegrator\nFramed\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static IReadOnlyDictionary<string, DiagramGeometry.Placement> Placements(TestHost host, string classId)
        => DiagramGeometry.Placements(
            host.Libraries, classId, host.Libraries.GetModelById(classId)!.Definition.ModelicaCode ?? "");

    // --- 1. the origin --------------------------------------------------------------------------

    [Fact]
    public void AnExtentIsRelativeToItsOrigin()
    {
        using var host = Load();

        var reset = Placements(host, "Lib.Integrator")["reset"];

        // origin={60,-120} with extent={{-20,-20},{20,20}} is a 40x40 box on the bottom edge, not a
        // 40x40 box in the middle of the diagram.
        Assert.Equal([40d, -140d, 80d, -100d], reset.Extent);
        Assert.Equal(90, reset.Rotation);

        // ...and the rotation turns about the origin, which is what Modelica says it turns about.
        Assert.Equal([60d, -120d], reset.RotationCentre);
    }

    [Fact]
    public void WithNoOriginTheExtentIsAlreadyAbsolute()
    {
        using var host = Load();

        var u = Placements(host, "Lib.Integrator")["u"];

        Assert.Equal([-140d, -20d, -100d, 20d], u.Extent);
        Assert.Equal(0, u.Rotation);
        Assert.Equal([-120d, 0d], u.RotationCentre);
    }

    [Fact]
    public void TheIconTransformationIsNotReadInsteadOfTheTransformation()
    {
        // They are two answers to two questions - where the component sits on the enclosing class's
        // diagram, and where it sits on its icon - and searching a declaration for the first
        // "extent=" returns whichever happens to be written first.
        using var host = Load();

        Assert.Equal([-140d, -20d, -100d, 20d], Placements(host, "Lib.Framed")["u"].Extent);
    }

    // --- 2. inherited components ----------------------------------------------------------------

    [Fact]
    public void AnInheritedConnectorIsPlacedAndDrawn()
    {
        using var host = Load();

        var placements = Placements(host, "Lib.Integrator");
        Assert.Equal(["reset", "set", "u", "y"], placements.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("Lib.Integrator")!, 800)!;

        // Each drawn at the centre of its own placement. y carries no %name of its own here, so it
        // is found by where it went rather than by a label - which is the thing under test anyway.
        Assert.Contains("translate(-120,0)", svg);   // u,     inherited
        Assert.Contains("translate(110,0)", svg);    // y,     inherited
        Assert.Contains("translate(60,120)", svg);   // set,   declared, placed by its origin
        Assert.Contains("translate(60,-120)", svg);  // reset, declared, placed by its origin
    }

    [Fact]
    public void ADeclarationShadowsAnInheritedOneOfTheSameName()
    {
        // Redeclaring a base class's component is how a derived class moves a port, and the
        // derived placement is the one that counts.
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package.Replace(
                "    extends SISO;\n",
                "    extends SISO;\n"
                + "    RealInput u \"moved\" annotation (Placement(transformation(extent={{-40,-40},{0,0}})));\n",
                StringComparison.Ordinal),
            ["package.order"] = "RealInput\nRealOutput\nSISO\nIntegrator\nFramed\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        Assert.Equal([-40d, -40d, 0d, 0d], Placements(host, "Lib.Integrator")["u"].Extent);
    }

    // --- 3. the connector's diagram layer -------------------------------------------------------

    [Fact]
    public void AConnectorIsDrawnWithItsDiagramLayer_NotItsIcon()
    {
        using var host = Load();

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("Lib.Integrator")!, 800)!;

        // RealInput's DIAGRAM polygon: a smaller triangle against the edge, carrying the name.
        Assert.Contains("0,50 100,0 0,-50", svg);
        Assert.Contains(">u<", svg);
        // Its ICON polygon fills the whole coordinate system and has no name beside it. Drawn in a
        // diagram it comes out several times the size a Modelica tool draws.
        Assert.DoesNotContain("-100,100 100,0 -100,-100", svg);
    }

    [Fact]
    public void AConnectorWithNoDiagramLayerFallsBackToItsIcon()
    {
        // RealOutput here declares only an Icon, which is legal and must still draw something.
        using var host = Load();

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("Lib.Integrator")!, 800)!;

        Assert.Contains("-100,60 100,0 -100,-60", svg);   // RealOutput's icon polygon
    }

    [Fact]
    public void AnOrdinaryComponentIsStillDrawnWithItsIcon()
    {
        // The diagram layer is the connector rule, not a general one: a block's diagram is its own
        // drawing and is not what it looks like inside someone else's.
        using var host = Load();

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("Lib.Integrator")!, 800)!;
        Assert.NotNull(svg);
    }

    // --- the numbers and the picture describe one diagram ----------------------------------------

    [Fact]
    public void GetDiagramLayoutReportsWhatGetDiagramImageDraws()
    {
        // They had a regex each. The tool reported a 40x40 box at the centre for a connector the
        // image drew on the bottom edge, and left out the inherited ports the image now draws.
        using var host = Load();
        var tools = new DiagramTools(host.Libraries, host.Resources, host.Session);

        var layout = ToolAssert.Ok<DiagramLayoutResult>(tools.GetDiagramLayout("Lib.Integrator"));
        var placements = Placements(host, "Lib.Integrator");

        foreach (var component in layout.Components.Where(c => c.Extent is not null))
        {
            var placement = placements[component.Name];
            Assert.Equal(
                placement.Extent.Select(v => (int)Math.Round(v)).ToArray(),
                component.Extent!.ToArray());
        }

        var u = Assert.Single(layout.Components, c => c.Name == "u");
        Assert.Equal("Lib.SISO", u.InheritedFrom);
        Assert.Equal([-140, -20, -100, 20], u.Extent);

        var reset = Assert.Single(layout.Components, c => c.Name == "reset");
        Assert.Null(reset.InheritedFrom);
        Assert.Equal([40, -140, 80, -100], reset.Extent);
        Assert.Equal(90, reset.Rotation);
    }
}
