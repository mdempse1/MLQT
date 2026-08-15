using System.Collections.Generic;
using System.Linq;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class UnusedImportsTests
{
    private static List<Finding> Check(string code)
    {
        var visitor = new UnusedImports();
        visitor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return visitor.Findings.Where(f => f.RuleId == RuleIds.UnusedImport).ToList();
    }

    [Fact]
    public void UnusedImport_IsFlagged()
    {
        var f = Assert.Single(Check("model M\n  import Modelica.Utilities.Streams;\n  Real x;\nend M;"));
        Assert.Equal("Streams", f.ElementPath);
        Assert.Equal("M", f.ModelId);
    }

    [Fact]
    public void UsedImport_AsType_IsNotFlagged()
    {
        // The imported name is used as a type — considered used.
        Assert.Empty(Check("model M\n  import Modelica.Units.SI.Length;\n  Length x;\nend M;"));
    }

    [Fact]
    public void UsedRenamedImport_IsNotFlagged()
    {
        Assert.Empty(Check("model M\n  import SI = Modelica.Units.SI;\n  SI.Length x;\nend M;"));
    }

    [Fact]
    public void UnusedRenamedImport_IsFlagged()
    {
        var f = Assert.Single(Check("model M\n  import SI = Modelica.Units.SI;\n  Real x;\nend M;"));
        Assert.Equal("SI", f.ElementPath);
    }

    [Fact]
    public void UsedInEquation_IsNotFlagged()
    {
        Assert.Empty(Check("model M\n  import C = Modelica.Constants;\n  Real x;\nequation\n  x = C.pi;\nend M;"));
    }

    [Fact]
    public void WildcardImport_IsNotFlagged()
    {
        // A wildcard binds no single checkable name, so it is never flagged.
        Assert.Empty(Check("model M\n  import Modelica.Units.SI.*;\n  Real x;\nend M;"));
    }

    [Fact]
    public void ImportUsedOnlyInNestedClass_IsFlaggedInOuter()
    {
        // Imports are not visible to nested classes, so an outer import used only by a nested class
        // is genuinely unused in the outer class (the nested reference relies on its own import).
        var code = "model M\n  import Modelica.Blocks.Continuous;\n  model Inner\n    Continuous.Integrator i;\n  end Inner;\nend M;";
        Assert.Contains(Check(code), f => f.ElementPath == "Continuous" && f.ModelId == "M");
    }
}
