using System.Collections.Generic;
using System.Linq;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class DuplicateDeclarationsTests
{
    private static List<Finding> Check(string code, bool components = true, bool imports = true)
    {
        var visitor = new DuplicateDeclarations(components, imports);
        visitor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return visitor.Findings.ToList();
    }

    [Fact]
    public void DuplicateComponent_OnSeparateLines_IsFlaggedOnce()
    {
        var f = Check("model Bar\n  Real a;\n  Real a;\nend Bar;");
        var dup = Assert.Single(f, x => x.RuleId == RuleIds.DuplicateDeclaration);
        Assert.Equal("Bar", dup.ModelId);
        Assert.Equal("a", dup.ElementPath);
    }

    [Fact]
    public void DuplicateComponent_InSameClause_IsFlagged()
    {
        var f = Check("model Bar\n  Real a, a;\nend Bar;");
        Assert.Single(f, x => x.RuleId == RuleIds.DuplicateDeclaration && x.ElementPath == "a");
    }

    [Fact]
    public void ThreeOccurrences_ReportedOnce()
    {
        var f = Check("model Bar\n  Real a;\n  Real a;\n  Real a;\nend Bar;");
        Assert.Single(f, x => x.RuleId == RuleIds.DuplicateDeclaration && x.ElementPath == "a");
    }

    [Fact]
    public void DistinctNames_NotFlagged()
    {
        Assert.Empty(Check("model Bar\n  Real a;\n  Real b;\n  Real c;\nend Bar;"));
    }

    [Fact]
    public void SameNameInDifferentModels_NotFlagged()
    {
        // Two standalone nested models each declaring 'a' — different scopes, not a duplicate.
        var f = Check("package P\n  model A\n    Real a;\n  end A;\n  model B\n    Real a;\n  end B;\nend P;");
        Assert.DoesNotContain(f, x => x.RuleId == RuleIds.DuplicateDeclaration);
    }

    [Fact]
    public void DuplicatePlainImport_IsFlagged()
    {
        var f = Check("model Bar\n  import Modelica.SIunits.Length;\n  import Modelica.SIunits.Length;\nend Bar;");
        Assert.Single(f, x => x.RuleId == RuleIds.DuplicateImport && x.ElementPath == "Length");
    }

    [Fact]
    public void DuplicateRenamedImportAlias_IsFlagged()
    {
        var f = Check("model Bar\n  import SI = Modelica.SIunits;\n  import SI = Modelica.Units.SI;\nend Bar;");
        Assert.Single(f, x => x.RuleId == RuleIds.DuplicateImport && x.ElementPath == "SI");
    }

    [Fact]
    public void WildcardImports_NotCompared()
    {
        var f = Check("model Bar\n  import Modelica.SIunits.*;\n  import Modelica.SIunits.*;\nend Bar;");
        Assert.DoesNotContain(f, x => x.RuleId == RuleIds.DuplicateImport);
    }

    [Fact]
    public void ImportsIgnored_WhenDisabled()
    {
        var f = Check("model Bar\n  import A.B.C;\n  import A.B.C;\nend Bar;", components: true, imports: false);
        Assert.DoesNotContain(f, x => x.RuleId == RuleIds.DuplicateImport);
    }

    [Fact]
    public void ComponentsIgnored_WhenDisabled()
    {
        var f = Check("model Bar\n  Real a;\n  Real a;\nend Bar;", components: false, imports: true);
        Assert.DoesNotContain(f, x => x.RuleId == RuleIds.DuplicateDeclaration);
    }
}
