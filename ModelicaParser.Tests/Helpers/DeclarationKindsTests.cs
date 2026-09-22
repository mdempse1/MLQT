using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

/// <summary>
/// B252 — what kind of declaration this is. Three of the five kinds are syntax; telling a variable
/// from a component is not, and is why this takes a resolver.
/// </summary>
public class DeclarationKindsTests
{
    private static DeclarationKind KindOf(string declaration, Func<string, bool>? isSimpleType = null)
    {
        var tree = ModelicaParserHelper.Parse($"model M\n  {declaration}\nend M;");
        var clause = tree.class_definition()[0].class_specifier().long_class_specifier()
            .composition().element_list()[0].element()[0].component_clause();
        return DeclarationKinds.KindOf(clause, isSimpleType);
    }

    [Theory]
    [InlineData("input Real u;")]
    [InlineData("output Real y;")]
    [InlineData("input Resistor r;")]
    // Both prefixes are legal together, and it is still part of the signature.
    [InlineData("parameter input Real n;")]
    [InlineData("flow input Real i;")]
    public void CausalDeclarations_ComeFirst(string declaration)
        => Assert.Equal(DeclarationKind.InputOutput, KindOf(declaration));

    [Fact]
    public void ConstantAndParameter_AreReadFromThePrefix()
    {
        Assert.Equal(DeclarationKind.Constant, KindOf("constant Real g = 9.81;"));
        Assert.Equal(DeclarationKind.Parameter, KindOf("parameter Real m = 1;"));
        // The prefix decides, whatever the type is: a parameter of a record type is a parameter.
        Assert.Equal(DeclarationKind.Parameter, KindOf("parameter MediumRecord medium;"));
    }

    [Fact]
    public void APredefinedType_IsAVariable_WithNoResolverAtAll()
    {
        Assert.Equal(DeclarationKind.Variable, KindOf("Real x;"));
        Assert.Equal(DeclarationKind.Variable, KindOf("Integer n;"));
        Assert.Equal(DeclarationKind.Variable, KindOf("Boolean b;"));
        Assert.Equal(DeclarationKind.Variable, KindOf("String s;"));
        Assert.Equal(DeclarationKind.Variable, KindOf("discrete Real xd;"));
        // A leading dot is Modelica 3.6's fully-qualified form of the same name.
        Assert.Equal(DeclarationKind.Variable, KindOf(".Real x;"));
    }

    [Fact]
    public void AnUnresolvedType_IsAComponent()
    {
        // The honest answer with no graph: leave it where a component goes rather than sort it on a
        // guess. It is also what the renderer does, so the two agree.
        Assert.Equal(DeclarationKind.Component, KindOf("SI.Length x;"));
        Assert.Equal(DeclarationKind.Component, KindOf("Resistor r;"));
    }

    [Fact]
    public void AResolvedSimpleType_IsAVariable()
    {
        // The case the whole item turns on: SI.Length is a variable by every convention, and a class
        // by the grammar. Only a resolver can tell.
        Assert.Equal(DeclarationKind.Variable, KindOf("SI.Length x;", t => t == "SI.Length"));
        Assert.Equal(DeclarationKind.Component, KindOf("Resistor r;", t => t == "SI.Length"));
    }

    [Fact]
    public void ThePrefixIsReadAsWords_NotAsText()
    {
        // GetText() runs the prefix keywords together, so a substring test would find 'parameter'
        // inside a type called 'parameterised' just as readily.
        Assert.Equal(DeclarationKind.Parameter, KindOf("flow parameter Real p;"));
        Assert.Equal(DeclarationKind.Component, KindOf("parameterised x;"));
        Assert.Equal(DeclarationKind.Component, KindOf("constantly c;"));
    }

    [Fact]
    public void TheOrderIsTheOneTheRuleAndTheRendererShare()
    {
        Assert.Equal(
            new[]
            {
                DeclarationKind.InputOutput, DeclarationKind.Constant, DeclarationKind.Parameter,
                DeclarationKind.Variable, DeclarationKind.Component
            },
            DeclarationKinds.Order);

        for (var i = 0; i < DeclarationKinds.Order.Count; i++)
            Assert.Equal(i, DeclarationKinds.PositionOf(DeclarationKinds.Order[i]));
    }

    [Fact]
    public void EveryKindHasSomethingToCallIt()
    {
        foreach (var kind in Enum.GetValues<DeclarationKind>())
        {
            var (one, many) = DeclarationKinds.Describe(kind);
            Assert.False(string.IsNullOrWhiteSpace(one));
            Assert.False(string.IsNullOrWhiteSpace(many));
        }
    }

    [Fact]
    public void ATypeThatIsNotThere_IsNotAQuantity()
    {
        Assert.False(DeclarationKinds.IsQuantity(null));
        Assert.False(DeclarationKinds.IsQuantity(""));
    }
}
