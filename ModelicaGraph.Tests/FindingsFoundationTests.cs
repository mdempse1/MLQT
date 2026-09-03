using System.Text.Json;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Phase 1 (findings foundation): the rule-id-keyed severity map, the bool facades and their
/// JSON migration, and the structured <see cref="StyleChecking.RunStyleCheckingFindings"/> path.
/// </summary>
public class FindingsFoundationTests
{
    private static ModelDefinition MakeModel(string name, string code) => new(name, code);

    // ---- Severity map / bool facades -----------------------------------------------------------

    [Fact]
    public void SeverityFor_DisabledRule_IsOff()
    {
        var s = new StyleCheckingSettings();
        Assert.Equal(RuleSeverity.Off, s.SeverityFor(RuleIds.ClassIcon));
    }

    [Fact]
    public void EnablingRule_StoresWarningSeverity_DisablingRemovesIt()
    {
        var s = new StyleCheckingSettings();

        s.ClassHasIcon = true;
        Assert.True(s.ClassHasIcon);
        Assert.Equal(RuleSeverity.Warning, s.SeverityFor(RuleIds.ClassIcon));
        Assert.True(s.RuleSeverities.ContainsKey(RuleIds.ClassIcon));

        s.ClassHasIcon = false;
        Assert.False(s.ClassHasIcon);
        Assert.Equal(RuleSeverity.Off, s.SeverityFor(RuleIds.ClassIcon));
        Assert.False(s.RuleSeverities.ContainsKey(RuleIds.ClassIcon));
    }

    [Fact]
    public void SetRuleSeverity_SetsExplicitLevel_OffRemoves()
    {
        var s = new StyleCheckingSettings();

        // An explicit Error is stored verbatim (not the catalog default of Warning).
        s.SetRuleSeverity(RuleIds.UnusedImport, RuleSeverity.Error);
        Assert.Equal(RuleSeverity.Error, s.SeverityFor(RuleIds.UnusedImport));
        Assert.True(s.IsRuleEnabled(RuleIds.UnusedImport));

        // Lowering to Info keeps it enabled at the new level.
        s.SetRuleSeverity(RuleIds.UnusedImport, RuleSeverity.Info);
        Assert.Equal(RuleSeverity.Info, s.SeverityFor(RuleIds.UnusedImport));

        // Off disables and drops it from the map.
        s.SetRuleSeverity(RuleIds.UnusedImport, RuleSeverity.Off);
        Assert.Equal(RuleSeverity.Off, s.SeverityFor(RuleIds.UnusedImport));
        Assert.False(s.RuleSeverities.ContainsKey(RuleIds.UnusedImport));
    }

    // ---- deterministic settings file ----------------------------------------------------------

    [Fact]
    public void RuleSeverities_AreWrittenInAlphabeticalOrder()
    {
        var settings = new StyleCheckingSettings();
        settings.SetRuleSeverity(RuleIds.UnusedImport, RuleSeverity.Warning);
        settings.SetRuleSeverity(RuleIds.ClassIcon, RuleSeverity.Error);
        settings.SetRuleSeverity(RuleIds.DuplicateImport, RuleSeverity.Warning);

        var written = RuleIdsInWrittenOrder(JsonSerializer.Serialize(settings));

        Assert.Equal(written.OrderBy(id => id, StringComparer.Ordinal), written);
    }

    [Fact]
    public void SameRules_ReachedInADifferentOrder_SerializeIdentically()
    {
        // .mlqt/settings.json is committed, so the file has to be a function of the settings and
        // nothing else. A plain Dictionary enumerates in insertion order until a removal frees a
        // slot to reuse — after which the order records which rules the user happened to toggle,
        // and saving unchanged settings still produced a diff with every rule moved.
        var first = new StyleCheckingSettings();
        first.SetRuleSeverity(RuleIds.ClassIcon, RuleSeverity.Error);
        first.SetRuleSeverity(RuleIds.UnusedImport, RuleSeverity.Warning);
        first.SetRuleSeverity(RuleIds.DuplicateImport, RuleSeverity.Warning);

        var second = new StyleCheckingSettings();
        second.SetRuleSeverity(RuleIds.DuplicateImport, RuleSeverity.Warning);
        second.SetRuleSeverity(RuleIds.PackageOrder, RuleSeverity.Error);
        second.SetRuleSeverity(RuleIds.UnusedImport, RuleSeverity.Warning);
        second.SetRuleSeverity(RuleIds.PackageOrder, RuleSeverity.Off);   // the removal
        second.SetRuleSeverity(RuleIds.ClassIcon, RuleSeverity.Error);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void ReadingAndWritingBackUnchangedSettings_ProducesTheSameFile()
    {
        var settings = new StyleCheckingSettings { ClassHasIcon = true, FollowNamingConvention = true };
        settings.SetRuleSeverity(RuleIds.PackageOrder, RuleSeverity.Error);
        settings.SetRuleSeverity(RuleIds.DuplicateImport, RuleSeverity.Warning);
        settings.NamingConvention.AdditionalPatterns["model"] = ["^[A-Z]"];
        settings.NamingConvention.AdditionalPatterns["function"] = ["^get_"];

        var options = new JsonSerializerOptions { WriteIndented = true };
        var written = JsonSerializer.Serialize(settings, options);
        var rewritten = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<StyleCheckingSettings>(written)!, options);

        Assert.Equal(written, rewritten);
    }

    [Fact]
    public void AdditionalPatterns_AreWrittenInAlphabeticalOrder()
    {
        var settings = new StyleCheckingSettings();
        settings.NamingConvention.AdditionalPatterns["record"] = ["^R"];
        settings.NamingConvention.AdditionalPatterns["block"] = ["^B"];
        settings.NamingConvention.AdditionalPatterns["model"] = ["^M"];

        var json = JsonSerializer.Serialize(settings);

        Assert.True(json.IndexOf("\"block\"", StringComparison.Ordinal)
                    < json.IndexOf("\"model\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"model\"", StringComparison.Ordinal)
                    < json.IndexOf("\"record\"", StringComparison.Ordinal));
    }

    /// <summary>The rule ids of the serialized RuleSeverities map, in the order they were written.</summary>
    private static List<string> RuleIdsInWrittenOrder(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("RuleSeverities")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToList();
    }

    // ---- JSON migration / round-trip -----------------------------------------------------------

    [Fact]
    public void LegacyBoolJson_MigratesToSeverityMap()
    {
        // A settings file written before Phase 1 has only the booleans.
        const string json = """{ "ImportStatementsFirst": true, "ClassHasIcon": true }""";

        var s = JsonSerializer.Deserialize<StyleCheckingSettings>(json)!;

        Assert.True(s.ImportStatementsFirst);
        Assert.True(s.ClassHasIcon);
        Assert.False(s.OneOfEachSection);
        Assert.Equal(RuleSeverity.Warning, s.SeverityFor(RuleIds.ImportStatementsFirst));
        Assert.Equal(RuleSeverity.Off, s.SeverityFor(RuleIds.OneOfEachSection));
    }

    [Fact]
    public void Settings_RoundTripThroughJson_PreservesEnablement()
    {
        var original = new StyleCheckingSettings
        {
            ClassHasDescription = true,
            FollowNamingConvention = true,
            ComponentsBeforeClasses = true // formatter flag, must survive too
        };

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(json)!;

        Assert.True(back.ClassHasDescription);
        Assert.True(back.FollowNamingConvention);
        Assert.True(back.ComponentsBeforeClasses);
        Assert.False(back.ClassHasIcon);
    }

    [Fact]
    public void RuleSeveritiesMap_IsSerialized()
    {
        // Phase 4 persists the map as the authoritative store.
        var s = new StyleCheckingSettings { ClassHasIcon = true };
        var json = JsonSerializer.Serialize(s);
        Assert.Contains("RuleSeverities", json);
        Assert.Contains(RuleIds.ClassIcon, json);
    }

    [Fact]
    public void ExplicitErrorSeverity_RoundTrips()
    {
        var s = new StyleCheckingSettings();
        s.RuleSeverities[RuleIds.ClassIcon] = RuleSeverity.Error;

        var back = JsonSerializer.Deserialize<StyleCheckingSettings>(JsonSerializer.Serialize(s))!;

        Assert.Equal(RuleSeverity.Error, back.SeverityFor(RuleIds.ClassIcon));
        Assert.True(back.ClassHasIcon); // the bool facade reflects "enabled"
    }

    [Theory]
    [InlineData("""{ "ClassHasIcon": true, "RuleSeverities": { "MLQT.Doc.ClassIcon": "Error" } }""")]
    [InlineData("""{ "RuleSeverities": { "MLQT.Doc.ClassIcon": "Error" }, "ClassHasIcon": true }""")]
    public void ExplicitMapSeverity_NotClobberedByBoolFacade_EitherOrder(string json)
    {
        // The map must win regardless of JSON property order.
        var s = JsonSerializer.Deserialize<StyleCheckingSettings>(json)!;
        Assert.Equal(RuleSeverity.Error, s.SeverityFor(RuleIds.ClassIcon));
    }

    // ---- RunStyleCheckingFindings --------------------------------------------------------------

    [Fact]
    public void RunStyleCheckingFindings_CarriesRuleIdSeverityAndElement()
    {
        var model = MakeModel("TestModel", "model TestModel\n  parameter Real x = 1.0;\nend TestModel;");
        var settings = new StyleCheckingSettings { ParameterHasDescription = true };

        var finding = Assert.Single(StyleChecking.RunStyleCheckingFindings(model, settings, "MyLib.TestModel"));

        Assert.Equal(RuleIds.ParameterDescription, finding.RuleId);
        Assert.Equal(RuleSeverity.Warning, finding.Severity);
        Assert.Equal("x", finding.ElementPath);
        Assert.Equal("MyLib.TestModel", finding.ModelId);
    }

    [Fact]
    public void RunStyleCheckingFindings_FingerprintStableAcrossReformatting()
    {
        var settings = new StyleCheckingSettings { ParameterHasDescription = true };

        var compact = MakeModel("M", "model M\n  parameter Real x = 1.0;\nend M;");
        var spaced = MakeModel("M", "model M\n\n\n  parameter Real x = 1.0;\n\nend M;");

        var f1 = Assert.Single(StyleChecking.RunStyleCheckingFindings(compact, settings, "L.M"));
        var f2 = Assert.Single(StyleChecking.RunStyleCheckingFindings(spaced, settings, "L.M"));

        Assert.NotEqual(f1.LineNumber, f2.LineNumber); // line shifted by the reformat
        Assert.Equal(f1.Fingerprint, f2.Fingerprint);  // identity survived
    }

    [Fact]
    public void RunStyleChecking_ProjectionMatchesFindings()
    {
        var code = "model TestModel\n  parameter Real x = 1.0;\n  constant Real g = 9.81;\nend TestModel;";
        var settings = new StyleCheckingSettings { ParameterHasDescription = true, ConstantHasDescription = true };

        var findings = StyleChecking.RunStyleCheckingFindings(MakeModel("TestModel", code), settings, "MyLib.TestModel");
        var messages = StyleChecking.RunStyleChecking(MakeModel("TestModel", code), settings, "MyLib.TestModel");

        Assert.Equal(findings.Count, messages.Count);
        for (int i = 0; i < findings.Count; i++)
        {
            Assert.Equal(findings[i].Message, messages[i].Summary);
            Assert.Equal(findings[i].ModelId, messages[i].ModelName);
            Assert.Equal(findings[i].LineNumber, messages[i].LineNumber);
            Assert.Equal("Style warning", messages[i].Severity);
            Assert.Equal("StyleChecking", messages[i].Source);
        }
    }
}
