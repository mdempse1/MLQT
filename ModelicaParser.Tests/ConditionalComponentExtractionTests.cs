using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// The two things a declaration carries that nothing read until B277 and B278: the <c>if</c> that
/// makes a component conditional, and the modification that says what its parameters are.
/// </summary>
public class ConditionalComponentExtractionTests
{
    private static ClassElement Component(string source, string name)
    {
        var iface = ClassInterfaceExtractor.Extract(ModelicaParserHelper.Parse(source));
        Assert.NotNull(iface);
        return Assert.Single(iface!.Elements,
            e => e.Kind == ClassElementKind.Component && e.Name == name);
    }

    [Fact]
    public void AConditionalComponentCarriesItsCondition()
    {
        var element = Component("""
            model M
              parameter Boolean useHeatPort = false;
              HeatPort heatPort if useHeatPort "optional";
            end M;
            """, "heatPort");

        Assert.Equal("useHeatPort", element.Condition);
        Assert.Equal("optional", element.Description);
    }

    [Fact]
    public void TheConditionKeepsTheSpacesItWasWrittenWith()
    {
        // GetText() concatenates token texts, so `use_reset and use_set` came back as
        // `use_resetanduse_set` — one identifier that resolves to nothing, which reads as an
        // undecidable condition rather than as a bug. It survived until a connector that should
        // have gone stayed on the picture.
        var element = Component("""
            model M
              RealInput set if use_reset and use_set;
            end M;
            """, "set");

        Assert.Equal("use_reset and use_set", element.Condition);
        Assert.False(ModelicaCondition.Evaluate(element.Condition, _ => "false"));
    }

    [Fact]
    public void AnOrdinaryComponentHasNoCondition()
        => Assert.Null(Component("model M\n  Real x;\nend M;", "x").Condition);

    [Fact]
    public void AComponentCarriesTheScalarModificationsItWasGiven()
    {
        var element = Component("""
            model M
              Inertia inertia1(J = 1, phi(fixed = true, start = 0)) "an inertia";
            end M;
            """, "inertia1");

        Assert.NotNull(element.Modifications);

        // The nested one configures a sub-component and is not a value this component takes.
        Assert.Equal("1", Assert.Single(element.Modifications!).Value);
        Assert.Equal("J", element.Modifications!.Keys.Single());
    }

    [Fact]
    public void AModificationAndABindingAreDifferentThings()
    {
        var element = Component("model M\n  Real k(min = 0) = 5;\nend M;", "k");

        Assert.Equal("5", element.DefaultValue);
        Assert.Equal("0", Assert.Single(element.Modifications!).Value);
    }

    [Fact]
    public void AComponentWithNoModificationHasNone()
        => Assert.Null(Component("model M\n  Real x;\nend M;", "x").Modifications);
}
