using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

public class ModelicaLanguageTests
{
    [Theory]
    [InlineData("Real")]
    [InlineData("Integer")]
    [InlineData("ExternalObject")]
    [InlineData("StateSelect")]
    [InlineData("der")]
    [InlineData("sum")]
    [InlineData("homotopy")]
    [InlineData("time")]
    [InlineData("rooted")]        // callable unqualified, and still written that way
    [InlineData("Connections")]   // the pseudo-package, which no uses(...) can declare
    public void LanguageNames_AreRecognised(string name)
        => Assert.True(ModelicaLanguage.IsBuiltInName(name));

    [Theory]
    [InlineData("Connections.branch")]
    [InlineData("Connections.rooted")]
    [InlineData("Real.foo")]
    public void FirstSegmentDecides(string reference)
        => Assert.True(ModelicaLanguage.IsBuiltInName(reference));

    [Theory]
    // Modelica is case-sensitive, and Modelica.Blocks.Math is full of classes that collide with a
    // built-in function under any other comparison. Each of these is a real MSL class (B246).
    [InlineData("Sum")]
    [InlineData("Product")]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("Abs")]
    [InlineData("Sign")]
    [InlineData("Sqrt")]
    [InlineData("Exp")]
    [InlineData("Log")]
    [InlineData("Sin")]
    // ...and these are not language names at all.
    [InlineData("Complex")]       // an operator record in MSL, resolvable as a class
    [InlineData("Line")]          // annotation grammar, but a library may define a class of the name
    [InlineData("Medium")]
    public void ClassNames_AreNotLanguageNames(string name)
        => Assert.False(ModelicaLanguage.IsBuiltInName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoName_IsNotALanguageName(string? name)
        => Assert.False(ModelicaLanguage.IsBuiltInName(name));

    [Fact]
    public void PredefinedTypes_AreTheTypesOnly()
    {
        // The narrow list a resolver asks against: a type that is never a class anywhere. An
        // operator is not one of them, or a resolver would stop looking for a class called Sum.
        Assert.Contains("Real", ModelicaLanguage.PredefinedTypes);
        Assert.Contains("ExternalObject", ModelicaLanguage.PredefinedTypes);
        Assert.Contains("enumeration", ModelicaLanguage.PredefinedTypes);
        Assert.DoesNotContain("sum", ModelicaLanguage.PredefinedTypes);
        Assert.DoesNotContain("time", ModelicaLanguage.PredefinedTypes);
        Assert.DoesNotContain("Complex", ModelicaLanguage.PredefinedTypes);
    }

    [Fact]
    public void EveryPredefinedType_IsAlsoALanguageName()
    {
        // The wider list is built from the narrower one, so the two cannot drift apart.
        foreach (var type in ModelicaLanguage.PredefinedTypes)
            Assert.Contains(type, ModelicaLanguage.Names);
    }
}
