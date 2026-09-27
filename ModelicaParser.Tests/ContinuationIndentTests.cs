namespace ModelicaParser.Tests;

/// <summary>
/// B465 - a line an argument list wraps onto is at least a level in from the line the list opened
/// on. The renderer indents a line by the level it is ended at, and the last line of a list wrapped
/// for length is ended after the list has gone back out a level, so it could come out shallower than
/// the line it continues: MSL's <c>Blocks.Discrete.ZeroOrderHold</c> had its Icon's
/// <c>graphics={Line(points=...</c> ten spaces in and the <c>color=</c> that continues it eight.
///
/// <para>B466 - the same for a modification's argument list: a nested modification on a
/// continuation line had its wrapped arguments at the column of its own name, or to the left of it
/// (MSL <c>Fluid.Fittings</c>: <c>m_flow(</c> with <c>min=</c> below it at the same indent and
/// <c>max=</c> two to the left).</para>
///
/// <para>B467 - an argument wrapped for length whose own list wraps is at the column of its
/// siblings. Its line was ended inside the extra level the wrap adds, so it came out a level deeper
/// than a sibling that did not wrap: Buildings' DXSystems <c>DryCoil</c> had <c>nomVal=</c> two
/// spaces to the right of <c>spe=</c> and <c>perCur=</c>.</para>
/// </summary>
public class ContinuationIndentTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    private const string GraphicsLineWrapped = """
        model M

          annotation (Icon(coordinateSystem(
            preserveAspectRatio=true,
            extent={{-100, -100}, {100, 100}}
          ),
            graphics={Line(points={{-78, -42}, {-52, -42}, {-52, 0}, {-26, 0}, {-26, 24}, {-6, 24}, {-6, 64}, {18, 64}, {18, 20}, {38, 20}, {38, 0}, {62, 0}},
              color={0, 0, 127})}));
        end M;
        """;

    [Fact]
    public void AGraphicsElementsWrappedArgumentIsALevelInFromTheLineItContinues()
    {
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  annotation (Icon(coordinateSystem(preserveAspectRatio=true, extent={{-100,-100},{100,100}}), graphics={Line(points={{-78,-42},{-52,-42},{-52,0},{-26,0},{-26,24},{-6,24},{-6,64},{18,64},{18,20},{38,20},{38,0},{62,0}}, color={0,0,127})}));
                end M;
                """),
            expectedOutput: Normalise(GraphicsLineWrapped));
    }

    private const string CallWrappedInAnExpression = """
        model M
          Real y;

        equation
          y = a
            + Some.Package.fn(table=table, iSam=pre(iSam),
              Q_flow=QAve_flow, samplePeriod=samplePeriod, rExt=rExt,
              hSeg=hSeg);
        end M;
        """;

    [Fact]
    public void ACallOnAContinuationLineWrapsItsArgumentsALevelInFromThatLine()
    {
        // Buildings' SingleUTubeBoundaryCondition: the call starts a continuation of the equation,
        // and its arguments were written at the same column as the call.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  y = a + Some.Package.fn(table=table, iSam=pre(iSam), Q_flow=QAve_flow, samplePeriod=samplePeriod, rExt=rExt, hSeg=hSeg);
                end M;
                """),
            expectedOutput: Normalise(CallWrappedInAnExpression),
            maxLineLength: 60);
    }

    private const string FirstArgumentMovedOnAContinuationLine = """
        function f
          input Real p;
          output State state;

        algorithm
          state := if size(X, 1) == nX then ThermodynamicState(p=p,
            T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X), X=X) else ThermodynamicState(
              p=p, T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X),
              X=cat(1, X, {1 - sum(X)}));
        end f;
        """;

    [Fact]
    public void AFirstArgumentMovedOffAContinuationLineIsALevelInFromIt()
    {
        // MSL's ReferenceMoistAir.setState_phX: the second call opens on a continuation line, and
        // its first argument, moved to a line of its own (B464), was written at that line's column.
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Real p;
                  output State state;
                algorithm
                  state := if size(X, 1) == nX then ThermodynamicState(p=p, T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X), X=X) else ThermodynamicState(p=p, T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X), X=cat(1, X, {1 - sum(X)}));
                end f;
                """),
            expectedOutput: Normalise(FirstArgumentMovedOnAContinuationLine));
    }

    [Fact]
    public void ACallOnAStatementsFirstLineWrapsAsItAlwaysHas()
    {
        // The continuation is already a level in from the line the call opened on, so the minimum
        // leaves it where it was.
        TestHelpers.AssertClass(Normalise("""
            model M
              Real y;

            equation
              y = Some.Package.fn(table=table, iSam=pre(iSam),
                Q_flow=QAve_flow, samplePeriod=samplePeriod);
            end M;
            """), maxLineLength: 60);
    }

    [Fact]
    public void ACommentAfterACallsBracketLeavesItsContinuationWhereItWas()
    {
        // The comment ends the line the '(' is on before the arguments start (B431), so it is that
        // line the continuation is kept in from - not the first argument's.
        TestHelpers.AssertClass(Normalise("""
            model M
              Real y;

            equation
              y = fn( // c
                a=12345678901234567890123456789012345678901234567890,
                b=2);
              y = fn(
                // own line
                a=1, b=2,
                cccccccccccccccccccccccccccccccccccc=3);
            end M;
            """), maxLineLength: 40);
    }

    private const string NestedModificationsWrapped = """
        model M
          Modelica.Fluid.Interfaces.FluidPorts_b ports[nPorts](redeclare each package Medium = Medium,
            m_flow(each max=if flowDirection == Types.PortFlowDirection.Leaving then 0 else +Constants.inf,
              each min=if flowDirection == Types.PortFlowDirection.Entering then 0 else -Constants.inf));
          Modelica.Fluid.Interfaces.FluidPort_a port_1(redeclare package Medium = Medium,
            m_flow(
              min=if (portFlowDirection_1 == PortFlowDirection.Entering) then 0.0 else -Modelica.Constants.inf,
              max=if (portFlowDirection_1 == PortFlowDirection.Leaving) then 0.0 else Modelica.Constants.inf));
        end M;
        """;

    [Fact]
    public void ANestedModificationsWrappedArgumentsAreALevelInFromItsName()
    {
        // MSL's Fluid.Sources (a later argument wrapped for length) and Fluid.Fittings (the first
        // argument moved to a line of its own, B464, and the next wrapped after it).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Modelica.Fluid.Interfaces.FluidPorts_b ports[nPorts](redeclare each package Medium = Medium, m_flow(each max=if flowDirection == Types.PortFlowDirection.Leaving then 0 else +Constants.inf, each min=if flowDirection == Types.PortFlowDirection.Entering then 0 else -Constants.inf));
                  Modelica.Fluid.Interfaces.FluidPort_a port_1(redeclare package Medium = Medium, m_flow(min=if (portFlowDirection_1 == PortFlowDirection.Entering) then 0.0 else -Modelica.Constants.inf, max=if (portFlowDirection_1 == PortFlowDirection.Leaving) then 0.0 else Modelica.Constants.inf));
                end M;
                """),
            expectedOutput: Normalise(NestedModificationsWrapped));
    }

    private const string NestedModificationAnArgumentALine = """
        model M
          Tt tt(
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=1,
            sub(a=1,
              b=2,
              c=3));
        end M;
        """;

    [Fact]
    public void ANestedModificationWrittenAnArgumentALineIsALevelInFromItsName()
    {
        // A list of more than two is written an argument a line whatever its length; its last
        // line was ended two levels out, to the left of the name it belongs to (B466). The
        // nested modification itself is at its sibling's column (B467), not a level past it.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Tt tt(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=1, sub(a=1, b=2, c=3));
                end M;
                """),
            expectedOutput: Normalise(NestedModificationAnArgumentALine),
            maxLineLength: 40);
    }

    private const string CallArgumentWhoseListWraps = """
        model M
          parameter Data.DXCoil datCoi(sta={Some.Long.Package.Stage(
            spe=900/60,
            nomVal=Some.Long.Package.NominalValues(
              Q_flow_nominal=-12000, COP_nominal=3, SHR_nominal=0.8),
            perCur=Some.Long.Package.Curve_I())}, nSta=1);
        end M;
        """;

    [Fact]
    public void ACallsWrappedArgumentWhoseOwnListWrapsIsAtItsSiblingsColumn()
    {
        // Buildings' DXSystems DryCoil: nomVal= was written two spaces right of spe= and perCur=,
        // because its line was ended inside the level its own wrapped list adds (B467).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Data.DXCoil datCoi(sta={Some.Long.Package.Stage(spe=900/60, nomVal=Some.Long.Package.NominalValues(Q_flow_nominal=-12000, COP_nominal=3, SHR_nominal=0.8), perCur=Some.Long.Package.Curve_I())}, nSta=1);
                end M;
                """),
            expectedOutput: Normalise(CallArgumentWhoseListWraps),
            maxLineLength: 60);
    }

    private const string MatrixArgumentWrapped = """
        model M
          Modelica.Blocks.Sources.CombiTimeTable intGai(
            extrapolation=Periodic,
            table=[0, 0; 3600, 0; 7200, 0; 10800, 0; 14400, 0;
              18000, 0; 21600, 0; 21600, 1000],
            columns={2});
        end M;
        """;

    [Fact]
    public void AWrappedMatrixArgumentIsAtItsSiblingsColumnWithItsRowsALevelIn()
    {
        // A matrix's lines are moved back to the level of the line that ends it (B462); the
        // line it begins on is already at its siblings' level (B467), so it is not moved again.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Modelica.Blocks.Sources.CombiTimeTable intGai(extrapolation=Periodic, table=[0, 0; 3600, 0; 7200, 0; 10800, 0; 14400, 0;
                    18000, 0; 21600, 0; 21600, 1000], columns={2});
                end M;
                """),
            expectedOutput: Normalise(MatrixArgumentWrapped),
            maxLineLength: 60);
    }

    [Fact]
    public void AModificationOnTheDeclarationsLineWrapsAsItAlwaysHas()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Real x(start=1,
                fixed=true,
                nominal(a=1111111111111111111111111, b=22222222222222222222222222222),
                min=0);
            end M;
            """));
    }

    [Theory]
    [InlineData(GraphicsLineWrapped, 100)]
    [InlineData(CallWrappedInAnExpression, 60)]
    [InlineData(FirstArgumentMovedOnAContinuationLine, 100)]
    [InlineData(NestedModificationsWrapped, 100)]
    [InlineData(NestedModificationAnArgumentALine, 40)]
    [InlineData(CallArgumentWhoseListWraps, 60)]
    [InlineData(MatrixArgumentWrapped, 60)]
    public void ASavedLayoutSavesBackUnchanged(string saved, int maxLineLength)
    {
        TestHelpers.AssertClass(Normalise(saved), maxLineLength: maxLineLength);
    }
}
