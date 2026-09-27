using ModelicaParser.Icons;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests.Icons;

/// <summary>
/// Which coordinate system a class draws in when it does not state one (B394), by MLS 3.6
/// §18.6.1.1: "The coordinate system attributes (extent and preserveAspectRatio) of a class are
/// separately defined by the following priority: 1. The coordinate system annotation given in the
/// class (if specified). 2. The coordinate systems of the first base class where the extent on the
/// extends-clause specifies a null-region (if any). 3. The default coordinate system."
///
/// <para>The icon merge used to keep the derived class's own system whenever it had an Icon
/// annotation at all, stated or not, and otherwise took the first base that <em>drew</em>
/// something. In the Buildings library that drew 52 icons - <c>DHC.ETS.BaseClasses.CollectorDistributor</c>,
/// <c>DHC.ETS.Combined.HeatPumpHeatExchanger</c> among them - in -100..100 when their bases state
/// -200..200 or -300..300, so each was scaled wrong wherever it appeared.</para>
/// </summary>
public class CoordinateSystemInheritanceTests
{
    // --- The rule ---------------------------------------------------------------------------

    private static IconData Stated(double[]? extent = null, bool? aspect = null, double? scale = null) => new()
    {
        CoordinateExtent = extent ?? [-100, -100, 100, 100],
        DeclaresExtent = extent is not null,
        PreserveAspectRatio = aspect ?? true,
        DeclaresPreserveAspectRatio = aspect is not null,
        InitialScale = scale ?? 0.1,
        DeclaresInitialScale = scale is not null,
    };

    [Fact]
    public void EachAttributeTheClassStatesIsItsOwn()
    {
        var resolved = IconData.ResolveCoordinateSystem(
            Stated([-50, -50, 50, 50], false, 0.2), Stated([-300, -300, 300, 300], true, 0.5));

        Assert.Equal([-50, -50, 50, 50], resolved.CoordinateExtent);
        Assert.False(resolved.PreserveAspectRatio);
        Assert.Equal(0.2, resolved.InitialScale);
    }

    [Fact]
    public void EachAttributeIsInheritedSeparately()
    {
        // coordinateSystem(preserveAspectRatio=false) alone - 27 MSL classes write that - still
        // takes its extent from the base.
        var resolved = IconData.ResolveCoordinateSystem(
            Stated(aspect: false), Stated([-300, -300, 300, 300], true, 0.5));

        Assert.Equal([-300, -300, 300, 300], resolved.CoordinateExtent);
        Assert.True(resolved.DeclaresExtent);
        Assert.False(resolved.PreserveAspectRatio);
        Assert.Equal(0.5, resolved.InitialScale);
    }

    [Fact]
    public void WithNothingStatedAndNoBase_ItIsTheDefault()
    {
        var resolved = IconData.ResolveCoordinateSystem(new IconData { CoordinateExtent = [1, 2, 3, 4] }, null);

        Assert.Equal([-100, -100, 100, 100], resolved.CoordinateExtent);
        Assert.False(resolved.DeclaresExtent);
        Assert.True(resolved.PreserveAspectRatio);
        Assert.Equal(0.1, resolved.InitialScale);
    }

    // --- What the extractor records ---------------------------------------------------------

    [Fact]
    public void TheExtractorRecordsWhichAttributesWereStated()
    {
        var icon = IconExtractor.ExtractIcon("""
            model M
              annotation (Icon(coordinateSystem(preserveAspectRatio=false, initialScale=0.2)));
            end M;
            """)!;

        Assert.False(icon.DeclaresExtent);
        Assert.True(icon.DeclaresPreserveAspectRatio);
        Assert.True(icon.DeclaresInitialScale);
    }

    [Fact]
    public void AnExtendsClauseMappingItsBaseIntoARegion_IsRecordedForItsOwnLayerOnly()
    {
        var result = IconExtractor.ExtractIconWithInheritance("""
            model M
              extends A annotation (IconMap(extent={{-50,-50},{50,50}}));
              extends B annotation (IconMap(extent={{0,0},{0,0}}), DiagramMap(extent={{-10,-10},{10,10}}));
              extends C annotation (IconMap(primitivesVisible=false));
              extends D;
            end M;
            """)!;

        Assert.Equal(["A"], result.MappedExtends);
    }

    // --- The icon merge ---------------------------------------------------------------------

    private static IconData? Icon(string code, Dictionary<string, string> bases)
        => IconSvgRenderer.ExtractIconWithInheritance(code, n => bases.GetValueOrDefault(n));

    [Fact]
    public void AnIconThatStatesNoExtent_DrawsInItsBasesSystem()
    {
        var bases = new Dictionary<string, string>
        {
            ["Base"] = """
                partial model Base
                  annotation (Icon(coordinateSystem(extent={{-300,-300},{300,300}}),
                    graphics={Rectangle(extent={{-300,-300},{300,300}})}));
                end Base;
                """,
        };

        var icon = Icon("""
            model M
              extends Base;
              annotation (Icon(coordinateSystem(preserveAspectRatio=false),
                graphics={Ellipse(extent={{-250,-250},{250,250}})}));
            end M;
            """, bases)!;

        Assert.Equal([-300, -300, 300, 300], icon.CoordinateExtent);
        Assert.False(icon.PreserveAspectRatio);
        Assert.Equal(2, icon.Graphics.Count);
    }

    [Fact]
    public void ABaseThatDrawsNothing_StillLendsItsSystem()
    {
        var bases = new Dictionary<string, string>
        {
            ["Frame"] = """
                partial model Frame
                  annotation (Icon(coordinateSystem(extent={{-200,-100},{200,100}})));
                end Frame;
                """,
        };

        var icon = Icon("""
            model M
              extends Frame;
              annotation (Icon(graphics={Rectangle(extent={{-200,-100},{200,100}})}));
            end M;
            """, bases)!;

        Assert.Equal([-200, -100, 200, 100], icon.CoordinateExtent);
    }

    [Fact]
    public void TheSystemComesFromTheFirstBase_ThroughItsOwnChain()
    {
        // M -> Middle (states nothing) -> Deep (states -200) is the first extends; Other states -50.
        var bases = new Dictionary<string, string>
        {
            ["Deep"] = """
                partial model Deep
                  annotation (Icon(coordinateSystem(extent={{-200,-200},{200,200}}), graphics={Line(points={{0,0},{1,1}})}));
                end Deep;
                """,
            ["Middle"] = """
                partial model Middle
                  extends Deep;
                  annotation (Icon(graphics={Line(points={{0,0},{2,2}})}));
                end Middle;
                """,
            ["Other"] = """
                partial model Other
                  annotation (Icon(coordinateSystem(extent={{-50,-50},{50,50}}), graphics={Line(points={{0,0},{3,3}})}));
                end Other;
                """,
        };

        var icon = Icon("model M extends Middle; extends Other; end M;", bases)!;

        Assert.Equal([-200, -200, 200, 200], icon.CoordinateExtent);
        Assert.Equal(3, icon.Graphics.Count);
    }

    [Fact]
    public void AFirstBaseThatStatesNothing_LendsTheDefault()
    {
        // The rule names the first base, not the first base that states something.
        var bases = new Dictionary<string, string>
        {
            ["Plain"] = "partial model Plain annotation (Icon(graphics={Line(points={{0,0},{1,1}})})); end Plain;",
            ["Wide"] = """
                partial model Wide
                  annotation (Icon(coordinateSystem(extent={{-300,-300},{300,300}}), graphics={Line(points={{0,0},{2,2}})}));
                end Wide;
                """,
        };

        var icon = Icon("model M extends Plain; extends Wide; end M;", bases)!;

        Assert.Equal([-100, -100, 100, 100], icon.CoordinateExtent);
    }

    [Fact]
    public void ABaseMappedIntoARegion_DoesNotLendItsSystem()
    {
        var bases = new Dictionary<string, string>
        {
            ["Small"] = """
                partial model Small
                  annotation (Icon(coordinateSystem(extent={{-10,-10},{10,10}}), graphics={Line(points={{0,0},{1,1}})}));
                end Small;
                """,
            ["Wide"] = """
                partial model Wide
                  annotation (Icon(coordinateSystem(extent={{-300,-300},{300,300}}), graphics={Line(points={{0,0},{2,2}})}));
                end Wide;
                """,
        };

        var icon = Icon("""
            model M
              extends Small annotation (IconMap(extent={{-50,-50},{50,50}}));
              extends Wide;
            end M;
            """, bases)!;

        Assert.Equal([-300, -300, 300, 300], icon.CoordinateExtent);
    }

    [Fact]
    public void AClassWithNoIconAndNoDrawingBase_HasNoIcon()
    {
        var bases = new Dictionary<string, string>
        {
            ["Frame"] = "partial model Frame annotation (Icon(coordinateSystem(extent={{-200,-100},{200,100}}))); end Frame;",
        };

        Assert.Null(Icon("model M extends Frame; end M;", bases));
    }
}
