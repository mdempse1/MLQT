using MLQT.McpServer.Helpers;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A diagram draws each base's Diagram graphics as the extends clause's <c>DiagramMap</c> says (MLS
/// 3.6 §18.6.3, B420): hidden with <c>primitivesVisible=false</c>, mapped into a region with a
/// non-null extent. Both used to be ignored - the base's graphics were drawn, at their own size.
/// </summary>
public class DiagramMapTests
{
    private static readonly string Package = """
        within;
        package C "c"
          partial model Square "a square filling the default system"
            annotation (Diagram(graphics={Rectangle(extent={{-80,-80},{80,80}})}));
          end Square;

          model Hidden "hides the base's graphics"
            extends Square annotation (DiagramMap(primitivesVisible=false));
            annotation (Diagram(graphics={Ellipse(extent={{-7,-7},{7,7}})}));
          end Hidden;

          model Mapped "maps the base into the upper right quarter"
            extends Square annotation (DiagramMap(extent={{0,0},{100,100}}));
          end Mapped;

          model Plain "draws the base where it is"
            extends Square;
          end Plain;
        end C;
        """.Replace("\r\n", "\n");

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "Square\nHidden\nMapped\nPlain\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static string Svg(TestHost host, string classId)
        => DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById(classId)!, 800)!;

    [Fact]
    public void TheBaseIsDrawnWhereItIs_WithoutAMap()
    {
        using var host = Load();

        Assert.Contains("<rect x=\"-80\" y=\"-80\" width=\"160\" height=\"160\"", Svg(host, "C.Plain"));
    }

    [Fact]
    public void PrimitivesVisibleFalse_HidesTheBasesGraphics()
    {
        using var host = Load();

        var svg = Svg(host, "C.Hidden");

        Assert.DoesNotContain("<rect x=\"-80\"", svg);
        Assert.Contains("<ellipse", svg);
    }

    [Fact]
    public void AnExtent_MapsTheBasesGraphicsIntoThatRegion()
    {
        using var host = Load();

        var svg = Svg(host, "C.Mapped");

        // -100..100 onto 0..100 is a half, centred at (50,50): the square becomes 10..90, drawn as
        // -40..40 about its origin, which the map moved to the region's centre.
        Assert.Contains("<rect x=\"-40\" y=\"-40\" width=\"80\" height=\"80\" transform=\"translate(50,50)\"", svg);
        Assert.DoesNotContain("<rect x=\"-80\"", svg);
    }
}
