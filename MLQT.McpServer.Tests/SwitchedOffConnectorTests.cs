using MLQT.McpServer.Helpers;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A connection to a conditional connector the instance has switched off ends where that connector
/// would be. Modelica allows the connection (it is dropped), and Dymola draws the line to the
/// connector's place. The router knew only the connectors the image draws - a switched-off one is
/// not drawn (B277) - so it guessed, and for a mechanical flange the guess was the component's
/// centre: every VehicleInterfaces example connects its engine's optional accelerator pedal, off by
/// default, and add_connection routed that line into the middle of the engine.
/// </summary>
public class SwitchedOffConnectorTests
{
    private const string Package = """
        within;
        package S "s"
          connector Flange "acausal"
            Real s;
            flow Real f;
          end Flange;

          model Engine "a pedal connector on its top edge, off by default"
            parameter Boolean usePedal = false;
            Flange pedal if usePedal annotation (Placement(transformation(extent={{-10,90},{10,110}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Engine;

          model Driver "a pedal connector on its bottom edge"
            Flange pedal annotation (Placement(transformation(extent={{-10,-110},{10,-90}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Driver;

          model Sys
            Engine engine annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            Driver driver annotation (Placement(transformation(extent={{-10,40},{10,60}})));
          equation
            connect(driver.pedal, engine.pedal);
          end Sys;
        end S;
        """;

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package.ReplaceLineEndings("\n"),
            ["package.order"] = "Flange\nEngine\nDriver\nSys\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    [Fact]
    public void ALineToASwitchedOffConnector_EndsWhereTheConnectorWouldBe_NotAtTheComponentsCentre()
    {
        using var host = Load();
        var code = host.Libraries.GetModelById("S.Sys")!.Definition.ModelicaCode!;

        var route = DiagramGeometry.RouteConnection(host.Libraries, "S.Sys", code, "driver.pedal", "engine.pedal");

        Assert.NotNull(route);
        Assert.Equal(new DiagramGeometry.Pt(0, 40), route![0]);   // the driver's pedal, on its bottom edge
        Assert.Equal(new DiagramGeometry.Pt(0, 10), route[^1]);   // the engine's, on its top edge - not (0,0)
    }

    [Fact]
    public void ASwitchedOffConnector_IsStillNotDrawn()
    {
        // Locating it is not drawing it: the instance has no such port, and the picture says so (B277).
        using var host = Load();

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("S.Sys")!, 800);

        Assert.NotNull(svg);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg!, ">pedal<"));  // the driver's label only
    }
}
