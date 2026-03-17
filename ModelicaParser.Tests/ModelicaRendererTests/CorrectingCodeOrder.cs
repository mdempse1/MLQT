using System;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

public class CorrectingCodeOrder
{

    [Fact]
    public void MultiplePublic_NoChange()
    {
        var testModel = """ 
            model Test
              Real y;
            public
              Real z;
              Real a;
            public
              Real b;

            equation
              y = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultiplePublic_Corrected()
    {
        var testModel = """ 
            model Test
              Real y;
            public
              Real z;
              Real a;
            public
              Real b;

            equation
              y = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              Real y;
              Real z;
              Real a;
              Real b;

            equation
              y = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }

    [Fact]
    public void MultipleProtected_NoChange()
    {
        var testModel = """ 
            model Test
              Real y;
            protected
              Real z;
              Real a;
            protected
              Real b;

            equation
              y = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleProtected_Corrected()
    {
        var testModel = """ 
            model Test
              Real y;
            protected
              Real z;
              Real a;
            protected
              Real b;

            equation
              y = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              Real y;
            protected
              Real z;
              Real a;
              Real b;

            equation
              y = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }

    [Fact]
    public void MultiplePublicProtected_NoChange()
    {
        var testModel = """ 
            model Test
              Real y;
            protected
              Real z;
              Real a;
            public
              Real b;
            protected
              Real c;
            public
              Real d;
            protected
              Real e;

            equation
              y = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultiplePublicProtected_Corrected()
    {
        var testModel = """ 
            model Test
              Real y;
            protected
              Real z;
              Real a;
            public
              Real b;
            protected
              Real c;
            public
              Real d;
            protected
              Real e;

            equation
              y = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              Real y;
              Real b;
              Real d;
            protected
              Real z;
              Real a;
              Real c;
              Real e;

            equation
              y = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }

    [Fact]
    public void MultipleEquation_NoChange()
    {
        var testModel = """ 
            model Test
              Real y;

            equation
              y = 1;

            equation
              z = 1;
              a = 1;
              
            equation
              b = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleEquation_Corrected()
    {
        var testModel = """ 
            model Test
              Real y;

            equation
              y = 1;

            equation
              z = 1;
              a = 1;

            equation
              b = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              Real y;

            equation
              y = 1;
              z = 1;
              a = 1;
              b = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }


    [Fact]
    public void MultipleAlgorithm_NoChange()
    {
        var testModel = """ 
            model Test
              Real y;

            algorithm
              y := 1;

            algorithm
              z := 1;
              a := 1;

            algorithm
              b := 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleAlgorithm_Corrected()
    {
        var testModel = """ 
            model Test
              Real y;

            algorithm
              y := 1;

            algorithm
              z := 1;
              a := 1;

            algorithm
              b := 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              Real y;

            algorithm
              y := 1;
              z := 1;
              a := 1;
              b := 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }

    [Fact]
    public void External_NoChange()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x);
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void External_Corrected()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x);
            end Test;
            """;

        var expectedOutput = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x);
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }

    [Fact]
    public void ExternalWithAnnotation_NoChange()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x)
              annotation (Library="Lib");
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ExternalWithAnnotationAndClassAnnotation_NoChange()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x)
              annotation (Library="Lib");

              annotation (
                Documentation(info="class annotation here")
              );
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ExternalWithAnnotationClassAnnotationAndFinalComment_NoChange()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x)
              annotation (Library="Lib");

              annotation (
                Documentation(info="class annotation here")
              );
            // A comment
            // on two lines
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }


    [Fact]
    public void ExternalWithAnnotationClassAnnotationAndFinalMultiLineComment_NoChange()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x)
              annotation (Library="Lib");

              annotation (
                Documentation(info="class annotation here")
              );
            /* A comment
            on two lines */
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }


    [Fact]
    public void ExternalWithAnnotationClassAnnotationAndComments_NoChange()
    {
        var testModel = """ 
            function Test
              input Real x;
              output Real y;
            external "C" y = f(x)
              annotation (Library="Lib");
            //Another comment

              annotation (
                Documentation(info="class annotation here")
              );
            // A comment
            // on two lines
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Imports_NoChange()
    {
        var testModel = """ 
            model Test
              Real x;
              Real y;
              import AnotherLib;

            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Imports_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
              Real y;
              import AnotherLib;

            equation
              x = 1;
            end Test;
            """;
            
        var expectedOutput = """ 
            model Test
              import AnotherLib;
              Real x;
              Real y;

            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }    

    [Fact]
    public void MultipleImports_NoChange()
    {
        var testModel = """ 
            model Test
              Real x;
              Real y;
            public
              import AnotherLib;
              Real a;
            protected
              import SomethingElse;
              Real b;

            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleImports_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
              Real y;
            public
              import AnotherLib;
              Real a;
            protected
              import SomethingElse;
              Real b;
            
            equation
              x = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              import AnotherLib;
              import SomethingElse;
              Real x;
              Real y;
              Real a;
            protected
              Real b;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }    

    [Fact]
    public void PublicExtends_NoChange()
    {
        var testModel = """ 
            model Test
              Real x;
              Real y;
              extends AnotherLib;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void PublicExtends_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
              Real y;
              extends AnotherLib;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              extends AnotherLib;
              Real x;
              Real y;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }    

    [Fact]
    public void ProtectedExtends_NoChange()
    {
        var testModel = """ 
            model Test
              Real x;
            protected
              Real y;
              extends AnotherLib;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ProtectedExtends_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
            protected
              Real y;
              extends AnotherLib;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              Real x;
            protected
              extends AnotherLib;
              Real y;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }    

    [Fact]
    public void MultipleExtends_NoChange()
    {
        var testModel = """ 
            model Test
              Real x;
              extends Something;
            protected
              Real y;
              extends AnotherLib;
              Real a;
            public
              extends AnotherThing;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleExtends_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
              extends Something;
            protected
              Real y;
              extends AnotherLib;
              Real a;
            public
              extends AnotherThing;
            
            equation
              x = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              extends Something;
              extends AnotherThing;
              Real x;
            protected
              extends AnotherLib;
              Real y;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }        

   [Fact]
    public void AllCodeElements_NoChange()
    {
        var testModel = """ 
            model Test
              Real x;
              extends Something;
              import Units;
            protected
              Real y;
              extends AnotherLib;
              Real a;
            public
              extends AnotherThing;
              import MoreUnits;
            protected
              extends BaseClass;
              import EvenMore;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void AllCodeElements_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
              extends Something;
              import Units;
            protected
              Real y;
              extends AnotherLib;
              Real a;
            public
              extends AnotherThing;
              import MoreUnits;
            protected
              extends BaseClass;
              import EvenMore;
            
            equation
              x = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              import Units;
              import MoreUnits;
              import EvenMore;
              extends Something;
              extends AnotherThing;
              Real x;
            protected
              extends AnotherLib;
              extends BaseClass;
              Real y;
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }     

    [Fact]
    public void CommentAtEndOfPublic_Corrected()
    {
        var testModel = """ 
            model Test
              Real x;
              extends Something;
              import Units;
              //a comment before extends
              extends AnotherThing;
              import MoreUnits;
              //final comment in the last public section
            protected
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        var expectedOutput = """ 
            model Test
              import Units;
              import MoreUnits;
              extends Something;
              //a comment before extends
              extends AnotherThing;
              Real x;
              //final comment in the last public section
            protected
              Real a;
            
            equation
              x = 1;
            end Test;
            """;

        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput, onlyOneOfEachSection: true);
    }

    [Fact]
    public void InterleavedSectionsWithRecords_Idempotent()
    {
        // Reproduces the pattern from FMU-generated models: interleaved public/protected
        // sections with both components and records (long class definitions).
        // With all reordering rules enabled, the first pass reorders elements.
        // The second pass must produce identical output.
        var testModel = """
            model TestModel "Test model"
              extends BaseIcon;
            public
              encapsulated package Types
                type MyReal = Real;
                type MyBool = Boolean;
              end Types;
              constant Integer n=4 "Number of items";
              constant Boolean flag=false "A flag";
              parameter Boolean useFeature=false "Enable feature";
            protected
              record controlSettings_rec
                Boolean timeDomain "Run for time domain";
                Real timeStep "Step size";
              end controlSettings_rec;
            public
              controlSettings_rec controlSettings;
            protected
              record controller_rec
                constant Boolean active=true "Active flag";
                parameter Integer nItems=4 "Number of items";
                parameter Real gain=4 "Gain of controller";
              protected
                record bus_rec
                protected
                  record driver_rec
                    Real position "Position signal";
                  end driver_rec;
                public
                  driver_rec driver;
                end bus_rec;
              public
                bus_rec bus;
              end controller_rec;
            public
              controller_rec controller annotation (Dialog);
            protected
              record losses_rec
                constant Boolean tableOnFile=false "Table on file";
                Real torqueLosses "Torque losses";
              end losses_rec;
            public
              losses_rec losses annotation (Dialog);

            initial equation
              controlSettings.timeStep = 0.001;

            equation
              controlSettings.timeDomain = true;
            end TestModel;
            """;

        var firstPass = TestHelpers.FormatCode(
            testModel,
            onlyOneOfEachSection: true,
            importsFirst: true,
            componentsBeforeClasses: true);
        var secondPass = TestHelpers.FormatCode(
            firstPass,
            onlyOneOfEachSection: true,
            importsFirst: true,
            componentsBeforeClasses: true);
        Assert.Equal(firstPass, secondPass);
    }

    private static List<string> RenderWithOneOfEachSection(string code, bool importsFirst = true)
    {
        var modelicaCode = code.StartsWith("within") ? code : "within;\n" + code;
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(modelicaCode);
        var visitor = new ModelicaRenderer(
            renderForCodeEditor: false,
            showAnnotations: true,
            excludeClassDefinitions: false,
            tokenStream,
            classNamesToExclude: null,
            maxLineLength: 100,
            oneOfEachSection: true,
            importsFirst: importsFirst,
            componentsBeforeClasses: importsFirst);
        visitor.Visit(parseTree);
        return visitor.Code;
    }

    [Fact]
    public void InitialEquation_WithOneOfEachSection_RendersCorrectly()
    {
        // Covers VisitEquation_section InitialEquation branch (lines 1841-1859 in ModelicaRenderer.cs)
        // Requires _oneOfEachSection == true AND code has 'initial equation' section
        var code = """
model WithInitialEquation "model with initial and regular equations"
  Real x "state variable";
initial equation
  x = 0.0;
equation
  der(x) = 1.0;
end WithInitialEquation;
""";

        var result = RenderWithOneOfEachSection(code);
        var fullCode = string.Join("\n", result);
        Assert.Contains("initial equation", fullCode);
        Assert.Contains("x = 0.0;", fullCode);
        Assert.Contains("der(x) = 1.0;", fullCode);
    }

    [Fact]
    public void InitialAlgorithm_WithOneOfEachSection_RendersCorrectly()
    {
        // Covers VisitAlgorithm_section InitialAlgorithm branch (lines 1906-1924 in ModelicaRenderer.cs)
        // Requires _oneOfEachSection == true AND code has 'initial algorithm' section
        var code = """
model WithInitialAlgorithm "model with initial and regular algorithms"
  Real x "counter";
initial algorithm
  x := 0.0;
algorithm
  x := x + 1.0;
end WithInitialAlgorithm;
""";

        var result = RenderWithOneOfEachSection(code);
        var fullCode = string.Join("\n", result);
        Assert.Contains("initial algorithm", fullCode);
        Assert.Contains("x := 0.0;", fullCode);
        Assert.Contains("x := x + 1.0;", fullCode);
    }

    [Fact]
    public void Model_WithOneOfEachSectionAndImportsLast_RendersCorrectly()
    {
        // Covers WriteComposition calls for public (line 770) and protected (line 787)
        // when _oneOfEachSection == true but _importsFirst == false
        var code = """
model TestImportsLast "model with elements in default order"
  Real x "public variable";
protected
  Real tmp "internal variable";
equation
  x = 1.0;
  tmp = x;
end TestImportsLast;
""";

        var result = RenderWithOneOfEachSection(code, importsFirst: false);
        var fullCode = string.Join("\n", result);
        Assert.Contains("Real x", fullCode);
        Assert.Contains("Real tmp", fullCode);
        Assert.Contains("x = 1.0;", fullCode);
    }

}
