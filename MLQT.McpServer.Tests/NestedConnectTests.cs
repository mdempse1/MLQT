using MLQT.McpServer.Helpers;
using Pt = MLQT.McpServer.Helpers.DiagramGeometry.Pt;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Connects inside a for, if or when equation, as the two diagram paths treat them. Both read the
/// class's connects through <c>ConnectClauses</c> now, and each decides what a nested one means to it
/// rather than not seeing it.
/// </summary>
public class NestedConnectTests
{
    private static readonly IReadOnlyList<Pt> AnyRoute = [new(0, 0), new(10, 0)];

    [Fact]
    public void TheAnnotator_LeavesANestedConnectAsTheUserWroteIt()
    {
        // One Line in a loop body cannot be every index's wiring, and one in an if branch would be
        // drawn whether or not the branch applies - so only the unconditional connect is annotated,
        // even when the router could route all three.
        const string code = """
            model M
            equation
              connect(a, b);
              for i in 1:2 loop
                connect(c[i], d[i]);
              end for;
              if use then
                connect(e, f);
              end if;
            end M;
            """;

        // A raw literal carries the checkout's line endings, and the annotator splices at offsets into
        // the parser's LF-normalised text - as a class's stored source always is.
        var annotated = ConnectionLineAnnotator.Annotate(
            code.ReplaceLineEndings("\n"), (_, _) => AnyRoute, (_, _) => null);

        Assert.Contains("connect(a, b) annotation (Line(points={{0,0},{10,0}}))", annotated);
        Assert.Contains("connect(c[i], d[i]);", annotated);
        Assert.Contains("connect(e, f);", annotated);
    }

    [Fact]
    public void TheImage_DrawsTheLineANestedConnectCarries()
    {
        // A connect in an if branch carries its Line annotation like any other, and Dymola draws it.
        // The image read only an equation section's direct children, so the line was missing.
        const string package = """
            within;
            package Lib "l"
              connector Pin "pin"
                Real v;
              end Pin;

              model Two "two pins"
                parameter Boolean use = true;
                Pin a annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
                Pin b annotation (Placement(transformation(extent={{40,-10},{60,10}})));
              equation
                if use then
                  connect(a, b) annotation (Line(points={{-40,0},{40,0}}, color={0,128,0}));
                end if;
              end Two;
            end Lib;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = package,
            ["package.order"] = "Pin\nTwo\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var svg = DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById("Lib.Two")!, 800);

        Assert.NotNull(svg);
        Assert.Contains("<polyline", svg);
        Assert.Contains("#008000", svg);
    }
}
