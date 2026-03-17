model EquilibriumDrumBoiler "Simple Evaporator with two states, see Åström, Bell: Drum-boiler dynamics, Automatica 36, 2000, pp.363-378"
  extends Modelica.Fluid.Interfaces.PartialTwoPort(
    final port_a_exposesState=true,
    final port_b_exposesState=true,
    redeclare replaceable package Medium = Modelica.Media.Water.StandardWater
      constrainedby Modelica.Media.Interfaces.PartialTwoPhaseMedium
  );
  import Modelica.Constants;
  import Modelica.Fluid.Types;
  parameter Modelica.Units.SI.Mass m_D "Mass of surrounding drum metal";
  parameter Medium.SpecificHeatCapacity cp_D "Specific heat capacity of drum metal";
  parameter Modelica.Units.SI.Volume V_t "Total volume inside drum";
  parameter Medium.AbsolutePressure p_start=system.p_start "Start value of pressure"
    annotation (Dialog(tab="Initialization"));
  parameter Modelica.Units.SI.Volume V_l_start=V_t/2 
    "Start value of liquid volumeStart value of volume"
    annotation (Dialog(tab="Initialization"));
  // Assumptions
  parameter Boolean allowFlowReversal=system.allowFlowReversal 
    "= true, if flow reversal is enabled, otherwise restrict flow to design direction (port_a -> port_b)"
    annotation (
      Dialog(tab="Assumptions"),
      Evaluate=true
    );
  parameter Types.Dynamics energyDynamics=system.energyDynamics "Formulation of energy balance"
    annotation (
      Evaluate=true,
      Dialog(
        tab="Assumptions",
        group="Dynamics"
      )
    );
  parameter Types.Dynamics massDynamics=system.massDynamics "Formulation of mass balance"
    annotation (
      Evaluate=true,
      Dialog(
        tab="Assumptions",
        group="Dynamics"
      )
    );
  Modelica.Thermal.HeatTransfer.Interfaces.HeatPort_a heatPort
    annotation (Placement(transformation(extent={{-10, -110}, {10, -90}})));
  Modelica.Blocks.Interfaces.RealOutput V(unit="m3") "Liquid volume"
    annotation (Placement(transformation(
      origin={40, 110},
      extent={{-10, -10}, {10, 10}},
      rotation=90
    )));
  /*
  Dummy comment
  */
  Medium.SaturationProperties sat "State vector to compute saturation properties";
  Medium.AbsolutePressure p(start=p_start, stateSelect=StateSelect.prefer)
    "Pressure inside drum boiler";
  Medium.Temperature T "Temperature inside drum boiler";
  Modelica.Units.SI.Volume V_v "Volume of vapour phase";
  Modelica.Units.SI.Volume V_l(start=V_l_start, stateSelect=StateSelect.prefer)
    "Volumes of liquid phase";
  Medium.SpecificEnthalpy h_v=Medium.dewEnthalpy(sat) "Specific enthalpy of vapour";
  Medium.SpecificEnthalpy h_l=Medium.bubbleEnthalpy(sat) "Specific enthalpy of liquid";
  Medium.Density rho_v=Medium.dewDensity(sat) "Density in vapour phase";
  Medium.Density rho_l=Medium.bubbleDensity(sat) "Density in liquid phase";
  Modelica.Units.SI.Mass m "Total mass of drum boiler";
  Modelica.Units.SI.Energy U "Internal energy";
  Medium.Temperature T_D=heatPort.T "Temperature of drum";
  Modelica.Units.SI.HeatFlowRate q_F=heatPort.Q_flow "Heat flow rate from furnace";
  Medium.SpecificEnthalpy h_W=inStream(port_a.h_outflow)
    "Feed water enthalpy (specific enthalpy close to feedwater port when mass flows in to the boiler)";
  Medium.SpecificEnthalpy h_S=inStream(port_b.h_outflow)
    "Steam enthalpy (specific enthalpy close to steam port when mass flows in to the boiler)";
  Modelica.Units.SI.MassFlowRate qm_W=port_a.m_flow "Feed water mass flow rate";
  Modelica.Units.SI.MassFlowRate qm_S=port_b.m_flow "Steam mass flow rate";
  /*outer Modelica.Fluid.Components.FluidOptions fluidOptions "Global default options";*/

equation
  // balance equations
  m = rho_v*V_v + rho_l*V_l + m_D "Total mass";
  U = rho_v*V_v*h_v + rho_l*V_l*h_l - p*V_t + m_D*cp_D*T_D "Total energy";
  if massDynamics == Types.Dynamics.SteadyState then
    0 = qm_W + qm_S "Steady state mass balance";
  else
    der(m) = qm_W + qm_S "Dynamic mass balance";
  end if;
  if energyDynamics == Types.Dynamics.SteadyState then
    0 = q_F + port_a.m_flow*actualStream(port_a.h_outflow)
      + port_b.m_flow*actualStream(port_b.h_outflow) "Steady state energy balance";
  else
    der(U) = q_F + port_a.m_flow*actualStream(port_a.h_outflow)
      + port_b.m_flow*actualStream(port_b.h_outflow) "Dynamic energy balance";
  end if;
  V_t = V_l + V_v;
  // Properties of saturated liquid and steam
  sat.psat = p;
  sat.Tsat = T;
  sat.Tsat = Medium.saturationTemperature(p);
  // ideal heat transfer between metal and water
  T_D = T;
  // boundary conditions at the ports
  port_a.p = p;
  port_a.h_outflow = h_l;
  port_b.p = p;
  port_b.h_outflow = h_v;
  // liquid volume
  V = V_l;
  // Check that two-phase equilibrium is actually possible
  assert(p < Medium.fluidConstants[1].criticalPressure - 10000, "Evaporator model requires subcritical pressure");

initial equation
  // Initial conditions
  // Note: p represents the energy as it is constrained by T_sat
  if energyDynamics == Types.Dynamics.FixedInitial then
    p = p_start;
  elseif energyDynamics == Types.Dynamics.SteadyStateInitial then
    der(p) = 0;
  end if;
  if massDynamics == Types.Dynamics.FixedInitial then
    V_l = V_l_start;
  elseif energyDynamics == Types.Dynamics.SteadyStateInitial then
    der(V_l) = 0;
  end if;

  annotation (
    Icon(
      coordinateSystem(
        preserveAspectRatio=false,
        extent={{-100, -100}, {100, 100}}
      ),
      graphics={
        Rectangle(
          extent={{-100, 64}, {100, -64}},
          fillPattern=FillPattern.Backward,
          fillColor={135, 135, 135}
        ),
        Rectangle(
          extent={{-100, -44}, {100, 44}},
          fillPattern=FillPattern.HorizontalCylinder,
          fillColor={255, 255, 255}
        ),
        Rectangle(
          extent=DynamicSelect({{-100, -44}, {100, 44}}, {{-100, -44}, {(-100 + 20*V_l/V_t), 44}}),
          fillPattern=FillPattern.HorizontalCylinder,
          fillColor={0, 127, 255}
        ),
        Ellipse(
          extent={{18, 0}, {48, -29}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{-1, 29}, {29, 0}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{43, 31}, {73, 2}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{-31, 1}, {-1, -28}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{50, 15}, {80, -14}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{-72, 25}, {-42, -4}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{71, -11}, {101, -40}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{72, 28}, {102, -1}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{71, 40}, {101, 11}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Line(points={{0, -64}, {0, -100}}, color={191, 0, 0}),
        Line(points={{40, 100}, {40, 64}}, color={0, 0, 127}),
        Ellipse(
          extent={{58, -11}, {88, -40}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        ),
        Ellipse(
          extent={{71, 1}, {101, -28}},
          lineColor={0, 0, 255},
          pattern=LinePattern.None,
          fillColor={255, 255, 255},
          fillPattern=FillPattern.Solid
        )
      }
    ),
    Documentation(
      revisions="<html>
<ul>
<li><em>Dec 2008</em> by R&uuml;diger Franke:<br>
     Adapt initialization to new Types.Dynamics</li>
<li><em>2 Nov 2005</em> by <a href=\"mailto:francesco.casella@polimi.it\">Francesco Casella</a>:<br>
     Initialization options fixed</li>
<li><em>6 Sep 2005</em><br>
    Model by R&uuml;diger Franke<br>
    See Franke, Rode, Kr&uuml;ger: On-line Optimization of Drum Boiler Startup, 3rd International Modelica Conference, Link&ouml;ping, 2003.<br>
    Modified after the 45th Design Meeting</li>
</ul>
</html>",
      info="<html>
<p>
Model of a simple evaporator with two states. The model assumes two-phase equilibrium inside the component; saturated steam goes out of the steam outlet.</p>
<p>
References: &Aring;str&ouml;m, Bell: Drum-boiler dynamics, Automatica 36, 2000, pp.363-378</p>
</html>"
    )
  );
end EquilibriumDrumBoiler;