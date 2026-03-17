using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

public class NamingConventionPresetsTests
{
    // ========================================================================
    // ModelicaStandard
    // ========================================================================

    [Fact]
    public void ModelicaStandard_PresetName()
    {
        var settings = NamingConventionPresets.ModelicaStandard();
        Assert.Equal("Modelica Standard", settings.PresetName);
    }

    [Fact]
    public void ModelicaStandard_ClassNamingIsPascalCase()
    {
        var settings = NamingConventionPresets.ModelicaStandard();
        Assert.Equal(NamingStyle.PascalCase, settings.ModelNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.BlockNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.ConnectorNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.RecordNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.TypeNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.PackageNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.ClassNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.OperatorNaming);
    }

    [Fact]
    public void ModelicaStandard_FunctionIsCamelCase()
    {
        var settings = NamingConventionPresets.ModelicaStandard();
        Assert.Equal(NamingStyle.CamelCase, settings.FunctionNaming);
    }

    [Fact]
    public void ModelicaStandard_AllElementsAreCamelCase()
    {
        var settings = NamingConventionPresets.ModelicaStandard();
        Assert.Equal(NamingStyle.CamelCase, settings.PublicVariableNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.PublicParameterNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.PublicConstantNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedVariableNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedParameterNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedConstantNaming);
    }

    [Fact]
    public void ModelicaStandard_AllowsUnderscoreSuffixes()
    {
        var settings = NamingConventionPresets.ModelicaStandard();
        Assert.True(settings.AllowUnderscoreSuffixes);
    }

    // ========================================================================
    // SnakeCase
    // ========================================================================

    [Fact]
    public void SnakeCase_PresetName()
    {
        var settings = NamingConventionPresets.SnakeCase();
        Assert.Equal("snake_case", settings.PresetName);
    }

    [Fact]
    public void SnakeCase_AllNamingIsSnakeCase()
    {
        var settings = NamingConventionPresets.SnakeCase();
        Assert.Equal(NamingStyle.SnakeCase, settings.ModelNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.FunctionNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.BlockNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.ConnectorNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.RecordNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.TypeNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.PackageNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.ClassNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.OperatorNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.PublicVariableNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.PublicParameterNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.PublicConstantNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.ProtectedVariableNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.ProtectedParameterNaming);
        Assert.Equal(NamingStyle.SnakeCase, settings.ProtectedConstantNaming);
    }

    [Fact]
    public void SnakeCase_DoesNotAllowUnderscoreSuffixes()
    {
        var settings = NamingConventionPresets.SnakeCase();
        Assert.False(settings.AllowUnderscoreSuffixes);
    }

    // ========================================================================
    // UpperCaseConstants
    // ========================================================================

    [Fact]
    public void UpperCaseConstants_PresetName()
    {
        var settings = NamingConventionPresets.UpperCaseConstants();
        Assert.Equal("Modelica + UPPER_CASE Constants", settings.PresetName);
    }

    [Fact]
    public void UpperCaseConstants_ConstantsAreUpperCase()
    {
        var settings = NamingConventionPresets.UpperCaseConstants();
        Assert.Equal(NamingStyle.UpperCase, settings.PublicConstantNaming);
        Assert.Equal(NamingStyle.UpperCase, settings.ProtectedConstantNaming);
    }

    [Fact]
    public void UpperCaseConstants_NonConstantElementsAreCamelCase()
    {
        var settings = NamingConventionPresets.UpperCaseConstants();
        Assert.Equal(NamingStyle.CamelCase, settings.PublicVariableNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.PublicParameterNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedVariableNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedParameterNaming);
    }

    [Fact]
    public void UpperCaseConstants_ClassNamingMatchesModelicaStandard()
    {
        var settings = NamingConventionPresets.UpperCaseConstants();
        Assert.Equal(NamingStyle.PascalCase, settings.ModelNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.FunctionNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.BlockNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.ConnectorNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.RecordNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.TypeNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.PackageNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.ClassNaming);
        Assert.Equal(NamingStyle.PascalCase, settings.OperatorNaming);
    }

    [Fact]
    public void UpperCaseConstants_AllowsUnderscoreSuffixes()
    {
        var settings = NamingConventionPresets.UpperCaseConstants();
        Assert.True(settings.AllowUnderscoreSuffixes);
    }

    // ========================================================================
    // All
    // ========================================================================

    [Fact]
    public void All_ContainsThreePresets()
    {
        Assert.Equal(3, NamingConventionPresets.All.Count);
    }

    [Fact]
    public void All_NamesMatchPresetNames()
    {
        foreach (var (name, factory) in NamingConventionPresets.All)
        {
            var settings = factory();
            Assert.Equal(name, settings.PresetName);
        }
    }

    [Fact]
    public void All_FactoriesReturnNewInstances()
    {
        foreach (var (_, factory) in NamingConventionPresets.All)
        {
            var a = factory();
            var b = factory();
            Assert.NotSame(a, b);
        }
    }

    [Fact]
    public void All_ContainsExpectedNames()
    {
        var names = NamingConventionPresets.All.Select(p => p.Name).ToList();
        Assert.Contains("Modelica Standard", names);
        Assert.Contains("snake_case", names);
        Assert.Contains("Modelica + UPPER_CASE Constants", names);
    }

    // ========================================================================
    // Each preset produces a valid ToConfig conversion
    // ========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AllPresets_ToConfig_ProducesNineClassRules(int index)
    {
        var settings = NamingConventionPresets.All[index].Factory();
        var config = settings.ToConfig();
        Assert.Equal(9, config.ClassNamingRules.Count);
        Assert.True(config.ClassNamingRules.ContainsKey("model"));
        Assert.True(config.ClassNamingRules.ContainsKey("function"));
        Assert.True(config.ClassNamingRules.ContainsKey("block"));
        Assert.True(config.ClassNamingRules.ContainsKey("connector"));
        Assert.True(config.ClassNamingRules.ContainsKey("record"));
        Assert.True(config.ClassNamingRules.ContainsKey("type"));
        Assert.True(config.ClassNamingRules.ContainsKey("package"));
        Assert.True(config.ClassNamingRules.ContainsKey("class"));
        Assert.True(config.ClassNamingRules.ContainsKey("operator"));
    }
}
