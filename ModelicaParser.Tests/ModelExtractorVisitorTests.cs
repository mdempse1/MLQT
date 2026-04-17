using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using System.Reflection;

namespace ModelicaParser.Tests;

public class ModelExtractorVisitorTests
{
  [Fact]
  public void ExtractModels_SimpleModel_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
model SimpleModel
  Real x;
end SimpleModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("SimpleModel", models[0].Name);
    Assert.Equal("model", models[0].ClassType);
    Assert.False(models[0].IsNested);
    Assert.Equal("", models[0].ParentModelName);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Contains("Real x;", models[0].SourceCode);
  }

  [Fact]
  public void ExtractModels_WithinStatement_SetsParentPackage()
  {
    // Arrange
    var code = @"
within MyPackage;
model TestModel
  Real x;
end TestModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("TestModel", models[0].Name);
    Assert.Equal("MyPackage", models[0].ParentModelName);
    Assert.False(models[0].IsNested);
  }

  [Fact]
  public void ExtractModels_WithinNestedPackage_SetsFullParentPath()
  {
    // Arrange
    var code = @"
within MyPackage.SubPackage;
model TestModel
  Real x;
end TestModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("TestModel", models[0].Name);
    Assert.Equal("MyPackage.SubPackage", models[0].ParentModelName);
  }

  [Fact]
  public void ExtractModels_NestedModel_DetectsNesting()
  {
    // Arrange
    var code = @"
model OuterModel
  Real x;

  model InnerModel
    Real y;
  end InnerModel;

end OuterModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(2, models.Count);

    var outer = models[0];
    Assert.Equal("OuterModel", outer.Name);
    Assert.False(outer.IsNested);

    var inner = models[1];
    Assert.Equal("InnerModel", inner.Name);
    Assert.True(inner.IsNested);
    Assert.Equal("OuterModel", inner.ParentModelName);
  }

  [Fact]
  public void ExtractModels_DeeplyNestedModels_TracksFullHierarchy()
  {
    // Arrange
    var code = @"
within MyPackage;
model Level1
  Real x;

  model Level2
    Real y;

    model Level3
      Real z;
    end Level3;
  end Level2;
end Level1;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(3, models.Count);

    Assert.Equal("Level1", models[0].Name);
    Assert.Equal("MyPackage", models[0].ParentModelName);
    Assert.False(models[0].IsNested);

    Assert.Equal("Level2", models[1].Name);
    Assert.Equal("MyPackage.Level1", models[1].ParentModelName);
    Assert.True(models[1].IsNested);

    Assert.Equal("Level3", models[2].Name);
    Assert.Equal("MyPackage.Level1.Level2", models[2].ParentModelName);
    Assert.True(models[2].IsNested);
  }

  [Fact]
  public void ExtractModels_MultipleModelsAtSameLevel_ExtractsAll()
  {
    // Arrange
    var code = @"
model Model1
  Real x;
end Model1;

model Model2
  Real y;
end Model2;

model Model3
  Real z;
end Model3;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(3, models.Count);
    Assert.Equal("Model1", models[0].Name);
    Assert.Equal("Model2", models[1].Name);
    Assert.Equal("Model3", models[2].Name);
    Assert.All(models, m => Assert.False(m.IsNested));
  }

  [Fact]
  public void ExtractModels_Block_DetectsCorrectType()
  {
    // Arrange
    var code = @"
block MyBlock
  Real x;
end MyBlock;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("MyBlock", models[0].Name);
    Assert.Equal("block", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_Function_DetectsCorrectType()
  {
    // Arrange
    var code = @"
function myFunction
  input Real x;
  output Real y;
algorithm
  y := x * 2;
end myFunction;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("myFunction", models[0].Name);
    Assert.Equal("function", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_Connector_DetectsCorrectType()
  {
    // Arrange
    var code = @"
connector MyConnector
  Real value;
end MyConnector;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("MyConnector", models[0].Name);
    Assert.Equal("connector", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_Record_DetectsCorrectType()
  {
    // Arrange
    var code = @"
record MyRecord
  Real x;
  Real y;
end MyRecord;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("MyRecord", models[0].Name);
    Assert.Equal("record", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_Type_DetectsCorrectType()
  {
    // Arrange
    var code = @"
type Voltage = Real(unit=""V"");";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("Voltage", models[0].Name);
    Assert.Equal("type", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_Package_DetectsCorrectType()
  {
    // Arrange
    var code = @"
package MyPackage
  model TestModel
    Real x;
  end TestModel;
end MyPackage;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(2, models.Count);
    Assert.Equal("MyPackage", models[0].Name);
    Assert.Equal("package", models[0].ClassType);
    Assert.Equal("TestModel", models[1].Name);
  }

  [Fact]
  public void ExtractModels_Class_DetectsCorrectType()
  {
    // Arrange
    var code = @"
class MyClass
  Real x;
end MyClass;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("MyClass", models[0].Name);
    Assert.Equal("class", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_PartialModel_DetectsModelType()
  {
    // Arrange
    var code = @"
partial model PartialModel
  Real x;
end PartialModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("PartialModel", models[0].Name);
    Assert.Equal("model", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_EncapsulatedModel_DetectsModelType()
  {
    // Arrange
    var code = @"
encapsulated model EncapsulatedModel
  Real x;
end EncapsulatedModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("EncapsulatedModel", models[0].Name);
    Assert.Equal("model", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_ShortClassSpecifier_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
model MyModel = BaseModel(param1=5);";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("MyModel", models[0].Name);
    Assert.Equal("model", models[0].ClassType);
    Assert.Contains("BaseModel", models[0].SourceCode);
  }

  [Fact]
  public void ExtractPackage_ShortClassSpecifier_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
package StandardWaterOnePhase = WaterIF97_pT 
  ""Water using the IF97 standard, explicit in p and T. Recommended for one-phase applications"";";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("StandardWaterOnePhase", models[0].Name);
    Assert.Equal("package", models[0].ClassType);
    Assert.Contains("WaterIF97_pT", models[0].SourceCode);
  }

  [Fact]
  public void ExtractNestedPackage_ShortClassSpecifier_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
package Water ""Water package""        
  package StandardWaterOnePhase = WaterIF97_pT 
    ""Water using the IF97 standard, explicit in p and T. Recommended for one-phase applications"";
end Water;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(2, models.Count);
    Assert.Equal("Water", models[0].Name);
    Assert.Equal("package", models[0].ClassType);
    Assert.Contains("StandardWaterOnePhase", models[0].SourceCode);
  }

  [Fact]
  public void ExtractModels_DerClassSpecifier_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
function derivativeFunc = der(baseFunc, x);";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("derivativeFunc", models[0].Name);
    Assert.Equal("function", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_ReplaceableModel_ExtractedAsNonStandalone()
  {
    // Arrange - replaceable models are elements of their parent, extracted but not standalone
    var code = @"
model Container
  replaceable model SubModel
    Real x;
  end SubModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal(string.Empty, models[0].ElementPrefix);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("replaceable", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_ReplaceableModelShortClass_ExtractedAsNonStandalone()
  {
    // Arrange - replaceable short class definitions are elements of their parent
    var code = @"
model Container
  replaceable model SubModel = AnotherModel.ClassType;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("replaceable", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_RedeclareModel_ExtractedAsNonStandalone()
  {
    // Arrange - redeclare models are elements of their parent
    var code = @"
model Container
  redeclare model SubModel
    Real x;
  end SubModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("redeclare", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_InnerModel_ExtractedAsNonStandalone()
  {
    // Arrange - inner models are elements of their parent
    var code = @"
model Container
  inner model SubModel
    Real x;
  end SubModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("inner", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_OuterModel_ExtractedAsNonStandalone()
  {
    // Arrange - outer models are elements of their parent
    var code = @"
model Container
  outer model SubModel
    Real x;
  end SubModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("outer", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_RedeclareFinalModel_ExtractedAsNonStandalone()
  {
    // Arrange - redeclare final models are elements of their parent
    var code = @"
model Container
  redeclare final model SubModel
    Real x;
  end SubModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("redeclare final", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_MultipleNonStandalonePrefixes_ExtractedAsNonStandalone()
  {
    // Arrange - inner replaceable models are elements of their parent
    var code = @"
model Container
  inner replaceable model SubModel
    Real x;
  end SubModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, SubModel marked as non-standalone with prefix
    Assert.Equal(2, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("SubModel", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("inner replaceable", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_StandaloneModelAfterPrefixed_ResetsFlag()
  {
    // Arrange - prefixed models are non-standalone, regular nested models are standalone
    var code = @"
model Container
  replaceable model NonStandalone
    Real x;
  end NonStandalone;

  model Standalone
    Real y;
  end Standalone;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert - all three extracted, NonStandalone is non-standalone, Standalone is standalone
    Assert.Equal(3, models.Count);
    Assert.Equal("Container", models[0].Name);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal(string.Empty, models[0].ElementPrefix);

    Assert.Equal("NonStandalone", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("replaceable", models[1].ElementPrefix);

    Assert.Equal("Standalone", models[2].Name);
    Assert.True(models[2].CanBeStoredStandalone);
    Assert.True(models[2].IsNested);
    Assert.Equal(string.Empty, models[2].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_TracksLineNumbers()
  {
    // Arrange
    var code = @"
model Model1
  Real x;
end Model1;

model Model2
  Real y;
end Model2;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(2, models.Count);
    Assert.Equal(2, models[0].StartLine);
    Assert.Equal(4, models[0].StopLine);
    Assert.Equal(6, models[1].StartLine);
    Assert.Equal(8, models[1].StopLine);
  }

  [Fact]
  public void ExtractModels_ExtractsSourceCode()
  {
    // Arrange
    var code = @"
model TestModel
  Real x;
  Real y;
equation
  y = x * 2;
end TestModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Contains("Real x;", models[0].SourceCode);
    Assert.Contains("Real y;", models[0].SourceCode);
    Assert.Contains("y = x * 2;", models[0].SourceCode);
  }

  [Fact]
  public void ExtractModels_EmptyModel_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
model EmptyModel
end EmptyModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("EmptyModel", models[0].Name);
  }

  [Fact]
  public void ExtractModels_ModelWithAnnotations_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
model AnnotatedModel
  Real x annotation(Documentation(info=""<html>Test</html>""));
end AnnotatedModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("AnnotatedModel", models[0].Name);
    Assert.Contains("annotation", models[0].SourceCode);
  }

  [Fact]
  public void ExtractModels_MultipleWithinStatements_UsesFirst()
  {
    // Arrange
    var code = @"
within Package1;
within Package2;
model TestModel
  Real x;
end TestModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("Package1", models[0].ParentModelName);
  }

  [Fact]
  public void ExtractModels_EmptyWithinStatement_HandlesGracefully()
  {
    // Arrange
    var code = @"
within;
model TestModel
  Real x;
end TestModel;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("TestModel", models[0].Name);
  }

  [Fact]
  public void ExtractModels_NoModels_ReturnsEmptyList()
  {
    // Arrange
    var code = @"
within MyPackage;
// Just comments, no models";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Empty(models);
  }

  [Fact]
  public void ExtractModels_ComplexNesting_ExtractsAllLevels()
  {
    // Arrange
    var code = @"
within MyPackage;

package SubPackage
  model OuterModel
    Real a;

    model MiddleModel
      Real b;

      model InnerModel
        Real c;
      end InnerModel;
    end MiddleModel;
  end OuterModel;

  model AnotherModel
    Real d;
  end AnotherModel;
end SubPackage;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(5, models.Count);

    Assert.Equal("SubPackage", models[0].Name);
    Assert.Equal("package", models[0].ClassType);
    Assert.Equal("MyPackage", models[0].ParentModelName);

    Assert.Equal("OuterModel", models[1].Name);
    Assert.Equal("MyPackage.SubPackage", models[1].ParentModelName);

    Assert.Equal("MiddleModel", models[2].Name);
    Assert.Equal("MyPackage.SubPackage.OuterModel", models[2].ParentModelName);

    Assert.Equal("InnerModel", models[3].Name);
    Assert.Equal("MyPackage.SubPackage.OuterModel.MiddleModel", models[3].ParentModelName);

    Assert.Equal("AnotherModel", models[4].Name);
    Assert.Equal("MyPackage.SubPackage", models[4].ParentModelName);
  }

  [Fact]
  public void ExtractModels_OperatorFunction_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
encapsulated operator function '+'
  input Complex c1;
  input Complex c2;
  output Complex result;
algorithm
  result.re := c1.re + c2.re;
  result.im := c1.im + c2.im;
end '+';";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("'+'", models[0].Name);
    Assert.Equal("function", models[0].ClassType);
  }

  [Fact]
  public void ExtractModels_OperatorRecord_ExtractsCorrectly()
  {
    // Arrange
    var code = @"
encapsulated operator record Complex
  Real re;
  Real im;
end Complex;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("Complex", models[0].Name);
    Assert.Equal("record", models[0].ClassType);
  }

  private List<ModelInfo> ExtractModels(string code)
  {
    var parseTree = ModelicaParserHelper.Parse(code);    
    var visitor = new ModelExtractorVisitor(ModelicaParserHelper.NormalizeLineEndings(code));
    visitor.Visit(parseTree);
    return visitor.Models;
  }

  [Fact]
  public void ComplexNestedNames_ExtractsCorrectly()
  {
    //Arrange
    var code = """
class 'function' "function"
  extends ModelicaReference.Icons.Information;

  class 'function partial application' "function partial application"
      extends ModelicaReference.Icons.Information;

      annotation (Documentation(info="<html>
<p>
A function partial application is a function call with certain
formal parameters bound to expressions. 
</p>
</html>"));
  end 'function partial application';

class 'pure function' "pure function"
  extends ModelicaReference.Icons.Information;

  annotation (Documentation(info="<html>
    <p>
Modelica functions are normally pure which makes it easy for humans to reason about the code
since they behave as mathematical functions, and possible for compilers to optimize.</p>
    </html>"));
end 'pure function';

   annotation (Documentation(info="<html>
<p>
Define specialized class <em>function</em>
</p>
</html>"));
end 'function';
""";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(3, models.Count);
    Assert.Equal("'function'", models[0].Name);
    Assert.Equal("'function partial application'", models[1].Name);
    Assert.Equal("'pure function'", models[2].Name);

  }

  [Fact]
  public void ExtractModels_GetVersionAnnotation_ExtractsCorrectly()
  {
    // Arrange
    var code = """
package TestModel

  annotation (version="1.2.3");
end Complex;
""";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("1.2.3", models[0].Version);
  }  

  [Fact]
  public void ExtractModels_GetUsesAnnotation_ExtractsCorrectly()
  {
    // Arrange
    var code = """
package TestModel

  annotation (uses(Modelica(version="4.0.0")));
end Complex;
""";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Null(models[0].Version);
    Assert.NotNull(models[0].Uses);
    Assert.Single(models[0].Uses!);
    Assert.Equal("Modelica", models[0].Uses!.First().Key);
    Assert.Equal("4.0.0", models[0].Uses!.First().Value);
  }

  [Fact]
  public void ExtractModels_GetUsesAndVersionAnnotation_ExtractsCorrectly()
  {
    // Arrange
    var code = """
package TestModel

  annotation (version="1.2.3", uses(Modelica(version="4.0.0")));
end Complex;
""";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Single(models);
    Assert.Equal("1.2.3", models[0].Version);
    Assert.NotNull(models[0].Uses);
    Assert.Single(models[0].Uses!);
    Assert.Equal("Modelica", models[0].Uses!.First().Key);
    Assert.Equal("4.0.0", models[0].Uses!.First().Value);
  }    

  [Fact]
  public void ExtractModels_RedeclaredRecord_WithinPackage_ExtractedAsNonStandalone()
  {
    // Arrange - redeclare record extends is an element, extracted but not standalone
    var code = """
within MyPackage;
package Records

  redeclare record extends DataRecord
    Real x;
  end DataRecord;
end Records;
""";

    // Act
    var models = ExtractModels(code);

    // Assert - both extracted, DataRecord marked as non-standalone
    Assert.Equal(2, models.Count);
    Assert.Equal("Records", models[0].Name);
    Assert.Equal("MyPackage", models[0].ParentModelName);
    Assert.True(models[0].CanBeStoredStandalone);
    Assert.Equal("DataRecord", models[1].Name);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.True(models[1].IsNested);
    Assert.Equal("MyPackage.Records", models[1].ParentModelName);
    Assert.Equal("redeclare", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_RedeclareFunctionExtends_CapturesPrefix()
  {
    // Arrange - redeclare function extends is the pattern found in Modelica.Media.Air.MoistAir
    var code = @"
package MoistAir
  redeclare function extends saturationPressure
    Real x;
  end saturationPressure;
end MoistAir;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(2, models.Count);
    Assert.Equal("MoistAir", models[0].Name);
    Assert.Equal(string.Empty, models[0].ElementPrefix);
    Assert.Equal("saturationPressure", models[1].Name);
    Assert.Equal("function", models[1].ClassType);
    Assert.False(models[1].CanBeStoredStandalone);
    Assert.Equal("redeclare", models[1].ElementPrefix);
  }

  [Fact]
  public void ExtractModels_StandaloneModelSourceCode_DoesNotIncludeElementContext()
  {
    // Arrange - a normal standalone model should NOT be affected by the element prefix logic
    var code = @"
package Container
  model StandaloneModel
    Real x;
  end StandaloneModel;
end Container;";

    // Act
    var models = ExtractModels(code);

    // Assert
    Assert.Equal(2, models.Count);
    var standalone = models[1];
    Assert.True(standalone.CanBeStoredStandalone);
    Assert.StartsWith("model", standalone.SourceCode.TrimStart());
  }

  // ── HasExperimentAnnotation ──────────────────────────────────────────

  [Fact]
  public void ExtractModels_ModelWithExperimentAnnotation_SetsHasExperimentAnnotation()
  {
    var code = @"
within TestLib;
model Demo
  Real x;
  annotation(experiment(StopTime=10));
end Demo;";

    var models = ExtractModels(code);

    Assert.Single(models);
    Assert.True(models[0].HasExperimentAnnotation);
  }

  [Fact]
  public void ExtractModels_ModelWithoutExperimentAnnotation_HasExperimentAnnotationIsFalse()
  {
    var code = @"
within TestLib;
model Plain
  Real x;
end Plain;";

    var models = ExtractModels(code);

    Assert.Single(models);
    Assert.False(models[0].HasExperimentAnnotation);
  }

  [Fact]
  public void ExtractModels_ModelWithDocumentationOnly_HasExperimentAnnotationIsFalse()
  {
    var code = @"
within TestLib;
model Documented
  Real x;
  annotation(Documentation(info=""<html>Hello</html>""));
end Documented;";

    var models = ExtractModels(code);

    Assert.Single(models);
    Assert.False(models[0].HasExperimentAnnotation);
  }

  [Fact]
  public void ExtractModels_NestedModelWithExperiment_OnlyInnerHasAnnotation()
  {
    var code = @"
package TestPkg
  model Outer
    model Inner
      Real x;
      annotation(experiment(StopTime=5));
    end Inner;
    Real y;
  end Outer;
end TestPkg;";

    var models = ExtractModels(code);

    var outer = models.First(m => m.Name == "Outer");
    var inner = models.First(m => m.Name == "Inner");
    Assert.False(outer.HasExperimentAnnotation);
    Assert.True(inner.HasExperimentAnnotation);
  }

  [Fact]
  public void ExtractModels_ExperimentWithMultipleAnnotations_DetectsExperiment()
  {
    var code = @"
within TestLib;
model Demo
  Real x;
  annotation(Documentation(info=""<html>Test</html>""), experiment(StopTime=10, Tolerance=1e-6));
end Demo;";

    var models = ExtractModels(code);

    Assert.Single(models);
    Assert.True(models[0].HasExperimentAnnotation);
  }
}
