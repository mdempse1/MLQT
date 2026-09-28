using MLQT.McpServer.Helpers;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The Diagram coordinate system a class inherits follows MLS 3.6 §18.6.1.1 (B394): each attribute
/// from the class where it states it, else from the <b>first</b> base whose extends clause leaves
/// <c>DiagramMap</c> at the null region, else the default. It used to be "the most derived class that
/// states an extent", which took the last of two bases rather than the first.
/// </summary>
public class DiagramCoordinateSystemTests
{
    private static readonly string Package = """
        within;
        package C "c"
          partial model Wide "states a wide diagram"
            annotation (Diagram(coordinateSystem(extent={{-300,-150},{300,150}}),
              graphics={Rectangle(extent={{-20,-20},{20,20}})}));
          end Wide;

          partial model Plain "states nothing"
            annotation (Diagram(graphics={Rectangle(extent={{-10,-10},{10,10}})}));
          end Plain;

          model WideFirst "the first base states one"
            extends Wide;
            extends Plain;
          end WideFirst;

          model PlainFirst "the first base states none"
            extends Plain;
            extends Wide;
          end PlainFirst;

          model MappedFirst "the first base is mapped into a region, so the next one lends"
            extends Plain annotation (DiagramMap(extent={{-50,-50},{50,50}}));
            extends Wide;
          end MappedFirst;

          model AspectOnly "states preserveAspectRatio and no extent"
            extends Wide;
            annotation (Diagram(coordinateSystem(preserveAspectRatio=false)));
          end AspectOnly;
        end C;
        """.Replace("\r\n", "\n");

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "Wide\nPlain\nWideFirst\nPlainFirst\nMappedFirst\nAspectOnly\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static string Svg(TestHost host, string classId)
        => DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById(classId)!, 800)!;

    [Fact]
    public void TheFirstBaseLendsItsSystem()
    {
        using var host = Load();

        Assert.Contains("viewBox=\"-300 -150 600 300\"", Svg(host, "C.WideFirst"));
    }

    [Fact]
    public void AFirstBaseThatStatesNothing_LendsTheDefault_NotALaterBasesSystem()
    {
        using var host = Load();

        Assert.Contains("viewBox=\"-100 -100 200 200\"", Svg(host, "C.PlainFirst"));
    }

    [Fact]
    public void ABaseMappedIntoARegion_IsPassedOver()
    {
        using var host = Load();

        Assert.Contains("viewBox=\"-300 -150 600 300\"", Svg(host, "C.MappedFirst"));
    }

    [Fact]
    public void StatingOnlyTheAspectRatio_KeepsTheBasesExtent()
    {
        using var host = Load();

        Assert.Contains("viewBox=\"-300 -150 600 300\"", Svg(host, "C.AspectOnly"));
    }
}
