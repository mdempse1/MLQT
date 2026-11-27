using ModelicaGraph.Analysis;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>MLQT.Connection.Incompatible</c> and <c>MLQT.Connection.Duplicate</c>, over real source, so the
/// ports are resolved and the connectors compared as a check does it.
/// </summary>
public class ConnectionIntegrityAnalyzerTests
{
    /// <summary>A small library of connectors of every kind the rule has to tell apart.</summary>
    private const string Connectors = """
        package Lib
          type Voltage = Real(unit = "V");
          connector Pin
            Real v;
            flow Real i;
          end Pin;
          connector OtherPin "Pin's members, but nothing flows"
            Real v;
            Real i;
          end OtherPin;
          connector HeatPort
            Real T;
            flow Real Q_flow;
          end HeatPort;
          connector PositivePin = Pin;
          connector RealInput = input Real;
          connector RealOutput = output Real;
          connector VoltageInput = input Voltage;
          connector IntegerInput = input Integer;
          expandable connector Bus
          end Bus;
          connector VendorPort
            extends OtherLib.BasePort;
          end VendorPort;
          model TwoPin
            Pin p;
            Pin n;
          end TwoPin;
          model Heater
            HeatPort port;
            Pin p;
          end Heater;
          model Source
            RealOutput y;
          end Source;
          model Sink
            VoltageInput u;
            IntegerInput k;
          end Sink;
          model Choosable
            replaceable Pin p;
          end Choosable;
          model Box
            replaceable connector PortType = Pin;
            PortType p;
          end Box;
        end Lib;
        """;

    private static List<Finding> Check(string model, StyleCheckingSettings? settings = null)
    {
        var graph = new DirectedGraph();
        var root = Path.Combine(Path.GetTempPath(), "ConnLib");
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(root, "Lib", "package.mo"), Connectors);
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(root, "Test.mo"), model);

        var checkedModels = graph.ModelNodes.Where(m => m.Id == "Test").ToList();
        settings ??= new StyleCheckingSettings { CheckIncompatibleConnections = true, CheckDuplicateConnections = true };
        return new ConnectionIntegrityAnalyzer().Analyze(new GraphAnalysisContext(graph, settings, checkedModels)).ToList();
    }

    private static string Model(string components, string equations) => $$"""
        model Test
        {{components}}
        equation
        {{equations}}
        end Test;
        """;

    [Fact]
    public void APinJoinedToAHeatPort_IsIncompatible()
    {
        var finding = Assert.Single(Check(Model(
            "  Lib.TwoPin r;\n  Lib.Heater h;",
            "  connect(r.p, h.port);")));

        Assert.Equal(RuleIds.IncompatibleConnection, finding.RuleId);
        Assert.Equal("Test", finding.ModelId);
        Assert.Equal(5, finding.LineNumber);
        Assert.Equal("h.port, r.p", finding.ElementPath);   // in order, as a duplicate's is
        Assert.Equal(
            "connect(r.p, h.port) joins Lib.Pin to Lib.HeatPort, which cannot be connected: " +
            "members i (flow), v against members Q_flow (flow), T",
            finding.Message);
    }

    [Fact]
    public void TheSameNamesWithoutTheFlow_AreIncompatible()
    {
        var finding = Assert.Single(Check(Model(
            "  Lib.TwoPin r;\n  Lib.OtherPin o;",
            "  connect(r.p, o);")));

        Assert.Contains("members i (flow), v against members i, v", finding.Message);
    }

    [Fact]
    public void TwoPinsOfOneType_AreCompatible()
    {
        Assert.Empty(Check(Model("  Lib.TwoPin a;\n  Lib.TwoPin b;", "  connect(a.n, b.p);")));
    }

    [Fact]
    public void AnAliasConnector_IsItsBase()
    {
        // connector PositivePin = Pin. The MCP server's old signature read it as the text "alias:Pin",
        // which nothing equals, so its add_connection refused to wire an alias to its base.
        Assert.Empty(Check(Model("  Lib.TwoPin a;\n  Lib.PositivePin p;", "  connect(a.p, p);")));
    }

    [Fact]
    public void ValueConnectors_CompareByTheirPredefinedType_NotByCausalityOrUnit()
    {
        // A RealOutput into a VoltageInput: both Real, whatever the unit; causality is another question.
        Assert.Empty(Check(Model("  Lib.Source s;\n  Lib.Sink t;", "  connect(s.y, t.u);")));

        var finding = Assert.Single(Check(Model("  Lib.Source s;\n  Lib.Sink t;", "  connect(s.y, t.k);")));
        Assert.EndsWith("cannot be connected: Real against Integer", finding.Message);
    }

    [Fact]
    public void TheSameIncompatiblePair_EitherWayRound_IsReportedOnce()
    {
        // The second is a duplicate of the first, and reported as one; it is not a second
        // incompatible connection.
        var findings = Check(Model("  Lib.TwoPin r;\n  Lib.Heater h;", "  connect(r.p, h.port);\n  connect(h.port, r.p);"));

        Assert.Equal([RuleIds.DuplicateConnection, RuleIds.IncompatibleConnection], findings.Select(f => f.RuleId).Order());
        Assert.Equal("h.port, r.p", findings.Single(f => f.RuleId == RuleIds.IncompatibleConnection).ElementPath);
    }

    [Fact]
    public void WhatARedeclarationDecides_IsNotJudged()
    {
        // Through a replaceable component, and through a replaceable connector class.
        Assert.Empty(Check(Model("  replaceable Lib.Heater h;\n  Lib.TwoPin r;", "  connect(r.p, h.port);")));
        Assert.Empty(Check(Model("  Lib.Box b;\n  Lib.Heater h;", "  connect(b.p, h.port);")));
    }

    [Theory]
    [InlineData("replaceable model M = Lib.Heater;")]
    [InlineData("replaceable model M\n    extends Lib.Heater;\n  end M;")]
    public void APortInheritedByAReplaceableClass_IsLeftToTheRedeclaration(string declaration)
    {
        // h.p's owner is Heater, the class declaring it; the replaceable M is only the class h is
        // declared as, and a redeclaration may make it one whose p is something else.
        Assert.Empty(Check($$"""
            model Test
              {{declaration}}
              M h;
              Lib.TwoPin r;
            equation
              connect(r.p, h.port);
            end Test;
            """));
    }

    [Fact]
    public void APortInheritedByAClassThatIsNotReplaceable_IsJudged()
    {
        Assert.Single(Check("""
            model Test
              model M = Lib.Heater;
              M h;
              Lib.TwoPin r;
            equation
              connect(r.p, h.port);
            end Test;
            """));
    }

    [Fact]
    public void AnExpandableConnector_TakesAnything()
    {
        Assert.Empty(Check(Model("  Lib.Bus bus;\n  Lib.Source s;", "  connect(s.y, bus.speed);\n  connect(bus, s.y);")));
    }

    [Fact]
    public void WhatMLQTCannotSee_IsNotJudged()
    {
        // A port that resolves to nothing is MLQT.Reference.Undeclared's to report; a connector with a
        // base in a library that is not loaded could have any members; a replaceable port is whatever
        // a redeclaration makes it.
        Assert.Empty(Check(Model("  Lib.TwoPin r;", "  connect(r.p, nowhere.p);")));
        Assert.Empty(Check(Model("  Lib.TwoPin r;\n  Lib.VendorPort v;", "  connect(r.p, v);")));
        Assert.Empty(Check(Model("  Lib.Choosable c;\n  Lib.Heater h;", "  connect(c.p, h.port);")));
    }

    [Fact]
    public void APortACheckedClassInherits_IsResolved()
    {
        var finding = Assert.Single(Check("""
            model Test
              extends Lib.TwoPin;
              Lib.Heater h;
            equation
              connect(p, h.port);
            end Test;
            """));

        Assert.Equal(RuleIds.IncompatibleConnection, finding.RuleId);
    }

    [Fact]
    public void TheSamePairTwice_EitherWayRoundAndSpacedAnyHow_IsADuplicate()
    {
        var findings = Check(Model(
            "  Lib.TwoPin a;\n  Lib.TwoPin b;",
            "  connect(a.n, b.p);\n  connect(b.p, a.n);\n  connect( a.n ,b.p );"));

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(RuleIds.DuplicateConnection, f.RuleId));
        Assert.Equal([6, 7], findings.Select(f => f.LineNumber));
        Assert.Equal("connect(b.p, a.n) joins the same ports as the connect on line 5", findings[0].Message);
    }

    [Fact]
    public void DifferentElementsOfAnArray_AreDifferentPorts()
    {
        Assert.Empty(Check(Model("  Lib.TwoPin a[2];\n  Lib.TwoPin b;", "  connect(a[1].n, b.p);\n  connect(a[2].n, b.p);")));
    }

    [Fact]
    public void TheSamePairInTwoBranchesOfAnIf_IsNotADuplicate_ButTwiceInOneBranchIs()
    {
        var findings = Check(Model(
            "  parameter Boolean useB = true;\n  Lib.TwoPin a;\n  Lib.TwoPin b;",
            """
              if useB then
                connect(a.n, b.p);
              else
                connect(a.n, b.p);
              end if;
              if useB then
                connect(a.p, b.n);
                connect(a.p, b.n);
              end if;
            """));

        var finding = Assert.Single(findings);
        Assert.Equal("a.p, b.n", finding.ElementPath);
        Assert.Equal("if useB", finding.Discriminator);
    }

    [Fact]
    public void TheElseBranchesOfTwoStatements_AreNotTheSameBranch_ButTwoIfsOnOneConditionAre()
    {
        // `else` alone said nothing about when it applies: these two never hold together.
        Assert.Empty(Check(Model(
            "  parameter Boolean useB = true;\n  Lib.TwoPin a;\n  Lib.TwoPin b;",
            """
              if useB then
              else
                connect(a.n, b.p);
              end if;
              if not useB then
              else
                connect(a.n, b.p);
              end if;
            """)));

        // Two statements on the same condition both connect when it holds.
        Assert.Single(Check(Model(
            "  parameter Boolean useB = true;\n  Lib.TwoPin a;\n  Lib.TwoPin b;",
            """
              if useB then
                connect(a.n, b.p);
              end if;
              if useB then
                connect(a.n, b.p);
              end if;
            """)));
    }

    [Fact]
    public void EveryDuplicate_HasAFingerprintOfItsOwn()
    {
        // A baseline is keyed by fingerprint, so two duplicates sharing one made a copy added later
        // read as accepted debt: two else branches of different ifs carried the same header, and a
        // third copy of a connect the same everything as the second.
        var findings = Check(Model(
            "  parameter Boolean useB = true;\n  Lib.TwoPin a;\n  Lib.TwoPin b;",
            """
              if useB then
              else
                connect(a.n, b.p);
                connect(a.n, b.p);
              end if;
              if not useB then
              else
                connect(a.n, b.p);
                connect(a.n, b.p);
              end if;
              connect(a.p, b.n);
              connect(a.p, b.n);
              connect(a.p, b.n);
            """));

        Assert.Equal(4, findings.Count);
        Assert.Equal(4, findings.Select(f => f.Fingerprint).Distinct().Count());
    }

    [Fact]
    public void ABranchIsItsCondition_HoweverItIsSpaced()
    {
        Assert.Single(Check(Model(
            "  parameter Boolean useB = true;\n  Lib.TwoPin a;\n  Lib.TwoPin b;",
            """
              if useB then
                connect(a.n, b.p);
              end if;
              if  useB   then
                connect(a.n, b.p);
              end if;
            """)));
    }

    [Fact]
    public void OffByDefault()
    {
        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.IncompatibleConnection));
        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.DuplicateConnection));
        Assert.Empty(Check(Model("  Lib.TwoPin r;\n  Lib.Heater h;", "  connect(r.p, h.port);\n  connect(r.p, h.port);"),
            new StyleCheckingSettings()));
    }

    [Fact]
    public void OnlyTheRuleThatIsOn_Reports()
    {
        var model = Model("  Lib.TwoPin r;\n  Lib.Heater h;", "  connect(r.p, h.port);\n  connect(r.p, h.port);");

        Assert.Equal([RuleIds.IncompatibleConnection],
            Check(model, new StyleCheckingSettings { CheckIncompatibleConnections = true }).Select(f => f.RuleId));
        Assert.Equal([RuleIds.DuplicateConnection],
            Check(model, new StyleCheckingSettings { CheckDuplicateConnections = true }).Select(f => f.RuleId));
    }

    // --- MLQT.Connection.DeepReference ---

    /// <summary>A component with an expandable bus holding a sub-bus, as the report that asked for the rule had.</summary>
    private const string Buses = """
          expandable connector DriverBus
            Real x;
          end DriverBus;
          expandable connector ControlBus
            DriverBus driverBus;
          end ControlBus;
          model Aero
            ControlBus controlBus;
            Lib.TwoPin r;
          end Aero;
          block Controller
            ControlBus controlBus;
          end Controller;
          record Data
            Real k;
          end Data;
        """;

    private static readonly StyleCheckingSettings DeepOnly = new() { CheckDeepConnectorReferences = true };

    private static List<Finding> CheckDeep(string components, string equations) => Check($$"""
        model Test
        {{Buses}}
        {{components}}
        equation
        {{equations}}
        end Test;
        """, DeepOnly);

    [Fact]
    public void ReachingThroughAComponentsBusIntoASubBus_IsReported()
    {
        // The case Dymola refused: aero is a model, so connect may name aero.controlBus and no deeper.
        var finding = Assert.Single(CheckDeep(
            "  Aero aero;\n  Lib.Source button;",
            "  connect(button.y, aero.controlBus.driverBus.x);"));

        Assert.Equal(RuleIds.DeepConnectorReference, finding.RuleId);
        Assert.Equal("Test", finding.ModelId);
        Assert.Equal("aero.controlBus.driverBus.x", finding.ElementPath);
        Assert.Equal("button.y", finding.Discriminator);
        Assert.StartsWith(
            "connect(button.y, aero.controlBus.driverBus.x) reaches through component aero (Test.Aero) " +
            "into aero.controlBus.driverBus.x.", finding.Message);
        Assert.Contains("Connect aero.controlBus to a connector of this class", finding.Message);
    }

    [Theory]
    [InlineData("aero.controlBus.newSignal")]        // an undeclared bus member, three names
    [InlineData("aero.r.p")]                         // a connector of a component of a component
    [InlineData("aero.r.p.v")]
    [InlineData("aeros[2].controlBus.driverBus")]    // through an element of an array of components
    [InlineData("controller.controlBus.driverBus")]  // a block is a component as a model is
    public void AnyArgumentPastAComponentsConnector_IsReported(string port)
    {
        var finding = Assert.Single(CheckDeep(
            "  Aero aero;\n  Aero aeros[2];\n  Controller controller;\n  Lib.Source button;",
            $"  connect(button.y, {port});"));

        Assert.Equal(ConnectionIntegrityAnalyzer.Normalised(port), finding.ElementPath);
    }

    [Theory]
    [InlineData("aero.controlBus")]                  // m.c
    [InlineData("bus.driverBus.x")]                  // c1.c2.c3, through a connector of the class
    [InlineData("bus.anything.atAll")]
    [InlineData("button.y")]
    [InlineData("u")]
    public void TheFormsTheLanguageAllows_AreNotReported(string port)
    {
        Assert.Empty(CheckDeep(
            "  Aero aero;\n  ControlBus bus;\n  Lib.Source button;\n  Lib.RealInput u;",
            $"  connect(button.y, {port});"));
    }

    [Theory]
    [InlineData("nowhere.controlBus.driverBus.x")]   // MLQT.Reference.Undeclared's to report
    [InlineData("data.k.x")]                         // a record holds no connectors: not this rule's
    [InlineData("Lib.Aero.controlBus.x")]            // a class-qualified name is no component of the class
    [InlineData(".aero.controlBus.driverBus.x")]
    [InlineData("other.controlBus.driverBus.x")]     // a type MLQT cannot see
    public void WhatTheRuleCannotJudge_IsNotReported(string port)
    {
        Assert.Empty(CheckDeep(
            "  Aero aero;\n  Data data;\n  Missing.Thing other;\n  Lib.Source button;",
            $"  connect(button.y, {port});"));
    }

    [Fact]
    public void AnInheritedComponent_IsAComponentOfTheClass()
    {
        Assert.Single(Check("""
            model Test
              model Holder
                Lib.TwoPin r;
              end Holder;
              extends Holder;
              Lib.TwoPin s;
            equation
              connect(s.p, r.p.v);
            end Test;
            """, DeepOnly));
    }

    [Fact]
    public void BothArguments_AreJudged_EachWithAFingerprintOfItsOwn()
    {
        var findings = CheckDeep(
            "  parameter Boolean on = true;\n  Aero a;\n  Aero b;",
            """
              connect(a.controlBus.driverBus, b.controlBus.driverBus);
              connect(a.controlBus.driverBus, b.r.n);
              for i in 1:2 loop
                connect(a.controlBus.driverBus, b.r.n);
              end for;
            """);

        Assert.Equal(6, findings.Count);
        Assert.Equal(6, findings.Select(f => f.Fingerprint).Distinct().Count());
    }

    [Fact]
    public void DeepReference_IsOffByDefault_AndOnlyItReportsWhenOnlyItIsOn()
    {
        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.DeepConnectorReference));
        var model = Model("  Lib.TwoPin r;\n  Lib.Heater h;", "  connect(r.p, h.port);\n  connect(r.p, h.port);\n  connect(r.p, h.p.v);");

        Assert.Empty(Check(model, new StyleCheckingSettings()));
        Assert.Equal([RuleIds.DeepConnectorReference], Check(model, DeepOnly).Select(f => f.RuleId));
    }

    [Theory]
    [InlineData(" a . b [ 1 ] ", "a.b[1]")]
    [InlineData("a.'quoted name'", "a.'quoted name'")]
    public void APortIsComparedWithoutItsSpacing_ExceptInsideAQuotedName(string written, string expected)
    {
        Assert.Equal(expected, ConnectionIntegrityAnalyzer.Normalised(written));
    }
}
