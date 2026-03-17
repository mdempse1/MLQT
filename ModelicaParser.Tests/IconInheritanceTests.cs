using Xunit;
using ModelicaParser.Icons;

namespace ModelicaParser.Tests;

/// <summary>
/// Tests for icon inheritance through extends clauses.
/// Tests the merging of base class icons with derived class icons.
/// </summary>
public class IconInheritanceTests
{
    #region IconData Merging Tests

    [Fact]
    public void WithBaseLayer_NullBase_ReturnsSameIcon()
    {
        // Arrange
        var derivedIcon = new IconData();
        derivedIcon.Graphics.Add(new RectanglePrimitive { Extent = new double[] { -50, -50, 50, 50 } });

        // Act
        var result = derivedIcon.WithBaseLayer(null);

        // Assert
        Assert.Same(derivedIcon, result);
    }

    [Fact]
    public void WithBaseLayer_EmptyBase_ReturnsSameIcon()
    {
        // Arrange
        var derivedIcon = new IconData();
        derivedIcon.Graphics.Add(new RectanglePrimitive());
        var baseIcon = new IconData(); // Empty, no graphics

        // Act
        var result = derivedIcon.WithBaseLayer(baseIcon);

        // Assert
        Assert.Same(derivedIcon, result);
    }

    [Fact]
    public void WithBaseLayer_ValidBase_MergesGraphics()
    {
        // Arrange
        var baseIcon = new IconData();
        baseIcon.Graphics.Add(new RectanglePrimitive { Extent = new double[] { -100, -100, 100, 100 } });

        var derivedIcon = new IconData();
        derivedIcon.Graphics.Add(new EllipsePrimitive { Extent = new double[] { -50, -50, 50, 50 } });

        // Act
        var result = derivedIcon.WithBaseLayer(baseIcon);

        // Assert
        Assert.NotSame(derivedIcon, result);
        Assert.Equal(2, result.Graphics.Count);
        // Base graphics should be first (drawn underneath)
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
        // Derived graphics should be second (drawn on top)
        Assert.IsType<EllipsePrimitive>(result.Graphics[1]);
    }

    [Fact]
    public void WithBaseLayer_PreservesCoordinateSystem()
    {
        // Arrange
        var baseIcon = new IconData
        {
            CoordinateExtent = new double[] { -200, -200, 200, 200 }
        };
        baseIcon.Graphics.Add(new RectanglePrimitive());

        var derivedIcon = new IconData
        {
            CoordinateExtent = new double[] { -100, -100, 100, 100 }
        };
        derivedIcon.Graphics.Add(new EllipsePrimitive());

        // Act
        var result = derivedIcon.WithBaseLayer(baseIcon);

        // Assert
        // Should preserve derived class coordinate system
        Assert.Equal(-100, result.CoordinateExtent[0]);
        Assert.Equal(100, result.CoordinateExtent[2]);
    }

    #endregion

    #region Inheritance Resolution Tests

    [Fact]
    public void ExtractIconWithInheritance_NoExtends_ReturnsOwnIcon()
    {
        // Arrange
        var code = @"
model TestModel
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end TestModel;";

        Func<string, string?> resolver = _ => null;

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(code, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
    }

    [Fact]
    public void ExtractIconWithInheritance_WithBaseClass_MergesIcons()
    {
        // Arrange
        var baseCode = @"
model BaseModel
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, fillColor={200,200,200}, fillPattern=FillPattern.Solid)}));
end BaseModel;";

        var derivedCode = @"
model DerivedModel
  extends BaseModel;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}}, fillColor={255,0,0}, fillPattern=FillPattern.Solid)}));
end DerivedModel;";

        Func<string, string?> resolver = name =>
        {
            if (name == "BaseModel") return baseCode;
            return null;
        };

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(derivedCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Graphics.Count);
        // Base icon (rectangle) should be first
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
        // Derived icon (ellipse) should be second
        Assert.IsType<EllipsePrimitive>(result.Graphics[1]);
    }

    [Fact]
    public void ExtractIconWithInheritance_BaseHasNoIcon_ReturnsOwnIcon()
    {
        // Arrange
        var baseCode = @"
model BaseModel
  Real x;
end BaseModel;";

        var derivedCode = @"
model DerivedModel
  extends BaseModel;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}})}));
end DerivedModel;";

        Func<string, string?> resolver = name =>
        {
            if (name == "BaseModel") return baseCode;
            return null;
        };

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(derivedCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<EllipsePrimitive>(result.Graphics[0]);
    }

    [Fact]
    public void ExtractIconWithInheritance_DerivedHasNoIcon_ReturnsBaseIcon()
    {
        // Arrange
        var baseCode = @"
model BaseModel
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end BaseModel;";

        var derivedCode = @"
model DerivedModel
  extends BaseModel;
  Real y;
end DerivedModel;";

        Func<string, string?> resolver = name =>
        {
            if (name == "BaseModel") return baseCode;
            return null;
        };

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(derivedCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
    }

    [Fact]
    public void ExtractIconWithInheritance_UnresolvedBase_ReturnsOwnIcon()
    {
        // Arrange
        var derivedCode = @"
model DerivedModel
  extends UnknownBaseModel;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}})}));
end DerivedModel;";

        Func<string, string?> resolver = _ => null; // Can't resolve anything

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(derivedCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Graphics);
    }

    [Fact]
    public void ExtractIconWithInheritance_MultipleExtends_MergesAll()
    {
        // Arrange
        var base1Code = @"
model Base1
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end Base1;";

        var base2Code = @"
model Base2
  annotation(Icon(graphics={Line(points={{-100,0},{100,0}})}));
end Base2;";

        var derivedCode = @"
model DerivedModel
  extends Base1;
  extends Base2;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}})}));
end DerivedModel;";

        Func<string, string?> resolver = name =>
        {
            return name switch
            {
                "Base1" => base1Code,
                "Base2" => base2Code,
                _ => null
            };
        };

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(derivedCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Graphics.Count);
    }

    [Fact]
    public void ExtractIconWithInheritance_DeepInheritance_ResolvesAllLevels()
    {
        // Arrange - Grandparent -> Parent -> Child
        var grandparentCode = @"
model Grandparent
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, fillColor={200,200,200}, fillPattern=FillPattern.Solid)}));
end Grandparent;";

        var parentCode = @"
model Parent
  extends Grandparent;
  annotation(Icon(graphics={Ellipse(extent={{-75,-75},{75,75}}, fillColor={150,150,150}, fillPattern=FillPattern.Solid)}));
end Parent;";

        var childCode = @"
model Child
  extends Parent;
  annotation(Icon(graphics={Polygon(points={{0,50},{-50,-50},{50,-50}}, fillColor={100,100,100}, fillPattern=FillPattern.Solid)}));
end Child;";

        Func<string, string?> resolver = name =>
        {
            return name switch
            {
                "Grandparent" => grandparentCode,
                "Parent" => parentCode,
                _ => null
            };
        };

        // Act
        var result = IconSvgRenderer.ExtractIconWithInheritance(childCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Graphics.Count);
        // Order should be: grandparent (rectangle), parent (ellipse), child (polygon)
        Assert.IsType<RectanglePrimitive>(result.Graphics[0]);
        Assert.IsType<EllipsePrimitive>(result.Graphics[1]);
        Assert.IsType<PolygonPrimitive>(result.Graphics[2]);
    }

    [Fact]
    public void ExtractIconWithInheritance_CircularInheritance_DoesNotLoop()
    {
        // Arrange - A extends B, B extends A (circular)
        var modelACode = @"
model ModelA
  extends ModelB;
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end ModelA;";

        var modelBCode = @"
model ModelB
  extends ModelA;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}})}));
end ModelB;";

        Func<string, string?> resolver = name =>
        {
            return name switch
            {
                "ModelA" => modelACode,
                "ModelB" => modelBCode,
                _ => null
            };
        };

        // Act - should not throw or loop infinitely
        var result = IconSvgRenderer.ExtractIconWithInheritance(modelACode, resolver);

        // Assert
        Assert.NotNull(result);
        // Should have graphics from both but not duplicate due to circular reference protection
    }

    [Fact]
    public void ExtractIconWithInheritance_MaxDepthExceeded_StopsRecursion()
    {
        // Arrange - Deep chain that exceeds max depth
        var code = @"
model Level1
  extends Level2;
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end Level1;";

        int resolveCount = 0;
        Func<string, string?> resolver = name =>
        {
            resolveCount++;
            // Return a model that extends another level
            return $@"
model {name}
  extends Level{resolveCount + 2};
  annotation(Icon(graphics={{Ellipse(extent={{{{-50,-50}},{{50,50}}}})}}));
end {name};";
        };

        // Act - with maxDepth of 3
        var result = IconSvgRenderer.ExtractIconWithInheritance(code, resolver, maxDepth: 3);

        // Assert
        Assert.NotNull(result);
        // Should stop at maxDepth, not recurse infinitely
        Assert.True(resolveCount <= 3);
    }

    #endregion

    #region Full Rendering with Inheritance Tests

    [Fact]
    public void ExtractAndRenderIconWithInheritance_ReturnsValidSvg()
    {
        // Arrange
        var baseCode = @"
model BaseModel
  annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, fillColor={255,255,255}, fillPattern=FillPattern.Solid)}));
end BaseModel;";

        var derivedCode = @"
model DerivedModel
  extends BaseModel;
  annotation(Icon(graphics={Ellipse(extent={{-50,-50},{50,50}}, fillColor={0,0,255}, fillPattern=FillPattern.Solid)}));
end DerivedModel;";

        Func<string, string?> resolver = name =>
        {
            if (name == "BaseModel") return baseCode;
            return null;
        };

        // Act
        var result = IconSvgRenderer.ExtractAndRenderIconWithInheritance(derivedCode, resolver);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("<svg", result);
        Assert.Contains("<rect", result); // Base rectangle
        Assert.Contains("<ellipse", result); // Derived ellipse
    }

    #endregion
}
