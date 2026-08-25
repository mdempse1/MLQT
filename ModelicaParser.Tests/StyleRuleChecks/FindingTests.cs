using System.Reflection;
using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class FindingFingerprintTests
{
    [Fact]
    public void Fingerprint_IsStable_GoldenValue()
    {
        // Golden value locks the algorithm. Critically, it also guards against anyone swapping in
        // string.GetHashCode(), which is randomised per process and would break stored baselines.
        var fp = FindingFingerprint.Compute("MLQT.Doc.ClassDescription", "MyLib.Sub.Foo", null, null);
        Assert.Equal("56b9c60f2395ff50c262f3b46e6fb77c", fp);
    }

    [Fact]
    public void Fingerprint_Is32LowercaseHexChars()
    {
        var fp = FindingFingerprint.Compute("R", "M", "e", "d");
        Assert.Equal(32, fp.Length);
        Assert.Matches("^[0-9a-f]{32}$", fp);
    }

    [Fact]
    public void Fingerprint_Deterministic()
    {
        Assert.Equal(
            FindingFingerprint.Compute("R", "M", "e", "d"),
            FindingFingerprint.Compute("R", "M", "e", "d"));
    }

    [Fact]
    public void Fingerprint_ExcludesLineNumber()
    {
        // The whole point: identity survives line shifts (and reformatting).
        var f1 = new Finding { RuleId = "R", ModelId = "M", Message = "m", LineNumber = 1 };
        var f2 = new Finding { RuleId = "R", ModelId = "M", Message = "m", LineNumber = 999 };
        Assert.Equal(f1.Fingerprint, f2.Fingerprint);
    }

    [Fact]
    public void Fingerprint_ExcludesMessage()
    {
        var f1 = new Finding { RuleId = "R", ModelId = "M", Message = "one", LineNumber = 1 };
        var f2 = new Finding { RuleId = "R", ModelId = "M", Message = "two", LineNumber = 1 };
        Assert.Equal(f1.Fingerprint, f2.Fingerprint);
    }

    [Theory]
    [InlineData("R", "M", "e", "d", "R2", "M", "e", "d")]  // rule differs
    [InlineData("R", "M", "e", "d", "R", "M2", "e", "d")]  // model differs
    [InlineData("R", "M", "e", "d", "R", "M", "e2", "d")]  // element differs
    [InlineData("R", "M", "e", "d", "R", "M", "e", "d2")]  // discriminator differs
    public void Fingerprint_DistinctForDifferentIdentity(
        string r1, string m1, string e1, string d1, string r2, string m2, string e2, string d2)
    {
        Assert.NotEqual(
            FindingFingerprint.Compute(r1, m1, e1, d1),
            FindingFingerprint.Compute(r2, m2, e2, d2));
    }

    [Fact]
    public void Fingerprint_NullAndEmpty_AreEquivalent()
    {
        Assert.Equal(
            FindingFingerprint.Compute("R", "M", null, null),
            FindingFingerprint.Compute("R", "M", "", ""));
    }

    [Fact]
    public void Fingerprint_ElementVsDiscriminator_NotConfused()
    {
        // NUL separator: "e" in the element slot must not collide with "e" in the discriminator slot.
        Assert.NotEqual(
            FindingFingerprint.Compute("R", "M", "e", null),
            FindingFingerprint.Compute("R", "M", null, "e"));
    }
}

public class FindingProjectionTests
{
    [Fact]
    public void ToLogMessage_PreservesLegacyShape()
    {
        var f = new Finding
        {
            RuleId = RuleIds.NamingConvention,
            ModelId = "MyLib.Foo",
            ElementPath = "r",
            Message = "some message",
            LineNumber = 42,
            Severity = RuleSeverity.Error
        };

        var lm = f.ToLogMessage();

        Assert.Equal("MyLib.Foo", lm.ModelName);
        Assert.Equal("some message", lm.Summary);
        Assert.Equal("Style warning", lm.Severity); // legacy constant, independent of Finding.Severity
        Assert.Equal("StyleChecking", lm.Source);
        Assert.Equal(42, lm.LineNumber);
        Assert.Equal("", lm.Details);
    }

    [Fact]
    public void ToLogMessage_CarriesTheDiscriminator()
    {
        // The desktop issues list acts on the flagged word — underlining it, accepting it into the
        // repository's list — and used to recover it by reading the message text back. That cannot be
        // done reliably when the word is quoted and the word itself contains a quote ("Stodola's").
        var f = new Finding
        {
            RuleId = RuleIds.SpellingDescription,
            ModelId = "MyLib.Foo",
            Discriminator = "Stodola's",
            Message = "Misspelled word 'Stodola's' in description",
        };

        Assert.Equal("Stodola's", f.ToLogMessage().Discriminator);
    }
}

public class RuleCatalogTests
{
    public static IEnumerable<object[]> AllRuleIdConstants()
    {
        foreach (var field in typeof(RuleIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.IsLiteral && field.FieldType == typeof(string))
                yield return new object[] { (string)field.GetRawConstantValue()! };
    }

    [Theory]
    [MemberData(nameof(AllRuleIdConstants))]
    public void EveryRuleId_IsRegistered(string ruleId)
    {
        Assert.True(RuleCatalog.IsKnown(ruleId), $"{ruleId} is not registered in RuleCatalog");
    }

    [Fact]
    public void BuiltIn_CountMatchesRuleIdConstants()
    {
        var constantCount = typeof(RuleIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Count(f => f.IsLiteral && f.FieldType == typeof(string));
        Assert.Equal(constantCount, RuleCatalog.BuiltIn.Count);
    }

    [Fact]
    public void DefaultSeverityFor_KnownRule_IsWarning()
        => Assert.Equal(RuleSeverity.Warning, RuleCatalog.DefaultSeverityFor(RuleIds.NamingConvention));

    [Fact]
    public void DefaultSeverityFor_UnknownRule_FallsBackToWarning()
        => Assert.Equal(RuleSeverity.Warning, RuleCatalog.DefaultSeverityFor("Not.A.Real.Rule"));

    [Fact]
    public void EveryDefinition_HasTitleCategoryAndDescription()
    {
        foreach (var def in RuleCatalog.BuiltIn.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Title));
            Assert.False(string.IsNullOrWhiteSpace(def.Category));
            Assert.False(string.IsNullOrWhiteSpace(def.Description));
        }
    }

}
