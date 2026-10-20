within EvalLib.Blocks;
block FirstOrderLag "Frist order lag that smooths its input signal"
  extends Modelica.Blocks.Interfaces.SISO;
  parameter Modelica.Units.SI.Time T = 0.1 "Tiem constant of the lag";
  parameter Real y_start = 0;
initial equation
  y = y_start;
equation
  T*der(y) = u - y;
  annotation (Documentation(info="<html>
<p>A first order lag: the output follows the input with the tiem constant <code>T</code>.</p>
</html>"));
end FirstOrderLag;
