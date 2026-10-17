within EvalLib.Examples;
model FilteredLoop "A step, amplified and then smoothed by a first order lag"
  extends Modelica.Icons.Example;
  Modelica.Blocks.Sources.Step step(startTime = 0.5)
    annotation (Placement(transformation(extent={{-80,-10},{-60,10}})));
  EvalLib.Blocks.Gain amplifier(k = 10)
    annotation (Placement(transformation(extent={{-30,-10},{-10,10}})));
  EvalLib.Blocks.FirstOrderLag lag(T = 0.2)
    annotation (Placement(transformation(extent={{20,-10},{40,10}})));
equation
  connect(step.y, amplifier.u);
  connect(amplifier.y, lag.u);
  annotation (experiment(StopTime = 3));
end FilteredLoop;
