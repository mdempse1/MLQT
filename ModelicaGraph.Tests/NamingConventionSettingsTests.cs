using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

public class NamingConventionSettingsTests
{
    // ========================================================================
    // Default values
    // ========================================================================

    [Fact]
    public void DefaultSettings_PresetNameIsModelicaStandard()
    {
        var settings = new NamingConventionSettings();
        Assert.Equal("Modelica Standard", settings.PresetName);
    }

    [Fact]
    public void DefaultSettings_ClassNamingDefaults()
    {
        var settings = new NamingConventionSettings();
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
    public void DefaultSettings_PublicElementDefaults()
    {
        var settings = new NamingConventionSettings();
        Assert.Equal(NamingStyle.CamelCase, settings.PublicVariableNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.PublicParameterNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.PublicConstantNaming);
    }

    [Fact]
    public void DefaultSettings_ProtectedElementDefaults()
    {
        var settings = new NamingConventionSettings();
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedVariableNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedParameterNaming);
        Assert.Equal(NamingStyle.CamelCase, settings.ProtectedConstantNaming);
    }

    [Fact]
    public void DefaultSettings_AllowUnderscoreSuffixesIsTrue()
    {
        var settings = new NamingConventionSettings();
        Assert.True(settings.AllowUnderscoreSuffixes);
    }

    [Fact]
    public void DefaultSettings_ExceptionNamesIsEmpty()
    {
        var settings = new NamingConventionSettings();
        Assert.Empty(settings.ExceptionNames);
    }

    // ========================================================================
    // ToConfig
    // ========================================================================

    [Fact]
    public void ToConfig_MapsAllClassNamingRules()
    {
        var settings = new NamingConventionSettings
        {
            ModelNaming = NamingStyle.SnakeCase,
            FunctionNaming = NamingStyle.UpperCase,
            BlockNaming = NamingStyle.CamelCase,
            ConnectorNaming = NamingStyle.Any,
            RecordNaming = NamingStyle.PascalCase,
            TypeNaming = NamingStyle.SnakeCase,
            PackageNaming = NamingStyle.UpperCase,
            ClassNaming = NamingStyle.CamelCase,
            OperatorNaming = NamingStyle.Any
        };

        var config = settings.ToConfig();

        Assert.Equal(NamingStyle.SnakeCase, config.ClassNamingRules["model"]);
        Assert.Equal(NamingStyle.UpperCase, config.ClassNamingRules["function"]);
        Assert.Equal(NamingStyle.CamelCase, config.ClassNamingRules["block"]);
        Assert.Equal(NamingStyle.Any, config.ClassNamingRules["connector"]);
        Assert.Equal(NamingStyle.PascalCase, config.ClassNamingRules["record"]);
        Assert.Equal(NamingStyle.SnakeCase, config.ClassNamingRules["type"]);
        Assert.Equal(NamingStyle.UpperCase, config.ClassNamingRules["package"]);
        Assert.Equal(NamingStyle.CamelCase, config.ClassNamingRules["class"]);
        Assert.Equal(NamingStyle.Any, config.ClassNamingRules["operator"]);
    }

    [Fact]
    public void ToConfig_MapsPublicElementNaming()
    {
        var settings = new NamingConventionSettings
        {
            PublicVariableNaming = NamingStyle.SnakeCase,
            PublicParameterNaming = NamingStyle.UpperCase,
            PublicConstantNaming = NamingStyle.PascalCase
        };

        var config = settings.ToConfig();
        Assert.Equal(NamingStyle.SnakeCase, config.PublicVariableNaming);
        Assert.Equal(NamingStyle.UpperCase, config.PublicParameterNaming);
        Assert.Equal(NamingStyle.PascalCase, config.PublicConstantNaming);
    }

    [Fact]
    public void ToConfig_MapsProtectedElementNaming()
    {
        var settings = new NamingConventionSettings
        {
            ProtectedVariableNaming = NamingStyle.UpperCase,
            ProtectedParameterNaming = NamingStyle.SnakeCase,
            ProtectedConstantNaming = NamingStyle.Any
        };

        var config = settings.ToConfig();
        Assert.Equal(NamingStyle.UpperCase, config.ProtectedVariableNaming);
        Assert.Equal(NamingStyle.SnakeCase, config.ProtectedParameterNaming);
        Assert.Equal(NamingStyle.Any, config.ProtectedConstantNaming);
    }

    [Fact]
    public void ToConfig_MapsAllowUnderscoreSuffixes()
    {
        var settingsTrue = new NamingConventionSettings { AllowUnderscoreSuffixes = true };
        Assert.True(settingsTrue.ToConfig().AllowUnderscoreSuffixes);

        var settingsFalse = new NamingConventionSettings { AllowUnderscoreSuffixes = false };
        Assert.False(settingsFalse.ToConfig().AllowUnderscoreSuffixes);
    }

    [Fact]
    public void ToConfig_MapsExceptionNames()
    {
        var settings = new NamingConventionSettings
        {
            ExceptionNames = ["NASCAR", "OMC", "ABC"]
        };

        var config = settings.ToConfig();
        Assert.Equal(3, config.ExceptionNames.Count);
        Assert.Contains("NASCAR", config.ExceptionNames);
        Assert.Contains("OMC", config.ExceptionNames);
        Assert.Contains("ABC", config.ExceptionNames);
    }

    [Fact]
    public void ToConfig_EmptyExceptionNames_ProducesEmptySet()
    {
        var settings = new NamingConventionSettings();
        var config = settings.ToConfig();
        Assert.Empty(config.ExceptionNames);
    }

    // ========================================================================
    // Equals
    // ========================================================================

    [Fact]
    public void Equals_IdenticalSettings_ReturnsTrue()
    {
        var a = NamingConventionPresets.ModelicaStandard();
        var b = NamingConventionPresets.ModelicaStandard();
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var settings = new NamingConventionSettings();
        Assert.False(settings.Equals(null!));
    }

    [Fact]
    public void Equals_DifferentPresetName_ReturnsFalse()
    {
        var a = new NamingConventionSettings { PresetName = "A" };
        var b = new NamingConventionSettings { PresetName = "B" };
        Assert.False(a.Equals(b));
    }

    [Theory]
    [InlineData(nameof(NamingConventionSettings.ModelNaming))]
    [InlineData(nameof(NamingConventionSettings.FunctionNaming))]
    [InlineData(nameof(NamingConventionSettings.BlockNaming))]
    [InlineData(nameof(NamingConventionSettings.ConnectorNaming))]
    [InlineData(nameof(NamingConventionSettings.RecordNaming))]
    [InlineData(nameof(NamingConventionSettings.TypeNaming))]
    [InlineData(nameof(NamingConventionSettings.PackageNaming))]
    [InlineData(nameof(NamingConventionSettings.ClassNaming))]
    [InlineData(nameof(NamingConventionSettings.OperatorNaming))]
    [InlineData(nameof(NamingConventionSettings.PublicVariableNaming))]
    [InlineData(nameof(NamingConventionSettings.PublicParameterNaming))]
    [InlineData(nameof(NamingConventionSettings.PublicConstantNaming))]
    [InlineData(nameof(NamingConventionSettings.ProtectedVariableNaming))]
    [InlineData(nameof(NamingConventionSettings.ProtectedParameterNaming))]
    [InlineData(nameof(NamingConventionSettings.ProtectedConstantNaming))]
    public void Equals_DifferentNamingStyle_ReturnsFalse(string propertyName)
    {
        var a = new NamingConventionSettings();
        var b = new NamingConventionSettings();

        // Change one property on b to SnakeCase (different from default PascalCase/CamelCase)
        var prop = typeof(NamingConventionSettings).GetProperty(propertyName)!;
        prop.SetValue(b, NamingStyle.SnakeCase);

        // Only fails if the default wasn't already SnakeCase
        if ((NamingStyle)prop.GetValue(a)! != NamingStyle.SnakeCase)
            Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentAllowUnderscoreSuffixes_ReturnsFalse()
    {
        var a = new NamingConventionSettings { AllowUnderscoreSuffixes = true };
        var b = new NamingConventionSettings { AllowUnderscoreSuffixes = false };
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentExceptionNames_ReturnsFalse()
    {
        var a = new NamingConventionSettings { ExceptionNames = ["A"] };
        var b = new NamingConventionSettings { ExceptionNames = ["B"] };
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_SameExceptionNamesDifferentOrder_ReturnsTrue()
    {
        var a = new NamingConventionSettings { ExceptionNames = ["A", "B", "C"] };
        var b = new NamingConventionSettings { ExceptionNames = ["C", "A", "B"] };
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_EmptyExceptionNames_ReturnsTrue()
    {
        var a = new NamingConventionSettings { ExceptionNames = [] };
        var b = new NamingConventionSettings { ExceptionNames = [] };
        Assert.True(a.Equals(b));
    }

    // ========================================================================
    // Clone
    // ========================================================================

    [Fact]
    public void Clone_ReturnsEqualButNotSameInstance()
    {
        var original = NamingConventionPresets.UpperCaseConstants();
        original.ExceptionNames = ["NASCAR", "OMC"];

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.True(original.Equals(clone));
    }

    [Fact]
    public void Clone_CopiesAllProperties()
    {
        var original = new NamingConventionSettings
        {
            PresetName = "Custom",
            ModelNaming = NamingStyle.SnakeCase,
            FunctionNaming = NamingStyle.UpperCase,
            BlockNaming = NamingStyle.Any,
            ConnectorNaming = NamingStyle.SnakeCase,
            RecordNaming = NamingStyle.UpperCase,
            TypeNaming = NamingStyle.Any,
            PackageNaming = NamingStyle.SnakeCase,
            ClassNaming = NamingStyle.UpperCase,
            OperatorNaming = NamingStyle.Any,
            PublicVariableNaming = NamingStyle.SnakeCase,
            PublicParameterNaming = NamingStyle.UpperCase,
            PublicConstantNaming = NamingStyle.Any,
            ProtectedVariableNaming = NamingStyle.SnakeCase,
            ProtectedParameterNaming = NamingStyle.UpperCase,
            ProtectedConstantNaming = NamingStyle.Any,
            AllowUnderscoreSuffixes = false,
            ExceptionNames = ["Test"]
        };

        var clone = original.Clone();

        Assert.Equal("Custom", clone.PresetName);
        Assert.Equal(NamingStyle.SnakeCase, clone.ModelNaming);
        Assert.Equal(NamingStyle.UpperCase, clone.FunctionNaming);
        Assert.Equal(NamingStyle.Any, clone.BlockNaming);
        Assert.Equal(NamingStyle.SnakeCase, clone.ConnectorNaming);
        Assert.Equal(NamingStyle.UpperCase, clone.RecordNaming);
        Assert.Equal(NamingStyle.Any, clone.TypeNaming);
        Assert.Equal(NamingStyle.SnakeCase, clone.PackageNaming);
        Assert.Equal(NamingStyle.UpperCase, clone.ClassNaming);
        Assert.Equal(NamingStyle.Any, clone.OperatorNaming);
        Assert.Equal(NamingStyle.SnakeCase, clone.PublicVariableNaming);
        Assert.Equal(NamingStyle.UpperCase, clone.PublicParameterNaming);
        Assert.Equal(NamingStyle.Any, clone.PublicConstantNaming);
        Assert.Equal(NamingStyle.SnakeCase, clone.ProtectedVariableNaming);
        Assert.Equal(NamingStyle.UpperCase, clone.ProtectedParameterNaming);
        Assert.Equal(NamingStyle.Any, clone.ProtectedConstantNaming);
        Assert.False(clone.AllowUnderscoreSuffixes);
        Assert.Single(clone.ExceptionNames);
        Assert.Contains("Test", clone.ExceptionNames);
    }

    [Fact]
    public void Clone_ExceptionNamesAreIndependent()
    {
        var original = new NamingConventionSettings { ExceptionNames = ["A"] };
        var clone = original.Clone();

        clone.ExceptionNames.Add("B");

        Assert.Single(original.ExceptionNames);
        Assert.Equal(2, clone.ExceptionNames.Count);
    }

    [Fact]
    public void Clone_ModifyingCloneDoesNotAffectOriginal()
    {
        var original = NamingConventionPresets.ModelicaStandard();
        var clone = original.Clone();

        clone.ModelNaming = NamingStyle.SnakeCase;
        clone.PresetName = "Modified";

        Assert.Equal(NamingStyle.PascalCase, original.ModelNaming);
        Assert.Equal("Modelica Standard", original.PresetName);
    }

    // ========================================================================
    // ToConfig roundtrip validation
    // ========================================================================

    [Fact]
    public void ToConfig_AllPresetsProduceValidConfig()
    {
        foreach (var (_, factory) in NamingConventionPresets.All)
        {
            var settings = factory();
            var config = settings.ToConfig();

            Assert.Equal(9, config.ClassNamingRules.Count);
            Assert.Equal(settings.ModelNaming, config.ClassNamingRules["model"]);
            Assert.Equal(settings.FunctionNaming, config.ClassNamingRules["function"]);
            Assert.Equal(settings.PublicVariableNaming, config.PublicVariableNaming);
            Assert.Equal(settings.PublicParameterNaming, config.PublicParameterNaming);
            Assert.Equal(settings.PublicConstantNaming, config.PublicConstantNaming);
            Assert.Equal(settings.ProtectedVariableNaming, config.ProtectedVariableNaming);
            Assert.Equal(settings.ProtectedParameterNaming, config.ProtectedParameterNaming);
            Assert.Equal(settings.ProtectedConstantNaming, config.ProtectedConstantNaming);
            Assert.Equal(settings.AllowUnderscoreSuffixes, config.AllowUnderscoreSuffixes);
        }
    }
}
