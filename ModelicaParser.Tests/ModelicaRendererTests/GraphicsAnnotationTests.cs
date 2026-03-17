using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for graphics annotation formatting.
/// These tests focus on the specific formatting rules for graphics elements.
/// </summary>
public class GraphicsAnnotationTests
{
    [Fact]
    public void SimpleLine_TwoArguments_SingleLine()
    {
        var testModel = """
        model Test

          annotation (Icon(graphics={Line(points={{0, 0}, {10, 10}}, color={0, 0, 0})}));
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void SimpleRectangle_ThreeArguments_MultiLine()
    {
        var testModel = """
        model Test

          annotation (
            Icon(
              graphics={
                Rectangle(
                  extent={{-100, 100}, {100, -100}},
                  fillColor={255, 255, 255},
                  fillPattern=FillPattern.Solid
                )
              }
            )
          );
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleGraphicsElements_Mixed()
    {
        var testModel = """
        model Test

          annotation (
            Icon(
              graphics={
                Rectangle(
                  extent={{-100, 100}, {100, -100}},
                  fillColor={255, 255, 255},
                  fillPattern=FillPattern.Solid
                ),
                Line(points={{0, 0}, {10, 10}}, color={0, 0, 0}),
                Ellipse(
                  extent={{-50, 50}, {50, -50}},
                  lineColor={0, 0, 255},
                  fillColor={255, 0, 0},
                  fillPattern=FillPattern.Solid
                )
              }
            )
          );
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void TwoLines_BothSingleLine()
    {
        var testModel = """
        model Test

          annotation (
            Icon(
              graphics={
                Line(points={{0, -64}, {0, -100}}, color={191, 0, 0}),
                Line(points={{40, 100}, {40, 64}}, color={0, 0, 127})
              }
            )
          );
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    
    [Fact]
    public void GraphicsAndCoordinateSystem_BothSingleLine()
    {
        var testModel = """
        model Test

          annotation (
            Icon(
              coordinateSystem(
                preserveAspectRatio=false,
                extent={{-100, -100}, {100, 100}}
              ),
              graphics={
                Line(points={{0, -64}, {0, -100}}, color={191, 0, 0}),
                Line(points={{40, 100}, {40, 64}}, color={0, 0, 127})
              }
            )
          );
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }
}
