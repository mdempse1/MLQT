within ;
package EvalDep "A small dependency the MCP tool evaluation loads, version 2.0.0"
  block Scale "Output the input multiplied by a factor, plus an offset"
    extends Modelica.Blocks.Interfaces.SISO;
    parameter Real factor = 1 "Factor applied to the input";
    parameter Real offset = 0 "Offset added after scaling";
  equation
    y = factor*u + offset;
  end Scale;
  annotation (version="2.0.0", uses(Modelica(version="4.1.0")),
    conversion(noneFromVersion="1.0.0"));
end EvalDep;
