using System;
using System.Collections.Concurrent;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="UnitResolver.ResolveAttributes"/>: the unit, displayUnit and quantity strings a type's
/// short-class chain fixes, the nearest definition winning attribute by attribute — what a static
/// units check needs, where <see cref="UnitResolver.Resolve"/> answers only yes or no.
/// </summary>
public class UnitResolverTests
{
    private static DirectedGraph Library(params (string Name, string Code, string ClassType)[] types)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("U", "U", "package U end U;") { ClassType = "package" });
        foreach (var (name, code, classType) in types)
            graph.AddNode(new ModelNode("U." + name, name, code)
            { ClassType = classType, IsNested = true, ParentModelName = "U" });
        graph.AddNode(new ModelNode("U.M", "M", "model M end M;")
        { ClassType = "model", IsNested = true, ParentModelName = "U" });
        return graph;
    }

    private static UnitAttributes Attributes(DirectedGraph graph, string type)
        => UnitResolver.ResolveAttributes(graph, "U.M", type, []);

    [Fact]
    public void ADirectAlias_ReportsEveryAttributeItWrites()
    {
        var graph = Library(("Torque",
            "type Torque = Real(final quantity=\"Torque\", final unit=\"N.m\", displayUnit=\"kN.m\");", "type"));

        Assert.Equal(new UnitAttributes(true, "N.m", "kN.m", "Torque"), Attributes(graph, "Torque"));
    }

    [Fact]
    public void AChain_TakesEachAttributeFromTheNearestDefinitionThatSetsIt()
    {
        var graph = Library(
            ("Pressure", "type Pressure = Real(quantity=\"Pressure\", unit=\"Pa\", displayUnit=\"bar\");", "type"),
            ("AbsolutePressure", "type AbsolutePressure = Pressure(displayUnit=\"kPa\");", "type"),
            ("Gauge", "type Gauge = AbsolutePressure(unit=\"bar\");", "type"),
            ("Stress", "type Stress = Pressure(quantity=\"Stress\");", "type"));

        Assert.Equal(new UnitAttributes(true, "Pa", "kPa", "Pressure"), Attributes(graph, "AbsolutePressure"));
        Assert.Equal(new UnitAttributes(true, "bar", "kPa", "Pressure"), Attributes(graph, "Gauge"));
        Assert.Equal(new UnitAttributes(true, "Pa", "bar", "Stress"), Attributes(graph, "Stress"));
    }

    [Fact]
    public void ARealWithNothingFixed_IsRealDerivedWithNoAttributes()
    {
        var graph = Library(("Fraction", "type Fraction = Real;", "type"));

        Assert.Equal(UnitAttributes.PlainReal, Attributes(graph, "Fraction"));
        Assert.Equal(UnitAttributes.PlainReal, Attributes(graph, "Real"));
        Assert.False(UnitAttributes.PlainReal.HasUnit);
    }

    [Theory]
    [InlineData("Integer")]
    [InlineData("Count")]       // an alias of a predefined non-Real type
    [InlineData("RealInput")]   // a connector: a signal, not a physical scalar
    [InlineData("Unknown")]     // resolves to nothing
    [InlineData("")]
    public void AnythingThatIsNotARealQuantity_IsNone(string type)
    {
        var graph = Library(
            ("Count", "type Count = Integer(quantity=\"Count\");", "type"),
            ("RealInput", "connector RealInput = input Real(unit=\"m\");", "connector"));

        Assert.Equal(UnitAttributes.None, Attributes(graph, type));
    }

    [Fact]
    public void ARealOverANonRealBase_DropsTheAttributesItWrites()
    {
        // The alias writes a unit, but its base is not a Real quantity, so there is no unit to report.
        var graph = Library(
            ("Count", "type Count = Integer;", "type"),
            ("Bad", "type Bad = Count(unit=\"m\");", "type"));

        Assert.Equal(UnitAttributes.None, Attributes(graph, "Bad"));
    }

    [Fact]
    public void AnEscapedLiteral_IsUnescaped_AndANonLiteralKeepsItsSourceText()
    {
        var graph = Library(
            ("Quoted", "type Quoted = Real(unit=\"a\\\"b\\\\c\");", "type"),
            ("Joined", "type Joined = Real(unit=\"N\" + \".m\", quantity=Q.name);", "type"),
            ("Bare", "type Bare = Real(unit);", "type"));

        Assert.Equal("a\"b\\c", Attributes(graph, "Quoted").Unit);
        var joined = Attributes(graph, "Joined");
        Assert.Equal("\"N\" + \".m\"", joined.Unit);   // spaces kept: never GetText()
        Assert.Equal("Q.name", joined.Quantity);       // and none added between adjacent tokens
        Assert.True(joined.HasUnit);

        // A unit modified with no value is still modified, as Resolve has always counted it.
        Assert.Equal(new UnitAttributes(true, "", null, null), Attributes(graph, "Bare"));
    }

    [Fact]
    public void Resolve_IsTheYesOrNoViewOfTheSameAnswer_AndSharesTheCache()
    {
        var graph = Library(
            ("Length", "type Length = Real(unit=\"m\", quantity=\"Length\");", "type"),
            ("Height", "type Height = Length;", "type"),
            ("Fraction", "type Fraction = Real;", "type"));
        var cache = new ConcurrentDictionary<string, UnitAttributes>(StringComparer.Ordinal);

        Assert.Equal((true, true), UnitResolver.Resolve(graph, "U.M", "Height", [], cache));
        Assert.Equal((true, false), UnitResolver.Resolve(graph, "U.M", "Fraction", [], cache));

        // The chain walked for Resolve is cached with its strings, so a later attribute question
        // about the same classes is answered from it.
        Assert.Equal(new UnitAttributes(true, "m", null, "Length"), cache["U.Length"]);
        Assert.Equal(cache["U.Length"], cache["U.Height"]);
        Assert.Equal(new UnitAttributes(true, "m", null, "Length"),
            UnitResolver.ResolveAttributes(graph, "U.M", "Height", [], cache));
    }

    [Fact]
    public void ACycle_IsNone()
    {
        var graph = Library(
            ("A", "type A = B(unit=\"m\");", "type"),
            ("B", "type B = A;", "type"));

        Assert.Equal(UnitAttributes.None, Attributes(graph, "A"));
    }
}
