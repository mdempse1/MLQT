using MLQT.McpServer.Helpers;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A <c>%parameter</c> label says what the parameter is set to, spaces and all, and shortens a
/// dotted value only when it names a constant (B317).
///
/// <para><c>y=pulse.y and step.y</c> was read with <c>GetText()</c> as <c>pulse.yandstep.y</c> - one
/// dotted name - and the label, which shows an enumeration literal by its last segment, printed
/// <b>y</b>.</para>
/// </summary>
public class DiagramLabelTests
{
    private static readonly string Package = """
        within;
        package L "l"
          package Types "types"
            type Mode = enumeration(Slow, Fast);
          end Types;

          block Show "labelled with its parameter"
            parameter Real k = 1;
            annotation (Icon(graphics={
              Rectangle(extent={{-100,-100},{100,100}}, lineColor={0,0,0}),
              Text(extent={{-100,-20},{100,20}}, textString="k=%k")}));
          end Show;

          model Sys "sys"
            Show s1(k = pulse.y and step.y) annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Show s2(k = L.Types.Mode.Fast) annotation (Placement(transformation(extent={{40,-10},{60,10}})));
            Show s3(k = pulse.y) annotation (Placement(transformation(extent={{-10,40},{10,60}})));
          end Sys;
        end L;
        """.Replace("\r\n", "\n");

    [Fact]
    public void AComponentReferenceIsShownWhole_AndAnEnumerationLiteralByItsName()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "Types\nShow\nSys\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("L.Sys")!, 800)!;

        Assert.Contains(">k=pulse.y and step.y<", svg);
        Assert.Contains(">k=Fast<", svg);
        Assert.Contains(">k=pulse.y<", svg);    // dotted, and still not a constant
    }
}
