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
    public void ASavedLayoutSavesBackUnchanged(string saved, int maxLineLength)
    {
        TestHelpers.AssertClass(Normalise(saved), maxLineLength: maxLineLength);
    }
}
