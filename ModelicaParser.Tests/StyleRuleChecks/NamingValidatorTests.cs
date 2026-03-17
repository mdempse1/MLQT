using Xunit;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class NamingValidatorTests
{
    // ── IsCamelCase ──

    [Theory]
    [InlineData("myVar", true)]
    [InlineData("x", true)]
    [InlineData("camelCase123", true)]
    [InlineData("pressure", true)]
    [InlineData("MyVar", false)]
    [InlineData("my_var", false)]
    [InlineData("UPPER", false)]
    [InlineData("", false)]
    public void IsCamelCase_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, NamingValidator.IsCamelCase(name));
    }

    // ── IsPascalCase ──

    [Theory]
    [InlineData("MyModel", true)]
    [InlineData("X", true)]
    [InlineData("PascalCase123", true)]
    [InlineData("Temperature", true)]
    [InlineData("myModel", false)]
    [InlineData("my_model", false)]
    [InlineData("MY_MODEL", false)]
    [InlineData("", false)]
    public void IsPascalCase_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, NamingValidator.IsPascalCase(name));
    }

    // ── IsSnakeCase ──

    [Theory]
    [InlineData("my_var", true)]
    [InlineData("x", true)]
    [InlineData("snake_case_123", true)]
    [InlineData("simple", true)]
    [InlineData("MyVar", false)]
    [InlineData("camelCase", false)]
    [InlineData("_leading", false)]
    [InlineData("trailing_", false)]
    [InlineData("double__underscore", false)]
    [InlineData("UPPER_CASE", false)]
    [InlineData("", false)]
    public void IsSnakeCase_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, NamingValidator.IsSnakeCase(name));
    }

    // ── IsUpperCase ──

    [Theory]
    [InlineData("MY_CONST", true)]
    [InlineData("X", true)]
    [InlineData("UPPER_123", true)]
    [InlineData("SIMPLE", true)]
    [InlineData("myConst", false)]
    [InlineData("My_Const", false)]
    [InlineData("_LEADING", false)]
    [InlineData("TRAILING_", false)]
    [InlineData("DOUBLE__UNDER", false)]
    [InlineData("", false)]
    public void IsUpperCase_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, NamingValidator.IsUpperCase(name));
    }

    // ── StripSuffix ──

    [Fact]
    public void StripSuffix_NoUnderscore_ReturnsOriginal()
    {
        var (baseName, suffix) = NamingValidator.StripSuffix("myVariable");
        Assert.Equal("myVariable", baseName);
        Assert.Null(suffix);
    }

    [Fact]
    public void StripSuffix_LeadingUnderscoreOnly_ReturnsOriginal()
    {
        var (baseName, suffix) = NamingValidator.StripSuffix("_x");
        Assert.Equal("_x", baseName);
        Assert.Null(suffix);
    }

    [Fact]
    public void StripSuffix_TrailingUnderscore_ReturnsOriginal()
    {
        var (baseName, suffix) = NamingValidator.StripSuffix("pressure_");
        Assert.Equal("pressure_", baseName);
        Assert.Null(suffix);
    }

    [Fact]
    public void StripSuffix_WithSuffix_StripsLastSegment()
    {
        var (baseName, suffix) = NamingValidator.StripSuffix("pressure_in");
        Assert.Equal("pressure", baseName);
        Assert.Equal("in", suffix);
    }

    [Fact]
    public void StripSuffix_SingleCharSuffix()
    {
        var (baseName, suffix) = NamingValidator.StripSuffix("temperature_a");
        Assert.Equal("temperature", baseName);
        Assert.Equal("a", suffix);
    }

    [Fact]
    public void StripSuffix_MultipleUnderscores_StripsOnlyLast()
    {
        var (baseName, suffix) = NamingValidator.StripSuffix("my_long_var_in");
        Assert.Equal("my_long_var", baseName);
        Assert.Equal("in", suffix);
    }

    // ── IsValid integration ──

    [Fact]
    public void IsValid_Any_AlwaysTrue()
    {
        Assert.True(NamingValidator.IsValid("anything_Goes", NamingStyle.Any));
        Assert.True(NamingValidator.IsValid("123", NamingStyle.Any));
    }

    [Fact]
    public void IsValid_SingleCharacter_AlwaysTrue()
    {
        Assert.True(NamingValidator.IsValid("x", NamingStyle.PascalCase));
        Assert.True(NamingValidator.IsValid("T", NamingStyle.CamelCase));
        Assert.True(NamingValidator.IsValid("p", NamingStyle.UpperCase));
    }

    // ── IsShortAbbreviation ──

    [Theory]
    [InlineData("T", true)]
    [InlineData("x", true)]
    [InlineData("P3", true)]
    [InlineData("V12", true)]
    [InlineData("T2", true)]
    [InlineData("a1", true)]
    [InlineData("myVar", false)]
    [InlineData("AB", false)]
    [InlineData("3P", false)]
    [InlineData("", false)]
    [InlineData("A1B", false)]
    public void IsShortAbbreviation_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, NamingValidator.IsShortAbbreviation(name));
    }

    [Fact]
    public void IsValid_ShortAbbreviation_AlwaysTrue()
    {
        Assert.True(NamingValidator.IsValid("P3", NamingStyle.CamelCase));
        Assert.True(NamingValidator.IsValid("V12", NamingStyle.CamelCase));
        Assert.True(NamingValidator.IsValid("T2", NamingStyle.PascalCase));
    }

    [Fact]
    public void IsValid_NullOrEmpty_AlwaysTrue()
    {
        Assert.True(NamingValidator.IsValid("", NamingStyle.PascalCase));
        Assert.True(NamingValidator.IsValid(null!, NamingStyle.PascalCase));
    }

    [Fact]
    public void IsValid_CamelCase_WithoutSuffix()
    {
        Assert.True(NamingValidator.IsValid("myVariable", NamingStyle.CamelCase));
        Assert.False(NamingValidator.IsValid("MyVariable", NamingStyle.CamelCase));
    }

    [Fact]
    public void IsValid_PascalCase_WithoutSuffix()
    {
        Assert.True(NamingValidator.IsValid("MyModel", NamingStyle.PascalCase));
        Assert.False(NamingValidator.IsValid("myModel", NamingStyle.PascalCase));
    }

    [Fact]
    public void IsValid_CamelCase_WithSuffixAllowed()
    {
        // "pressure_in" → base "pressure" → camelCase ✓
        Assert.True(NamingValidator.IsValid("pressure_in", NamingStyle.CamelCase, allowSuffixes: true));
    }

    [Fact]
    public void IsValid_CamelCase_WithSuffixNotAllowed()
    {
        // Without suffix stripping, underscore fails camelCase
        Assert.False(NamingValidator.IsValid("pressure_in", NamingStyle.CamelCase, allowSuffixes: false));
    }

    [Fact]
    public void IsValid_PascalCase_WithSuffixAllowed()
    {
        // "Temperature_a" → base "Temperature" → PascalCase ✓
        Assert.True(NamingValidator.IsValid("Temperature_a", NamingStyle.PascalCase, allowSuffixes: true));
    }

    [Fact]
    public void IsValid_SnakeCase_DirectMatch()
    {
        Assert.True(NamingValidator.IsValid("my_variable", NamingStyle.SnakeCase));
        Assert.False(NamingValidator.IsValid("myVariable", NamingStyle.SnakeCase));
    }

    [Fact]
    public void IsValid_UpperCase_DirectMatch()
    {
        Assert.True(NamingValidator.IsValid("MY_CONSTANT", NamingStyle.UpperCase));
        Assert.False(NamingValidator.IsValid("myConstant", NamingStyle.UpperCase));
    }

    [Fact]
    public void IsValid_SuffixStripping_ReducesToSingleChar_Valid()
    {
        // "T_a" → base "T" → single char → always valid
        Assert.True(NamingValidator.IsValid("T_a", NamingStyle.CamelCase, allowSuffixes: true));
    }

}
