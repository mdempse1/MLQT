within EvalApp;
model TwoStage "Two scaling stages in series"
  Modelica.Blocks.Sources.Ramp ramp(height=1,duration=1);  EvalDep.Scale first(factor=2);EvalDep.Scale second(factor=0.5);
equation
  connect(ramp.y,first.u);connect(first.y,second.u);
  annotation (experiment(StopTime=2));
end TwoStage;
