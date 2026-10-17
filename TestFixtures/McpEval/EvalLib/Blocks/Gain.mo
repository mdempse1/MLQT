within EvalLib.Blocks;
block Gain "Output the input multiplied by a gain"
  extends Modelica.Blocks.Interfaces.SISO;
  parameter Real k = 1 "Gain";
equation
  y = k*u;
end Gain;
