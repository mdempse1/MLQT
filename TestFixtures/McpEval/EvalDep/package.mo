within ;
package EvalDep "A small dependency the MCP tool evaluation loads, version 1.0.0"
  block Scale "Output the input multiplied by a factor"
    extends Modelica.Blocks.Interfaces.SISO;
    parameter Real factor = 1 "Factor applied to the input";
  equation
    y = factor*u;
  end Scale;
  annotation (version="1.0.0", uses(Modelica(version="4.1.0")));
end EvalDep;
