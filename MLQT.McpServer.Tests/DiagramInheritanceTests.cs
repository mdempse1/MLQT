using MLQT.McpServer.Helpers;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A class's diagram is inherited: the base's connections, its diagram graphics and its coordinate
/// system are part of what the derived class draws (B316).
///
/// <para>Since B196 the inherited <i>components</i> were drawn, but the connections and the Diagram
/// layer were read from the class's own text only. For the everyday <c>extends PartialX</c> pattern
/// the picture showed the base's components with no wires and no background, which an agent reads
/// as a model nobody has connected.</para>
/// </summary>
public class DiagramInheritanceTests
{
    private static readonly string Package = """
        within;
        package I "i"
          connector Pin "pin"
            Real v;
            annotation (Icon(graphics={Ellipse(extent={{-40,-40},{40,40}}, lineColor={0,0,255})}));
          end Pin;

          model Part "part"
            Pin p annotation (Placement(transformation(extent={{90,-10},{110,10}})));
            Pin n annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, lineColor={0,0,0})}));
          end Part;

          partial model Root "the deepest layer"
            annotation (Diagram(coordinateSystem(extent={{-300,-150},{300,150}}),
              graphics={Rectangle(extent={{-290,-140},{290,140}}, lineColor={17,34,51})}));
          end Root;

          partial model PartialSys "components, wiring and a background"
            extends Root;
            Part a annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Part b annotation (Placement(transformation(extent={{40,-10},{60,10}})));
          equation
            connect(a.p, b.n) annotation (Line(points={{-39,0},{39,0}}, color={10,200,30}));
            connect(a.n, b.p);
            annotation (Diagram(graphics={Text(extent={{-100,60},{100,80}}, textString="from the base",
              textColor={200,100,50})}));
          end PartialSys;

          model Sys "only the extends"
            extends PartialSys;
          end Sys;

          model Short = PartialSys "the same, as a short class";

          block Gain "ports it does not place"
            input Real u;
            output Real y;
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, lineColor={0,0,0})}));
          end Gain;

          partial model PartialChain "an unannotated connection between inherited blocks"
            Gain g1 annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Gain g2 annotation (Placement(transformation(extent={{40,-10},{60,10}})));
          equation
            connect(g1.y, g2.u);
          end PartialChain;

          model Chain "only the extends"
            extends PartialChain;
          end Chain;
        end I;
        """.Replace("\r\n", "\n");

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "Pin\nPart\nRoot\nPartialSys\nSys\nShort\nGain\nPartialChain\nChain\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static string Svg(TestHost host, string classId)
        => DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById(classId)!, 800)!;

    [Fact]
    public void AnInheritedConnectionIsDrawn_AsItsBaseAnnotatedIt()
    {
        using var host = Load();

        Assert.Contains("points=\"-39,0 39,0\" fill=\"none\" stroke=\"#0AC81E\"", Svg(host, "I.Sys"));
    }

    [Fact]
    public void AnInheritedConnectionWithNoLineIsRouted()
    {
        using var host = Load();

        // Two polylines: the annotated one and the one routed from a.n to b.p.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(Svg(host, "I.Sys"), "<polyline").Count);
    }

    [Fact]
    public void TheBasesDiagramGraphicsAreDrawn_DeepestFirst()
    {
        using var host = Load();

        var svg = Svg(host, "I.Sys");

        Assert.Contains(">from the base<", svg);
        var root = svg.IndexOf("#112233", StringComparison.Ordinal);
        Assert.True(root >= 0, "Root's background is drawn");
        Assert.True(root < svg.IndexOf(">from the base<", StringComparison.Ordinal),
            "the deepest base is the bottom layer");
    }

    [Fact]
    public void TheCoordinateSystemIsInherited()
    {
        using var host = Load();

        Assert.Contains("viewBox=\"-300 -150 600 300\"", Svg(host, "I.Sys"));
    }

    [Fact]
    public void AShortClassDrawsItsBasesDiagram_WiresAndBackgroundIncluded()
    {
        // `model Short = PartialSys` is an extends clause in all but syntax. Its components came
        // through the element walk and its wiring and graphics did not, which drew the picture B316
        // was about: the base's parts, unconnected, on nothing.
        using var host = Load();

        Assert.Equal(Svg(host, "I.Sys"), Svg(host, "I.Short"));
    }

    [Fact]
    public void AnInheritedComponentsUnplacedPortIsGuessedFromItsCausality()
    {
        // Gain places neither port, so the router falls back to input-left, output-right - which
        // needs the component's type, and the class's own text does not declare the component.
        using var host = Load();

        Assert.Contains("points=\"-40,0 40,0\"", Svg(host, "I.Chain"));
    }
}
