using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.ParserVisitors;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaGraph.Tests;

public class NameUseCollectorTests
{
    private static List<NameUse> Collect(string code) => NameUseCollector.Collect(ModelicaParserHelper.Parse(code));

    private static List<string> Of(List<NameUse> uses, NameUseKind kind) =>
        uses.Where(u => u.Kind == kind).Select(u => u.Kind == NameUseKind.Modifier ? $"{u.Text}:{string.Join('.', u.Path)}" : u.Text).ToList();

    [Fact]
    public void ValuesCallsAndTypes_AreToldApart()
    {
        var uses = Collect("""
            model M
              Lib.T t(k = a.b[i]) if c;
              Real x[n];
            equation
              x = f(y, z = w);
              g(1);
              connect(p.q, r.s);
            end M;
            """);

        Assert.Equal(["a.b", "i", "c", "n", "x", "y", "w", "p.q", "r.s"], Of(uses, NameUseKind.Value));
        Assert.Equal(["f", "g"], Of(uses, NameUseKind.Call));
        Assert.Equal(["Lib.T", "Real"], Of(uses, NameUseKind.Type));
        Assert.Equal(["Lib.T:k"], Of(uses, NameUseKind.Modifier));
    }

    [Fact]
    public void ALeadingDot_IsKept()
    {
        var uses = Collect("model M\n  Real x = .Lib.c + .Lib.f(1);\nend M;");
        Assert.Equal([".Lib.c"], Of(uses, NameUseKind.Value));
        Assert.Equal([".Lib.f"], Of(uses, NameUseKind.Call));
    }

    [Fact]
    public void Statements_CallOrAssign()
    {
        var uses = Collect("""
            function f
            algorithm
              (a, b) := g(c);
              h(d);
              e := 1;
              der(v) := 2;
            end f;
            """);

        Assert.Equal(["g", "h"], Of(uses, NameUseKind.Call));
        Assert.Equal(["a", "b", "c", "d", "e", "v"], Of(uses, NameUseKind.Value));
    }

    [Fact]
    public void AnExternalCall_KeepsItsArgumentsAndResult()
    {
        var uses = Collect("function f\n  input Real u;\n  output Real y;\nexternal \"C\" y = c_f(u, n);\nend f;");

        Assert.Equal(["y", "u", "n"], Of(uses, NameUseKind.Value));
        Assert.Empty(Of(uses, NameUseKind.Call));
    }

    [Fact]
    public void IteratorsAreBound_InsideTheirLoopOnly()
    {
        var uses = Collect("""
            model M
              Real a[3] = {i * k for i in 1:3};
              Real s = sum(a[j] for j in 1:3);
            algorithm
              for m in 1:n loop
                a[m] := m;
              end for;
              a[1] := m;
            end M;
            """);

        Assert.Equal(["k", "a", "n", "a", "a", "m"], Of(uses, NameUseKind.Value));
    }

    [Fact]
    public void ExtendsAndShortClassBases_AreBases()
    {
        var extendsUses = Collect("model M\n  extends B(x = 1, y(start = 2));\nend M;");
        Assert.Equal(["B"], Of(extendsUses, NameUseKind.Base));
        Assert.Equal(["B:x", "B:y", "B:y.start"], Of(extendsUses, NameUseKind.Modifier));
        Assert.All(extendsUses.Where(u => u.Kind == NameUseKind.Modifier), u => Assert.True(u.OwnerIsBase));

        var shortUses = Collect("model R2 = R[n](r = 2);");
        Assert.Equal(["R"], Of(shortUses, NameUseKind.Base));
        Assert.Equal(["n"], Of(shortUses, NameUseKind.Value));
        Assert.Equal(["R:r"], Of(shortUses, NameUseKind.Modifier));
    }

    [Fact]
    public void AnEnumerationOrADerClass_NamesOnlyItsType()
    {
        Assert.Empty(Collect("type E = enumeration(a, b);"));
        var der = Collect("type D = der(F, x, y);");
        Assert.Equal(["F"], Of(der, NameUseKind.Type));
        Assert.Single(der);
    }

    [Fact]
    public void AClassExtends_ChecksItsValuesButNotItsTargets()
    {
        var uses = Collect("model extends M(k = v)\nend M;");
        Assert.Equal(["v"], Of(uses, NameUseKind.Value));
        Assert.Empty(Of(uses, NameUseKind.Modifier));
    }

    [Fact]
    public void AConstrainingClause_IsATypeWithModifications()
    {
        var uses = Collect("model M\n  replaceable R r constrainedby C(x = 1);\nend M;");
        Assert.Equal(["R", "C"], Of(uses, NameUseKind.Type));
        Assert.Equal(["C:x"], Of(uses, NameUseKind.Modifier));
    }

    [Fact]
    public void Redeclarations_NameAMemberAndAType()
    {
        var uses = Collect("""
            model M
              P p(redeclare package Medium = W, redeclare Real x(start = 1) = 2, replaceable model N = Q constrainedby K,
                  redeclare replaceable S s);
            end M;
            """);

        Assert.Equal(["P:Medium", "P:x", "Real:start", "P:N", "P:s"], Of(uses, NameUseKind.Modifier));
        Assert.Equal(["W", "Q"], Of(uses, NameUseKind.Base));
        Assert.Equal(["P", "Real", "K", "S"], Of(uses, NameUseKind.Type));
    }

    [Fact]
    public void APartialApplication_CallsItsFunction()
    {
        var uses = Collect("model M\n  Real y = integrate(function g(a = b), 0, 1);\nend M;");
        Assert.Equal(["integrate", "g"], Of(uses, NameUseKind.Call));
        Assert.Equal(["b"], Of(uses, NameUseKind.Value));
    }

    [Fact]
    public void ImportsAnnotationsDescriptionsAndNestedClasses_AreLeftOut()
    {
        var uses = Collect("""
            model M "uses nothing"
              import A.B.*;
              import C = D.E;
              Real x "x" annotation (Dialog(enable = q));
              model Inner
                Real y = z;
              end Inner;
              annotation (Icon(graphics = {Line(points = {{0, 0}})}));
            end M;
            """);

        Assert.Equal(["A.B.*", "C = D.E"], Of(uses, NameUseKind.Import));
        Assert.Equal(["Real"], Of(uses, NameUseKind.Type));
        Assert.Empty(Of(uses, NameUseKind.Value));
    }

    [Fact]
    public void ABreakModification_IsNotAName()
    {
        Assert.Equal(["B"], Collect("model M\n  extends B(break x);\nend M;").Select(u => u.Text));
    }

    [Fact]
    public void AnEmptyFile_HasNoUses()
    {
        Assert.Empty(Collect("within P;"));
    }

    [Fact]
    public void OwnerType_IsOnlyAModifiers()
    {
        var uses = Collect("model M\n  R r(x = 1);\nend M;");
        Assert.Equal("R", uses.Single(u => u.Kind == NameUseKind.Modifier).OwnerType);
        Assert.Null(uses.Single(u => u.Kind == NameUseKind.Type).OwnerType);
    }
}
