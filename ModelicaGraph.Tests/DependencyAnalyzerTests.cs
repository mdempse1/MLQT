using Xunit;
using ModelicaParser.Helpers;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Tests;

public class DependencyAnalyzerTests
{
    [Fact]
    public void AnalyzeCode_WithSimpleDependency_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var baseModel = new ModelNode("Base", "Base", "model Base\n  Real x;\nend Base;");
        var derivedModel = new ModelNode("Derived", "Derived",
            "model Derived\n  Base b;\n  Real y;\nend Derived;");

        graph.AddNode(baseModel);
        graph.AddNode(derivedModel);

        // Act
        var analyzer = new ModelAnalyzer("Derived", graph);
        var parseTree = ModelicaParserHelper.Parse(derivedModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("Base", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithInheritance_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var baseModel = new ModelNode("BaseClass", "BaseClass", "model BaseClass\nend BaseClass;");
        var derivedModel = new ModelNode("DerivedClass", "DerivedClass",
            "model DerivedClass\n  extends BaseClass;\nend DerivedClass;");

        graph.AddNode(baseModel);
        graph.AddNode(derivedModel);

        // Act
        var analyzer = new ModelAnalyzer("DerivedClass", graph);
        var parseTree = ModelicaParserHelper.Parse(derivedModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("BaseClass", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithImportAlias_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var targetModel = new ModelNode("PackageA.Helper", "Helper", "model Helper\nend Helper;");
        var consumerModel = new ModelNode("Consumer", "Consumer",
            "model Consumer\n  import H = PackageA.Helper;\n  H h;\nend Consumer;");

        graph.AddNode(targetModel);
        graph.AddNode(consumerModel);

        // Act
        var analyzer = new ModelAnalyzer("Consumer", graph);
        var parseTree = ModelicaParserHelper.Parse(consumerModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("PackageA.Helper", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithWildcardImport_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var targetModel = new ModelNode("PackageA.Component", "Component", "model Component\nend Component;");
        var consumerModel = new ModelNode("Consumer", "Consumer",
            "model Consumer\n  import PackageA.*;\n  Component c;\nend Consumer;");

        graph.AddNode(targetModel);
        graph.AddNode(consumerModel);

        // Act
        var analyzer = new ModelAnalyzer("Consumer", graph);
        var parseTree = ModelicaParserHelper.Parse(consumerModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("PackageA.Component", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithPackageScope_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var helperModel = new ModelNode("MyPackage.Helper", "Helper", "model Helper\nend Helper;");
        var consumerModel = new ModelNode("MyPackage.Consumer", "Consumer",
            "model Consumer\n  Helper h;\nend Consumer;");

        graph.AddNode(helperModel);
        graph.AddNode(consumerModel);

        // Act
        var analyzer = new ModelAnalyzer("MyPackage.Consumer", graph);
        var parseTree = ModelicaParserHelper.Parse(consumerModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("MyPackage.Helper", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithNestedPackageScope_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var helperModel = new ModelNode("PackageA.PackageB.Helper", "Helper", "model Helper\nend Helper;");
        var consumerModel = new ModelNode("PackageA.PackageB.SubPackage.Consumer", "Consumer",
            "model Consumer\n  Helper h;\nend Consumer;");

        graph.AddNode(helperModel);
        graph.AddNode(consumerModel);

        // Act
        var analyzer = new ModelAnalyzer("PackageA.PackageB.SubPackage.Consumer", graph);
        var parseTree = ModelicaParserHelper.Parse(consumerModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("PackageA.PackageB.Helper", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithFunctionCall_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var functionModel = new ModelNode("MathHelper", "MathHelper",
            "function MathHelper\n  input Real x;\n  output Real y;\nend MathHelper;");
        var consumerModel = new ModelNode("Calculator", "Calculator",
            "model Calculator\n  Real result;\nequation\n  result = MathHelper(5.0);\nend Calculator;");

        graph.AddNode(functionModel);
        graph.AddNode(consumerModel);

        // Act
        var analyzer = new ModelAnalyzer("Calculator", graph);
        var parseTree = ModelicaParserHelper.Parse(consumerModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("MathHelper", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithBuiltInTypes_DoesNotAddDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model = new ModelNode("TestModel", "TestModel",
            "model TestModel\n  Real x;\n  Integer i;\n  Boolean b;\n  String s;\nend TestModel;");

        graph.AddNode(model);

        // Act
        var analyzer = new ModelAnalyzer("TestModel", graph);
        var parseTree = ModelicaParserHelper.Parse(model.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Empty(analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithBuiltInFunctions_DoesNotAddDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model = new ModelNode("TestModel", "TestModel",
            @"model TestModel
  Real x;
equation
  x = sin(time) + cos(time) + sqrt(abs(time));
end TestModel;");

        graph.AddNode(model);

        // Act
        var analyzer = new ModelAnalyzer("TestModel", graph);
        var parseTree = ModelicaParserHelper.Parse(model.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Empty(analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithSelfReference_DoesNotAddDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model = new ModelNode("TestModel", "TestModel",
            "model TestModel\n  TestModel inner;\nend TestModel;");

        graph.AddNode(model);

        // Act
        var analyzer = new ModelAnalyzer("TestModel", graph);
        var parseTree = ModelicaParserHelper.Parse(model.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.DoesNotContain("TestModel", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithNonExistentReference_DoesNotAddDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model = new ModelNode("TestModel", "TestModel",
            "model TestModel\n  NonExistentType x;\nend TestModel;");

        graph.AddNode(model);

        // Act
        var analyzer = new ModelAnalyzer("TestModel", graph);
        var parseTree = ModelicaParserHelper.Parse(model.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Empty(analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithFullyQualifiedName_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var targetModel = new ModelNode("PackageA.PackageB.Component", "Component", "model Component\nend Component;");
        var consumerModel = new ModelNode("Consumer", "Consumer",
            "model Consumer\n  PackageA.PackageB.Component c;\nend Consumer;");

        graph.AddNode(targetModel);
        graph.AddNode(consumerModel);

        // Act
        var analyzer = new ModelAnalyzer("Consumer", graph);
        var parseTree = ModelicaParserHelper.Parse(consumerModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("PackageA.PackageB.Component", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithMultipleDependencies_DetectsAll()
    {
        // Arrange
        var graph = new DirectedGraph();
        var comp1 = new ModelNode("Component1", "Component1", "model Component1\nend Component1;");
        var comp2 = new ModelNode("Component2", "Component2", "model Component2\nend Component2;");
        var comp3 = new ModelNode("Component3", "Component3", "model Component3\nend Component3;");
        var consumer = new ModelNode("System", "System",
            @"model System
  Component1 c1;
  Component2 c2;
  Component3 c3;
end System;");

        graph.AddNode(comp1);
        graph.AddNode(comp2);
        graph.AddNode(comp3);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("System", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("Component1", analyzer.ReferencedModels);
        Assert.Contains("Component2", analyzer.ReferencedModels);
        Assert.Contains("Component3", analyzer.ReferencedModels);
        Assert.Equal(3, analyzer.ReferencedModels.Count);
    }

    [Fact]
    public void AnalyzeCode_WithComponentReference_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var comp = new ModelNode("Component", "Component", "model Component\n  Real value;\nend Component;");
        var consumer = new ModelNode("System", "System",
            @"model System
  Component c;
  Real x;
equation
  x = c.value;
end System;");

        graph.AddNode(comp);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("System", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("Component", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithImportStatement_ParsesCorrectly()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model = new ModelNode("TestModel", "TestModel",
            @"model TestModel
  import Modelica.Math.sin;
  import Modelica.Constants.*;
  import C = Modelica.Constants;
  Real x;
end TestModel;");

        graph.AddNode(model);

        // Act - should not throw
        var analyzer = new ModelAnalyzer("TestModel", graph);
        var parseTree = ModelicaParserHelper.Parse(model.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert - no dependencies added since imported models don't exist in graph
        Assert.Empty(analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithAliasedImportReference_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var comp1 = new ModelNode("PackageA.Component1", "Component1", "model Component1\nend Component1;");
        var comp2 = new ModelNode("PackageA.Component2.SubComponent", "SubComponent", "model SubComponent\nend SubComponent;");
        var consumer = new ModelNode("System", "System",
            @"model System
  import C1 = PackageA.Component1;
  import C2 = PackageA.Component2;
  C1 comp1;
  C2.SubComponent comp2;
end System;");

        graph.AddNode(comp1);
        graph.AddNode(comp2);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("System", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("PackageA.Component1", analyzer.ReferencedModels);
        Assert.Contains("PackageA.Component2.SubComponent", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithEmptyCode_HandlesGracefully()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model = new ModelNode("Empty", "Empty", "model Empty\nend Empty;");

        graph.AddNode(model);

        // Act
        var analyzer = new ModelAnalyzer("Empty", graph);
        var parseTree = ModelicaParserHelper.Parse(model.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Empty(analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithDerivativeFunction_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var baseFunc = new ModelNode("f", "f", "function f\n  input Real x;\n  output Real y;\nend f;");
        var consumer = new ModelNode("System", "System",
            @"model System
  Real x, y;
equation
  y = der(f(x));
end System;");

        graph.AddNode(baseFunc);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("System", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("f", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithQualifiedFunctionCall_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var func = new ModelNode("MathLib.Functions.calculate", "calculate",
            "function calculate\n  input Real x;\n  output Real y;\nend calculate;");
        var consumer = new ModelNode("System", "System",
            @"model System
  Real result;
equation
  result = MathLib.Functions.calculate(5.0);
end System;");

        graph.AddNode(func);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("System", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("MathLib.Functions.calculate", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithParentPackageReference_ResolvesDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var parentHelper = new ModelNode("PackageA.Helper", "Helper", "model Helper\nend Helper;");
        var deepModel = new ModelNode("PackageA.SubPackage.DeepModel", "DeepModel",
            "model DeepModel\n  Helper h;\nend DeepModel;");

        graph.AddNode(parentHelper);
        graph.AddNode(deepModel);

        // Act
        var analyzer = new ModelAnalyzer("PackageA.SubPackage.DeepModel", graph);
        var parseTree = ModelicaParserHelper.Parse(deepModel.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("PackageA.Helper", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithRootLevelModel_NoPackageScope()
    {
        // Arrange
        var graph = new DirectedGraph();
        var helper = new ModelNode("Helper", "Helper", "model Helper\nend Helper;");
        var consumer = new ModelNode("Consumer", "Consumer",
            "model Consumer\n  Helper h;\nend Consumer;");

        graph.AddNode(helper);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("Consumer", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("Helper", analyzer.ReferencedModels);
    }

    [Fact]
    public void AnalyzeCode_WithArrayTypeSpecifier_DetectsDependency()
    {
        // Arrange
        var graph = new DirectedGraph();
        var component = new ModelNode("Component", "Component", "model Component\nend Component;");
        var consumer = new ModelNode("System", "System",
            "model System\n  Component[3] components;\nend System;");

        graph.AddNode(component);
        graph.AddNode(consumer);

        // Act
        var analyzer = new ModelAnalyzer("System", graph);
        var parseTree = ModelicaParserHelper.Parse(consumer.Definition.ModelicaCode);
        analyzer.Visit(parseTree);

        // Assert
        Assert.Contains("Component", analyzer.ReferencedModels);
    }
}
