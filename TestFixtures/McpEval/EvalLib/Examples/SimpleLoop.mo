within EvalLib.Examples;
model SimpleLoop
  extends Modelica.Icons.Example;
  Modelica.Blocks.Sources.Sine sine(f = 1)
    annotation (Placement(transformation(extent={{-80,-10},{-60,10}})));
  EvalLib.Blocks.Gain gain(k = 4)
    annotation (Placement(transformation(extent={{-30,-10},{-10,10}})));
  Modelica.Blocks.Continuous.Integrator integrator
    annotation (Placement(transformation(extent={{20,-10},{40,10}})));
equation
  connect(sine.y, gain.u);
  connect(gain.y, integrator.u);
  annotation (experiment(StopTime = 2));
end SimpleLoop;
