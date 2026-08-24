using System.Linq;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// The per-source half of <c>MLQT.Unused.Import</c>: what each class imports, and what it uses. The
/// decision itself is cross-model and lives in <c>UnusedImportAnalyzer</c> — an import is visible to
/// every class nested below it, and those normally live in other files.
/// </summary>
public class ImportScopeExtractorTests
{
    private static ImportScope Outermost(string code)
    {
        var extractor = new ImportScopeExtractor();
        extractor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return Assert.IsType<ImportScope>(extractor.OutermostScope);
    }

    private static bool IsUnused(string code, string alias)
    {
        var scope = Outermost(code);
        return scope.Imports.Any(i => i.Alias == alias) && !scope.UsedIdentifiers.Contains(alias);
    }

    [Fact]
    public void PlainImport_BindsItsLastSegment()
    {
        var scope = Outermost("model M\n  import Modelica.Utilities.Streams;\n  Real x;\nend M;");
        var binding = Assert.Single(scope.Imports);
        Assert.Equal("Streams", binding.Alias);
        Assert.Equal(2, binding.Line);
        Assert.Equal("M", scope.ModelId);
    }

    [Fact]
    public void RenamedImport_BindsTheAlias()
    {
        var binding = Assert.Single(Outermost("model M\n  import SI = Modelica.Units.SI;\n  Real x;\nend M;").Imports);
        Assert.Equal("SI", binding.Alias);
    }

    [Fact]
    public void WildcardImport_BindsNothingCheckable()
    {
        Assert.Empty(Outermost("model M\n  import Modelica.Units.SI.*;\n  Real x;\nend M;").Imports);
    }

    [Fact]
    public void ImportsOwnPath_DoesNotCountAsUse()
    {
        // Without this every import would mark itself used and the rule could never fire.
        Assert.True(IsUnused("model M\n  import Modelica.Utilities.Streams;\n  Real x;\nend M;", "Streams"));
        Assert.True(IsUnused("model M\n  import SI = Modelica.Units.SI;\n  Real x;\nend M;", "SI"));
    }

    [Fact]
    public void UseAsType_Counts()
    {
        Assert.False(IsUnused("model M\n  import Modelica.Units.SI.Length;\n  Length x;\nend M;", "Length"));
    }

    [Fact]
    public void UseInEquation_Counts()
    {
        Assert.False(IsUnused(
            "model M\n  import C = Modelica.Constants;\n  Real x;\nequation\n  x = C.pi;\nend M;", "C"));
    }

    [Fact]
    public void UseInANestedClassOfTheSameSource_CountsForTheEnclosingClass()
    {
        // Modelica looks a simple name up in each enclosing scope in turn, so the nested class's
        // reference resolves against the outer import — the import is used. A replaceable class is
        // the case that stays in its parent's source; a standalone nested class gets its own node and
        // is folded in by the analyzer instead (see UnusedImportAnalyzerTests).
        Assert.False(IsUnused(
            "model M\n  import Modelica.Blocks.Continuous;\n" +
            "  replaceable model Inner\n    Continuous.Integrator i;\n  end Inner;\nend M;",
            "Continuous"));
    }
}

/// <summary>
/// The text scan the analyzer uses for descendants that live in other files. It errs towards "used"
/// on purpose; the one thing it must not do is let an import clause satisfy itself.
/// </summary>
public class IdentifierUsageScannerTests
{
    [Theory]
    [InlineData("  SI.Length x;", "SI", true)]
    [InlineData("  Real x = 1;", "SI", false)]
    [InlineData("  SIunits.Length x;", "SI", false)]      // whole word only
    [InlineData("  MySI.Length x;", "SI", false)]
    [InlineData("  import Modelica.Units.SI;", "SI", false)]   // an import cannot satisfy itself
    [InlineData("  import Cv = Modelica.Units.Conversions;", "Cv", false)]
    [InlineData("  import Modelica.Units.SI; Real x = SI.Length;", "SI", true)]
    [InlineData("  x = 1 \"see SI\";", "SI", true)]        // a comment reads as a use: under-report
    public void Mentions_MatchesWholeWordsOutsideImports(string source, string identifier, bool expected)
        => Assert.Equal(expected, IdentifierUsageScanner.Mentions(source, identifier));

    [Fact]
    public void Mentions_HandlesEmptyInput()
    {
        Assert.False(IdentifierUsageScanner.Mentions(null, "SI"));
        Assert.False(IdentifierUsageScanner.Mentions("", "SI"));
        Assert.False(IdentifierUsageScanner.Mentions("SI", ""));
    }

    [Fact]
    public void Mentions_FindsAUseAfterAnImportOfTheSameNameElsewhere()
    {
        const string source = "model M\n  import Modelica.Units.SI;\n  SI.Length x;\nend M;";
        Assert.True(IdentifierUsageScanner.Mentions(source, "SI"));
    }
}
