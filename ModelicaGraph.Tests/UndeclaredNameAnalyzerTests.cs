using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class UndeclaredNameAnalyzerTests
{
    // Each source is loaded as a file, as a library is, so nested classes become nodes of their own.
    private static List<Finding> Run(params string[] sources) => Run(_ => { }, sources);

    private static List<Finding> Run(System.Action<DirectedGraph> arrange, params string[] sources)
    {
        var graph = new DirectedGraph();
        for (var i = 0; i < sources.Length; i++)
            GraphBuilder.LoadModelicaFile(graph, Path.Combine(Path.GetTempPath(), $"undeclared{i}.mo"), sources[i]);
        arrange(graph);
        var settings = new StyleCheckingSettings { CheckUndeclaredNames = true };
        var context = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(context, new IGraphAnalyzer[] { new UndeclaredNameAnalyzer() })
            .Where(f => f.RuleId == RuleIds.UndeclaredName)
            .ToList();
    }

    // A library that declares what it uses, so a name found nowhere cannot be one it did not load.
    private static string Library(string body, string uses = "") => $$"""
        package P
        {{body}}
          annotation (uses({{uses}}));
        end P;
        """;

    private static List<string> Names(IEnumerable<Finding> findings) => findings.Select(f => f.ElementPath!).ToList();

    // Loaded beside a case whose names must be left unjudged, so that the case passes because they
    // were judged unknowable - not because the analysis threw, which it would swallow.
    private const string Control = "package C\n  model K\n    Real c = undeclaredControl;\n  end K;\nend C;";

    private static void OnlyTheControl(List<Finding> findings) =>
        Assert.Equal(["undeclaredControl"], Names(findings));

    // ---- variables ---------------------------------------------------------------------------

    [Fact]
    public void AnUndeclaredVariable_IsReportedWhereItIsFirstUsed()
    {
        var finding = Assert.Single(Run(Library("""
              model M
                Real x;
              equation
                x = k;
                der(x) = k;
              end M;
            """)));

        Assert.Equal("P.M", finding.ModelId);
        Assert.Equal("k", finding.ElementPath);
        Assert.Equal(4, finding.LineNumber);
        Assert.Contains("'k' is not declared", finding.Message);
    }

    [Fact]
    public void UndeclaredNamesInDeclarations_AreReported()
    {
        Assert.Equal(["n", "a", "b", "c"], Names(Run(Library("""
              model M
                Real x[n];
                Real y = a;
                Real z(start = b) if c;
              end M;
            """))));
    }

    [Fact]
    public void UndeclaredNamesInAnAlgorithm_AreReported()
    {
        Assert.Equal(["u", "v"], Names(Run(Library("""
              function f
                output Real y;
              algorithm
                y := u;
                v := 1;
              end f;
            """))));
    }

    [Fact]
    public void DeclaredLocalInheritedAndBuiltInNames_AreNot()
    {
        OnlyTheControl(Run(Control, Library("""
              constant Real c = 1;
              model Base
                parameter Real k = 1;
              end Base;
              model M
                extends Base;
                Real x(start = c);
                Real y[2];
              equation
                der(x) = -k * x + time + sin(x) + size(y, 1) + .sqrt(4);
                y = {i for i in 1:2};
                for j in 1:2 loop
                  y[j] = sum(y[m] for m in 1:2);
                end for;
              end M;
            """)));
    }

    [Fact]
    public void AnIteratorUsedOutsideItsLoop_IsReported()
    {
        Assert.Equal(["i"], Names(Run(Library("""
              model M
                Real y[2];
              equation
                for i in 1:2 loop
                  y[i] = 1;
                end for;
                y[1] = i;
              end M;
            """))));
    }

    [Fact]
    public void AnImportedConstant_IsDeclared()
    {
        OnlyTheControl(Run(Control, Library("""
              package Constants
                constant Real pi = 3.14;
              end Constants;
              model M
                import P.Constants.pi;
                Real x = pi + Constants.pi;
              end M;
            """)));
    }

    [Fact]
    public void ANonConstantOfAnEnclosingClass_IsNotVisible()
    {
        var finding = Assert.Single(Run(Library("""
              model Outer
                parameter Real n = 1;
                function f
                  output Real y = n;
                end f;
              end Outer;
            """)));

        Assert.Equal("P.Outer.f", finding.ModelId);
        Assert.Contains("only a constant of an enclosing class is visible", finding.Message);
    }

    [Fact]
    public void AShortClassModification_SeesTheClassDeclaringIt()
    {
        // `function g = f(k = n)` is written in Outer, so Outer's parameter is in scope for it.
        OnlyTheControl(Run(Control, Library("""
              function f
                input Real k;
                output Real y = k;
              end f;
              model Outer
                parameter Real n = 1;
                function g = f(k = n);
              end Outer;
            """)));
    }

    [Fact]
    public void AnUndeclaredNameUsedTwice_IsReportedOnce()
    {
        Assert.Single(Run(Library("""
              model M
                Real x = k + k;
                Real y = k;
              end M;
            """)));
    }

    [Fact]
    public void AnnotationsAndNamedArguments_AreNotLookedUp()
    {
        OnlyTheControl(Run(Control, Library("""
              function f
                input Real u;
                output Real y = u;
              end f;
              model M
                parameter Boolean use = true annotation (Dialog(enable = notDeclared));
                Real x = f(u = 1);
                annotation (Icon(graphics = {Line(points = {{0, 0}})}));
              end M;
            """)));
    }

    [Fact]
    public void AnExternalFunctionName_IsNotLookedUp()
    {
        OnlyTheControl(Run(Control, Library("""
              function f
                input Real u;
                output Real y;
              external "C" y = c_function(u);
              end f;
            """)));
    }

    // ---- what cannot be known is never reported -----------------------------------------------

    [Fact]
    public void ABaseThatIsNotLoaded_LeavesTheClassUnjudged()
    {
        OnlyTheControl(Run(Control, Library("""
              model M
                extends Other.Base;
                Real x = k;
              end M;
            """, uses: "Other(version=\"1.0\")")));
    }

    [Fact]
    public void ABaseRecoveredFromDocumentation_LeavesTheClassUnjudged()
    {
        OnlyTheControl(Run(
            graph => graph.GetNode<ModelNode>("V.Base")!.IsExternalStub = true,
            Control,
            "package V\n  model Base\n  end Base;\nend V;",
            Library("""
              model M
                extends V.Base;
                Real x = k;
              end M;
            """, uses: "V(version=\"1.0\")")));
    }

    [Fact]
    public void AWildcardImportOfALibraryNotLoaded_LeavesTheNameUnjudged()
    {
        OnlyTheControl(Run(Control, Library("""
              model M
                import Other.Constants.*;
                Real x = e;
              end M;
            """, uses: "Other(version=\"1.0\")")));
    }

    [Fact]
    public void AQualifiedImportNotLoaded_LeavesOnlyItsOwnNameUnjudged()
    {
        Assert.Equal(["g"], Names(Run(Library("""
              model M
                import Other.Constants.e;
                import Alias = Other.Constants;
                Real x = e + Alias.f + g;
              end M;
            """, uses: "Other(version=\"1.0\")"))));
    }

    [Fact]
    public void ANameThatCouldBeALibraryNotLoaded_IsNotReported()
    {
        // No uses(...) annotation: Lib.f and Lib.T could be in any library at all.
        OnlyTheControl(Run(Control, """
            package P
              model M
                Lib.T t;
                Real x = Lib.f(1) + Lib.c;
              end M;
            end P;
            """));
    }

    [Fact]
    public void ANameUsesSaysNoLibraryHas_IsReported()
    {
        Assert.Equal(["Lib.T", "Lib.f", "Lib.c"], Names(Run(Library("""
              model M
                Lib.T t;
                Real x = Lib.f(1) + Lib.c;
              end M;
            """, uses: "Modelica(version=\"4.1.0\")"))));
    }

    [Fact]
    public void ALibraryUsesDeclaresButIsNotLoaded_IsNotReported()
    {
        OnlyTheControl(Run(Control, Library("""
              model M
                Lib.T t;
                Real x = Lib.f(1);
              end M;
            """, uses: "Lib(version=\"1.0\")")));
    }

    [Fact]
    public void APlainVariable_CannotBeALibrary()
    {
        // One segment, not called: no class is a value, so there is no library it could be in.
        Assert.Equal(["k"], Names(Run("package P\n  model M\n    Real x = k;\n  end M;\nend P;")));
    }

    [Fact]
    public void AClassWhosePackageIsNotLoaded_IsNotJudged()
    {
        // One file of a library: what its packages declare is not in the graph.
        OnlyTheControl(Run(Control, "within Lib.Sub;\nmodel M\n  Real x = k;\nend M;"));
    }

    // ---- members -------------------------------------------------------------------------------

    private const string Sub = """
          model Sub
            parameter Real gain = 1;
            Real a;
          protected
            Real hidden;
          end Sub;
        """;

    [Fact]
    public void AMissingMember_IsReported()
    {
        var finding = Assert.Single(Run(Library(Sub + """
              model M
                Sub s;
                Real x = s.gain + s.missing;
              end M;
            """)));

        Assert.Equal("s.missing", finding.ElementPath);
        Assert.Contains("P.Sub has no element of that name", finding.Message);
    }

    [Fact]
    public void AProtectedMemberThroughADot_IsReported()
    {
        var finding = Assert.Single(Run(Library(Sub + """
              model M
                Sub s;
                Real x = s.hidden;
              end M;
            """)));

        Assert.Contains("protected in P.Sub", finding.Message);
    }

    [Fact]
    public void AMissingClassInALoadedPackage_IsReported()
    {
        Assert.Equal(["P.Missing", "P.Pkg.Gone"], Names(Run(Library("""
              package Pkg
                model Here
                end Here;
              end Pkg;
              model M
                P.Missing a;
                P.Pkg.Here b;
                P.Pkg.Gone c;
              end M;
            """))));
    }

    [Fact]
    public void AnEnumerationLiteral_IsCheckedAgainstItsList()
    {
        Assert.Equal(["Types.E.c"], Names(Run(Library("""
              package Types
                type E = enumeration(a, b);
              end Types;
              model M
                Types.E e1 = Types.E.a;
                Types.E e2 = Types.E.c;
              end M;
            """))));
    }

    [Fact]
    public void AMemberOfAnExpandableConnector_IsNotJudged()
    {
        OnlyTheControl(Run(Control, Library("""
              expandable connector Bus
              end Bus;
              model M
                Bus bus;
                Real x = bus.anything;
              end M;
            """)));
    }

    [Fact]
    public void AMemberThroughSomethingReplaceable_IsNotJudged()
    {
        OnlyTheControl(Run(Control, Library(Sub + """
              model M
                replaceable Sub s;
                replaceable package Medium = Pkg;
                Medium.State st;
                Real x = s.extra + st.extra;
              end M;
              package Pkg
                record State
                end State;
              end Pkg;
            """)));
    }

    private const string ComplexRecord = """
        operator record Complex
          replaceable Real re;
          replaceable Real im;
        end Complex;
        """;

    [Fact]
    public void AMemberThroughComplex_IsNotJudged()
    {
        // TypeResolver counts MSL's Complex as predefined and never resolves it, so a class built on
        // it has no members it can see. That makes the class unknown, not its members undeclared.
        OnlyTheControl(Run(Control, ComplexRecord, """
            package P
              connector ComplexInput = input Complex;
              model M
                ComplexInput u;
                Real x = u.re + u.im + u.typo;
              end M;
            end P;
            """));
    }

    [Fact]
    public void AMemberOfAShortClassOfARecord_IsDeclared()
    {
        OnlyTheControl(Run(Control, Library("""
              record R
                Real re;
                Real im;
              end R;
              connector RInput = input R;
              model M
                RInput u;
                Real x = u.re + u.im;
              end M;
            """)));
    }

    // ---- modifications --------------------------------------------------------------------------

    [Fact]
    public void AModificationOfAMissingMember_IsReported()
    {
        var finding = Assert.Single(Run(Library(Sub + """
              model M
                Sub s(gain = 2, gian = 3);
              end M;
            """)));

        Assert.Equal("gian", finding.ElementPath);
        Assert.Equal("modifier:Sub", finding.Discriminator);
        Assert.Contains("P.Sub has no element of that name", finding.Message);
    }

    [Fact]
    public void ANestedModification_IsFollowedThroughTheMembers()
    {
        // A name below a missing one is not reported again: `nope` is, `nope.x` is not.
        Assert.Equal(["s.a.strat", "nope"], Names(Run(Library(Sub + """
              model Holder
                Sub s;
              end Holder;
              model M
                Holder h(s(a(start = 1, strat = 2)));
                Holder t(nope(x = 1));
              end M;
            """))));
    }

    [Fact]
    public void AnAttributeOfAPredefinedType_IsChecked()
    {
        Assert.Equal(["fixd", "stat"], Names(Run(Library("""
              type Voltage = Real(unit = "V");
              connector RealOutput = output Real;
              model M
                Real x(start = 0, fixed = true, nominal = 1, fixd = true);
                Voltage v(start = 1, stat = 2);
                RealOutput y(min = 0);
              end M;
            """))));
    }

    [Fact]
    public void AnIntegerHasNoUnit()
    {
        Assert.Equal(["unit"], Names(Run(Library("""
              model M
                Integer i(start = 0, unit = "1");
                Boolean b(start = true, fixed = true);
                String s(start = "");
              end M;
            """))));
    }

    [Fact]
    public void AnExtendsModification_IsCheckedAgainstTheBase()
    {
        Assert.Equal(["gian"], Names(Run(Library(Sub + """
              model M
                extends Sub(gain = 2, gian = 3);
              end M;
            """))));
    }

    [Fact]
    public void AShortClassModification_IsCheckedAgainstItsBase()
    {
        Assert.Equal(["gian"], Names(Run(Library(Sub + """
              model Sub2 = Sub(gain = 2, gian = 3);
            """))));
    }

    [Fact]
    public void ARedeclarationOfAMissingElement_IsReported()
    {
        Assert.Equal(["Mediium"], Names(Run(Library("""
              package Water
              end Water;
              model Pipe
                replaceable package Medium = Water;
              end Pipe;
              model M
                Pipe p1(redeclare package Medium = Water);
                Pipe p2(redeclare package Mediium = Water);
              end M;
            """))));
    }

    // ---- types, bases and imports ----------------------------------------------------------------

    [Fact]
    public void AnImportOfAMissingClassInALoadedLibrary_IsReported()
    {
        var finding = Assert.Single(Run(Library("""
              package Constants
                constant Real pi = 3.14;
              end Constants;
              model M
                import P.Constants.pi;
                import P.Constants.tau;
                import Other.Thing;
              end M;
            """, uses: "Other(version=\"1.0\")")));

        Assert.Equal("P.Constants.tau", finding.ElementPath);
        Assert.StartsWith("import P.Constants.tau:", finding.Message);
    }

    [Fact]
    public void AnUndeclaredBase_IsReported()
    {
        Assert.Equal(["P.Missing"], Names(Run(Library("""
              model M
                extends P.Missing;
              end M;
            """))));
    }
}
