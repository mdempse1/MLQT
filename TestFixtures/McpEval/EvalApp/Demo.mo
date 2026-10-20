within EvalApp;
model Demo "A sine wave scaled by EvalDep"
  Modelica.Blocks.Sources.Sine sine(f=2);EvalDep.Scale scale(factor=3);
equation
  connect(sine.y,scale.u);
  annotation (experiment(StopTime=1));
end Demo;
