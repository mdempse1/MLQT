using System.Text.Json;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// What a SARIF consumer is given about each rule, and what happens to accepted debt.
///
/// <para>GitHub reads a documented subset of SARIF. A rule descriptor there needs
/// <c>fullDescription</c> and <c>help</c> as well as <c>shortDescription</c> — <c>help.markdown</c>
/// is what renders in the alert body — and it supports neither <c>baselineState</c> nor
/// <c>suppressions</c>, so anything written arrives as an open alert whatever it is tagged with.
/// Both facts are about the consumer rather than the format, so they are pinned here.</para>
/// </summary>
public class SarifMetadataTests
{
    private const string Model = """
        model Undescribed
          parameter Real gain = 1;
        end Undescribed;
        """;

    private const string Settings =
        """
        {
          "RuleSeverities": {
            "MLQT.Doc.ClassDescription": "Warning",
            "MLQT.Doc.ParameterDescription": "Error"
          }
        }
        """;

    private sealed class TempLibrary : IDisposable
    {
        private readonly TempWorkspace _workspace = new TempWorkspace("mlqt-sarif-meta")
            .Write("Undescribed.mo", Model)
            .WithSettings(Settings);

        public string Path => _workspace.Root;
        public string At(string name) => _workspace.PathTo(name);

        public void Dispose() => _workspace.Dispose();
    }

    private static JsonElement Sarif(string text)
    {
        var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    // ---- B9: rule metadata ---------------------------------------------------------------------

    [Fact]
    public void EachRuleCarriesEverythingGitHubDocumentsAsRequired()
    {
        using var lib = new TempLibrary();

        var (_, stdout, _) = Cli.Run("check", lib.Path, "--format", "sarif", "--fail-on", "off");

        var rules = Sarif(stdout).GetProperty("runs")[0].GetProperty("tool")
            .GetProperty("driver").GetProperty("rules").EnumerateArray().ToList();

        Assert.NotEmpty(rules);
        foreach (var rule in rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("shortDescription").GetProperty("text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("fullDescription").GetProperty("text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("help").GetProperty("text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("help").GetProperty("markdown").GetString()));
        }
    }

    [Fact]
    public void TheAlertBodySaysWhatTheRuleIsAndWhereToConfigureIt()
    {
        // help.markdown is the alert body. An alert that names a rule and says nothing else leaves
        // the reader to go and look the rule up, which is the state B9 existed to fix.
        using var lib = new TempLibrary();

        var (_, stdout, _) = Cli.Run("check", lib.Path, "--format", "sarif", "--fail-on", "off");

        var rule = Sarif(stdout).GetProperty("runs")[0].GetProperty("tool")
            .GetProperty("driver").GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == "MLQT.Doc.ClassDescription");

        var markdown = rule.GetProperty("help").GetProperty("markdown").GetString()!;
        Assert.Contains("Class has description", markdown);                    // the title
        Assert.Contains("A class must have a description string.", markdown);  // what is wrong
        Assert.Contains("MLQT.Doc.ClassDescription", markdown);                // which rule
        Assert.Contains("settings-reference", markdown);                       // where to configure it

        Assert.Equal("Documentation", rule.GetProperty("properties").GetProperty("category").GetString());
        Assert.Equal(["Documentation"],
            rule.GetProperty("properties").GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList());
    }

    // ---- B42: a diagnostic is not a rule, and its alert must not pretend otherwise --------------

    private const string Unparseable = """
        model Broken
          parameter Real = ;
        """;

    private sealed class BrokenLibrary : IDisposable
    {
        private readonly TempWorkspace _workspace = new TempWorkspace("mlqt-sarif-diag")
            .Write("Broken.mo", Unparseable)
            .WithSettings(Settings);

        public string Path => _workspace.Root;

        public void Dispose() => _workspace.Dispose();
    }

    private static JsonElement RuleFor(string sarif, string id) =>
        Sarif(sarif).GetProperty("runs")[0].GetProperty("tool").GetProperty("driver")
            .GetProperty("rules").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == id);

    [Fact]
    public void ADiagnosticsAlertDoesNotOfferToSwitchItOff()
    {
        using var lib = new BrokenLibrary();

        var (_, stdout, _) = Cli.Run("check", lib.Path, "--format", "sarif", "--fail-on", "off");

        var ids = Sarif(stdout).GetProperty("runs")[0].GetProperty("tool").GetProperty("driver")
            .GetProperty("rules").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()!)
            .Where(ModelicaParser.StyleRules.RuleIds.IsDiagnostic)
            .ToList();

        Assert.NotEmpty(ids);   // otherwise the assertions below are vacuous

        foreach (var id in ids)
        {
            var markdown = RuleFor(stdout, id).GetProperty("help").GetProperty("markdown").GetString()!;

            // It has no setting to change, so the rule wording described a control that is not there.
            Assert.DoesNotContain("Configure this rule's severity", markdown);
            Assert.Contains("cannot be switched", markdown);
        }
    }

    [Fact]
    public void ADiagnosticsAlertLinksToThePageThatNamesIt()
    {
        // settings-reference.md deliberately does not carry the diagnostics, so pointing an alert
        // there was B16's defect again - a "learn more" landing somewhere the rule is not mentioned.
        using var lib = new BrokenLibrary();

        var (_, stdout, _) = Cli.Run("check", lib.Path, "--format", "sarif", "--fail-on", "off");

        var diagnostic = Sarif(stdout).GetProperty("runs")[0].GetProperty("tool").GetProperty("driver")
            .GetProperty("rules").EnumerateArray()
            .First(r => ModelicaParser.StyleRules.RuleIds.IsDiagnostic(r.GetProperty("id").GetString()!));

        Assert.Contains("cli.md#diagnostics", diagnostic.GetProperty("helpUri").GetString());
    }

    [Fact]
    public void AnOrdinaryRuleStillLinksToTheSettingsReference()
    {
        using var lib = new TempLibrary();

        var (_, stdout, _) = Cli.Run("check", lib.Path, "--format", "sarif", "--fail-on", "off");

        Assert.Contains(
            "settings-reference.md",
            RuleFor(stdout, "MLQT.Doc.ClassDescription").GetProperty("helpUri").GetString());
    }

    // ---- B10: accepted debt --------------------------------------------------------------------

    [Fact]
    public void AcceptedDebtIsLeftOutOfSarifBySeparateDefault()
    {
        // Every finding here is in the baseline, so the SARIF is empty — which is the point: GitHub
        // would otherwise show five open alerts for debt the team has already agreed to.
        using var lib = new TempLibrary();
        var baseline = lib.At("baseline.json");
        Cli.Run("baseline", "create", lib.Path, "--baseline", baseline);

        var (_, stdout, stderr) = Cli.Run(
            "check", lib.Path, "--baseline", baseline, "--format", "sarif", "--fail-on", "off");

        Assert.Empty(Sarif(stdout).GetProperty("runs")[0].GetProperty("results").EnumerateArray());
        Assert.Contains("accepted-debt finding(s) left out of the SARIF", stderr);
    }

    [Fact]
    public void WithTheFlag_AcceptedDebtIsKeptAndStillTagged()
    {
        using var lib = new TempLibrary();
        var baseline = lib.At("baseline.json");
        Cli.Run("baseline", "create", lib.Path, "--baseline", baseline);

        var (_, stdout, stderr) = Cli.Run(
            "check", lib.Path, "--baseline", baseline, "--format", "sarif",
            "--sarif-include-accepted", "--fail-on", "off");

        var results = Sarif(stdout).GetProperty("runs")[0].GetProperty("results").EnumerateArray().ToList();
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("unchanged", r.GetProperty("baselineState").GetString()));
        Assert.DoesNotContain("left out of the SARIF", stderr);
    }

    [Fact]
    public void WithoutABaseline_NothingIsOmittedAndNothingIsSaid()
    {
        using var lib = new TempLibrary();

        var (_, stdout, stderr) = Cli.Run("check", lib.Path, "--format", "sarif", "--fail-on", "off");

        Assert.NotEmpty(Sarif(stdout).GetProperty("runs")[0].GetProperty("results").EnumerateArray());
        Assert.DoesNotContain("left out of the SARIF", stderr);
    }

    [Fact]
    public void ARuleOnlyAcceptedDebtFiredIsNotDescribedEither()
    {
        // The rules array is built from what is being reported. Describing a rule with no results
        // would be harmless but misleading in a viewer that lists rules.
        using var lib = new TempLibrary();
        var baseline = lib.At("baseline.json");
        Cli.Run("baseline", "create", lib.Path, "--baseline", baseline);

        var (_, stdout, _) = Cli.Run(
            "check", lib.Path, "--baseline", baseline, "--format", "sarif", "--fail-on", "off");

        Assert.Empty(Sarif(stdout).GetProperty("runs")[0].GetProperty("tool")
            .GetProperty("driver").GetProperty("rules").EnumerateArray());
    }

    [Fact]
    public void TheFlagIsCarriedOnTheParsedOptions()
    {
        Assert.True(CheckOptions.TryParse(["lib", "--sarif-include-accepted"], out var options, out _));
        Assert.True(options!.SarifIncludeAccepted);

        Assert.True(CheckOptions.TryParse(["lib"], out var plain, out _));
        Assert.False(plain!.SarifIncludeAccepted);
    }
}
