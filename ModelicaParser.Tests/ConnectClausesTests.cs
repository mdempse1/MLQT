using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

/// <summary>
/// The one reader of a class's connect equations. It exists because the readers it replaced
/// disagreed: three looked only at an equation section's direct children, so a connect in a for loop
/// - how an array of components is wired - read as no connection at all, while a fourth walked the
/// whole tree and found it.
/// </summary>
public class ConnectClausesTests
{
    private static IReadOnlyList<ConnectClause> Read(string code)
        => ConnectClauses.In(ModelicaParserHelper.Parse(code)?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition());

    [Fact]
    public void AConnectWrittenInTheSection_IsNotNested()
    {
        var clause = Assert.Single(Read("model M\nequation\n  connect(a, b);\nend M;"));

        Assert.Equal("a", clause.PortA);
        Assert.Equal("b", clause.PortB);
        Assert.False(clause.IsNested);
        Assert.False(clause.InLoop);
        Assert.Empty(clause.Within);
    }

    [Fact]
    public void AConnectInAForLoop_IsFound_WithTheLoopItIsIn()
    {
        const string code = "model N\nequation\n  for i in 1:3 loop\n    connect(a[i], b[i]);\n  end for;\nend N;";

        var clause = Assert.Single(Read(code));

        Assert.Equal("a[i]", clause.PortA);
        Assert.Equal("b[i]", clause.PortB);
        Assert.True(clause.IsNested);
        Assert.True(clause.InLoop);
        var scope = Assert.Single(clause.Scopes);
        Assert.Equal(ConnectScopeKind.For, scope.Kind);
        Assert.Equal("for i in 1:3", scope.Header);
        Assert.Equal(["i"], scope.LoopIndices);
    }

    [Fact]
    public void ALoopOverSeveralIndices_NamesEachOfThem()
    {
        const string code =
            "model N\nequation\n  for i in 1:n, j in 1:m loop\n    connect(a[i, j], b[i, j]);\n  end for;\nend N;";

        var scope = Assert.Single(Assert.Single(Read(code)).Scopes);

        Assert.Equal(["i", "j"], scope.LoopIndices);
        Assert.Equal("for i in 1:n, j in 1:m", scope.Header);
    }

    [Fact]
    public void EachBranchOfAnIf_IsItsOwnScope()
    {
        const string code = """
            model M
            equation
              if use_a and use_b then
                connect(a, p);
              elseif use_b then
                connect(b, p);
              else
                connect(c, p);
              end if;
            end M;
            """;

        var clauses = Read(code);

        Assert.Equal(["a", "b", "c"], clauses.Select(c => c.PortA));
        // The condition keeps its spaces: GetText() would have made `use_aanduse_b` of it.
        Assert.Equal(["if use_a and use_b"], clauses[0].Within);
        Assert.Equal(["elseif use_b"], clauses[1].Within);
        Assert.Equal(["else"], clauses[2].Within);
        Assert.All(clauses, c => Assert.Equal(ConnectScopeKind.If, Assert.Single(c.Scopes).Kind));
        Assert.All(clauses, c => Assert.False(c.InLoop));
    }

    [Fact]
    public void AConditionWrittenAcrossLines_IsOneLineInItsHeader()
    {
        const string code =
            "model M\nequation\n  if use_a and\n     use_b then\n    connect(a, p);\n  end if;\nend M;";

        Assert.Equal(new[] { "if use_a and use_b" }, Assert.Single(Read(code)).Within);
    }

    [Fact]
    public void AWhenAndItsElsewhen_AreScopesToo()
    {
        // A connect is not legal in a when equation, but the grammar takes one, and a reader that
        // stopped there would quietly lose it rather than show it to whoever is reading the class.
        const string code =
            "model M\nequation\n  when x > 0 then\n    connect(a, p);\n  elsewhen y then\n    connect(b, p);\n  end when;\nend M;";

        var clauses = Read(code);

        Assert.Equal(["when x > 0"], clauses[0].Within);
        Assert.Equal(["elsewhen y"], clauses[1].Within);
        Assert.All(clauses, c => Assert.Equal(ConnectScopeKind.When, Assert.Single(c.Scopes).Kind));
    }

    [Fact]
    public void NestedScopes_AreListedOutermostFirst()
    {
        const string code = """
            model M
            equation
              if useArray then
                for i in 1:n loop
                  connect(a[i], b[i]);
                end for;
              end if;
            end M;
            """;

        var clause = Assert.Single(Read(code));

        Assert.Equal(["if useArray", "for i in 1:n"], clause.Within);
        Assert.True(clause.InLoop);
    }

    [Fact]
    public void TheSpan_IsTheConnectItself()
    {
        const string code = "model N\nequation\n  for i in 1:3 loop\n    connect(a[i], b[i]);\n  end for;\nend N;";

        var clause = Assert.Single(Read(code));

        Assert.Equal("connect(a[i], b[i])", code[clause.Start..(clause.Stop + 1)]);
    }

    [Fact]
    public void ConnectsAreInSourceOrder_AcrossSectionsAndNesting()
    {
        const string code = """
            model M
            equation
              connect(a, p);
              for i in 1:2 loop
                x[i] = 0;
                connect(b[i], p);
              end for;
            equation
              // a comment-only line
              connect(c, p);
            end M;
            """;

        Assert.Equal(["a", "b[i]", "c"], Read(code).Select(c => c.PortA));
    }

    [Fact]
    public void AnAlgorithmSection_HasNoConnects()
    {
        Assert.Empty(Read("model M\nalgorithm\n  for i in 1:2 loop\n    x := i;\n  end for;\nend M;"));
    }

    [Fact]
    public void NoComposition_IsNoConnects()
    {
        Assert.Empty(ConnectClauses.In(null));
        Assert.Empty(Read("type T = Real;"));
    }
}
