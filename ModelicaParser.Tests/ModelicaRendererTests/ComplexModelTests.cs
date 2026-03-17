using Newtonsoft.Json.Bson;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify complex Modelica models.
/// Test models are loaded from the Resources folder.
/// </summary>
public class ComplexModelTests
{
    //Test cases still needed for:
    // - replacable models
    // - conditional components
    // - redeclarations
    // - annotations
    // - external functions with annotations & class annotations

    /// <summary>
    /// Gets the path to a resource file in the Resources folder.
    /// This method works regardless of where the test is executed from.
    /// </summary>
    private static string GetResourcePath(string fileName)
    {
        // Option 1: Use current directory (works when files are copied to output)
        var outputPath = Path.Combine("Resources", fileName);
        if (File.Exists(outputPath))
            return outputPath;

        // Option 2: Navigate from test assembly location
        var assemblyLocation = Path.GetDirectoryName(typeof(ComplexModelTests).Assembly.Location);
        if (assemblyLocation != null)
        {
            var assemblyPath = Path.Combine(assemblyLocation, "Resources", fileName);
            if (File.Exists(assemblyPath))
                return assemblyPath;
        }

        // Option 3: Navigate from current directory up to project root
        var currentDir = Directory.GetCurrentDirectory();
        var projectPath = Path.Combine(currentDir, "Resources", fileName);
        if (File.Exists(projectPath))
            return projectPath;

        // Option 4: Try going up directories to find the Resources folder
        var dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            var resourcesPath = Path.Combine(dir.FullName, "Resources", fileName);
            if (File.Exists(resourcesPath))
                return resourcesPath;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find resource file: {fileName}. Searched in multiple locations starting from: {currentDir}");
    }

    [Fact]
    public void EquilibriumDrumBoiler_FormatsCorrectly()
    {
        var testModel = File.ReadAllText(GetResourcePath("EquilibriumDrumBoiler.mo"));
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void BuildingsModel_FormatsCorrectly()
    {
        var testModel = """
within Buildings.Controls.Predictors.Validation.BaseClasses;
partial model PartialSimpleTestCase "Partial base class for simple test case of base load prediction"
  extends Modelica.Icons.Example;
  parameter Modelica.Units.SI.Time tPeriod=24*3600 "Period";
  parameter Modelica.Units.SI.Time tSample=3600 "Sampling period";
  parameter Integer nPre(min=1)=12 "Number of time steps to predict";
  ElectricalLoad baseLoad(final nPre=nPre,
    use_dayOfAdj=false,
    predictionModel=Buildings.Controls.Predictors.Types.PredictionModel.Average) "Baseload prediction"
    annotation (Placement(transformation(extent={{60, -10}, {80, 10}})));
  Modelica.Blocks.Sources.BooleanPulse tri(width=4/24*100/7,
    period=7*tPeriod,
    startTime=1.5*tPeriod) "Sample trigger"
    annotation (Placement(transformation(extent={{-40, 70}, {-20, 90}})));
  Sources.DayType dayType(nout=2)
    "Outputs the type of the day for each hour where load is to be predicted"
    annotation (Placement(transformation(extent={{-40, 30}, {-20, 50}})));
  Modelica.Blocks.Logical.Not notEventDay "Output true if it is not an event day"
    annotation (Placement(transformation(extent={{0, 70}, {20, 90}})));
  // The Sampler is reimplemented to avoid in Dymola 2016 the translation warning
  // for the models in the parent package:
  //   The initial conditions for variables of type Boolean are not fully specified.
  //   Dymola has selected default initial conditions.
  //   Assuming fixed default start value for the discrete non-states:
  //     ...firstTrigger(start = false)
  //     ...
protected
  
  block Sampler
    extends Modelica.Blocks.Discrete.Sampler(firstTrigger(start=false, fixed=true));
  end Sampler;

equation
  connect(tri.y, notEventDay.u)
    annotation (Line(
      points={{-19, 80}, {-2, 80}},
      color={255, 0, 255},
      smooth=Smooth.None
    ));
  connect(notEventDay.y, baseLoad.storeHistory)
    annotation (Line(
      points={{21, 80}, {44, 80}, {44, 5}, {58, 5}},
      color={255, 0, 255},
      smooth=Smooth.None
    ));
  connect(dayType.y, baseLoad.typeOfDay)
    annotation (Line(
      points={{-19, 40}, {18, 40}, {18, 10}, {58, 10}},
      color={0, 127, 0},
      smooth=Smooth.None
    ));

  annotation (
    Documentation(
      info="<html>
<p>
Partial base class to build test for the load prediction.
</p>
<p>
This model has been added to the library to verify and demonstrate the correct implementation
of the load prediction model based on a simple input scenario.
</p>
</html>",
      revisions="<html>
<ul>
<li>
September 24, 2015 by Michael Wetter:<br/>
Implemented <code>Sampler</code> to avoid a translation warning
because <code>Sampler.firstTrigger</code> does not set the <code>fixed</code>
attribute in MSL 3.2.1.
</li>
<li>
March 20, 2014 by Michael Wetter:<br/>
First implementation.
</li>
</ul>
</html>"
    )
  );
end PartialSimpleTestCase;
""";

    var expectedOutput = """
partial model PartialSimpleTestCase "Partial base class for simple test case of base load prediction"
  extends Modelica.Icons.Example;
  parameter Modelica.Units.SI.Time tPeriod=24*3600 "Period";
  parameter Modelica.Units.SI.Time tSample=3600 "Sampling period";
  parameter Integer nPre(min=1)=12 "Number of time steps to predict";
  ElectricalLoad baseLoad(final nPre=nPre,
    use_dayOfAdj=false,
    predictionModel=Buildings.Controls.Predictors.Types.PredictionModel.Average) "Baseload prediction"
    annotation (Placement(transformation(extent={{60, -10}, {80, 10}})));
  Modelica.Blocks.Sources.BooleanPulse tri(width=4/24*100/7,
    period=7*tPeriod,
    startTime=1.5*tPeriod) "Sample trigger"
    annotation (Placement(transformation(extent={{-40, 70}, {-20, 90}})));
  Sources.DayType dayType(nout=2)
    "Outputs the type of the day for each hour where load is to be predicted"
    annotation (Placement(transformation(extent={{-40, 30}, {-20, 50}})));
  Modelica.Blocks.Logical.Not notEventDay "Output true if it is not an event day"
    annotation (Placement(transformation(extent={{0, 70}, {20, 90}})));
  // The Sampler is reimplemented to avoid in Dymola 2016 the translation warning
  // for the models in the parent package:
  //   The initial conditions for variables of type Boolean are not fully specified.
  //   Dymola has selected default initial conditions.
  //   Assuming fixed default start value for the discrete non-states:
  //     ...firstTrigger(start = false)
  //     ...
protected
  
  block Sampler
    extends Modelica.Blocks.Discrete.Sampler(firstTrigger(start=false, fixed=true));
  end Sampler;

equation
  connect(tri.y, notEventDay.u)
    annotation (Line(
      points={{-19, 80}, {-2, 80}},
      color={255, 0, 255},
      smooth=Smooth.None
    ));
  connect(notEventDay.y, baseLoad.storeHistory)
    annotation (Line(
      points={{21, 80}, {44, 80}, {44, 5}, {58, 5}},
      color={255, 0, 255},
      smooth=Smooth.None
    ));
  connect(dayType.y, baseLoad.typeOfDay)
    annotation (Line(
      points={{-19, 40}, {18, 40}, {18, 10}, {58, 10}},
      color={0, 127, 0},
      smooth=Smooth.None
    ));

  annotation (
    Documentation(
      info="<html>
<p>
Partial base class to build test for the load prediction.
</p>
<p>
This model has been added to the library to verify and demonstrate the correct implementation
of the load prediction model based on a simple input scenario.
</p>
</html>",
      revisions="<html>
<ul>
<li>
September 24, 2015 by Michael Wetter:<br/>
Implemented <code>Sampler</code> to avoid a translation warning
because <code>Sampler.firstTrigger</code> does not set the <code>fixed</code>
attribute in MSL 3.2.1.
</li>
<li>
March 20, 2014 by Michael Wetter:<br/>
First implementation.
</li>
</ul>
</html>"
    )
  );
end PartialSimpleTestCase;
""";

    TestHelpers.AssertClass(testModel,false,expectedOutput, 100, true);
    }

    [Fact]
    public void Render_WithMultipleImports_AllFormattedCorrectly()
    {
        var testModel="""
within Modelica.Fluid.Fittings;
model MultiPort "Multiply a port; useful if multiple connections shall be made to a port exposing a state"

  function positiveMax
    extends Modelica.Icons.Function;
    input Real x;
    output Real y;

  algorithm
    y := max(x, 1e-10);
  end positiveMax;
  import Modelica.Constants;
  replaceable package Medium = Modelica.Media.Interfaces.PartialMedium
    annotation (choicesAllMatching);
  // Ports
  parameter Integer nPorts_b=0
    "Number of outlet ports (mass is distributed evenly between the outlet ports"
    annotation (Dialog(connectorSizing=true));
  Modelica.Fluid.Interfaces.FluidPort_a port_a(redeclare package Medium = Medium)
    annotation (Placement(transformation(extent={{-50, -10}, {-30, 10}})));
  Modelica.Fluid.Interfaces.FluidPorts_b ports_b[nPorts_b](redeclare each package Medium = Medium)
    annotation (Placement(transformation(extent={{30, 40}, {50, -40}})));
  Medium.MassFraction ports_b_Xi_inStream[nPorts_b, Medium.nXi] "inStream mass fractions at ports_b";
  Medium.ExtraProperty ports_b_C_inStream[nPorts_b, Medium.nC] "inStream extra properties at ports_b";

equation
  // Only one connection allowed to a port to avoid unwanted ideal mixing
  for i in 1:nPorts_b loop
    assert(cardinality(ports_b[i]) <= 1, "
each ports_b[i] of boundary shall at most be connected to one component.
If two or more connections are present, ideal mixing takes
place with these connections, which is usually not the intention
of the modeller. Increase nPorts_b to add an additional port.
");
  end for;
  // mass and momentum balance
  0 = port_a.m_flow + sum(ports_b.m_flow);
  ports_b.p = fill(port_a.p, nPorts_b);
  // mixing at port_a
  port_a.h_outflow = sum({positiveMax(ports_b[j].m_flow)*inStream(ports_b[j].h_outflow) for j in 1:nPorts_b})/sum({positiveMax(ports_b[j].m_flow) for j in 1:nPorts_b});
  for j in 1:nPorts_b loop
    // expose stream values from port_a to ports_b
    ports_b[j].h_outflow = inStream(port_a.h_outflow);
    ports_b[j].Xi_outflow = inStream(port_a.Xi_outflow);
    ports_b[j].C_outflow = inStream(port_a.C_outflow);
    ports_b_Xi_inStream[j, :] = inStream(ports_b[j].Xi_outflow);
    ports_b_C_inStream[j, :] = inStream(ports_b[j].C_outflow);
  end for;
  for i in 1:Medium.nXi loop
    port_a.Xi_outflow[i] = (positiveMax(ports_b.m_flow)*ports_b_Xi_inStream[:, i])/sum(positiveMax(ports_b.m_flow));
  end for;
  for i in 1:Medium.nC loop
    port_a.C_outflow[i] = (positiveMax(ports_b.m_flow)*ports_b_C_inStream[:, i])/sum(positiveMax(ports_b.m_flow));
  end for;

  annotation (
    Icon(
      coordinateSystem(
        preserveAspectRatio=true,
        extent={{-40, -100}, {40, 100}}
      ),
      graphics={
        Line(
          points={{-40, 0}, {40, 0}},
          color={0, 128, 255},
          thickness=1
        ),
        Line(
          points={{-40, 0}, {40, 26}},
          color={0, 128, 255},
          thickness=1
        ),
        Line(
          points={{-40, 0}, {40, -26}},
          color={0, 128, 255},
          thickness=1
        ),
        Text(
          extent={{-150, 100}, {150, 60}},
          textColor={0, 0, 255},
          textString="%name"
        )
      }
    ),
    Documentation(info="<html>
<p>
This model is useful if multiple connections shall be made to a port of a volume model exposing a state,
like a pipe with ModelStructure av_vb.
The mixing is shifted into the volume connected to port_a and the result is propagated back to each ports_b.
</p>
<p>
If multiple connections were directly made to the volume,
then ideal mixing would take place in the connection set, outside the volume. This is normally not intended.
</p>
</html>")
  );
end MultiPort;
""";

        var expectedOutput="""
model MultiPort "Multiply a port; useful if multiple connections shall be made to a port exposing a state"
  import Modelica.Constants;

  function positiveMax
    extends Modelica.Icons.Function;
    input Real x;
    output Real y;

  algorithm
    y := max(x, 1e-10);
  end positiveMax;
  replaceable package Medium = Modelica.Media.Interfaces.PartialMedium
    annotation (choicesAllMatching);
  // Ports
  parameter Integer nPorts_b=0
    "Number of outlet ports (mass is distributed evenly between the outlet ports"
    annotation (Dialog(connectorSizing=true));
  Modelica.Fluid.Interfaces.FluidPort_a port_a(redeclare package Medium = Medium)
    annotation (Placement(transformation(extent={{-50, -10}, {-30, 10}})));
  Modelica.Fluid.Interfaces.FluidPorts_b ports_b[nPorts_b](redeclare each package Medium = Medium)
    annotation (Placement(transformation(extent={{30, 40}, {50, -40}})));
  Medium.MassFraction ports_b_Xi_inStream[nPorts_b, Medium.nXi] "inStream mass fractions at ports_b";
  Medium.ExtraProperty ports_b_C_inStream[nPorts_b, Medium.nC] "inStream extra properties at ports_b";

equation
  // Only one connection allowed to a port to avoid unwanted ideal mixing
  for i in 1:nPorts_b loop
    assert(cardinality(ports_b[i]) <= 1, "
each ports_b[i] of boundary shall at most be connected to one component.
If two or more connections are present, ideal mixing takes
place with these connections, which is usually not the intention
of the modeller. Increase nPorts_b to add an additional port.
");
  end for;
  // mass and momentum balance
  0 = port_a.m_flow + sum(ports_b.m_flow);
  ports_b.p = fill(port_a.p, nPorts_b);
  // mixing at port_a
  port_a.h_outflow = sum({positiveMax(ports_b[j].m_flow)*inStream(ports_b[j].h_outflow) for j in 1:nPorts_b})/sum({positiveMax(ports_b[j].m_flow) for j in 1:nPorts_b});
  for j in 1:nPorts_b loop
    // expose stream values from port_a to ports_b
    ports_b[j].h_outflow = inStream(port_a.h_outflow);
    ports_b[j].Xi_outflow = inStream(port_a.Xi_outflow);
    ports_b[j].C_outflow = inStream(port_a.C_outflow);
    ports_b_Xi_inStream[j, :] = inStream(ports_b[j].Xi_outflow);
    ports_b_C_inStream[j, :] = inStream(ports_b[j].C_outflow);
  end for;
  for i in 1:Medium.nXi loop
    port_a.Xi_outflow[i] = (positiveMax(ports_b.m_flow)*ports_b_Xi_inStream[:, i])/sum(positiveMax(ports_b.m_flow));
  end for;
  for i in 1:Medium.nC loop
    port_a.C_outflow[i] = (positiveMax(ports_b.m_flow)*ports_b_C_inStream[:, i])/sum(positiveMax(ports_b.m_flow));
  end for;

  annotation (
    Icon(
      coordinateSystem(
        preserveAspectRatio=true,
        extent={{-40, -100}, {40, 100}}
      ),
      graphics={
        Line(
          points={{-40, 0}, {40, 0}},
          color={0, 128, 255},
          thickness=1
        ),
        Line(
          points={{-40, 0}, {40, 26}},
          color={0, 128, 255},
          thickness=1
        ),
        Line(
          points={{-40, 0}, {40, -26}},
          color={0, 128, 255},
          thickness=1
        ),
        Text(
          extent={{-150, 100}, {150, 60}},
          textColor={0, 0, 255},
          textString="%name"
        )
      }
    ),
    Documentation(info="<html>
<p>
This model is useful if multiple connections shall be made to a port of a volume model exposing a state,
like a pipe with ModelStructure av_vb.
The mixing is shifted into the volume connected to port_a and the result is propagated back to each ports_b.
</p>
<p>
If multiple connections were directly made to the volume,
then ideal mixing would take place in the connection set, outside the volume. This is normally not intended.
</p>
</html>")
  );
end MultiPort;
""";
    var output1 = TestHelpers.AssertClass(testModel,false,expectedOutput, 100, true, true, false);
    var output2 = TestHelpers.AssertClass(output1,false,expectedOutput, 100, true, true, false);
    TestHelpers.AssertClass(output2,false,expectedOutput, 100, true, true, false);
    }
}
