using System.Text.Json;
using ModelicaGraph.Analysis;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.TestSupport;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The files ModelicaGraph's types are written to keep their exact form when the contracts are
/// compiled in rather than read by reflection - which is what lets an obfuscated build write them
/// (B502). Byte-for-byte, because <c>.mlqt/settings.json</c> is committed: a contract that settled a
/// name or an order differently would rewrite every team's file on its next save.
/// </summary>
public class JsonContractTests
{
    private static readonly JsonSerializerOptions SettingsFileAsBefore = new() { WriteIndented = true, NewLine = "\n" };

    [Fact]
    public void TheSettingsFileContractIsReflections()
    {
        JsonContractAssert.MatchesReflection(typeof(StyleCheckingSettings), ModelicaGraphJson.Contracts, SettingsFileAsBefore);
    }

    [Fact]
    public void TheSettingsFileIsWrittenAsItWasByReflection()
    {
        var settings = NonDefaultSettings();

        Assert.Equal(JsonSerializer.Serialize(settings, SettingsFileAsBefore), settings.ToJson());
    }

    [Fact]
    public void TheSettingsFileReadsBackWhatItWrote()
    {
        var settings = NonDefaultSettings();

        var read = StyleCheckingSettings.FromJson(settings.ToJson());

        Assert.NotNull(read);
        Assert.Equal(settings.ToJson(), read.ToJson());
        Assert.Equal(RuleSeverity.Error, read.RuleSeverities[RuleIds.ClassDescription]);
        Assert.Equal(["Old"], read.ExcludedLibraries);
        Assert.Equal(RuleSeverity.Off, read.RuleSeverities[RuleCatalog.Configurable.First(d => d.EnabledByDefault).Id]);
    }

    [Fact]
    public void ASettingsFileWrittenByReflectionIsRead()
    {
        // Every .mlqt/settings.json committed before the contracts were compiled in.
        var settings = NonDefaultSettings();

        var read = StyleCheckingSettings.FromJson(JsonSerializer.Serialize(settings, SettingsFileAsBefore));

        Assert.Equal(settings.ToJson(), read!.ToJson());
    }

    [Fact]
    public void TheMetricsHistoryContractIsReflections()
    {
        JsonContractAssert.MatchesReflection(typeof(List<MetricsSnapshot>), ModelicaGraphJson.Contracts, new() { WriteIndented = true });
    }

    private static StyleCheckingSettings NonDefaultSettings()
    {
        var settings = new StyleCheckingSettings
        {
            CommitRequiresIssueNumber = true,
            ApplyFormattingRules = true,
            ExcludedLibraries = ["Old"],
            FormattingExcludedModels = ["Lib.Generated"],
            SpellCheckLanguages = ["en_GB"],
        };
        settings.RuleSeverities[RuleIds.ClassDescription] = RuleSeverity.Error;
        // Off is written down only for a rule that is on until switched off; for any other rule it is
        // the absence of a key, and a hand-written Off would be dropped on reading.
        settings.SetRuleEnabled(RuleCatalog.Configurable.First(d => d.EnabledByDefault).Id, false);
        settings.NamingConvention.ModelNaming = NamingStyle.CamelCase;
        settings.RecordDefaults();
        return settings;
    }
}
