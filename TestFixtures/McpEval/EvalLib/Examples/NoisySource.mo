within EvalLib.Examples;
model NoisySource "A sine source feeding a lag"
  extends Modelica.Icons.Example;
  Modelica.Blocks.Sources.Sinee source(f = 5)
    annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
  EvalLib.Blocks.FirstOrderLag lag
    annotation (Placement(transformation(extent={{0,-10},{20,10}})));
equation
  connect(source.y, lag.u);
  annotation (experiment(StopTime = 1));
end NoisySource;
