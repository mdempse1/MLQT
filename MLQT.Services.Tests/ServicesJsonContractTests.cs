using System.Text.Json;
using System.Text.Json.Serialization;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.TestSupport;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// The baseline and the settings store's repository list keep their exact form with their contracts
/// compiled in, which is what lets an obfuscated build read and write them (B502).
/// </summary>
public class ServicesJsonContractTests
{
    [Fact]
    public void TheBaselineContractIsReflections()
    {
        JsonContractAssert.MatchesReflection(typeof(Baseline.BaselineFile), MlqtServicesJsonContext.Default, new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }

    [Fact]
    public void TheRepositoryListContractIsReflections()
    {
        JsonContractAssert.MatchesReflection(typeof(RepositorySettingsCollection), MlqtServicesJsonContext.Default);
    }

    [Fact]
    public void TheSettingsStoreTakesTheRepositoryListFromTheCompiledContract()
    {
        // The store falls back to reflection for a host's own types, so this is the check that the
        // stack's own type does not quietly go that way too.
        var info = MlqtServicesJson.SettingValues.GetTypeInfo(typeof(RepositorySettingsCollection));

        Assert.Same(MlqtServicesJsonContext.Default, info.OriginatingResolver);
    }

    [Fact]
    public void TheSettingsStoreStillWritesAHostsOwnTypes()
    {
        var json = JsonSerializer.Serialize(new HostSetting("dark", 3), MlqtServicesJson.SettingValues);

        Assert.Equal("""{"Theme":"dark","Size":3}""", json);
    }

    [Fact]
    public void ABaselineWrittenAndReadKeepsItsRules()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mlqt-baseline-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "baseline.json");
        try
        {
            var rules = new Dictionary<string, RuleSeverity> { ["MLQT.Style.ClassDescription"] = RuleSeverity.Error };
            new Baseline([new BaselineEntry("fp", "MLQT.Style.ClassDescription", "Lib.A", null, "no description")],
                createdUtc: null, revision: "abc", branch: "main", rules: rules).Save(path);

            var text = File.ReadAllText(path);
            Assert.Contains("\"ruleId\": \"MLQT.Style.ClassDescription\"", text);
            Assert.Contains("\"MLQT.Style.ClassDescription\": \"Error\"", text);

            var read = Baseline.Load(path);
            Assert.Equal(RuleSeverity.Error, read.Rules!["MLQT.Style.ClassDescription"]);
            Assert.Equal("abc", read.Revision);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    public sealed record HostSetting(string Theme, int Size);
}
