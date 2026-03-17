using Xunit;
using ModelicaParser.Icons;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

/// <summary>
/// Tests for IconExtractor class.
/// Tests icon annotation extraction from Modelica code.
/// </summary>
public class IconExtractorTests
{
    #region Basic Extraction Tests

    [Fact]
    public void ExtractIcon_NullCode_ReturnsNull()
    {
        // Act
        var result = IconExtractor.ExtractIcon((string)null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIcon_EmptyCode_ReturnsNull()
    {
        // Act
        var result = IconExtractor.ExtractIcon("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIcon_ModelWithoutIcon_ReturnsNull()
    {
        // Arrange
        var code = @"
model SimpleModel
  Real x;
end SimpleModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIcon_ModelWithEmptyIcon_ReturnsIconWithNoGraphics()
    {
        // Arrange
        var code = @"
model SimpleModel
  annotation(Icon(graphics={}));
end SimpleModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.HasGraphics);
    }

    #endregion

    #region Rectangle Tests

    [Fact]
    public void ExtractIcon_WithRectangle_ExtractsRectangle()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasGraphics);
        Assert.Single(result.Graphics);
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);

        var rect = (RectanglePrimitive)result.Graphics[0];
        Assert.Equal(-100, rect.Extent[0]);
        Assert.Equal(-100, rect.Extent[1]);
        Assert.Equal(100, rect.Extent[2]);
        Assert.Equal(100, rect.Extent[3]);
    }

    [Fact]
    public void ExtractIcon_WithRectangleFillColor_ExtractsFillColor()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={255,0,0}, fillPattern=FillPattern.Solid)
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var rect = (RectanglePrimitive)result.Graphics[0];
        Assert.Equal(255, rect.FillColor[0]);
        Assert.Equal(0, rect.FillColor[1]);
        Assert.Equal(0, rect.FillColor[2]);
        Assert.Equal("Solid", rect.FillPattern);
    }

    [Fact]
    public void ExtractIcon_WithRoundedRectangle_ExtractsRadius()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, radius=10)
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var rect = (RectanglePrimitive)result.Graphics[0];
        Assert.Equal(10, rect.Radius);
    }

    #endregion

    #region Ellipse Tests

    [Fact]
    public void ExtractIcon_WithEllipse_ExtractsEllipse()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Ellipse(extent={{-50,-50},{50,50}})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<EllipsePrimitive>(result.Graphics[0]);

        var ellipse = (EllipsePrimitive)result.Graphics[0];
        Assert.Equal(-50, ellipse.Extent[0]);
        Assert.Equal(-50, ellipse.Extent[1]);
        Assert.Equal(50, ellipse.Extent[2]);
        Assert.Equal(50, ellipse.Extent[3]);
    }

    [Fact]
    public void ExtractIcon_WithArc_ExtractsStartAndEndAngles()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Ellipse(extent={{-50,-50},{50,50}}, startAngle=0, endAngle=180)
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var ellipse = (EllipsePrimitive)result.Graphics[0];
        Assert.Equal(0, ellipse.StartAngle);
        Assert.Equal(180, ellipse.EndAngle);
    }

    #endregion

    #region Line Tests

    [Fact]
    public void ExtractIcon_WithLine_ExtractsLine()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Line(points={{-100,0},{100,0}})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<LinePrimitive>(result.Graphics[0]);

        var line = (LinePrimitive)result.Graphics[0];
        Assert.Equal(2, line.Points.Count);
        Assert.Equal(-100, line.Points[0][0]);
        Assert.Equal(0, line.Points[0][1]);
        Assert.Equal(100, line.Points[1][0]);
        Assert.Equal(0, line.Points[1][1]);
    }

    [Fact]
    public void ExtractIcon_WithLineColor_ExtractsColor()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Line(points={{-100,0},{100,0}}, color={0,0,255})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var line = (LinePrimitive)result.Graphics[0];
        Assert.Equal(0, line.LineColor[0]);
        Assert.Equal(0, line.LineColor[1]);
        Assert.Equal(255, line.LineColor[2]);
    }

    #endregion

    #region Polygon Tests

    [Fact]
    public void ExtractIcon_WithPolygon_ExtractsPolygon()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Polygon(points={{0,100},{-100,-100},{100,-100}})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<PolygonPrimitive>(result.Graphics[0]);

        var polygon = (PolygonPrimitive)result.Graphics[0];
        Assert.Equal(3, polygon.Points.Count);
    }

    #endregion

    #region Text Tests

    [Fact]
    public void ExtractIcon_WithText_ExtractsText()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Text(extent={{-100,-100},{100,100}}, textString=""Hello"")
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<TextPrimitive>(result.Graphics[0]);

        var text = (TextPrimitive)result.Graphics[0];
        Assert.Equal("Hello", text.TextString);
    }

    [Fact]
    public void ExtractIcon_WithTextAlignment_ExtractsAlignment()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Text(extent={{-100,-100},{100,100}}, textString=""Test"", horizontalAlignment=TextAlignment.Left)
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var text = (TextPrimitive)result.Graphics[0];
        Assert.Equal("Left", text.HorizontalAlignment);
    }

    #endregion

    #region Multiple Graphics Tests

    [Fact]
    public void ExtractIcon_WithMultipleGraphics_ExtractsAll()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}),
    Ellipse(extent={{-50,-50},{50,50}}),
    Line(points={{-100,0},{100,0}})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Graphics.Count);
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
        Assert.IsType<EllipsePrimitive>(result.Graphics[1]);
        Assert.IsType<LinePrimitive>(result.Graphics[2]);
    }

    #endregion

    #region Coordinate System Tests

    [Fact]
    public void ExtractIcon_WithCoordinateSystem_ExtractsExtent()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(
    coordinateSystem(extent={{-200,-200},{200,200}}),
    graphics={Rectangle(extent={{-100,-100},{100,100}})}
  ));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(-200, result.CoordinateExtent[0]);
        Assert.Equal(-200, result.CoordinateExtent[1]);
        Assert.Equal(200, result.CoordinateExtent[2]);
        Assert.Equal(200, result.CoordinateExtent[3]);
    }

    #endregion

    #region Extends Clause Tests

    [Fact]
    public void ExtractIconWithInheritance_NoExtends_ReturnsEmptyExtendsList()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIconWithInheritance(code);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.HasExtends);
        Assert.Empty(result.ExtendsClasses);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithExtends_ExtractsBaseClassName()
    {
        // Arrange
        var code = @"
model DerivedModel
  extends BaseModel;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}})}));
end DerivedModel;";

        // Act
        var result = IconExtractor.ExtractIconWithInheritance(code);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasExtends);
        Assert.Single(result.ExtendsClasses);
        Assert.Equal("BaseModel", result.ExtendsClasses[0]);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithQualifiedExtends_ExtractsFullName()
    {
        // Arrange
        var code = @"
model DerivedModel
  extends Modelica.Icons.Example;
end DerivedModel;";

        // Act
        var result = IconExtractor.ExtractIconWithInheritance(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ExtendsClasses);
        Assert.Equal("Modelica.Icons.Example", result.ExtendsClasses[0]);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithMultipleExtends_ExtractsAll()
    {
        // Arrange
        var code = @"
model DerivedModel
  extends BaseModel1;
  extends BaseModel2;
  extends Modelica.Icons.Example;
end DerivedModel;";

        // Act
        var result = IconExtractor.ExtractIconWithInheritance(code);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.ExtendsClasses.Count);
        Assert.Contains("BaseModel1", result.ExtendsClasses);
        Assert.Contains("BaseModel2", result.ExtendsClasses);
        Assert.Contains("Modelica.Icons.Example", result.ExtendsClasses);
    }

    #endregion

    #region Visibility Tests

    [Fact]
    public void ExtractIcon_WithVisibleFalse_SetsVisibleProperty()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, visible=false)
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var rect = (RectanglePrimitive)result.Graphics[0];
        Assert.False(rect.Visible);
    }

    #endregion

    #region Transform Tests

    [Fact]
    public void ExtractIcon_WithOrigin_ExtractsOrigin()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-50,-50},{50,50}}, origin={25,25})
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var rect = (RectanglePrimitive)result.Graphics[0];
        Assert.Equal(25, rect.Origin[0]);
        Assert.Equal(25, rect.Origin[1]);
    }

    [Fact]
    public void ExtractIcon_WithRotation_ExtractsRotation()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Rectangle(extent={{-50,-50},{50,50}}, rotation=45)
  }));
end TestModel;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        var rect = (RectanglePrimitive)result.Graphics[0];
        Assert.Equal(45, rect.Rotation);
    }

    #endregion

    #region Nested Class Tests

    [Fact]
    public void ExtractIcon_WithNestedClass_DoesNotIncludeNestedClassIcon()
    {
        // Arrange - Parent class has a rectangle, nested class has an ellipse
        // The parent icon should only contain the rectangle, not the ellipse
        // Note: In Modelica, the class annotation goes at the END, before 'end'
        var code = @"
package TestPackage
  model NestedModel
    annotation(Icon(graphics={
      Ellipse(extent={{-50,-50},{50,50}}, fillColor={255,0,0}, fillPattern=FillPattern.Solid)
    }));
  end NestedModel;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={200,200,200}, fillPattern=FillPattern.Solid)
  }));
end TestPackage;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics); // Should only have the rectangle, not the ellipse
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
    }

    [Fact]
    public void ExtractIcon_WithMultipleNestedClasses_DoesNotIncludeAnyNestedIcons()
    {
        // Arrange - Parent has a rectangle, two nested classes have ellipse and line
        // Note: In Modelica, the class annotation goes at the END, before 'end'
        var code = @"
package TestPackage
  model NestedModel1
    annotation(Icon(graphics={
      Ellipse(extent={{-50,-50},{50,50}})
    }));
  end NestedModel1;

  model NestedModel2
    annotation(Icon(graphics={
      Line(points={{-100,0},{100,0}})
    }));
  end NestedModel2;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}})
  }));
end TestPackage;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics); // Should only have the rectangle
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithNestedClass_DoesNotIncludeNestedExtends()
    {
        // Arrange - Parent extends BaseA, nested class extends BaseB
        // The parent should only have BaseA in extends, not BaseB
        var code = @"
model ParentModel
  extends BaseA;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}})
  }));

  model NestedModel
    extends BaseB;
    annotation(Icon(graphics={
      Ellipse(extent={{-50,-50},{50,50}})
    }));
  end NestedModel;
end ParentModel;";

        // Act
        var result = IconExtractor.ExtractIconWithInheritance(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ExtendsClasses); // Should only have BaseA
        Assert.Equal("BaseA", result.ExtendsClasses[0]);
    }

    [Fact]
    public void ExtractIcon_DeeplyNestedClass_DoesNotIncludeDeepNestedIcons()
    {
        // Arrange - Three levels of nesting
        // Note: In Modelica, the class annotation goes at the END, before 'end'
        var code = @"
package Level1
  package Level2
    model Level3
      annotation(Icon(graphics={
        Line(points={{-50,0},{50,0}})
      }));
    end Level3;
    annotation(Icon(graphics={
      Ellipse(extent={{-75,-75},{75,75}})
    }));
  end Level2;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}})
  }));
end Level1;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics); // Should only have the rectangle from Level1
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
    }

    [Fact]
    public void ExtractIcon_ParentHasNoIcon_NestedHasIcon_ReturnsNull()
    {
        // Arrange - Parent has no icon, nested class has one
        // Note: In Modelica, components go in element list, nested classes also in element list
        var code = @"
package TestPackage
  model NestedModel
    annotation(Icon(graphics={
      Ellipse(extent={{-50,-50},{50,50}})
    }));
  end NestedModel;
end TestPackage;";

        // Act
        var result = IconExtractor.ExtractIcon(code);

        // Assert
        Assert.Null(result); // Parent has no icon, should not return nested class icon
    }

    #endregion

    #region Additional Property Coverage Tests

    [Fact]
    public void ExtractIcon_LineWithArrowProperties_ExtractsCorrectly()
    {
        // Covers ProcessLineProperty: arrow, arrowSize, smooth, color
        var code = @"model Test
  annotation(Icon(graphics={
    Line(
      points={{-100,0},{100,0}},
      arrow={Arrow.None,Arrow.Filled},
      arrowSize=10.0,
      smooth=Smooth.None,
      color={0,128,255}
    )
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        var line = Assert.IsType<LinePrimitive>(result.Graphics[0]);
        Assert.Equal("Filled", line.ArrowEnd);
        Assert.Equal(10.0, line.ArrowSize);
        Assert.Equal("None", line.Smooth);
    }

    [Fact]
    public void ExtractIcon_RectangleWithBorderPattern_ExtractsCorrectly()
    {
        // Covers ProcessRectangleProperty: borderPattern
        var code = @"model Test
  annotation(Icon(graphics={
    Rectangle(
      extent={{-100,-100},{100,100}},
      borderPattern=BorderPattern.Raised,
      radius=5.0
    )
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        var rect = Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
        Assert.Equal("Raised", rect.BorderPattern);
        Assert.Equal(5.0, rect.Radius);
    }

    [Fact]
    public void ExtractIcon_EllipseWithClosureAndAngles_ExtractsCorrectly()
    {
        // Covers ProcessEllipseProperty: startAngle, endAngle, closure
        var code = @"model Test
  annotation(Icon(graphics={
    Ellipse(
      extent={{-100,-100},{100,100}},
      startAngle=45.0,
      endAngle=270.0,
      closure=EllipseClosure.Chord
    )
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        var ellipse = Assert.IsType<EllipsePrimitive>(result.Graphics[0]);
        Assert.Equal(45.0, ellipse.StartAngle);
        Assert.Equal(270.0, ellipse.EndAngle);
        Assert.Equal("Chord", ellipse.Closure);
    }

    [Fact]
    public void ExtractIcon_TextWithAllProperties_ExtractsCorrectly()
    {
        // Covers ProcessTextProperty: fontSize, fontName, textStyle, textColor, horizontalAlignment
        var code = @"model Test
  annotation(Icon(graphics={
    Text(
      extent={{-100,-100},{100,100}},
      textString=""Hello"",
      fontSize=12.0,
      fontName=""Arial"",
      textStyle={TextStyle.Bold,TextStyle.Italic},
      textColor={255,0,0},
      horizontalAlignment=TextAlignment.Right
    )
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        var text = Assert.IsType<TextPrimitive>(result.Graphics[0]);
        Assert.Equal(12.0, text.FontSize);
        Assert.Equal("Arial", text.FontName);
        Assert.Contains("Bold", text.FontStyles);
        Assert.Contains("Italic", text.FontStyles);
        Assert.Equal("Right", text.HorizontalAlignment);
    }

    [Fact]
    public void ExtractIcon_BitmapWithFileName_ExtractsCorrectly()
    {
        // Covers ProcessBitmapProperty: fileName, imageSource
        var code = @"model Test
  annotation(Icon(graphics={
    Bitmap(
      extent={{-100,-100},{100,100}},
      fileName=""modelica://MyLib/Resources/image.png"",
      imageSource=""""
    )
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        var bitmap = Assert.IsType<BitmapPrimitive>(result.Graphics[0]);
        Assert.Equal("modelica://MyLib/Resources/image.png", bitmap.FileName);
    }

    [Fact]
    public void ExtractIcon_NestedExtendsAtDepthTwo_NotIncludedInExtendsClasses()
    {
        // Covers line 115: _classDepth != 1 path in VisitExtends_clause
        var code = @"model Outer ""outer""
  model Inner ""inner""
    extends Base;
  end Inner;
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}})
  }));
end Outer;";
        var result = IconExtractor.ExtractIconWithInheritance(code);
        Assert.NotNull(result);
        // The extends from nested class (depth 2) should NOT be included
        Assert.Empty(result.ExtendsClasses);
    }

    [Fact]
    public void ExtractIconWithInheritance_NullCode_ReturnsNull()
    {
        // Already covered by ExtractIcon_NullCode_ReturnsNull, but explicitly test string overload
        var result = IconExtractor.ExtractIconWithInheritance((string)null!);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIcon_CommonProperties_ExtractsLineColor()
    {
        // Covers the common properties in ProcessNamedArgument: visible, origin, rotation,
        // lineColor, fillColor, fillPattern, linePattern, thickness
        var code = @"model Test
  annotation(Icon(graphics={
    Rectangle(
      extent={{-100,-100},{100,100}},
      visible=true,
      origin={10.0,20.0},
      rotation=45.0,
      lineColor={0,0,255},
      fillColor={255,255,0},
      fillPattern=FillPattern.Solid,
      linePattern=LinePattern.Dash,
      lineThickness=2.0
    )
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        var rect = Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
        Assert.True(rect.Visible);
        Assert.Equal(45.0, rect.Rotation);
        Assert.Equal("Dash", rect.LinePattern);
        Assert.Equal(2.0, rect.LineThickness);
    }

    [Fact]
    public void ExtractIcon_UnknownPrimitiveType_IsIgnored()
    {
        // Covers line 329: CreatePrimitive returns null for unknown type
        var code = @"model Test
  annotation(Icon(graphics={
    UnknownShape(extent={{-100,-100},{100,100}})
  }));
end Test;";
        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        // Unknown type should be ignored, resulting in empty graphics
        Assert.Empty(result.Graphics);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithWithinClause_CapturesPackage()
    {
        var code = @"within Modelica.Blocks;
model TestModel
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end TestModel;";

        var result = IconExtractor.ExtractIconWithInheritance(code);
        Assert.NotNull(result);
        Assert.Equal("Modelica.Blocks", result.WithinPackage);
    }

    [Fact]
    public void ExtractIcon_CoordinateSystemPreserveAspectRatio_Extracted()
    {
        var code = @"model Test
  annotation(Icon(
    coordinateSystem(extent={{-100,-100},{100,100}}, preserveAspectRatio=false),
    graphics={Rectangle(extent={{-100,-100},{100,100}})}
  ));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.False(result.PreserveAspectRatio);
    }

    [Fact]
    public void ExtractIcon_CoordinateSystemInitialScale_Extracted()
    {
        var code = @"model Test
  annotation(Icon(
    coordinateSystem(extent={{-100,-100},{100,100}}, initialScale=0.5),
    graphics={Rectangle(extent={{-100,-100},{100,100}})}
  ));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        Assert.Equal(0.5, result.InitialScale);
    }

    [Fact]
    public void ExtractIcon_PolygonWithSmooth_Extracted()
    {
        var code = @"model Test
  annotation(Icon(graphics={
    Polygon(points={{0,100},{-100,-100},{100,-100}}, smooth=Smooth.Bezier)
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var polygon = Assert.IsType<PolygonPrimitive>(result.Graphics[0]);
        Assert.Equal("Bezier", polygon.Smooth);
    }

    [Fact]
    public void ExtractIcon_TextWithStringProperty_Extracted()
    {
        // "string" is an alternative property name for textString
        var code = @"model Test
  annotation(Icon(graphics={
    Text(extent={{-100,-100},{100,100}}, string=""Alt"")
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var text = Assert.IsType<TextPrimitive>(result.Graphics[0]);
        Assert.Equal("Alt", text.TextString);
    }

    [Fact]
    public void ExtractIcon_LineWithPatternProperty_Extracted()
    {
        // "pattern" is an alternative name for linePattern on common properties
        var code = @"model Test
  annotation(Icon(graphics={
    Line(points={{-100,0},{100,0}}, pattern=LinePattern.Dot)
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var line = Assert.IsType<LinePrimitive>(result.Graphics[0]);
        Assert.Equal("Dot", line.LinePattern);
    }

    [Fact]
    public void ExtractIcon_LineWithThicknessProperty_Extracted()
    {
        // "thickness" is an alternative name for lineThickness
        var code = @"model Test
  annotation(Icon(graphics={
    Line(points={{-100,0},{100,0}}, thickness=3.0)
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var line = Assert.IsType<LinePrimitive>(result.Graphics[0]);
        Assert.Equal(3.0, line.LineThickness);
    }

    [Fact]
    public void ExtractIcon_BitmapWithImageSource_Extracted()
    {
        var code = @"model Test
  annotation(Icon(graphics={
    Bitmap(extent={{-100,-100},{100,100}}, imageSource=""iVBORw0KGg=="")
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var bitmap = Assert.IsType<BitmapPrimitive>(result.Graphics[0]);
        Assert.Equal("iVBORw0KGg==", bitmap.ImageSource);
    }

    [Fact]
    public void ExtractIcon_ParseTree_Overload_Works()
    {
        var code = @"model Test
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end Test;";

        var parseTree = ModelicaParser.Helpers.ModelicaParserHelper.Parse(code);
        var result = IconExtractor.ExtractIcon(parseTree);
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
    }

    [Fact]
    public void ExtractIcon_TextWithTextColor_Extracted()
    {
        var code = @"model Test
  annotation(Icon(graphics={
    Text(extent={{-100,-100},{100,100}}, textString=""Hi"", textColor={0,128,0})
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var text = Assert.IsType<TextPrimitive>(result.Graphics[0]);
        Assert.Equal(0, text.TextColor[0]);
        Assert.Equal(128, text.TextColor[1]);
        Assert.Equal(0, text.TextColor[2]);
    }

    [Fact]
    public void ExtractIcon_LineArrowWithOpenArrow_Extracted()
    {
        var code = @"model Test
  annotation(Icon(graphics={
    Line(points={{-100,0},{100,0}}, arrow={Arrow.None,Arrow.Open}, arrowSize=5)
  }));
end Test;";

        var result = IconExtractor.ExtractIcon(code);
        Assert.NotNull(result);
        var line = Assert.IsType<LinePrimitive>(result.Graphics[0]);
        Assert.Equal("None", line.ArrowStart);
        Assert.Equal("Open", line.ArrowEnd);
        Assert.Equal(5.0, line.ArrowSize);
    }

    #endregion
}
