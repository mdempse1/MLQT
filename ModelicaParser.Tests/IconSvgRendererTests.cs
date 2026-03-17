using Xunit;
using ModelicaParser.Icons;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

/// <summary>
/// Tests for IconSvgRenderer class.
/// Tests SVG generation from IconData.
/// </summary>
public class IconSvgRendererTests
{
    #region Basic Rendering Tests

    [Fact]
    public void RenderToSvg_NullIcon_ReturnsNull()
    {
        // Act
        var result = IconSvgRenderer.RenderToSvg(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void RenderToSvg_EmptyIcon_ReturnsNull()
    {
        // Arrange
        var icon = new IconData();

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void RenderToSvg_WithGraphics_ReturnsSvgString()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<svg", result);
        Assert.Contains("</svg>", result);
    }

    [Fact]
    public void RenderToSvg_WithCustomSize_SetsWidthAndHeight()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive());

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon, size: 48);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("width=\"48\"", result);
        Assert.Contains("height=\"48\"", result);
    }

    #endregion

    #region Rectangle Rendering Tests

    [Fact]
    public void RenderToSvg_Rectangle_ContainsRectElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<rect", result);
    }

    [Fact]
    public void RenderToSvg_RoundedRectangle_ContainsRadiusAttributes()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            Radius = 10
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("rx=\"10\"", result);
        Assert.Contains("ry=\"10\"", result);
    }

    [Fact]
    public void RenderToSvg_RectangleWithFill_ContainsFillColor()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            FillColor = new int[] { 255, 0, 0 },
            FillPattern = "Solid"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("fill=\"#FF0000\"", result);
    }

    [Fact]
    public void RenderToSvg_RectangleWithStroke_ContainsStrokeColor()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            LineColor = new int[] { 0, 0, 255 },
            LinePattern = "Solid"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("stroke=\"#0000FF\"", result);
    }

    #endregion

    #region Ellipse Rendering Tests

    [Fact]
    public void RenderToSvg_Ellipse_ContainsEllipseElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new EllipsePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<ellipse", result);
    }

    [Fact]
    public void RenderToSvg_Arc_ContainsPathElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new EllipsePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            StartAngle = 0,
            EndAngle = 180
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<path", result);
        Assert.Contains("A ", result); // Arc command
    }

    #endregion

    #region Line Rendering Tests

    [Fact]
    public void RenderToSvg_TwoPointLine_ContainsLineElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new LinePrimitive
        {
            Points = new List<double[]>
            {
                new double[] { -100, 0 },
                new double[] { 100, 0 }
            }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<line", result);
    }

    [Fact]
    public void RenderToSvg_MultiPointLine_ContainsPolylineElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new LinePrimitive
        {
            Points = new List<double[]>
            {
                new double[] { -100, 0 },
                new double[] { 0, 100 },
                new double[] { 100, 0 }
            }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<polyline", result);
    }

    [Fact]
    public void RenderToSvg_DashedLine_ContainsDashArray()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new LinePrimitive
        {
            Points = new List<double[]>
            {
                new double[] { -100, 0 },
                new double[] { 100, 0 }
            },
            LinePattern = "Dash"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("stroke-dasharray", result);
    }

    #endregion

    #region Polygon Rendering Tests

    [Fact]
    public void RenderToSvg_Polygon_ContainsPolygonElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new PolygonPrimitive
        {
            Points = new List<double[]>
            {
                new double[] { 0, 100 },
                new double[] { -100, -100 },
                new double[] { 100, -100 }
            }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<polygon", result);
    }

    #endregion

    #region Text Rendering Tests

    [Fact]
    public void RenderToSvg_Text_ContainsTextElement()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new TextPrimitive
        {
            Extent = new double[] { -100, -50, 100, 50 },
            TextString = "Hello"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<text", result);
        Assert.Contains("Hello", result);
    }

    [Fact]
    public void RenderToSvg_TextWithSpecialChars_EscapesCharacters()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new TextPrimitive
        {
            Extent = new double[] { -100, -50, 100, 50 },
            TextString = "<test>&\"value\""
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("&lt;test&gt;", result);
        Assert.Contains("&amp;", result);
    }

    #endregion

    #region Visibility Tests

    [Fact]
    public void RenderToSvg_InvisiblePrimitive_NotRendered()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            Visible = false
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        // Icon has graphics but none visible - should still return SVG structure
        // but without the rect element
        Assert.NotNull(result);
        Assert.DoesNotContain("<rect", result);
    }

    #endregion

    #region ViewBox Tests

    [Fact]
    public void RenderToSvg_DefaultCoordinateSystem_HasCorrectViewBox()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive());

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        // Default coordinate system is -100,-100 to 100,100
        Assert.Contains("viewBox=\"-100 -100 200 200\"", result);
    }

    [Fact]
    public void RenderToSvg_CustomCoordinateSystem_HasCorrectViewBox()
    {
        // Arrange
        var icon = new IconData
        {
            CoordinateExtent = new double[] { -200, -200, 200, 200 }
        };
        icon.Graphics.Add(new RectanglePrimitive());

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("viewBox=\"-200 -200 400 400\"", result);
    }

    #endregion

    #region Bitmap Tests

    [Fact]
    public void RenderToSvg_BitmapWithImageSource_EmbedsPngDataUri()
    {
        // Arrange — iVBOR is the base64 prefix for PNG magic bytes (\x89PNG)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "iVBORw0KGgoAAAANSUhEUg=="
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<image", result);
        Assert.Contains("data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==", result);
        // Images must have scale(1,-1) to counteract the parent group's Y-axis flip
        Assert.Contains("scale(1,-1)", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithJpegImageSource_EmbedsJpegDataUri()
    {
        // Arrange — /9j/ is the base64 prefix for JPEG magic bytes (\xFF\xD8\xFF)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "/9j/4AAQSkZJRgAB"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("data:image/jpeg;base64,/9j/4AAQSkZJRgAB", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithBmpImageSource_EmbedsBmpDataUri()
    {
        // Arrange — Qk is the base64 prefix for BMP magic bytes ("BM")
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "Qk1GAAAAAAAAAD"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("data:image/bmp;base64,Qk1GAAAAAAAAAD", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithSvgImageSource_EmbedsSvgDataUri()
    {
        // Arrange — PHN2 is the base64 prefix for "<sv" (start of <svg)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjwvc3ZnPg=="
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("data:image/svg+xml;base64,", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithFileName_UsesResolverResult()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            FileName = "modelica://TestLib/Resources/icon.png"
        });
        const string expectedDataUri = "data:image/png;base64,ABC123==";

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon, fileNameResolver: _ => expectedDataUri);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<image", result);
        Assert.Contains(expectedDataUri, result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithFileName_NoResolver_UsesFileNameAsHref()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            FileName = "/absolute/path/to/icon.png"
        });

        // Act — no resolver provided
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("href=\"/absolute/path/to/icon.png\"", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithFileName_ResolverReturnsNull_UsesFileNameAsHref()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            FileName = "modelica://Unknown/Resources/icon.png"
        });

        // Act — resolver cannot resolve and returns null
        var result = IconSvgRenderer.RenderToSvg(icon, fileNameResolver: _ => null);

        // Assert — falls back to the raw fileName
        Assert.NotNull(result);
        Assert.Contains("href=\"modelica://Unknown/Resources/icon.png\"", result);
    }

    [Fact]
    public void RenderToSvg_BitmapImageSourceTakesPriorityOverFileName()
    {
        // Arrange — both imageSource and fileName present; imageSource wins
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "iVBORw0KGgoAAAANSUhEUg==",
            FileName = "modelica://Lib/Resources/icon.png"
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon, fileNameResolver: _ => "data:image/png;base64,FROMFILE==");

        // Assert — imageSource data is used, not the file resolver result
        Assert.NotNull(result);
        Assert.Contains("data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==", result);
        Assert.DoesNotContain("FROMFILE==", result);
    }

    #endregion

    #region Transform Tests

    [Fact]
    public void RenderToSvg_PrimitiveWithOrigin_ContainsTranslateTransform()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            Origin = new double[] { 25, 25 }
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("translate(25,25)", result);
    }

    [Fact]
    public void RenderToSvg_PrimitiveWithRotation_ContainsRotateTransform()
    {
        // Arrange
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            Rotation = 45
        });

        // Act
        var result = IconSvgRenderer.RenderToSvg(icon);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("rotate(45)", result);
    }

    #endregion

    #region Multi-Level Inheritance Tests

    [Fact]
    public void ExtractAndRenderIconWithInheritance_MultipleExtends_DrawsInCorrectOrder()
    {
        // Arrange: class with two extends clauses. In Modelica, the 1st extends is the deepest
        // layer (drawn first/bottom) and the 2nd extends is drawn on top of it.
        // This simulates CombiTable2Ds which extends SI2SO (1st) and CombiTable2DBase (2nd):
        //   - SI2SO provides a background Rectangle that should appear BELOW CombiTable2DBase graphics
        //   - CombiTable2DBase provides foreground graphics that should appear ON TOP

        var sisoCode = @"
block SI2SO
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={255,255,255}, fillPattern=FillPattern.Solid)
  }));
end SI2SO;";

        var tableBaseCode = @"
block CombiTable2DBase
  annotation(Icon(graphics={
    Line(points={{-60,-40},{-60,40}}, color={0,0,0})
  }));
end CombiTable2DBase;";

        var classCode = @"
block CombiTable2Ds
  extends SI2SO;
  extends CombiTable2DBase;
end CombiTable2Ds;";

        string? Resolver(string name) => name switch
        {
            "SI2SO" => sisoCode,
            "CombiTable2DBase" => tableBaseCode,
            _ => null
        };

        // Act
        var icon = IconSvgRenderer.ExtractIconWithInheritance(classCode, Resolver);

        // Assert: both primitives present
        Assert.NotNull(icon);
        Assert.True(icon.HasGraphics);
        Assert.Equal(2, icon.Graphics.Count);

        // 1st extends (SI2SO Rectangle) must be drawn BEFORE 2nd extends (CombiTable2DBase Line).
        // In the graphics list, earlier index = drawn first = appears underneath.
        Assert.IsType<RectanglePrimitive>(icon.Graphics[0]);
        Assert.IsType<LinePrimitive>(icon.Graphics[1]);
    }

    [Fact]
    public void ExtractAndRenderIconWithInheritance_TwoLevels_MergesAllGraphics()
    {
        // Arrange: grandparent has a circle, parent inherits it and adds a rectangle,
        // child inherits both and adds a line. Result should have all three.
        var grandparentCode = @"
within Pkg;
block GrandParent
  annotation(Icon(graphics={
    Ellipse(extent={{-50,-50},{50,50}}, fillColor={0,0,255}, fillPattern=FillPattern.Solid)
  }));
end GrandParent;";

        var parentCode = @"
block Parent
  extends GrandParent;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={200,200,200}, fillPattern=FillPattern.Solid)
  }));
end Parent;";

        var childCode = @"
within Pkg;
block Child
  extends Parent;
  annotation(Icon(graphics={
    Line(points={{-50,0},{50,0}}, color={0,0,0})
  }));
end Child;";

        // Resolver: Parent is resolved directly (no package qualifier needed), GrandParent via walk-up from "Pkg"
        string? Resolver(string name) => name switch
        {
            "Parent" or "Pkg.Parent" => parentCode,
            "GrandParent" or "Pkg.GrandParent" => grandparentCode,
            _ => null
        };

        // Act
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(childCode, Resolver);

        // Assert: all three primitives present
        Assert.NotNull(result);
        Assert.Contains("<ellipse", result);
        Assert.Contains("<rect", result);
        Assert.Contains("<line", result);
    }

    [Fact]
    public void ExtractAndRenderIconWithInheritance_UnqualifiedExtendsResolvesViaWithinPackage()
    {
        // Arrange: simulates Modelica.Blocks.Discrete.Sampler -> Interfaces.DiscreteSISO -> DiscreteBlock
        // The child's within clause is "Modelica.Blocks"; parent's class is at Modelica.Blocks.Interfaces.DiscreteSISO.
        // DiscreteSISO is an inner class (no within clause) in Interfaces package.
        // DiscreteSISO extends DiscreteBlock (unqualified, resolved relative to Modelica.Blocks.Interfaces).

        var discreteBlockCode = @"
block DiscreteBlock
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={95,95,95}, fillPattern=FillPattern.Solid)
  }));
end DiscreteBlock;";

        // Inner class snippet - no 'within' clause
        var discreteSisoCode = @"
block DiscreteSISO
  extends DiscreteBlock;
  annotation(Icon(graphics={
    Line(points={{-80,0},{80,0}})
  }));
end DiscreteSISO;";

        var samplerCode = @"
within Modelica.Blocks;
block Sampler
  extends Interfaces.DiscreteSISO;
  annotation(Icon(graphics={
    Ellipse(extent={{-25,-10},{-45,10}}, fillColor={255,255,255}, fillPattern=FillPattern.Solid)
  }));
end Sampler;";

        string? Resolver(string name) => name switch
        {
            "Modelica.Blocks.Interfaces.DiscreteSISO" => discreteSisoCode,
            "Modelica.Blocks.Interfaces.DiscreteBlock" => discreteBlockCode,
            _ => null
        };

        // Act
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(samplerCode, Resolver);

        // Assert: icon from DiscreteBlock (rect), DiscreteSISO (line), and Sampler (ellipse) all present
        Assert.NotNull(result);
        Assert.Contains("<rect", result);
        Assert.Contains("<line", result);
        Assert.Contains("<ellipse", result);
    }

    [Fact]
    public void ExtractAndRenderIconWithInheritance_CircularInheritance_DoesNotInfiniteLoop()
    {
        // Arrange: A extends B, B extends A (circular)
        var codeA = @"
block A
  extends B;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}})
  }));
end A;";

        var codeB = @"
block B
  extends A;
  annotation(Icon(graphics={
    Ellipse(extent={{-50,-50},{50,50}})
  }));
end B;";

        string? Resolver(string name) => name switch
        {
            "B" => codeB,
            "A" => codeA,
            _ => null
        };

        // Act: must terminate without stack overflow
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(codeA, Resolver);

        // Assert: at least one primitive rendered
        Assert.NotNull(result);
    }

    [Fact]
    public void ExtractAndRenderIconWithInheritance_NoWithinClause_UsesInitialPackageContext()
    {
        // Arrange: simulates real-world stored class body with no 'within' clause (inner-class snippet
        // as stored in the graph). Four-level chain:
        //   Sampler (Modelica.Blocks.Discrete) -> Interfaces.DiscreteSISO
        //   -> DiscreteBlock (unqualified sibling in Interfaces)
        //   -> Modelica.Blocks.Icons.DiscreteBlock (fully qualified, has the graphics)
        //
        // Without initialPackageContext, "Interfaces.DiscreteSISO" cannot be resolved
        // because there is no 'within' clause and packageContext starts null.

        var iconsDiscreteBlockCode = @"
block DiscreteBlock
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={95,95,95}, fillPattern=FillPattern.Solid)
  }));
end DiscreteBlock;";

        var interfacesDiscreteBlockCode = @"
partial block DiscreteBlock
  extends Modelica.Blocks.Icons.DiscreteBlock;
end DiscreteBlock;";

        var discreteSisoCode = @"
partial block DiscreteSISO
  extends DiscreteBlock;
end DiscreteSISO;";

        // Class body only — no 'within' clause, as stored by GraphBuilder
        var samplerCode = @"
block Sampler
  extends Interfaces.DiscreteSISO;
  annotation(Icon(graphics={
    Ellipse(extent={{-25,-10},{-45,10}}, fillColor={255,255,255}, fillPattern=FillPattern.Solid)
  }));
end Sampler;";

        string? Resolver(string name) => name switch
        {
            "Modelica.Blocks.Interfaces.DiscreteSISO" => discreteSisoCode,
            "Modelica.Blocks.Interfaces.DiscreteBlock" => interfacesDiscreteBlockCode,
            "Modelica.Blocks.Icons.DiscreteBlock" => iconsDiscreteBlockCode,
            _ => null
        };

        // Act: provide initialPackageContext = "Modelica.Blocks.Discrete" as LibraryDataService does
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
            samplerCode,
            Resolver,
            initialPackageContext: "Modelica.Blocks.Discrete");

        // Assert: graphics from Icons.DiscreteBlock (rect) and Sampler (ellipse) both present
        Assert.NotNull(result);
        Assert.Contains("<rect", result);
        Assert.Contains("<ellipse", result);
    }

    [Fact]
    public void ExtractAndRenderIconWithInheritance_FullyQualifiedExtends_PropagatesPackageContext()
    {
        // Arrange: simulates FirstOrderHold which uses the FULLY-QUALIFIED extends name
        // "Modelica.Blocks.Interfaces.DiscreteSISO" instead of the relative "Interfaces.DiscreteSISO".
        // Direct resolution succeeds for the first extends, but the next level (DiscreteSISO extending
        // DiscreteBlock, an unqualified sibling) must still get the correct package context.

        var iconsDiscreteBlockCode = @"
block DiscreteBlock
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={95,95,95}, fillPattern=FillPattern.Solid)
  }));
end DiscreteBlock;";

        var interfacesDiscreteBlockCode = @"
partial block DiscreteBlock
  extends Modelica.Blocks.Icons.DiscreteBlock;
end DiscreteBlock;";

        var discreteSisoCode = @"
partial block DiscreteSISO
  extends DiscreteBlock;
end DiscreteSISO;";

        // Uses fully-qualified extends name (unlike Sampler which uses relative "Interfaces.DiscreteSISO")
        var firstOrderHoldCode = @"
block FirstOrderHold
  extends Modelica.Blocks.Interfaces.DiscreteSISO;
  annotation(Icon(graphics={
    Line(points={{-79,0},{80,0}}, color={0,0,255})
  }));
end FirstOrderHold;";

        string? Resolver(string name) => name switch
        {
            "Modelica.Blocks.Interfaces.DiscreteSISO" => discreteSisoCode,
            "Modelica.Blocks.Interfaces.DiscreteBlock" => interfacesDiscreteBlockCode,
            "Modelica.Blocks.Icons.DiscreteBlock" => iconsDiscreteBlockCode,
            _ => null
        };

        // Act: initialPackageContext from model ID "Modelica.Blocks.Discrete.FirstOrderHold"
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
            firstOrderHoldCode,
            Resolver,
            initialPackageContext: "Modelica.Blocks.Discrete");

        // Assert: graphics from Icons.DiscreteBlock (rect) and FirstOrderHold (line) both present
        Assert.NotNull(result);
        Assert.Contains("<rect", result);
        Assert.Contains("<line", result);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithinPackageWalksUpHierarchy()
    {
        // Arrange: class in "Pkg.Sub" extends "Sibling" which lives at "Pkg.Sibling"
        // Walk-up: first tries "Pkg.Sub.Sibling" (fail), then "Pkg.Sibling" (success)
        var siblingCode = @"
block Sibling
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={0,128,0}, fillPattern=FillPattern.Solid)
  }));
end Sibling;";

        var derivedCode = @"
within Pkg.Sub;
block Derived
  extends Sibling;
end Derived;";

        string? Resolver(string name) => name switch
        {
            "Pkg.Sibling" => siblingCode,
            _ => null
        };

        // Act
        var icon = IconSvgRenderer.ExtractIconWithInheritance(derivedCode, Resolver);

        // Assert: inherited rectangle from Sibling
        Assert.NotNull(icon);
        Assert.True(icon.HasGraphics);
        var svg = IconSvgRenderer.RenderToSvg(icon);
        Assert.NotNull(svg);
        Assert.Contains("<rect", svg);
    }

    #endregion

    #region Additional Coverage Tests

    [Fact]
    public void RenderToSvg_BitmapWithImageSource_ContainsImageElement()
    {
        // Covers: RenderBitmap with non-empty ImageSource (lines 571-575)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("<image", result);
        Assert.Contains("data:image/png;base64,", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithFileName_ContainsImageElement()
    {
        // Covers: RenderBitmap with non-empty FileName (lines 578-583)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            FileName = "modelica://MyLib/Resources/icon.png"
        });

        var result = IconSvgRenderer.RenderToSvg(icon, fileNameResolver: name => $"data:image/png;base64,abc");

        Assert.NotNull(result);
        Assert.Contains("<image", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithEmptyFileName_ReturnsEmpty()
    {
        // Covers: RenderBitmap returning "" when no image source or file (line 585)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 }
        });

        var result = IconSvgRenderer.RenderToSvg(icon);
        // With no image source and no filename, bitmap renders nothing but SVG is still returned
        Assert.NotNull(result);
    }

    [Fact]
    public void RenderToSvg_EllipsePartialArc_ContainsPathElement()
    {
        // Covers: RenderEllipse with partial arc (StartAngle != 0 || EndAngle != 360) → RenderArc
        var icon = new IconData();
        icon.Graphics.Add(new EllipsePrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            StartAngle = 0,
            EndAngle = 180
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("<path", result);
    }

    [Fact]
    public void RenderToSvg_EllipseArcWithRadialClosure_ContainsLPath()
    {
        // Covers: RenderArc with Radial closure (lines 419-421)
        var icon = new IconData();
        icon.Graphics.Add(new EllipsePrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            StartAngle = 30,
            EndAngle = 270,
            Closure = "Radial"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("<path", result);
        Assert.Contains(" L ", result);
    }

    [Fact]
    public void RenderToSvg_EllipseArcWithChordClosure_ContainsZPath()
    {
        // Covers: RenderArc with Chord closure (lines 422-425)
        var icon = new IconData();
        icon.Graphics.Add(new EllipsePrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            StartAngle = 0,
            EndAngle = 180,
            Closure = "Chord"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("<path", result);
    }

    [Fact]
    public void RenderToSvg_LineWithBezierSmooth_ContainsPathElement()
    {
        // Covers: RenderLine with Smooth == "Bezier" and >= 4 points → RenderBezierLine (lines 438,439,455-469)
        var icon = new IconData();
        icon.Graphics.Add(new LinePrimitive
        {
            Points = new List<double[]>
            {
                new double[] { -80, 0 },
                new double[] { -20, 60 },
                new double[] { 20, -60 },
                new double[] { 80, 0 }
            },
            Smooth = "Bezier"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("<path", result);
    }

    [Fact]
    public void RenderToSvg_PolygonWithBezierSmooth_ContainsPathElement()
    {
        // Covers: RenderPolygon with Smooth == "Bezier" and >= 4 points → RenderBezierPolygon (lines 487-509)
        var icon = new IconData();
        icon.Graphics.Add(new PolygonPrimitive
        {
            Points = new List<double[]>
            {
                new double[] { 0, 100 },
                new double[] { 70, 30 },
                new double[] { 40, -80 },
                new double[] { -40, -80 },
                new double[] { -70, 30 }
            },
            Smooth = "Bezier"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("<path", result);
    }

    [Fact]
    public void RenderToSvg_TextWithLeftAlignment_UsesStartAnchor()
    {
        // Covers: HorizontalAlignment "Left" case (lines 529,530,536)
        var icon = new IconData();
        icon.Graphics.Add(new TextPrimitive
        {
            Extent = new double[] { -100, -50, 100, 50 },
            TextString = "LeftAligned",
            HorizontalAlignment = "Left"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("text-anchor=\"start\"", result);
    }

    [Fact]
    public void RenderToSvg_TextWithRightAlignment_UsesEndAnchor()
    {
        // Covers: HorizontalAlignment "Right" case (lines 536,537)
        var icon = new IconData();
        icon.Graphics.Add(new TextPrimitive
        {
            Extent = new double[] { -100, -50, 100, 50 },
            TextString = "RightAligned",
            HorizontalAlignment = "Right"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("text-anchor=\"end\"", result);
    }

    [Fact]
    public void RenderToSvg_TextWithBoldItalicUnderline_ContainsFontStyles()
    {
        // Covers: FontStyles Bold, Italic, Underline in text rendering
        var icon = new IconData();
        icon.Graphics.Add(new TextPrimitive
        {
            Extent = new double[] { -100, -50, 100, 50 },
            TextString = "Styled",
            FontStyles = new List<string> { "Bold", "Italic", "Underline" },
            FontName = "Arial"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("font-weight=\"bold\"", result);
        Assert.Contains("font-style=\"italic\"", result);
        Assert.Contains("text-decoration=\"underline\"", result);
        Assert.Contains("font-family=\"Arial\"", result);
    }

    [Fact]
    public void RenderToSvg_PrimitiveWithRotationAndOrigin_ContainsTransform()
    {
        // Covers: GetTransformAttribute with non-zero origin and rotation (lines 628-634)
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -50, -50, 50, 50 },
            Rotation = 45.0,
            Origin = new double[] { 10.0, 20.0 }
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("transform=", result);
        Assert.Contains("translate(", result);
        Assert.Contains("rotate(", result);
    }

    [Fact]
    public void RenderToSvg_RectangleWithRadius_ContainsRxRy()
    {
        // Covers: RenderRectangle with Radius > 0 (line 366)
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            Radius = 10.0
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("rx=", result);
        Assert.Contains("ry=", result);
    }

    [Fact]
    public void RenderToSvg_PrimitiveWithLinePatternNone_ContainsStrokeNone()
    {
        // Covers: GetStyleAttribute LinePattern == "None" path (lines 671-674)
        var icon = new IconData();
        icon.Graphics.Add(new RectanglePrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            LinePattern = "None",
            FillPattern = "Solid",
            FillColor = new int[] { 0, 128, 255 }
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("stroke=\"none\"", result);
    }

    [Fact]
    public void RenderToSvg_PrimitiveWithDashPattern_ContainsDashArray()
    {
        // Covers: GetDashArray and GetStyleAttribute with various patterns (lines 662-670, 703-706)
        var patterns = new[] { "Dash", "Dot", "DashDot", "DashDotDot" };
        foreach (var pattern in patterns)
        {
            var icon = new IconData();
            icon.Graphics.Add(new RectanglePrimitive
            {
                Extent = new double[] { -100, -100, 100, 100 },
                LinePattern = pattern
            });
            var result = IconSvgRenderer.RenderToSvg(icon);
            Assert.NotNull(result);
            Assert.Contains("stroke-dasharray=", result);
        }
    }

    [Fact]
    public void RenderToSvg_LineWithDashPattern_ContainsDashArray()
    {
        // Covers: GetLineStyleAttribute with non-empty dash pattern (lines 686-690)
        var icon = new IconData();
        icon.Graphics.Add(new LinePrimitive
        {
            Points = new List<double[]> { new double[] { -80, 0 }, new double[] { 80, 0 } },
            LinePattern = "Dash"
        });

        var result = IconSvgRenderer.RenderToSvg(icon);

        Assert.NotNull(result);
        Assert.Contains("stroke-dasharray=", result);
    }

    [Fact]
    public void IconPrimitives_TypeProperty_ReturnsCorrectType()
    {
        // Covers: Type property getters in all primitive classes (line 8 in each)
        Assert.Equal("Bitmap", new BitmapPrimitive().Type);
        Assert.Equal("Ellipse", new EllipsePrimitive().Type);
        Assert.Equal("Line", new LinePrimitive().Type);
        Assert.Equal("Polygon", new PolygonPrimitive().Type);
        Assert.Equal("Rectangle", new RectanglePrimitive().Type);
        Assert.Equal("Text", new TextPrimitive().Type);
    }

    [Fact]
    public void ExtractAndRenderIconWithInheritance_WithParseTree_ReturnsNull()
    {
        // Covers: ExtractAndRenderIconWithInheritance(parseTree, ...) overload (lines 148-158)
        // and ExtractIconWithInheritance(parseTree, ...) (lines 190-197)
        var code = """
model SimpleModel
  Real x;
equation
  x = 1.0;
end SimpleModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        static string? Resolver(string name) => null;
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(parseTree, Resolver);
        Assert.Null(result);
    }

    [Fact]
    public void DetectImageMimeType_JpegData_ReturnsJpegMime()
    {
        // Covers: DetectImageMimeTypeFromBase64 JPEG branch (line 612)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "/9j/test"  // JPEG magic bytes in base64
        });
        var result = IconSvgRenderer.RenderToSvg(icon);
        Assert.NotNull(result);
        Assert.Contains("data:image/jpeg", result);
    }

    [Fact]
    public void DetectImageMimeType_BmpData_ReturnsBmpMime()
    {
        // Covers: DetectImageMimeTypeFromBase64 BMP branch (line 614)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "QkTest"  // BMP magic bytes in base64
        });
        var result = IconSvgRenderer.RenderToSvg(icon);
        Assert.NotNull(result);
        Assert.Contains("data:image/bmp", result);
    }

    [Fact]
    public void DetectImageMimeType_SvgData_ReturnsSvgMime()
    {
        // Covers: DetectImageMimeTypeFromBase64 SVG branch (line 617)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "PHN2Test"  // SVG base64 prefix
        });
        var result = IconSvgRenderer.RenderToSvg(icon);
        Assert.NotNull(result);
        Assert.Contains("data:image/svg+xml", result);
    }

    [Fact]
    public void RenderToSvg_BitmapWithRotation_ContainsTransform()
    {
        // Covers: GetBitmapTransformAttribute with rotation (line 600)
        var icon = new IconData();
        icon.Graphics.Add(new BitmapPrimitive
        {
            Extent = new double[] { -100, -100, 100, 100 },
            ImageSource = "iVBORtest",
            Rotation = 90.0,
            Origin = new double[] { 5.0, 5.0 }
        });
        var result = IconSvgRenderer.RenderToSvg(icon);
        Assert.NotNull(result);
        Assert.Contains("rotate(", result);
    }

    #endregion
}
