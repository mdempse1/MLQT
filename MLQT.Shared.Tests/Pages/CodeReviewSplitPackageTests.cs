using MLQT.Shared.Pages;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B242 — which findings offer <b>Split into files</b>.
///
/// <para>The work is <c>PackageSplitter</c>'s and is tested there against real files. What is here
/// is the page's own decision: which rows get the button. Offering it on a finding it cannot fix is
/// a button that reports failure; not offering it on one it can leaves the user with <b>Format All
/// Files</b>, which restructures the whole repository to correct one package.</para>
/// </summary>
public class CodeReviewSplitPackageTests
{
    private static LogMessage Finding(string ruleId, string source = LogMessage.StyleCheckingSource) =>
        new("Lib.Arrived", "Style warning", 1, "package Arrived is stored as a single file")
        {
            RuleId = ruleId,
            Source = source,
        };

    [Fact]
    public void ASingleFilePackageFindingOffersTheFix()
    {
        Assert.True(CodeReview.CanSplitPackage(Finding(RuleIds.SingleFilePackage)));
    }

    [Fact]
    public void EveryOtherRuleDoesNot()
    {
        // The button restructures files on disk. It belongs to one rule, and a finding that merely
        // mentions a package is not that rule.
        Assert.False(CodeReview.CanSplitPackage(Finding(RuleIds.PackageOrder)));
        Assert.False(CodeReview.CanSplitPackage(Finding(RuleIds.ClassDescription)));
        Assert.False(CodeReview.CanSplitPackage(Finding(RuleIds.UnusedClass)));
    }

    [Fact]
    public void AFindingFromSomewhereElseDoesNot()
    {
        // Parse diagnostics and external-tool results reach this list too. Only a style finding
        // carries a rule id this page can act on.
        Assert.False(CodeReview.CanSplitPackage(
            Finding(RuleIds.SingleFilePackage, source: "Dymola")));
    }

    [Fact]
    public void NothingIsNotAFinding()
    {
        Assert.False(CodeReview.CanSplitPackage(null));
    }

    [Fact]
    public void TheTwoRowActionsAreIndependent()
    {
        // Suppress and Split are offered on different sets, and a finding can have both: waiving the
        // rule and fixing it are different decisions, and the row should not force a choice.
        var finding = Finding(RuleIds.SingleFilePackage);

        Assert.True(CodeReview.CanSplitPackage(finding));
        Assert.True(CodeReview.CanSuppressRule(finding));
    }
}
