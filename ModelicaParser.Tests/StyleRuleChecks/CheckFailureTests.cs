using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// The sentence MLQT uses to say its own results are incomplete.
///
/// <para>It was written by hand in four places, two of them word for word, so the same failure read
/// differently depending on which pass hit it. It is worth holding still: it is what tells somebody a
/// total is short, and it is where a support conversation starts.</para>
/// </summary>
public class CheckFailureTests
{
    private static readonly Exception Boom = new InvalidOperationException("the thing broke");

    [Fact]
    public void ItNamesWhatFailedAndWhy()
    {
        var finding = CheckFailure.For("Lib.Foo", Boom);

        Assert.Equal(RuleIds.CheckFailed, finding.RuleId);
        Assert.Equal("Lib.Foo", finding.ModelId);
        Assert.Equal(RuleSeverity.Error, finding.Severity);
        Assert.Equal(
            "Checking this class failed (InvalidOperationException: the thing broke). " +
            "Its findings are missing from these results.",
            finding.Message);
    }

    [Fact]
    public void AnalysisAlsoLosesTheClassesEdges_AndSaysSo()
    {
        // Dependency analysis failing costs more than the class's own findings: every graph rule now
        // reasons about a graph with a hole in it.
        var finding = CheckFailure.For(
            "Lib.Foo", Boom, CheckFailure.Analysing, alsoMissing: "its dependencies");

        Assert.Equal(
            "Analysing this class failed (InvalidOperationException: the thing broke). " +
            "Its findings and its dependencies are missing from these results.",
            finding.Message);
    }

    [Fact]
    public void TheMessageProjectionCarriesTheRuleAndFingerprint()
    {
        // The desktop issue list reads the flat shape, and needs the rule id to clear the row on a
        // re-check - a class that failed once may well succeed next time.
        var message = CheckFailure.Message("Lib.Foo", Boom);

        Assert.Equal(RuleIds.CheckFailed, message.RuleId);
        Assert.Equal("Lib.Foo", message.ModelName);
        Assert.Equal("StyleChecking", message.Source);
        Assert.False(string.IsNullOrEmpty(message.Fingerprint));
        // The text lands in Summary, which is the column the issue list shows; style findings carry
        // no Details, and the list opens a detail dialog only when there are some.
        Assert.Equal(CheckFailure.For("Lib.Foo", Boom).Message, message.Summary);
        Assert.Equal("", message.Details);
    }

    /// <summary>
    /// A whole-graph pass can fail more than once on the class it attributes failures to, so those
    /// reports have to be distinguishable. Without the discriminator they share a fingerprint and the
    /// second one reads as a duplicate of the first.
    /// </summary>
    [Fact]
    public void SeveralFailuresOnOneClassStayDistinct()
    {
        var first = CheckFailure.For("Lib", Boom, "The A analysis", discriminator: "A");
        var second = CheckFailure.For("Lib", Boom, "The B analysis", discriminator: "B");

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void TheFingerprintIgnoresTheExceptionText()
    {
        // Two runs of the same broken check must look like one problem, not a new one each time -
        // exception messages carry paths, ids and counts that move between runs.
        var first = CheckFailure.For("Lib.Foo", new IOException("locked by process 1234"));
        var second = CheckFailure.For("Lib.Foo", new IOException("locked by process 5678"));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }
}
