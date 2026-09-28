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
///
/// <para>B468 - an array's call elements that do not fit start a line each, at one column. Each
/// was written on the last line of the element before it, so B465's floor put each one's wrapped
/// arguments a level deeper than the last one's, and those lines ran to 170 characters: Buildings'
/// <c>DryCoil</c> had <c>sta={Stage(...), Stage(...), ...}</c> with <c>spe=</c> at 4, 6, 8 and 10.</para>
///
/// <para>B469 - an argument too long for its line, in a list written an argument a line anyway, is
/// at its siblings' column. It also took a wrapped argument's indent: MSL's
/// <c>ModelicaTest.Rotational</c> had a graphics <c>Text</c>'s <c>textString=</c> two spaces right
/// of <c>extent=</c> and <c>textColor=</c>.</para>
///
/// <para>B470 - an expression wrapped before a <c>+</c> or <c>-</c> continues a level in from the
/// level it is written at. The line was ended after the argument list it is in had gone back out,
/// so MSL's Media package had <c>+ reference_T</c> at the column of the <c>T=</c> it
/// continues.</para>
///
/// <para>B475 - the same after a wrapped <c>=</c>: the right-hand side's line carries its own
/// continuation indent, so a level in from the level it is written at was that line's column, and
/// Buildings' <c>TwoPortMatrixRLC</c> had the <c>+</c> under the <c>=</c>. It is a level in from
/// that line.</para>
///
/// <para>B476 - the graphics of an annotation that is not a long class's own, in an Icon or
/// Diagram written an argument a line, are laid out as a class annotation's are. They were not
/// recognised as graphics, so each element's arguments were written an argument a line at the
/// element's own column: MSL's <c>RealInput</c> had <c>fillColor=</c> under
/// <c>graphics={Polygon(</c>.</para>
///
/// <para>B474 - an array of calls still on the line it opened on is wrapped as B468 wraps one that
/// has already wrapped. Wrapping it inside a list's first argument would leave that argument over
/// more than one line, which B464 does not move, so the argument is moved first. Buildings' FLEXLAB
/// constructions and MSL's PumpingSystem had such arrays on one line of up to 600 characters.</para>
///
/// <para>B482 - a short class definition's description that does not fit starts a line of its own,
/// a level in, as a component's does. It was never measured, so once an array wrapped the closing
/// argument joined its short last line and the description ran past the limit.</para>
///
/// <para>B483 - an array of calls in a call's later positional argument moves that argument to a
/// line of its own before it wraps, as one in a first argument does (B474). Positional arguments are
/// never wrapped for length, so the array broke mid-list: MSL's <c>InitAngle</c> had
/// <c>{der(angle[1]),</c> at the end of one line and the rest of the array on the next.</para>
///
/// <para>B484 - an array with no argument to move, whose line is already past the limit when a call
/// in it has to wrap, moves to a line of its own. Buildings' IEEE 34-bus grid had
/// <c>cables={LowVoltageCables.PvcAl120(),</c> ending a 109-character line.</para>
///
/// <para>B485 - a term wrapped before a <c>+</c> or <c>-</c> inside an if-expression or an array is
/// a level further in than the statement's own continuation, which is where it was, reading as a
/// term of the whole right-hand side: MSL's <c>PolyphaseElectroMagneticConverter</c> had
/// <c>+ sTM[j, k].im*v[k].re for k in 1:m}));</c> at the column of any other continuation.</para>
///
/// <para>B487 - an if-expression that does not fit where it starts has each <c>elseif</c> and
/// <c>else</c> start a line, and a call's later positional argument that does not fit after its
/// <c>,</c> but does on a line of its own starts one. Both were wrapped only where the line ran out,
/// at a <c>+</c> or <c>-</c> mid-term: MSL's <c>PartialFriction</c> had <c>else if startBackward
/// then sa</c> ending one line and <c>+ tau0_max/unitTorque else if ...</c> starting the next.</para>
///
/// <para>B489 - shapes B487 left. A call's first positional argument that does not fit after its
/// <c>(</c> starts a line as a later one does: Buildings' gFunction ended a 116-character line with
/// <c>timeGeometric(tSho_min,</c>. A long logical expression wraps before an <c>or</c> or
/// <c>and</c> whose operand does not fit, as an arithmetic one does before a <c>+</c>: Buildings'
/// VerifyDifferenceThreePeriods had its if-expression's condition whole on one line and broke inside
/// <c>abs(u1 - u2)</c> instead. An argument after one whose if-expression started its branches on
/// lines of their own starts a line: MSL's Fluid.Machines wrote <c>homotopy</c>'s second argument
/// after the first's last branch, on its line. Whether a named argument in a statement fits after
/// its <c>,</c> is judged by its length as written, as a positional argument's is: MSL's
/// ReferenceMoistAir left <c>X=cat(1, X,</c> ending a line it did not fit.</para>
///
/// <para>B491 - shapes B489 left. A <c>+</c> or <c>-</c> inside a subscript is not wrapped:
/// Buildings' ElectricalLoad ended a line with <c>TOutFut_in_internal[m</c> and started the next
/// with <c>- 1]</c>. The condition of an if, elseif, when, elsewhen or while wraps in every branch,
/// before an <c>and</c> or <c>or</c> as well as a <c>+</c>, a level past what it guards: the
/// continuation was cleared by the first equation nested in it, so only the first branch's wrapped,
/// at the column of its body, and B489 left conditions unwrapped for that reason. A component's
/// binding wraps as an equation's right-hand side does, where a declaration's expression never
/// wrapped at an operator. An expression in parentheses too long for a line of its own wraps inside
/// them, a level in from the line its <c>(</c> is on, where nothing inside parentheses wrapped: MSL's
/// MassWithStopAndFriction had a 300-character <c>else (if ... )</c>.</para>
///
/// <para>B494 - shapes B491 left. A condition does not wrap after a first operand that is a lone
/// Boolean name: MSL's CombiTable1Ds ended a line with <c>if tableOnFile</c> and started the next
/// with <c>and fileName &lt;&gt; "NoName" ...</c>. The links of a polynomial in nested form are
/// wrapped at one column, one a line, where each stepped a level further in: MSL's IF97
/// <c>hlowerofp1</c> was a staircase eleven levels deep. A component's binding whose first line
/// would end past the limit starts a line of its own after its <c>=</c>, a level in: Buildings'
/// Templates heat pump had <c>cpSou_default=if ... then ...</c> at 152 characters.</para>
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
            T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X), X=X)
              else ThermodynamicState(p=p,
                T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X),
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

    private const string ArrayOfWrappedCalls = """
        model M
          parameter Data.DXCoil datCoi(sta={Some.Long.Package.Stage(
            spe=900/60,
            nomVal=Some.Long.Package.NominalValues(
              Q_flow_nominal=-12000, COP_nominal=3),
            perCur=Some.Long.Package.Curve_I()),
            Some.Long.Package.Stage(spe=1200/60,
              nomVal=Some.Long.Package.NominalValues(
                Q_flow_nominal=-18000, COP_nominal=3),
              perCur=Some.Long.Package.Curve_I()),
            Some.Long.Package.Stage(spe=1800/60,
              nomVal=Some.Long.Package.NominalValues(
                Q_flow_nominal=-21000, COP_nominal=3),
              perCur=Some.Long.Package.Curve_II())}, nSta=3);
        end M;
        """;

    [Fact]
    public void AnArraysCallElementsThatDoNotFitEachStartALineAtTheSameColumn()
    {
        // Buildings' DXSystems DryCoil: each Stage( followed the last line of the Stage before it,
        // so each one's wrapped arguments were a level deeper than the last one's (B468).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Data.DXCoil datCoi(sta={Some.Long.Package.Stage(spe=900/60, nomVal=Some.Long.Package.NominalValues(Q_flow_nominal=-12000, COP_nominal=3), perCur=Some.Long.Package.Curve_I()), Some.Long.Package.Stage(spe=1200/60, nomVal=Some.Long.Package.NominalValues(Q_flow_nominal=-18000, COP_nominal=3), perCur=Some.Long.Package.Curve_I()), Some.Long.Package.Stage(spe=1800/60, nomVal=Some.Long.Package.NominalValues(Q_flow_nominal=-21000, COP_nominal=3), perCur=Some.Long.Package.Curve_II())}, nSta=3);
                end M;
                """),
            expectedOutput: Normalise(ArrayOfWrappedCalls),
            maxLineLength: 60);
    }

    private const string ArrayOfCallsAnArgumentALine = """
        model M
          parameter Data cellData2(
            Qnom=18000,
            useLinearSOCDependency=false,
            Ri=cellData2.OCVmax/Isc,
            Idis=0.1,
            nRC=2,
            rcData={Some.Package.RCData(
              R=0.2*cellData2.Ri,
              C=60/(0.2*cellData2.Ri)
            ),
            Some.Package.RCData(
              R=0.1*cellData2.Ri,
              C=10/(0.1*cellData2.Ri)
            )}
          );
        end M;
        """;

    [Fact]
    public void AnArraysCallElementInsideAListWrittenAnArgumentALineStartsWhereTheOneBeforeEnded()
    {
        // MSL's Batteries.Examples.BatteryDischargeCharge: the elements' ')' is at the column the
        // list puts them at, so the next element starts there too rather than a level in.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Data cellData2(Qnom=18000, useLinearSOCDependency=false, Ri=cellData2.OCVmax/Isc, Idis=0.1, nRC=2, rcData={Some.Package.RCData(R=0.2*cellData2.Ri, C=60/(0.2*cellData2.Ri)), Some.Package.RCData(R=0.1*cellData2.Ri, C=10/(0.1*cellData2.Ri))});
                end M;
                """),
            expectedOutput: Normalise(ArrayOfCallsAnArgumentALine),
            maxLineLength: 60);
    }

    private const string ArrayElementsWrappedOrNot = """
        model M
          Real y;

        equation
          y = a
            + Some.Package.total(parts={Some.Package.part(first=1,
              second=2, third=3),
              Some.Package.part(first=4, second=5, third=6)});
          y = Some.Package.total(parts={Some.Package.part(first=1,
            second=2, third=3), Some.Long.Package.With.A.Longer.Name.constantValue, 1});
          y = Some.Package.total(parts={Some.Package.part(first=1,
            second=2, third=3),
            Some.Package.part(first=4, second=5000)});
          y = Some.Package.total(parts={Some.Package.part(first=1,
            second=2, third=3), Some.Package.part(first=4, second=500)});
        end M;
        """;

    [Fact]
    public void AnArraysCallElementIsWrappedAsAWrappedArgumentIsAndNothingElseIs()
    {
        // In order: an array opened on a continuation line keeps its wrapped element a level in
        // from that line (B465); an element that is not a call is not wrapped for length; and a
        // call is wrapped with the three-character margin a wrapped argument has - 58 characters
        // wrap at 60, 57 do not.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  y = a + Some.Package.total(parts={Some.Package.part(first=1, second=2, third=3), Some.Package.part(first=4, second=5, third=6)});
                  y = Some.Package.total(parts={Some.Package.part(first=1, second=2, third=3), Some.Long.Package.With.A.Longer.Name.constantValue, 1});
                  y = Some.Package.total(parts={Some.Package.part(first=1, second=2, third=3), Some.Package.part(first=4, second=5000)});
                  y = Some.Package.total(parts={Some.Package.part(first=1, second=2, third=3), Some.Package.part(first=4, second=500)});
                end M;
                """),
            expectedOutput: Normalise(ArrayElementsWrappedOrNot),
            maxLineLength: 60);
    }

    private const string AnnotationArraysOfCalls = """
        connector C
          Real x;

          annotation (
            defaultComponentName="port_n",
            Diagram(graphics={
              Text(
                extent={{-100, 100}, {100, 60}},
                textColor={255, 170, 85},
                textString="%name"
              ),
              Ellipse(
                extent={{-40, 40}, {40, -40}},
                lineColor={255, 170, 85},
                fillColor={255, 255, 255},
                fillPattern=FillPattern.Solid
              )
            }),
            Documentation(figures={Figure(title="Anti-windup", plots={Plot(
              title="Reference tracking", curves={Curve(y=integrator.y, legend="Reference speed")}),
              Plot(title="Anti-windup limiter", identifier="limiter")})})
          );
        end C;
        """;

    [Fact]
    public void AGraphicsArrayKeepsItsOwnLayoutAndADocumentationFiguresPlotsAreWrapped()
    {
        // A graphics annotation's elements are already a line each, at their own column (MSL
        // Magnetic.QuasiStatic.FundamentalWave.Interfaces.NegativeMagneticPort). A figure's plots
        // are wrapped like any other call's (MSL Blocks' UsersGuide): the second Plot( followed
        // the first's last line, a level deeper than it.
        TestHelpers.AssertClass(
            Normalise("""
                connector C
                  Real x;
                  annotation (defaultComponentName="port_n", Diagram(graphics={Text(extent={{-100, 100}, {100, 60}}, textColor={255, 170, 85}, textString="%name"), Ellipse(extent={{-40, 40}, {40, -40}}, lineColor={255, 170, 85}, fillColor={255, 255, 255}, fillPattern=FillPattern.Solid)}), Documentation(figures={Figure(title="Anti-windup", plots={Plot(title="Reference tracking", curves={Curve(y=integrator.y, legend="Reference speed")}), Plot(title="Anti-windup limiter", identifier="limiter")})}));
                end C;
                """),
            expectedOutput: Normalise(AnnotationArraysOfCalls),
            maxLineLength: 60);
    }

    private const string GraphicsElementWithALongArgument = """
        model M

          annotation (
            Diagram(graphics={
              Text(
                extent={{-80, 100}, {80, 60}},
                textColor={0, 0, 255},
                textString="Since the initialization was changed some elements here are redundant (e.g. inertia4, inertia5)."
              ),
              Text(
                extent={{10, 20}, {90, -30}},
                textColor={0, 0, 255},
                textString="These two parts are identical
        concerning structure, parameters
        and initialization."
              )
            })
          );
        end M;
        """;

    [Fact]
    public void ALongArgumentInAListWrittenAnArgumentALineIsAtItsSiblingsColumn()
    {
        // MSL's ModelicaTest.Rotational: a graphics element is written an argument a line anyway,
        // and a textString too long for the line also took the wrap-for-length indent, two spaces
        // right of extent= and textColor= (B469).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  annotation (Diagram(graphics={Text(extent={{-80, 100}, {80, 60}}, textColor={0, 0, 255}, textString="Since the initialization was changed some elements here are redundant (e.g. inertia4, inertia5)."), Text(extent={{10, 20}, {90, -30}}, textColor={0, 0, 255}, textString="These two parts are identical
                concerning structure, parameters
                and initialization.")}));
                end M;
                """),
            expectedOutput: Normalise(GraphicsElementWithALongArgument));
    }

    private const string ArgumentExpressionContinued = """
        function f
          input Real p;
          input Real h;
          output State state;

        algorithm
          state := ThermodynamicState(p=p,
            T=(h - reference_h - (p - reference_p)*((1 - beta_const*reference_T)/reference_d))/cp_const
              + reference_T);
        end f;
        """;

    [Fact]
    public void AnArgumentsExpressionContinuedOnTheNextLineIsALevelInFromTheArgument()
    {
        // MSL's Media package (SimpleMedium's setState_phX): '+ reference_T' continues T='s
        // expression, and was written at T='s own column (B470).
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Real p;
                  input Real h;
                  output State state;
                algorithm
                  state := ThermodynamicState(p=p, T=(h - reference_h - (p - reference_p)*((1 - beta_const*reference_T)/reference_d))/cp_const + reference_T);
                end f;
                """),
            expectedOutput: Normalise(ArgumentExpressionContinued));
    }

    private const string RightHandSideContinued = """
        model M
          Real v;

        equation
          terminal_n.phase[1].v - terminal_p.phase[1].v
              = productAC1p(Z11, terminal_n.phase[1].i) + productAC1p(Z12, terminal_n.phase[2].i)
                + productAC1p(Z13, terminal_n.phase[3].i);
          for i in 1:3 loop
            y[i] = productAC1p(Z11, terminal_n.phase[1].i) + productAC1p(Z12, terminal_n.phase[2].i)
              + productAC1p(Z13, terminal_n.phase[3].i);
          end for;
        end M;
        """;

    [Fact]
    public void ARightHandSideContinuedAfterAWrappedEqualsIsALevelInFromTheEqualsLine()
    {
        // Buildings' TwoPortMatrixRLC: the '+' continuing the right-hand side was at the column of
        // the '=' line it continues (B475), where one continuing an unwrapped equation is a level in.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real v;
                equation
                  terminal_n.phase[1].v - terminal_p.phase[1].v = productAC1p(Z11, terminal_n.phase[1].i) + productAC1p(Z12, terminal_n.phase[2].i) + productAC1p(Z13, terminal_n.phase[3].i);
                  for i in 1:3 loop
                    y[i] = productAC1p(Z11, terminal_n.phase[1].i) + productAC1p(Z12, terminal_n.phase[2].i) + productAC1p(Z13, terminal_n.phase[3].i);
                  end for;
                end M;
                """),
            expectedOutput: Normalise(RightHandSideContinued));
    }

    private const string ShortClassGraphics = """
        connector ModeTypeOutput = output Types.Mode "Output connector"
          annotation (
            defaultComponentName="y",
            Icon(
              coordinateSystem(
                preserveAspectRatio=true,
                extent={{-100.0, -100.0}, {100.0, 100.0}}
              ),
              graphics={
                Polygon(
                  lineColor={0, 127, 0},
                  fillColor={255, 255, 255},
                  fillPattern=FillPattern.Solid,
                  points={{-100.0, 100.0}, {100.0, 0.0}, {-100.0, -100.0}}
                )
              }
            ),
            Diagram(
              coordinateSystem(
                preserveAspectRatio=true,
                extent={{-100.0, -100.0}, {100.0, 100.0}}
              ),
              graphics={
                Polygon(
                  lineColor={0, 127, 0},
                  fillColor={255, 255, 255},
                  fillPattern=FillPattern.Solid,
                  points={{-100.0, 50.0}, {0.0, 0.0}, {-100.0, -50.0}}
                ),
                Text(
                  textColor={0, 0, 127},
                  extent={{30.0, 60.0}, {30.0, 110.0}},
                  textString="%name"
                )
              }
            )
          );
        """;

    [Fact]
    public void AShortClassDefinitionsGraphicsAreLaidOutAsALongClasssAre()
    {
        // Buildings' CHPs ModeTypeOutput, and MSL's RealInput and RealOutput: the annotation of a
        // short class definition is not the one a long class's composition carries, so its
        // graphics were not recognised and each element's arguments were written at the element's
        // own column (B476).
        TestHelpers.AssertClass(
            Normalise("""
                connector ModeTypeOutput = output Types.Mode "Output connector" annotation (defaultComponentName="y", Icon(coordinateSystem(preserveAspectRatio=true, extent={{-100.0,-100.0},{100.0,100.0}}), graphics={Polygon(lineColor={0,127,0}, fillColor={255,255,255}, fillPattern=FillPattern.Solid, points={{-100.0,100.0},{100.0,0.0},{-100.0,-100.0}})}), Diagram(coordinateSystem(preserveAspectRatio=true, extent={{-100.0,-100.0},{100.0,100.0}}), graphics={Polygon(lineColor={0,127,0}, fillColor={255,255,255}, fillPattern=FillPattern.Solid, points={{-100.0,50.0},{0.0,0.0},{-100.0,-50.0}}), Text(textColor={0,0,127}, extent={{30.0,60.0},{30.0,110.0}}, textString="%name")}));
                """),
            expectedOutput: Normalise(ShortClassGraphics));
    }

    private const string ComponentGraphics = """
        model M
          Real x
            annotation (Icon(
              coordinateSystem(extent={{-100, -100}, {100, 100}}),
              graphics={
                Polygon(
                  lineColor={0, 127, 0},
                  fillColor={255, 255, 255},
                  fillPattern=FillPattern.Solid,
                  points={{-100, 50}, {0, 0}, {-100, -50}}
                )
              }
            ));
          Real y
            annotation (Icon(graphics={Polygon(lineColor={0, 127, 0}, fillColor={255, 255, 255},
              fillPattern=FillPattern.Solid)}));
        end M;
        """;

    [Fact]
    public void AComponentsGraphicsInAnIconWrittenAnArgumentALineAreLaidOutAsTheClasssAre()
    {
        // The same in a component's annotation (B476). An Icon on one line keeps the graphics
        // wrapped for length, and the next component's annotation is not taken for the class's.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real x annotation (Icon(coordinateSystem(extent={{-100,-100},{100,100}}), graphics={Polygon(lineColor={0,127,0}, fillColor={255,255,255}, fillPattern=FillPattern.Solid, points={{-100,50},{0,0},{-100,-50}})}));
                  Real y annotation (Icon(graphics={Polygon(lineColor={0,127,0}, fillColor={255,255,255}, fillPattern=FillPattern.Solid)}));
                end M;
                """),
            expectedOutput: Normalise(ComponentGraphics));
    }

    private const string ArraysWrappedFromTheirOpeningLine = """
        model M
          record R = Some.Package.Generic(
            final material={Solids.Insulation(x=0.08255),
              Solids.Plywood(x=0.0127), Solids.Gypsum(x=0.01588)},
            final nLay=3) "South wall";
          Some.OpenTank tank(
            crossArea=0.2,
            nPorts=3,
            height=20,
            level_start=2,
            use_portsData=true,
            portsData={Vessels.PortsData(diameter=0.1),
              Vessels.PortsData(diameter=0.1),
              Vessels.PortsData(
                diameter=0.1,
                height=6
              )}
          );
          Some.OpenTank tank2(
            crossArea=0.2,
            nPorts=3,
            height=20,
            level_start=2,
            use_portsData=true,
            portsData={P(d=1), P(d=2),
              Vessels.PortsData(
                diameter=0.1,
                height=6
              )}
          );
          M r(a=1, n_y={f(a), f(b), f(c), f(d), f(e), f(g), f(h),
            f(i)});
          Some.Fixed.Rotation crankAngle2(
            n_y={0, Math.cos(aa), Math.sin(aa)}, animation=false);
          Some.Fixed crankAngle1(
            n_y={0, Math.cos(crankAngleOffset),
              Math.sin(crankAngleOffset)}, animation=false);

        algorithm
          residue := {Math.atan2(cross(R1[1, :], R1[2, :])*R2[2, :], R1[1, :]*R2[1, :]),
            Math.atan2(R1[2, :]*R2[1, :], R1[3, :]*R2[3, :])};
        end M;
        """;

    [Fact]
    public void AnArrayOfCallsStillOnTheLineItOpenedOnIsWrappedForLength()
    {
        // In order: Buildings' FLEXLAB constructions - the list's first argument is moved to a
        // line of its own before its array wraps, since once it spans lines it could not be moved
        // (B464) and the record's first line ran past the limit; MSL's Tanks, twice - in a list
        // written an argument a line the wrapped elements are a level in, and so is the ')' of one
        // written an argument a line, whether or not an element before it wrapped; an array in a
        // later argument does not move the first; an array that fits once its argument has moved
        // is not wrapped, and one that does not wraps from the moved line; and an array with no
        // argument to move wraps from the statement's line (MSL MultiBody's Frames.Orientation).
        // Each array was left on the line it opened on, up to 600 characters long (B474).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  record R = Some.Package.Generic(final material={Solids.Insulation(x=0.08255), Solids.Plywood(x=0.0127), Solids.Gypsum(x=0.01588)}, final nLay=3) "South wall";
                  Some.OpenTank tank(crossArea=0.2, nPorts=3, height=20, level_start=2, use_portsData=true, portsData={Vessels.PortsData(diameter=0.1), Vessels.PortsData(diameter=0.1), Vessels.PortsData(diameter=0.1, height=6)});
                  Some.OpenTank tank2(crossArea=0.2, nPorts=3, height=20, level_start=2, use_portsData=true, portsData={P(d=1), P(d=2), Vessels.PortsData(diameter=0.1, height=6)});
                  M r(a=1, n_y={f(a),f(b),f(c),f(d),f(e),f(g),f(h),f(i)});
                  Some.Fixed.Rotation crankAngle2(n_y={0, Math.cos(aa), Math.sin(aa)}, animation=false);
                  Some.Fixed crankAngle1(n_y={0, Math.cos(crankAngleOffset), Math.sin(crankAngleOffset)}, animation=false);
                algorithm
                  residue := {Math.atan2(cross(R1[1, :], R1[2, :])*R2[2, :], R1[1, :]*R2[1, :]), Math.atan2(R1[2, :]*R2[1, :], R1[3, :]*R2[3, :])};
                end M;
                """),
            expectedOutput: Normalise(ArraysWrappedFromTheirOpeningLine),
            maxLineLength: 60);
    }

    private const string ShortClassDescriptionsWrapped = """
        model M
          record R = Buildings.HeatTransfer.Data.OpaqueConstructions.Generic(
            final material={Buildings.HeatTransfer.Data.Solids.InsulationBoard(x=0.08255),
              Buildings.HeatTransfer.Data.Solids.Plywood(x=0.0127),
              Buildings.HeatTransfer.Data.Solids.GypsumBoard(x=0.01588)}, final nLay=3)
            "South wall in test bed X2";
          type T = Real(final quantity="Temp", final unit="K")
            "A description that is long enough to wrap onto a line of its own"
            annotation (absoluteValue=true);
          replaceable package Medium = Some.Media.Water "Medium in the system";
          type E = enumeration(
            a "First",
            b "Second"
          ) "Enumeration defining in which way the fixed orientation of frame_b with respect to frame_a is given";
        end M;
        """;

    [Fact]
    public void AShortClassDescriptionThatDoesNotFitStartsALineOfItsOwn()
    {
        // Buildings' FLEXLAB Construction2: once the array wrapped (B474) the closing argument
        // joined its short last line, and the description after the ')' took that line to 105
        // characters. A short class's description was never measured; a component's always was,
        // and is moved to a line of its own, a level in (B482). One that fits stays, and so does an
        // enumeration's, whose ')' starts its line and would be left alone on it.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  record R = Buildings.HeatTransfer.Data.OpaqueConstructions.Generic(final material={Buildings.HeatTransfer.Data.Solids.InsulationBoard(x=0.08255), Buildings.HeatTransfer.Data.Solids.Plywood(x=0.0127), Buildings.HeatTransfer.Data.Solids.GypsumBoard(x=0.01588)}, final nLay=3) "South wall in test bed X2";
                  type T = Real(final quantity="Temp", final unit="K") "A description that is long enough to wrap onto a line of its own" annotation(absoluteValue=true);
                  replaceable package Medium = Some.Media.Water "Medium in the system";
                  type E = enumeration(a "First", b "Second") "Enumeration defining in which way the fixed orientation of frame_b with respect to frame_a is given";
                end M;
                """),
            expectedOutput: Normalise(ShortClassDescriptionsWrapped));
    }

    private const string PositionalArraysMovedWhole = """
        model M
          Real R_rel;

        equation
          R_rel = Frames.axesRotations(sequence_start, {angle[1], angle[2], angle[3]},
            {der(angle[1]), der(angle[2]), der(angle[3])});
          when initial() then
            x = 1;
          elsewhen newInput({T, X, mInlets_flow, QGaiRad_flow, TAveInlet},
            {pre(TLast), pre(XLast), pre(mInlets_flowLast), pre(QGaiRad_flowLast), pre(TAveInletLast)}) then
            x = 2;
          end when;
          y = f(a,
            {g(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa), g(bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb),
              g(ccccccccccccccccccccccccccccccccccccccccc), g(ddddddddddddddddddddddddddddd)});
        end M;
        """;

    [Fact]
    public void AnArrayOfCallsInALaterPositionalArgumentMovesTheArgumentBeforeWrapping()
    {
        // MSL's MultiBody.Joints.Internal.InitAngle and Buildings' EnergyPlus RoomModel: a call's
        // positional arguments are never wrapped for length, so only the array broke, after its
        // first element - '{der(angle[1]),' at the end of one line and the rest of the array on the
        // next. The argument now moves to a line of its own first, as a first argument does (B474),
        // and its array wraps from there only if it still does not fit (B483).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real R_rel;
                equation
                  R_rel = Frames.axesRotations(sequence_start, {angle[1], angle[2], angle[3]}, {der(angle[1]), der(angle[2]), der(angle[3])});
                  when initial() then
                    x = 1;
                  elsewhen newInput({T, X, mInlets_flow, QGaiRad_flow, TAveInlet}, {pre(TLast), pre(XLast), pre(mInlets_flowLast), pre(QGaiRad_flowLast), pre(TAveInletLast)}) then
                    x = 2;
                  end when;
                  y = f(a, {g(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa), g(bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb), g(ccccccccccccccccccccccccccccccccccccccccc), g(ddddddddddddddddddddddddddddd)});
                end M;
                """),
            expectedOutput: Normalise(PositionalArraysMovedWhole));
    }

    private const string ArraysMovedOffALongLine = """
        record G "Grid"
          extends Buildings.Electrical.Transmission.Grids.PartialGrid(
            nNodes=34,
            nLinks=33,
            l=[48; 16],
            redeclare Buildings.Electrical.Transmission.LowVoltageCables.Generic cables=
              {LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(),
                LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl70(), LowVoltageCables.PvcAl35()}
          );
          parameter Real[3] cL=
            {(Modelica.Math.log(k0) - b - a)/yL^2, (-b*yL - 2*Modelica.Math.log(k0) + 2*b + 2*a)/yL,
              Modelica.Math.log(k0)} "Polynomial coefficients";
          parameter String filNam[2]=
            {Modelica.Utilities.Files.loadResource("modelica://Buildings/Resources/Data/DHC/Loads/Examples/MediumOffice.mos"),
              Modelica.Utilities.Files.loadResource("modelica://Buildings/Resources/Data/DHC/Loads/Examples/MediumOffice.mos")};
          parameter Real x[3]={ // comment
            Some.Long.Package.Function.name(aaaaaaaaaaaaaaaaaaaaaaaaa,
              bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb), Other.fn(ccccccccccccccccc),
              Other.fn(dddddddddd)};
        end G;
        """;

    [Fact]
    public void AnArrayWithNoArgumentToMoveIsMovedOffALineItHasTakenPastTheLimit()
    {
        // Buildings' IEEE_34_AL120: the array is in a later argument of a list written an argument
        // a line, so there is no first argument to move (B474) and its argument already starts its
        // line; wrapping only the later elements left 'cables={LowVoltageCables.PvcAl120(),' at
        // 109 characters. The array moves to a line of its own after the '=' (B484) - as
        // Buildings' PartialDamperExponential 'cL=' does - but not when what it has written would
        // not fit there either: a string that long cannot be helped by moving it, though as a
        // component's binding whose first line is past the limit it is moved whole (B494). Nor when a
        // comment after the '{' has ended the line the array opened on - where, in a component's
        // binding, a call's positional argument that does not fit starts a line (B487, B491).
        TestHelpers.AssertClass(
            Normalise("""
                record G "Grid"
                  extends Buildings.Electrical.Transmission.Grids.PartialGrid(
                    nNodes=34,
                    nLinks=33,
                    l=[48; 16],
                    redeclare Buildings.Electrical.Transmission.LowVoltageCables.Generic cables={LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl120(), LowVoltageCables.PvcAl70(), LowVoltageCables.PvcAl35()});
                  parameter Real[3] cL={(Modelica.Math.log(k0) - b - a)/yL^2, (-b*yL - 2*Modelica.Math.log(k0) + 2*b + 2*a)/yL, Modelica.Math.log(k0)} "Polynomial coefficients";
                  parameter String filNam[2]={Modelica.Utilities.Files.loadResource("modelica://Buildings/Resources/Data/DHC/Loads/Examples/MediumOffice.mos"), Modelica.Utilities.Files.loadResource("modelica://Buildings/Resources/Data/DHC/Loads/Examples/MediumOffice.mos")};
                  parameter Real x[3]={ // comment
                    Some.Long.Package.Function.name(aaaaaaaaaaaaaaaaaaaaaaaaa, bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb), Other.fn(ccccccccccccccccc), Other.fn(dddddddddd)};
                end G;
                """),
            expectedOutput: Normalise(ArraysMovedOffALongLine));
    }

    private const string BranchAndArrayTermsContinued = """
        model M
          Real y;

        equation
          p1.i = if control then s1*unitVoltage*Goff + s3*unitCurrent
              else s1*unitCurrent + s3*unitVoltage*Goff;
          y = Complex(sum({sTM[j, k].re*v[k].re - sTM[j, k].im*v[k].im for k in 1:m}),
            sum({sTM[j, k].re*v[k].im + sTM[j, k].im*v[k].re for k in 1:m}));
          w = sum({sTM[j, k].re*v[k].re*aaaaaaaaaaaaaaaaaaaa
              - sTM[j, k].im*v[k].im*bbbbbbbbbbbbbbbbbbbbbbbbb + cccccccccccccccccccc for k in 1:m});
          i = smooth(1, if (v > Maxexp*Vt) then Ids*(exp(Maxexp)*(1 + v/Vt - Maxexp) - 1) + v/R
              else if ((v + Bv) < -Maxexp*(Nbv*Vt)) then -Ids - Ibv*exp(Maxexp)*(1 - (v + Bv)/(Nbv*Vt) - Maxexp)
                + v/R
              else Ids*(exp(v/Vt) - 1) - Ibv*exp(-(v + Bv)/(Nbv*Vt)) + v/R);
          z = if flag then 0
              else Modelica.Math.exp(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*bbbbbbbbbbbbbbbbbbbbbbb
                + ccccccccccccccccccccccccccccccc*dddddddd);
          assert(s_rel >= -1e-12, "flange_b.s - flange_a.s (= " + String(s_rel, significantDigits=14)
            + ") >= 0 required for GasForce2 component.\n" + "Most likely, the component has to be flipped.");
        end M;
        """;

    [Fact]
    public void ATermWrappedInsideAnIfExpressionOrAnArrayIsALevelInFromTheStatementsContinuation()
    {
        // MSL's IdealIntermediateSwitch, PolyphaseElectroMagneticConverter and ZDiode: a term
        // wrapped before a '+' or '-' inside an if-expression's branch, or inside an array, was at
        // the statement's own continuation column, where it read as a term of the whole right-hand
        // side - '+ s3*unitVoltage*Goff' under the 'if', not in the else-branch it continues. It is
        // a level further in (B485), and so is one inside a call inside a branch. A term inside
        // a call's parentheses only is not: an assert's message continues at the statement's
        // column as before. Since B487 the if-expressions here break at their 'else' first, and a
        // term wrapped inside a branch that starts a line is a level in from the 'else' too.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  p1.i = if control then s1*unitVoltage*Goff + s3*unitCurrent else s1*unitCurrent + s3*unitVoltage*Goff;
                  y = Complex(sum({sTM[j, k].re*v[k].re - sTM[j, k].im*v[k].im for k in 1:m}), sum({sTM[j, k].re*v[k].im + sTM[j, k].im*v[k].re for k in 1:m}));
                  w = sum({sTM[j, k].re*v[k].re*aaaaaaaaaaaaaaaaaaaa - sTM[j, k].im*v[k].im*bbbbbbbbbbbbbbbbbbbbbbbbb + cccccccccccccccccccc for k in 1:m});
                  i = smooth(1, if (v > Maxexp*Vt) then Ids*(exp(Maxexp)*(1 + v/Vt - Maxexp) - 1) + v/R else if ((v + Bv) < -Maxexp*(Nbv*Vt)) then -Ids - Ibv*exp(Maxexp)*(1 - (v + Bv)/(Nbv*Vt) - Maxexp) + v/R else Ids*(exp(v/Vt) - 1) - Ibv*exp(-(v + Bv)/(Nbv*Vt)) + v/R);
                  z = if flag then 0 else Modelica.Math.exp(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*bbbbbbbbbbbbbbbbbbbbbbb + ccccccccccccccccccccccccccccccc*dddddddd);
                  assert(s_rel >= -1e-12, "flange_b.s - flange_a.s (= " + String(s_rel, significantDigits=14) + ") >= 0 required for GasForce2 component.\n" + "Most likely, the component has to be flipped.");
                end M;
                """),
            expectedOutput: Normalise(BranchAndArrayTermsContinued));
    }

    private const string BranchesAndArgumentsStartLines = """
        model M
          Real y;

        equation
          a_relfric/unitAngularAcceleration
              = if locked then 0
                  else if free then sa
                  else if startForward then sa - tau0_max/unitTorque
                  else if startBackward then sa + tau0_max/unitTorque
                  else if pre(mode) == Forward then sa - tau0_max/unitTorque
                  else if pre(mode) == Backward then sa + tau0_max/unitTorque
                  else sa - sign(w_relfric)*tau0_max/unitTorque;
          stopped = if s <= smin + L/2 then -1 else if s >= smax - L/2 then +1 else 0;
          for j in 1:m loop
            vSymmetricalComponent[j]
                = Complex(sum({sTM[j, k].re*v[k].re - sTM[j, k].im*v[k].im for k in 1:m}),
                  sum({sTM[j, k].re*v[k].im + sTM[j, k].im*v[k].re for k in 1:m}));
          end for;
          mEva_flow = -dX*smooth(1, noEvent(Buildings.Utilities.Math.Functions.spliceFunction(
            pos=if abs(mAir_flow) > mAir_flow_small/3 then abs(mAir_flow)*(1 - Modelica.Math.exp(-K2*m*abs(mAir_flow)^(-0.2))) else 0,
            neg=K2*mAir_flow_small^(-0.2)*m*mAir_flow^2, x=abs(mAir_flow) - 2*mAir_flow_small/3,
            deltax=mAir_flow_small/3)));
          assert(p > triple.ptriple, "IF97 medium function boundary23ofp called with too low pressure\n"
            + "p = " + String(p) + " Pa <= " + String(triple.ptriple) + " Pa (triple point pressure)");
          assert(abs(flowCharacteristics.y[size(flowCharacteristics.y, 1)] - 1) < Modelica.Constants.eps,
            "flowCharateristics.y[end] must be 1.");
          assert(noEvent(length2_n2_a > 1e-10) and some_other_condition_long_enough_to_matter, "
        The length of axis vector n is too small");
          y = if u > uMax then uMax + kkkkkkkkkkkkkkkkkkkkkkk*(u - uMax) + mmmmmmmmmmm*(u - uMax)^2
              else if u < uMin then uMin
              else u;
          fstatus[2] = if IN_con.target == TYP.UndevOne or IN_con.target == TYP.UndevBoth then if Pr > prandtlMax or Pr < prandtlMin then 1 else 0
              else 0;
          q = if flag then 0
              else if other then if c then aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
                + cccccccccccccccccccccccccccc*ddddddddddddddd else 1
              else 2;
        end M;
        """;

    private const string BranchesStartLinesInAFunction = """
        function f
          input Real p;
          output State state;

        algorithm
          d := smooth(1, if state.T < 278.15 then -0.042860825*state.T + 1011.9695761
              elseif state.T < 373.15 then 0.000015009*state.T^3 - 0.01813488505*state.T^2
                + 6.5619527954075*state.T + 254.900074971947
              else -0.7025109*state.T + 1220.35045233);
          R := Orientation(
            T=TM.axisRotation(sequence[3], angles[3])*TM.axisRotation(sequence[2], angles[2])*TM.axisRotation(sequence[1], angles[1]),
            w=zeros(3));
        end f;
        """;

    [Fact]
    public void ALongIfExpressionBreaksAtItsBranchesAndACallBetweenItsArguments()
    {
        // In order: MSL's Rotational PartialFriction - an if-expression that does not fit starts
        // each 'else if' on a line of its own, a level in from the statement's continuation, where
        // it was wrapped only where the line ran out, mid-term ('else if startBackward then sa' then
        // '+ tau0_max/unitTorque else if ...'); one that fits stays on its line;
        // PolyphaseElectroMagneticConverter - a call's later positional argument that does not fit
        // after the ',' starts a line of its own, a level in from the line the call opened on,
        // rather than wrapping inside it at a '+'; Buildings' Evaporation - nothing inside a first
        // argument that can still be moved to a line of its own (B464) ends a line, since the
        // argument would then not be moved; an argument that would not fit on a line of its own
        // either stays where it is and wraps as before; a short one after a long first argument
        // starts a line; one holding a string written over several lines stays where it is; and an
        // 'else if' whose rest would fit after the 'else' still starts each branch a line, as the
        // chain it continues does, while MSL Dissipation's if-expression in another's 'then' does
        // not break at all, since its 'else' would start a line at the column of the outer one's;
        // a term wrapped inside such a one, in a branch that starts a line, is a level in from it.
        // Buildings' TemperatureDependentDensity - 'elseif' breaks as 'else if' does, and a
        // branch too long for its line still wraps at a '+', a level in from the 'elseif'. And
        // MSL's axesRotations: a later argument inside a first argument that can still be moved
        // does not start a line (B487).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  a_relfric/unitAngularAcceleration = if locked then 0 else if free then sa else if startForward then sa - tau0_max/unitTorque else if startBackward then sa + tau0_max/unitTorque else if pre(mode) == Forward then sa - tau0_max/unitTorque else if pre(mode) == Backward then sa + tau0_max/unitTorque else sa - sign(w_relfric)*tau0_max/unitTorque;
                  stopped = if s <= smin + L/2 then -1 else if s >= smax - L/2 then +1 else 0;
                  for j in 1:m loop
                    vSymmetricalComponent[j] = Complex(sum({sTM[j,k].re*v[k].re - sTM[j,k].im*v[k].im for k in 1:m}), sum({sTM[j,k].re*v[k].im + sTM[j,k].im*v[k].re for k in 1:m}));
                  end for;
                  mEva_flow = -dX*smooth(1, noEvent(Buildings.Utilities.Math.Functions.spliceFunction(pos=if abs(mAir_flow) > mAir_flow_small/3 then abs(mAir_flow)*(1 - Modelica.Math.exp(-K2*m*abs(mAir_flow)^(-0.2))) else 0, neg=K2*mAir_flow_small^(-0.2)*m*mAir_flow^2, x=abs(mAir_flow) - 2*mAir_flow_small/3, deltax=mAir_flow_small/3)));
                  assert(p > triple.ptriple, "IF97 medium function boundary23ofp called with too low pressure\n" + "p = " + String(p) + " Pa <= " + String(triple.ptriple) + " Pa (triple point pressure)");
                  assert(abs(flowCharacteristics.y[size(flowCharacteristics.y, 1)] - 1) < Modelica.Constants.eps, "flowCharateristics.y[end] must be 1.");
                  assert(noEvent(length2_n2_a > 1e-10) and some_other_condition_long_enough_to_matter, "
                The length of axis vector n is too small");
                  y = if u > uMax then uMax + kkkkkkkkkkkkkkkkkkkkkkk*(u - uMax) + mmmmmmmmmmm*(u - uMax)^2 else if u < uMin then uMin else u;
                  fstatus[2] = if IN_con.target == TYP.UndevOne or IN_con.target == TYP.UndevBoth then if Pr > prandtlMax or Pr < prandtlMin then 1 else 0 else 0;
                  q = if flag then 0 else if other then if c then aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb + cccccccccccccccccccccccccccc*ddddddddddddddd else 1 else 2;
                end M;
                """),
            expectedOutput: Normalise(BranchesAndArgumentsStartLines));
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Real p;
                  output State state;
                algorithm
                  d := smooth(1, if state.T < 278.15 then -0.042860825*state.T + 1011.9695761 elseif state.T < 373.15 then 0.000015009*state.T^3 - 0.01813488505*state.T^2 + 6.5619527954075*state.T + 254.900074971947 else -0.7025109*state.T + 1220.35045233);
                  R := Orientation(T=TM.axisRotation(sequence[3], angles[3])*TM.axisRotation(sequence[2], angles[2])*TM.axisRotation(sequence[1], angles[1]), w=zeros(3));
                end f;
                """),
            expectedOutput: Normalise(BranchesStartLinesInAFunction));
    }

    private const string FirstPositionalArgumentsStartLines = """
        function f
          input Real p;
          output Real y;

        algorithm
          tSho := Buildings.Fluid.Geothermal.Borefields.BaseClasses.HeatTransfer.ThermalResponseFactors.timeGeometric(
            tSho_min, tSho_max, nTimSho);
          (RDelta, R) := Buildings.Fluid.Geothermal.Borefields.BaseClasses.Boreholes.BaseClasses.Functions.multipoleThermalResistances(
            2, 3, xPip, yPip, rBor, rPip, kFil, kSoi, RFluPip);
          y := Modelica.Math.Matrices.solve(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb);
          y := Some.Package.Name.fn(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,
            b);
          lambda := Modelica.Media.IdealGases.Common.Functions.thermalConductivityEstimateWithALongerName(
            specificHeatCapacityCp(state), method=method, data=data);
          y := Buildings.Fluid.Geothermal.Borefields.BaseClasses.HeatTransfer.ThermalResponseFactors.timeGeometric(t);
        end f;
        """;

    [Fact]
    public void AFirstPositionalArgumentThatDoesNotFitAfterItsParenthesisStartsALine()
    {
        // In order: Buildings' gFunction and multipoleThermalResistances - a call's first positional
        // argument that does not fit after its '(', but does on a line of its own, starts one, a
        // level in from the line the call opened on, where it was left at the end of a line already
        // past the limit and the next argument started the line instead ('(2,' then '3, xPip, ...');
        // one that fits after the '(' stays there, one too long for a line of its own stays after
        // the '(' too, and so does the only argument of a call; one followed only by named
        // arguments starts a line as one followed by positional ones does (B489).
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Real p;
                  output Real y;
                algorithm
                  tSho := Buildings.Fluid.Geothermal.Borefields.BaseClasses.HeatTransfer.ThermalResponseFactors.timeGeometric(tSho_min, tSho_max, nTimSho);
                  (RDelta, R) := Buildings.Fluid.Geothermal.Borefields.BaseClasses.Boreholes.BaseClasses.Functions.multipoleThermalResistances(2, 3, xPip, yPip, rBor, rPip, kFil, kSoi, RFluPip);
                  y := Modelica.Math.Matrices.solve(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb);
                  y := Some.Package.Name.fn(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, b);
                  lambda := Modelica.Media.IdealGases.Common.Functions.thermalConductivityEstimateWithALongerName(specificHeatCapacityCp(state), method=method, data=data);
                  y := Buildings.Fluid.Geothermal.Borefields.BaseClasses.HeatTransfer.ThermalResponseFactors.timeGeometric(t);
                end f;
                """),
            expectedOutput: Normalise(FirstPositionalArgumentsStartLines));
    }

    private const string LogicalOperatorsStartLines = """
        model M
          Real y;

        equation
          diff = if (time >= t0) and (time < t1) or (time >= t2) and (time < t3)
                or (time >= t4) and (time < t5) then abs(u1 - u2)
              else 0;
          startForward = pre(mode) == Stuck
            and (sa > tau0_max/unitTorque or pre(startForward) and sa > tau0/unitTorque)
            or pre(mode) == Backward and w_relfric > w_small or initial() and (w_relfric > 0);
          newActive = activeSteps > 0 and not Modelica.Math.BooleanVectors.anyTrue(suspend.reset)
            and not outerState.subgraphStatePort.suspend
            or Modelica.Math.BooleanVectors.anyTrue(resume.set) or outerState.subgraphStatePort.resume;
          assert(IN_con.geometry == TYP.PlainFin or IN_con.geometry == TYP.LouverFin
            or IN_con.geometry == TYP.SlitFin or IN_con.geometry == TYP.WavyFin,
            "Unknown choice of geometry is selected");
          fstatus[2] = if IN_con.target == TYP.UndevOne or IN_con.target == TYP.UndevBoth then if Pr > prandtlMax or Pr < prandtlMin then 1 else 0
              else 0;
          ok = aaaaaaaaaaaaaaaaaaaaaaa or bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb;
          if (p < triple.ptriple) or (p > data.PLIMIT1) or (h < hlowerofp1(p))
              or ((p < 10.0e6) and (h > hupperofp5(p))) then
            y = 1;
          end if;
          connect(valIso.port_bChiWat, inlPumChiWatPri.port_a)
            annotation (Line(
              points={{-60, 80}, {-40, 80}},
              color={0, 0, 0},
              visible=have_chiWat and typArrPumPri == Buildings.Templates.Components.Types.PumpArrangement.Headered
            ));
        end M;
        """;

    private const string LogicalOperatorsInAFunction = """
        function f
          input Boolean b=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb and ccccccccc;
          output Rec r;

        algorithm
          r := Some.Package.Record(
            isOn=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb and ccccccccccccccccc,
            y=1);
          startForward := pre(mode) == Stuck and (sa > f0_max/unitForce and s < (smax - L/2)
              or pre(startForward) and sa > f0/unitForce and s < (smax - L/2))
            or pre(mode) == Backward and v_relfric > v_small;
        end f;
        """;

    [Fact]
    public void ALongLogicalExpressionWrapsBeforeItsOrAndAnd()
    {
        // In order: Buildings' VerifyDifferenceThreePeriods - an if-expression's condition that does
        // not fit wraps before the 'or' whose term does not fit, where it was never wrapped and the
        // line broke inside 'abs(u1 - u2)'; MSL's PartialFriction - a Boolean right-hand side wraps
        // before 'and' and 'or' the same way; StateGraph - the 'or' after a term that has wrapped
        // starts a line, so what it joins is not read as part of the 'and' before it; Dissipation - an
        // 'or' whose term fits after it stays; an if-expression inside another's 'then' does not wrap
        // its condition (B487); a term too long for a line of its own stays where it is; an
        // if-equation's condition wraps a level past the equations it guards (B491); an annotation's
        // does not wrap. In a function: a declaration's binding wraps as an equation does (B491);
        // nothing inside a first argument that may still be moved to a line of its own
        // wraps (B464); parentheses too long for a line of their own wrap inside, a level in from
        // their '(' line (B491).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  diff = if (time >= t0) and (time < t1) or (time >= t2) and (time < t3) or (time >= t4) and (time < t5) then abs(u1 - u2) else 0;
                  startForward = pre(mode) == Stuck and (sa > tau0_max/unitTorque or pre(startForward) and sa > tau0/unitTorque) or pre(mode) == Backward and w_relfric > w_small or initial() and (w_relfric > 0);
                  newActive = activeSteps > 0 and not Modelica.Math.BooleanVectors.anyTrue(suspend.reset) and not outerState.subgraphStatePort.suspend or Modelica.Math.BooleanVectors.anyTrue(resume.set) or outerState.subgraphStatePort.resume;
                  assert(IN_con.geometry == TYP.PlainFin or IN_con.geometry == TYP.LouverFin or IN_con.geometry == TYP.SlitFin or IN_con.geometry == TYP.WavyFin, "Unknown choice of geometry is selected");
                  fstatus[2] = if IN_con.target == TYP.UndevOne or IN_con.target == TYP.UndevBoth then if Pr > prandtlMax or Pr < prandtlMin then 1 else 0 else 0;
                  ok = aaaaaaaaaaaaaaaaaaaaaaa or bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb;
                  if (p < triple.ptriple) or (p > data.PLIMIT1) or (h < hlowerofp1(p)) or ((p < 10.0e6) and (h > hupperofp5(p))) then
                    y = 1;
                  end if;
                  connect(valIso.port_bChiWat, inlPumChiWatPri.port_a) annotation (Line(points={{-60, 80}, {-40, 80}}, color={0, 0, 0}, visible=have_chiWat and typArrPumPri == Buildings.Templates.Components.Types.PumpArrangement.Headered));
                end M;
                """),
            expectedOutput: Normalise(LogicalOperatorsStartLines));
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Boolean b = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb and ccccccccc;
                  output Rec r;
                algorithm
                  r := Some.Package.Record(isOn=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb and ccccccccccccccccc, y=1);
                  startForward := pre(mode) == Stuck and (sa > f0_max/unitForce and s < (smax - L/2) or pre(startForward) and sa > f0/unitForce and s < (smax - L/2)) or pre(mode) == Backward and v_relfric > v_small;
                end f;
                """),
            expectedOutput: Normalise(LogicalOperatorsInAFunction));
    }

    private const string ArgumentsAfterBranchesStartLines = """
        function f
          input Real p;
          output State state;

        algorithm
          head := homotopy(if s > 0 then (N/N_nominal)^2*flowCharacteristic(V_flow_single*N_nominal/N)
              else (N/N_nominal)^2*flowCharacteristic(0) - s*unitHead,
            if checkValveHomotopy == Types.CheckValveHomotopyType.Open then N/N_nominal*flowCharacteristic(V_flow_single_init)
                else N/N_nominal*flowCharacteristic(0) - s*unitHead);
          state := ThermodynamicState(d=density_ph(p, h, region=region),
            phase=if region == 0 then 0
                else if region == 4 then 2
                else if regionnnnnnnnnnnnnnnnnnnn == 5 then 3
                else 1,
            h=h, p=p);
          y := smooth(1, if xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx > 0 then aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
              else bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb,
            x=1);
          z := if flag then g(1, 2)
              else h(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, c);
        end f;
        """;

    [Fact]
    public void AnArgumentAfterAnIfExpressionThatBrokeItsBranchesStartsALine()
    {
        // In order: MSL's Fluid.Machines - the positional argument after one whose if-expression
        // started its branches on lines of their own starts a line, where it followed the last
        // branch on its line and read as part of it; Media's IF97 package - a named argument after
        // such a one does the same, and so do the named arguments after such a positional one; and a
        // call written in a branch that starts a line keeps its arguments on that line (B489).
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Real p;
                  output State state;
                algorithm
                  head := homotopy(if s > 0 then (N/N_nominal)^2*flowCharacteristic(V_flow_single*N_nominal/N) else (N/N_nominal)^2*flowCharacteristic(0) - s*unitHead, if checkValveHomotopy == Types.CheckValveHomotopyType.Open then N/N_nominal*flowCharacteristic(V_flow_single_init) else N/N_nominal*flowCharacteristic(0) - s*unitHead);
                  state := ThermodynamicState(d=density_ph(p, h, region=region), phase=if region == 0 then 0 else if region == 4 then 2 else if regionnnnnnnnnnnnnnnnnnnn == 5 then 3 else 1, h=h, p=p);
                  y := smooth(1, if xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx > 0 then aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa else bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, x=1);
                  z := if flag then g(1, 2) else h(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, c);
                end f;
                """),
            expectedOutput: Normalise(ArgumentsAfterBranchesStartLines));
    }

    private const string NamedArgumentsEstimatedAsWritten = """
        function f
          input Real p;
          input Real xxxxxxxxxxxx=Buildings.Utilities.Math.Functions.cubicHermite(x=u, x1=xd[i],
            x2=xd[i + 1], y1=yd[i]);
          output State state;

        algorithm
          state := ThermodynamicState(p=p,
            T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X),
            X=cat(1, X, {1 - sum(X)}));
          z := Buildings.Utilities.Math.Functions.cubicHermiteLinearExtrapolation(x=u, x1=xd[i],
            x2=xd[i + 1], y1=yd[i], y2=yd[i + 1], y1d=d[i], y2d=d[i + 1]);
        end f;
        """;

    [Fact]
    public void ANamedArgumentInAStatementIsEstimatedAsWritten()
    {
        // In order: MSL's ReferenceMoistAir and Buildings' cubicHermiteLinearExtrapolation - whether a
        // named argument in a statement fits after its ',' is judged by its length as written, with
        // its spaces, as a positional argument's is (B487), where it was judged from its text alone
        // and left 'X=cat(1, X,' and 'x2=xd[i' ending lines they did not fit, their argument wrapped
        // inside. A declaration's binding is judged the same way (B491), so 'x2=xd[i + 1],' starts a
        // line there too; a declaration's modifications are judged as they were (B489).
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Real p;
                  input Real xxxxxxxxxxxx=Buildings.Utilities.Math.Functions.cubicHermite(x=u, x1=xd[i], x2=xd[i + 1], y1=yd[i]);
                  output State state;
                algorithm
                  state := ThermodynamicState(p=p, T=Modelica.Media.Air.ReferenceMoistAir.Utilities.Inverses.T_phX(p, h, X), X=cat(1, X, {1 - sum(X)}));
                  z := Buildings.Utilities.Math.Functions.cubicHermiteLinearExtrapolation(x=u, x1=xd[i], x2=xd[i + 1], y1=yd[i], y2=yd[i + 1], y1d=d[i], y2d=d[i + 1]);
                end f;
                """),
            expectedOutput: Normalise(NamedArgumentsEstimatedAsWritten));
    }

    private const string SubscriptsNotWrapped = """
        function f
          input Integer m;

        algorithm
          PPre[m] := Buildings.Controls.Predictors.BaseClasses.weatherRegression(
            TCur=if m == 1 then TOut_in_internal else TOutFut_in_internal[m - 1],
            T={T[_typeOfDay[m], iSam[m], i] for i in 1:nHis},
            P={P[_typeOfDay[m], iSam[m], i] for i in 1:nHis});
          kOpa[i + nConExt + 2*nConPar] := Modelica.Constants.sigma*epsConBou[i]*AOpa[i + nConExt + 2*nConPar];
          x := aaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            + ccccccccccc[dddddddddddddddd + eeeeeeeee];
        end f;
        """;

    [Fact]
    public void APlusOrMinusInsideASubscriptIsNotWrapped()
    {
        // In order: Buildings' ElectricalLoad, which ended a line with 'TOutFut_in_internal[m' and
        // started the next with '- 1], T=...', so its first argument, now on one line, is moved to a
        // line of its own (B464); Buildings' InfraredRadiationExchange, which ended one with
        // 'AOpa[i + nConExt' and started the next with '+ 2*nConPar];'; and a '+' outside the
        // subscript still wraps, taking the subscripted term with it (B491).
        TestHelpers.AssertClass(
            Normalise("""
                function f
                  input Integer m;
                algorithm
                  PPre[m] := Buildings.Controls.Predictors.BaseClasses.weatherRegression(TCur=if m == 1 then TOut_in_internal else TOutFut_in_internal[m - 1], T={T[_typeOfDay[m], iSam[m], i] for i in 1:nHis}, P={P[_typeOfDay[m], iSam[m], i] for i in 1:nHis});
                  kOpa[i + nConExt + 2*nConPar] := Modelica.Constants.sigma*epsConBou[i]*AOpa[i + nConExt + 2*nConPar];
                  x := aaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb + ccccccccccc[dddddddddddddddd + eeeeeeeee];
                end f;
                """),
            expectedOutput: Normalise(SubscriptsNotWrapped));
    }

    private const string ControlConditionsWrapped = """
        model M
          Real y;

        equation
          if not ATotExt > 0 and not ATotWin > 0 and not AInt > 0 and AFloor > 0 then
            connect(thermSplitterIntGains.portOut[1], floorRC.port_a);
          elseif ATotExt > 0 and not ATotWin > 0 and not AInt > 0 and AFloor > 0
              or not ATotExt > 0 and ATotWin > 0 and not AInt > 0 and AFloor > 0
              or not ATotExt > 0 and not ATotWin > 0 and AInt > 0 and AFloor > 0 then
            connect(thermSplitterIntGains.portOut[2], floorRC.port_a);
          end if;
          if Modelica.Math.isEqual(eta_mf1, 1.0, Modelica.Constants.eps)
              and Modelica.Math.isEqual(eta_mf2, 1.0, Modelica.Constants.eps) then
            y = 1;
          end if;
          when Modelica.Math.BooleanVectors.anyTrue({u[i] <> pre(y[i]) for i in 1:nin})
              and time - time_change > holdDuration then
            y = 2;
          end when;

        algorithm
          if (p < triple.ptriple) or (p > data.PLIMIT1) or (h < hlowerofp1(p))
              or ((p < 10.0e6) and (h > hupperofp5(p))) or ((p >= 10.0e6) and (h > hupperofp2(p))) then
            y := 1;
          elseif aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
              + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb > 0 then
            y := 2;
          end if;
          while aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > 0
              and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb > 0 loop
            y := 3;
          end while;
          for i in aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb loop
            y := 4;
          end for;
        end M;
        """;

    [Fact]
    public void AControlConditionWrapsALevelPastWhatItGuardsInEveryBranch()
    {
        // In order: Buildings' ThreeElements - an elseif's condition wraps, where the continuation
        // was cleared by the first equation nested in the if and it never wrapped; MSL's LossyGear -
        // an if's condition wraps before 'and' rather than between a call's arguments; Buildings'
        // IntegerArrayHold - a when's before 'and' rather than at a '-'; MSL's IF97 region_ph - an
        // if statement's condition wraps; an elseif statement's at a '+'; a while's. Each
        // continuation is a level past the body, so it is not read as one of its statements (B491).
        // A for loop's range is not wrapped before an 'and' or 'or'.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  if not ATotExt > 0 and not ATotWin > 0 and not AInt > 0 and AFloor > 0 then
                    connect(thermSplitterIntGains.portOut[1], floorRC.port_a);
                  elseif ATotExt > 0 and not ATotWin > 0 and not AInt > 0 and AFloor > 0 or not ATotExt > 0 and ATotWin > 0 and not AInt > 0 and AFloor > 0 or not ATotExt > 0 and not ATotWin > 0 and AInt > 0 and AFloor > 0 then
                    connect(thermSplitterIntGains.portOut[2], floorRC.port_a);
                  end if;
                  if Modelica.Math.isEqual(eta_mf1, 1.0, Modelica.Constants.eps) and Modelica.Math.isEqual(eta_mf2, 1.0, Modelica.Constants.eps) then
                    y = 1;
                  end if;
                  when Modelica.Math.BooleanVectors.anyTrue({u[i] <> pre(y[i]) for i in 1:nin}) and time - time_change > holdDuration then
                    y = 2;
                  end when;
                algorithm
                  if (p < triple.ptriple) or (p > data.PLIMIT1) or (h < hlowerofp1(p)) or ((p < 10.0e6) and (h > hupperofp5(p))) or ((p >= 10.0e6) and (h > hupperofp2(p))) then
                    y := 1;
                  elseif aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb > 0 then
                    y := 2;
                  end if;
                  while aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > 0 and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb > 0 loop
                    y := 3;
                  end while;
                  for i in aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa and bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb loop
                    y := 4;
                  end for;
                end M;
                """),
            expectedOutput: Normalise(ControlConditionsWrapped));
    }

    private const string DeclarationBindingsWrapped = """
        model M
          parameter SI.Voltage ViNominal=VaNominal
            - Machines.Thermal.convertResistance(Ra, TaRef, alpha20a, TaNominal)*IaNominal
            - Machines.Losses.DCMachines.brushVoltageDrop(brushParameters, IaNominal) "Voltage";
          parameter Modelica.Units.SI.MassFlowRate m_flow_nominal=m0_flow_cor + m0_flow_sou + m0_flow_eas
            + m0_flow_nor + m0_flow_wes "Nominal air mass flow rate";
          final parameter Boolean is_twoWay=typ == Buildings.Templates.Components.Types.Valve.TwoWayModulating
            or typ == Buildings.Templates.Components.Types.Valve.TwoWayTwoPosition;
          Real x(start=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb);
        end M;
        """;

    [Fact]
    public void ADeclarationsBindingWrapsAsAnEquationDoes()
    {
        // In order: MSL's DcPermanentMagnetData - a binding wraps before a '-' as an equation's
        // right-hand side does, where a declaration's expression never wrapped at an operator;
        // Buildings' ClosedLoop - the description follows the last line; Buildings' Templates Valve -
        // before an 'or'. A modification's own value is not wrapped (B491).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter SI.Voltage ViNominal=VaNominal - Machines.Thermal.convertResistance(Ra, TaRef, alpha20a, TaNominal)*IaNominal - Machines.Losses.DCMachines.brushVoltageDrop(brushParameters, IaNominal) "Voltage";
                  parameter Modelica.Units.SI.MassFlowRate m_flow_nominal=m0_flow_cor + m0_flow_sou + m0_flow_eas + m0_flow_nor + m0_flow_wes "Nominal air mass flow rate";
                  final parameter Boolean is_twoWay=typ == Buildings.Templates.Components.Types.Valve.TwoWayModulating or typ == Buildings.Templates.Components.Types.Valve.TwoWayTwoPosition;
                  Real x(start=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb);
                end M;
                """),
            expectedOutput: Normalise(DeclarationBindingsWrapped));
    }

    private const string LongBindingsMoved = """
        model M
          parameter Modelica.Units.SI.SpecificHeatCapacity cpSou_default=
            if typ == Buildings.Templates.Components.Types.HeatPump.AirToWater then Buildings.Utilities.Psychrometrics.Constants.cpAir
                else Buildings.Utilities.Psychrometrics.Constants.cpWatLiq
            "Source fluid default specific heat capacity";
          parameter Modelica.Units.SI.SpecificHeatCapacity cpHea_default=
            MediumHea.specificHeatCapacityCp(MediumHea.setState_pTX(MediumHea.p_default, MediumHea.T_default,
              MediumHea.X_default)) "Specific heat capacity";
          final parameter Buildings.Controls.OBC.ASHRAE.G36.Types.Title24ClimateZone tit24CliZon=
            datAll.tit24CliZon "California Title 24 climate zone";
          parameter SI.Voltage ViNominal=VaNominal
            - Machines.Thermal.convertResistance(Ra, TaRef, alpha20a, TaNominal)*IaNominal
            - Machines.Losses.DCMachines.brushVoltageDrop(brushParameters, IaNominal) "Voltage";
          SI.Length h=if IN_con.geometry == TYP.RectangularFin then IN_con.D_h*(1 + IN_con.alpha)/(2*IN_con.alpha)
              else IN_con.b;
          final parameter Integer nSenDpHeaWatRem(final min=if typCtl == Buildings.Templates.Plants.HeatPumps.Types.Controller.OpenLoop then 1 else 0)=1
            "Number of sensors";
          Real x(start=Buildings.Templates.Components.Types.HeatPump.AirToWater + Buildings.Templates.Components.Types.HeatPump.AirToWater);
        end M;
        """;

    [Fact]
    public void ABindingWhoseFirstLineWouldPassTheLimitStartsALineOfItsOwn()
    {
        // In order: Buildings' Templates heat pump - an if-expression can break only at its
        // branches, so 'cpSou_default=if ... then ...cpAir' ended its first line at 152 characters;
        // the binding moves whole to a line of its own a level in, laid out from there as a
        // statement starting that line would be - its 'else' two levels past the 'if', as an
        // equation's is past its start. Buildings' StorageTankWithExternalHeatExchanger - a call's
        // arguments then wrap from there. (In AConditionDoesNotWrapAfterALoneFlag, MSL's
        // CombiTable1Ds: an 'else' inside an argument moves in with the argument, where kept at
        // the declaration's level it came to the argument's column.) Buildings' G36VAVMultiZone - a short binding moves too, when with it the line would
        // pass the limit. MSL's DcPermanentMagnetData - a binding that wraps before a '-' within the
        // limit stays where it starts (B491). MSL's Dissipation - not after 20 characters or fewer,
        // as an equation's left-hand side does not wrap at its '='. Buildings' heat pump
        // PartialController - not when the line is past the limit before the '='. A modification's
        // value is not moved (B494).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Modelica.Units.SI.SpecificHeatCapacity cpSou_default=if typ == Buildings.Templates.Components.Types.HeatPump.AirToWater then Buildings.Utilities.Psychrometrics.Constants.cpAir else Buildings.Utilities.Psychrometrics.Constants.cpWatLiq "Source fluid default specific heat capacity";
                  parameter Modelica.Units.SI.SpecificHeatCapacity cpHea_default=MediumHea.specificHeatCapacityCp(MediumHea.setState_pTX(MediumHea.p_default, MediumHea.T_default, MediumHea.X_default)) "Specific heat capacity";
                  final parameter Buildings.Controls.OBC.ASHRAE.G36.Types.Title24ClimateZone tit24CliZon=datAll.tit24CliZon "California Title 24 climate zone";
                  parameter SI.Voltage ViNominal=VaNominal - Machines.Thermal.convertResistance(Ra, TaRef, alpha20a, TaNominal)*IaNominal - Machines.Losses.DCMachines.brushVoltageDrop(brushParameters, IaNominal) "Voltage";
                  SI.Length h=if IN_con.geometry == TYP.RectangularFin then IN_con.D_h*(1 + IN_con.alpha)/(2*IN_con.alpha) else IN_con.b;
                  final parameter Integer nSenDpHeaWatRem(final min=if typCtl == Buildings.Templates.Plants.HeatPumps.Types.Controller.OpenLoop then 1 else 0)=1 "Number of sensors";
                  Real x(start=Buildings.Templates.Components.Types.HeatPump.AirToWater + Buildings.Templates.Components.Types.HeatPump.AirToWater);
                end M;
                """),
            expectedOutput: Normalise(LongBindingsMoved));
    }

    private const string ParenthesesWrappedInside = """
        model M
          Real y;

        equation
          startForward = pre(mode) == Stuck and (sa > f0_max/unitForce and s < (smax - L/2)
              or pre(startForward) and sa > f0/unitForce and s < (smax - L/2));
          mode = if (pre(mode) == Backward or startBackward) and v_relfric > 0 then Forward
              else (if (pre(mode) == Forward or pre(mode) == Free or startForward) and v_relfric > 0
                  and s < (smax - L/2) then Forward
                else if (pre(mode) == Backward or pre(mode) == Free or startBackward) and v_relfric < 0
                  and s > (smin + L/2) then Backward
                else Stuck);
          y = offset
            + (if time < startTime then 0
              else if time < (startTime + duration) then (time - startTime)*height/duration
              else height);
          h = 639675.036*(0.173379420894777
              + pi1*(-0.022914084306349
              + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7)))));
          z = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*(bbbbbbbbbbbbbbbbbbbbbbbbbbbb + ccccccccccccc);
          y = f((aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
              + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb), c);
          y = g(
            x=(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb),
            c=1);
          y = [(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)];
          connect(valIso.port_bChiWat, inlPumChiWatPri.port_a)
            annotation (Line(
              points={{-60, 80}, {-40, 80}},
              color={0, 0, 0},
              rotation=(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)
            ));
        end M;
        """;

    [Fact]
    public void AParenthesisedExpressionTooLongForALineWrapsInsideItsParentheses()
    {
        // In order, from MSL's MassWithStopAndFriction: a parenthesised logical expression wraps
        // before its 'or', a level in from the line its '(' is on; a parenthesised if-expression
        // breaks at its own branches, a level in from the '(' line, as one standing alone does -
        // where nothing inside parentheses wrapped and the 'else (if ... )' ran to 300 characters;
        // MSL's Blocks.Sources.Ramp - the same after a wrapped '+'; MSL's IF97 - nested parentheses
        // too long for a line wrap inside each, a chain of them at one column (B494); a parenthesised
        // expression that would fit on a line of its own is left whole, as before; one in a call's
        // first positional argument wraps, but one in a first named argument that may yet be moved
        // (B464) does not, nor one inside a matrix's brackets or in an annotation (B491).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  startForward = pre(mode) == Stuck and (sa > f0_max/unitForce and s < (smax - L/2) or pre(startForward) and sa > f0/unitForce and s < (smax - L/2));
                  mode = if (pre(mode) == Backward or startBackward) and v_relfric > 0 then Forward else (if (pre(mode) == Forward or pre(mode) == Free or startForward) and v_relfric > 0 and s < (smax - L/2) then Forward else if (pre(mode) == Backward or pre(mode) == Free or startBackward) and v_relfric < 0 and s > (smin + L/2) then Backward else Stuck);
                  y = offset + (if time < startTime then 0 else if time < (startTime + duration) then (time - startTime)*height/duration else height);
                  h = 639675.036*(0.173379420894777 + pi1*(-0.022914084306349 + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7)))));
                  z = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*(bbbbbbbbbbbbbbbbbbbbbbbbbbbb + ccccccccccccc);
                  y = f((aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb), c);
                  y = g(x=(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb), c=1);
                  y = [(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)];
                  connect(valIso.port_bChiWat, inlPumChiWatPri.port_a) annotation (Line(points={{-60, 80}, {-40, 80}}, color={0, 0, 0}, rotation=(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)));
                end M;
                """),
            expectedOutput: Normalise(ParenthesesWrappedInside));
    }

    private const string NestedChainsFlat = """
        model M
          Real y;

        equation
          h = 639675.036*(0.173379420894777
              + pi1*(-0.022914084306349
              + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7)))));
          h = 639675.036*(0.173379420894777
              + pi1*(-0.022914084306349
              + pi1*(-0.00017146768241932
              + pi1*(-4.18695814670391e-6
              + pi1*(-2.41630417490008e-7
              + pi1*(1.73545618580828e-11
              + o[1]*pi1*(8.43755552264362e-14
              + o[2]*o[3]*pi1*(5.35429206228374e-35 + (-7.06381628462585e-47 + 9.64504638626269e-49*pi1)*pi1))))))));
          h = 639675.036*(0.173379420894777
              - pi1*(-0.022914084306349
                + pi1*(-0.00017146768241932
                + pi1*(-4.18695814670391e-6
                + pi1*(-2.41630417490008e-7 + pi1*(1.73545618580828e-11 + o[1]*pi1*(8.43755552264362e-14)))))));
          h = 639675.036*(0.173379420894777
              + (pi1 + 1)*(-0.022914084306349
                + pi1*(-0.00017146768241932
                + pi1*(-4.18695814670391e-6
                + pi1*(-2.41630417490008e-7 + pi1*(1.73545618580828e-11 + o[1]*pi1*(8.43755552264362e-14)))))));
          f_rod = (-revolute.tau
              - revolute.e*(frame_ib.t + frame_im.t + cross(rRod2_ib, frame_im.f)
                - cross(rRod2_ib, Frames.resolveRelative(rod1.f_b_a1, rod1.frame_a.R, rod1.frame_b.R))))/aux;
          dh = R*(1/MMX[Water]*(Utilities.smoothMax_der(X[Water], 0.0, 1e-9, dX[Water], 0.0, 0.0)
              + dp/p*Utilities.smoothMax(X[Water], 0.0, 1e-9))
              + 1/MMX[Air]*(Utilities.smoothMax_der(X[Air], 0.0, 1e-9, dX[Air], 0.0, 0.0)
                + dp/p*Utilities.smoothMax(X[Air], 0.0, 1e-9)));
          g = 1.5*(0.1
              + x*((aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
                + cccccccccccccccccccccccc)
                + x*(0.2
                + x*(0.3 + x*(0.4 + x*(0.5 + x*(0.6 + x*(0.7 + x*(0.8 + x*(0.9 + x*(1.0 + x*(1.1 + x*(1.2 + x*(1.3 + x*1.4))))))))))))));
          h = 2.5*(0.1
              + (0.2
                + x*(0.3 + x*(0.4 + x*(0.5 + x*(0.6 + x*(0.7 + x*(0.8 + x*(0.9 + x*(1.0 + x*(1.1 + x*(1.2 + x*(1.3 + x*1.4)))))))))))));
          h = 3.5*(0.1
              + x*(0.2
              + x*(0.3
              + x*(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
                + cccccccccccccccccccccc))));
          h = 4.5*(0.1
              + x*(0.2
              + x*(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
              + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)));
          h = 5.5*(0.1
              + x*(0.2
                + x*(0.3
                + x*(0.4
                + x*(0.5 + x*(0.6 + x*(0.7 + x*(0.8 + x*(0.9 + x*(1.0 + x*(1.1 + x*(1.2 + x*(1.3 + x*(1.4 + x*(1.5 + x*1.6))))))))))))))^2);
        end M;
        """;

    [Fact]
    public void ALinkOfANestedPolynomialIsWrappedAtTheColumnOfTheOneItEnds()
    {
        // In order: MSL's IF97 hlowerofp1 - a polynomial in nested form, each link two terms ending
        // in the next after a '+' and a coefficient of names and numbers, is written a link a line at
        // one column, where each stepped a level further in (B491); the same at any length; a chain
        // whose first link follows a '-', or a parenthesised coefficient, steps once there and is flat
        // after it; MSL's JointSSR - the last parentheses holding more than two terms step in, or their
        // terms would read as the outer ones; MSL's MoistAir - a chain of one link steps in; a link whose
        // first term wraps steps in, and the chain after it is flat; so does one with no coefficient;
        // a chain's last parentheses of more than two terms step in, and of two are a link; a link
        // raised to a power steps in (B494).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                equation
                  h = 639675.036*(0.173379420894777 + pi1*(-0.022914084306349 + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7)))));
                  h = 639675.036*(0.173379420894777 + pi1*(-0.022914084306349 + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7 + pi1*(1.73545618580828e-11 + o[1]*pi1*(8.43755552264362e-14 + o[2]*o[3]*pi1*(5.35429206228374e-35 + (-7.06381628462585e-47 + 9.64504638626269e-49*pi1)*pi1))))))));
                  h = 639675.036*(0.173379420894777 - pi1*(-0.022914084306349 + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7 + pi1*(1.73545618580828e-11 + o[1]*pi1*(8.43755552264362e-14)))))));
                  h = 639675.036*(0.173379420894777 + (pi1 + 1)*(-0.022914084306349 + pi1*(-0.00017146768241932 + pi1*(-4.18695814670391e-6 + pi1*(-2.41630417490008e-7 + pi1*(1.73545618580828e-11 + o[1]*pi1*(8.43755552264362e-14)))))));
                  f_rod = (-revolute.tau - revolute.e*(frame_ib.t + frame_im.t + cross(rRod2_ib, frame_im.f) - cross(rRod2_ib, Frames.resolveRelative(rod1.f_b_a1, rod1.frame_a.R, rod1.frame_b.R))))/aux;
                  dh = R*(1/MMX[Water]*(Utilities.smoothMax_der(X[Water], 0.0, 1e-9, dX[Water], 0.0, 0.0) + dp/p*Utilities.smoothMax(X[Water], 0.0, 1e-9)) + 1/MMX[Air]*(Utilities.smoothMax_der(X[Air], 0.0, 1e-9, dX[Air], 0.0, 0.0) + dp/p*Utilities.smoothMax(X[Air], 0.0, 1e-9)));
                  g = 1.5*(0.1 + x*((aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb + cccccccccccccccccccccccc) + x*(0.2 + x*(0.3 + x*(0.4 + x*(0.5 + x*(0.6 + x*(0.7 + x*(0.8 + x*(0.9 + x*(1.0 + x*(1.1 + x*(1.2 + x*(1.3 + x*1.4))))))))))))));
                  h = 2.5*(0.1 + (0.2 + x*(0.3 + x*(0.4 + x*(0.5 + x*(0.6 + x*(0.7 + x*(0.8 + x*(0.9 + x*(1.0 + x*(1.1 + x*(1.2 + x*(1.3 + x*1.4)))))))))))));
                  h = 3.5*(0.1 + x*(0.2 + x*(0.3 + x*(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb + cccccccccccccccccccccc))));
                  h = 4.5*(0.1 + x*(0.2 + x*(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa + bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)));
                  h = 5.5*(0.1 + x*(0.2 + x*(0.3 + x*(0.4 + x*(0.5 + x*(0.6 + x*(0.7 + x*(0.8 + x*(0.9 + x*(1.0 + x*(1.1 + x*(1.2 + x*(1.3 + x*(1.4 + x*(1.5 + x*1.6))))))))))))))^2);
                end M;
                """),
            expectedOutput: Normalise(NestedChainsFlat));
    }

    private const string FlagsKeptWithTheirConditions = """
        model M
          parameter Modelica.Blocks.Types.ExternalCombiTimeTable tableID=
            Modelica.Blocks.Types.ExternalCombiTimeTable(
              if tableOnFile then if isCsvExt then "Values" else tableName else "NoName", if tableOnFile and fileName <> "NoName"
                  and not Modelica.Utilities.Strings.isEmpty(fileName) then fileName
                else "NoName",
              table, startTime/timeScale) "External table object";
          final parameter Boolean have_senVHeaWatPri=
            cfg.have_heaWat and (if cfg.have_hrc or not have_senVHeaWatSec
                  or cfg.typDis == Buildings.Templates.Plants.HeatPumps.Types.Distribution.Variable1Only then true
                else have_senVHeaWatPri_select) "Set to true for plants with primary HW flow sensor";
          final parameter Integer n=if have_pumChiWatPriDed
                or have_chiWat and typArrPumPri == Buildings.Templates.Components.Types.PumpArrangement.Headered then nPumChiWatPri_select
              else 0;
          final parameter Modelica.Units.SI.Time t_in_start=
            if initDelay and (abs(m_flow_start) > 1E-10*m_flow_nominal) then min(
              length/m_flow_start*(rho*dh^2/4*Modelica.Constants.pi), 0)
                else 0 "Initial value of input time at inlet";
          Real y;

        equation
          if useSomethingQuiteLong and aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb then
            y = 1;
          end if;
          z = if zerTim == Buildings.Utilities.Time.Types.ZeroTime.NY2027
                or zerTim == Buildings.Utilities.Time.Types.ZeroTime.Custom then 1
              elseif useSomethingQuiteLong and aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb then 2
              else 3;
        end M;
        """;

    [Fact]
    public void AConditionDoesNotWrapAfterALoneFlag()
    {
        // In order: MSL's CombiTable1Ds - a condition whose first operand is a lone Boolean name
        // does not wrap after it, but at its next 'and', where it ended a line with 'if tableOnFile';
        // Buildings' heat pump PartialController - the same at a later 'or', 'not' or a dotted name
        // being a flag too; an 'or' whose right-hand side is an 'and' of several still wraps after
        // the flag, as the 'and' would otherwise read as joining the 'or' (B489); Buildings'
        // PlugFlowTransportDelay - a condition of two operands is kept whole, where it ended a line
        // with 'if initDelay'; an if-equation's condition the same; one whose first operand is a
        // comparison wraps after it, as before, and an elseif's condition keeps its flag (B494).
        // Three of the bindings start a line of their own, their first lines being past the limit
        // on the declaration's; 'n=' does not, its first line ending within it (B494).
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Modelica.Blocks.Types.ExternalCombiTimeTable tableID=Modelica.Blocks.Types.ExternalCombiTimeTable(if tableOnFile then if isCsvExt then "Values" else tableName else "NoName", if tableOnFile and fileName <> "NoName" and not Modelica.Utilities.Strings.isEmpty(fileName) then fileName else "NoName", table, startTime/timeScale) "External table object";
                  final parameter Boolean have_senVHeaWatPri=cfg.have_heaWat and (if cfg.have_hrc or not have_senVHeaWatSec or cfg.typDis == Buildings.Templates.Plants.HeatPumps.Types.Distribution.Variable1Only then true else have_senVHeaWatPri_select) "Set to true for plants with primary HW flow sensor";
                  final parameter Integer n=if have_pumChiWatPriDed or have_chiWat and typArrPumPri == Buildings.Templates.Components.Types.PumpArrangement.Headered then nPumChiWatPri_select else 0;
                  final parameter Modelica.Units.SI.Time t_in_start=if initDelay and (abs(m_flow_start) > 1E-10*m_flow_nominal) then min(length/m_flow_start*(rho*dh^2/4*Modelica.Constants.pi), 0) else 0 "Initial value of input time at inlet";
                  Real y;
                equation
                  if useSomethingQuiteLong and aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb then
                    y = 1;
                  end if;
                  z = if zerTim == Buildings.Utilities.Time.Types.ZeroTime.NY2027 or zerTim == Buildings.Utilities.Time.Types.ZeroTime.Custom then 1 elseif useSomethingQuiteLong and aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb then 2 else 3;
                end M;
                """),
            expectedOutput: Normalise(FlagsKeptWithTheirConditions));
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
    [InlineData(ArrayOfWrappedCalls, 60)]
    [InlineData(ArrayOfCallsAnArgumentALine, 60)]
    [InlineData(ArrayElementsWrappedOrNot, 60)]
    [InlineData(AnnotationArraysOfCalls, 60)]
    [InlineData(GraphicsElementWithALongArgument, 100)]
    [InlineData(ArgumentExpressionContinued, 100)]
    [InlineData(RightHandSideContinued, 100)]
    [InlineData(ShortClassGraphics, 100)]
    [InlineData(ComponentGraphics, 100)]
    [InlineData(ArraysWrappedFromTheirOpeningLine, 60)]
    [InlineData(ShortClassDescriptionsWrapped, 100)]
    [InlineData(PositionalArraysMovedWhole, 100)]
    [InlineData(ArraysMovedOffALongLine, 100)]
    [InlineData(BranchAndArrayTermsContinued, 100)]
    [InlineData(BranchesAndArgumentsStartLines, 100)]
    [InlineData(BranchesStartLinesInAFunction, 100)]
    [InlineData(FirstPositionalArgumentsStartLines, 100)]
    [InlineData(LogicalOperatorsStartLines, 100)]
    [InlineData(LogicalOperatorsInAFunction, 100)]
    [InlineData(ArgumentsAfterBranchesStartLines, 100)]
    [InlineData(NamedArgumentsEstimatedAsWritten, 100)]
    [InlineData(SubscriptsNotWrapped, 100)]
    [InlineData(ControlConditionsWrapped, 100)]
    [InlineData(DeclarationBindingsWrapped, 100)]
    [InlineData(ParenthesesWrappedInside, 100)]
    [InlineData(FlagsKeptWithTheirConditions, 100)]
    [InlineData(NestedChainsFlat, 100)]
    [InlineData(LongBindingsMoved, 100)]
    public void ASavedLayoutSavesBackUnchanged(string saved, int maxLineLength)
    {
        TestHelpers.AssertClass(Normalise(saved), maxLineLength: maxLineLength);
    }
}
