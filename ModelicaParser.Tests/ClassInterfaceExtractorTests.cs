using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class ClassInterfaceExtractorTests
{
    private static ClassInterface Extract(string code) => ClassInterfaceExtractor.ExtractFromCode(code);

    [Fact]
    public void ClassDescription_IsExtracted()
    {
        var iface = Extract("model M \"a model\"\n  Real x;\nend M;");
        Assert.Equal("a model", iface.Description);
    }

    [Fact]
    public void ConcatenatedDescription_IsJoined()
    {
        var iface = Extract("model M \"part one \" + \"part two\"\n  Real x;\nend M;");
        Assert.Equal("part one part two", iface.Description);
    }

    [Fact]
    public void Component_NameTypeAndDescription()
    {
        var iface = Extract("model M\n  Real x \"the x\";\nend M;");
        var e = Assert.Single(iface.Elements);
        Assert.Equal(ClassElementKind.Component, e.Kind);
        Assert.Equal("x", e.Name);
        Assert.Equal("Real", e.Type);
        Assert.Equal("the x", e.Description);
        Assert.True(e.IsPublic);
        Assert.Null(e.Variability);
        Assert.Null(e.Causality);
    }

    [Fact]
    public void Parameter_And_Constant_Variability()
    {
        var iface = Extract("model M\n  parameter Real k = 2 \"gain\";\n  constant Integer N = 3;\nend M;");
        var k = iface.Elements.Single(e => e.Name == "k");
        Assert.Equal("parameter", k.Variability);
        Assert.Equal("2", k.DefaultValue);
        Assert.Equal("gain", k.Description);

        var n = iface.Elements.Single(e => e.Name == "N");
        Assert.Equal("constant", n.Variability);
        Assert.Equal("3", n.DefaultValue);
    }

    [Fact]
    public void Discrete_Variability()
    {
        var iface = Extract("model M\n  discrete Real d;\nend M;");
        Assert.Equal("discrete", Assert.Single(iface.Elements).Variability);
    }

    [Fact]
    public void Connector_CausalityAndQualifiedType()
    {
        var iface = Extract(
            "block B\n  input Modelica.Blocks.Interfaces.RealInput u;\n  output Real y;\nend B;");
        var u = iface.Elements.Single(e => e.Name == "u");
        Assert.Equal("input", u.Causality);
        Assert.Equal("Modelica.Blocks.Interfaces.RealInput", u.Type);

        var y = iface.Elements.Single(e => e.Name == "y");
        Assert.Equal("output", y.Causality);
    }

    [Fact]
    public void FlowConnection_OnConnectorMember()
    {
        var iface = Extract("connector Pin\n  Real v;\n  flow Real i;\nend Pin;");
        Assert.Equal("flow", iface.Elements.Single(e => e.Name == "i").Connection);
        Assert.Null(iface.Elements.Single(e => e.Name == "v").Connection);
    }

    [Fact]
    public void MultipleComponentsInOneClause_ShareTypeAndPrefix()
    {
        var iface = Extract("model M\n  parameter Real a, b, c;\nend M;");
        Assert.Equal(3, iface.Elements.Count);
        Assert.All(iface.Elements, e =>
        {
            Assert.Equal("Real", e.Type);
            Assert.Equal("parameter", e.Variability);
        });
        Assert.Equal(new[] { "a", "b", "c" }, iface.Elements.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void ProtectedSection_MarksVisibility()
    {
        var iface = Extract("model M\n  Real pub;\nprotected\n  Real sec;\npublic\n  Real pub2;\nend M;");
        Assert.True(iface.Elements.Single(e => e.Name == "pub").IsPublic);
        Assert.False(iface.Elements.Single(e => e.Name == "sec").IsPublic);
        Assert.True(iface.Elements.Single(e => e.Name == "pub2").IsPublic);
    }

    [Fact]
    public void ExtendsClause_IsExtracted()
    {
        var iface = Extract("model M\n  extends Modelica.Icons.Example;\n  Real x;\nend M;");
        var ext = iface.Elements.Single(e => e.Kind == ClassElementKind.Extends);
        Assert.Equal("Modelica.Icons.Example", ext.Name);
        Assert.Equal("Modelica.Icons.Example", ext.Type);
    }

    [Fact]
    public void Import_Plain_Alias_And_Wildcard()
    {
        var plain = Extract("model M\n  import Modelica.SIunits;\nend M;");
        Assert.Equal("Modelica.SIunits", plain.Elements.Single(e => e.Kind == ClassElementKind.Import).Name);

        var alias = Extract("model M\n  import SI = Modelica.SIunits;\nend M;");
        Assert.Equal("SI = Modelica.SIunits", alias.Elements.Single(e => e.Kind == ClassElementKind.Import).Name);

        var wild = Extract("model M\n  import Modelica.SIunits.*;\nend M;");
        Assert.Equal("Modelica.SIunits.*", wild.Elements.Single(e => e.Kind == ClassElementKind.Import).Name);
    }

    [Fact]
    public void NestedClass_ListedButNotRecursed()
    {
        var iface = Extract(
            "package P\n  model Inner \"inner model\"\n    Real deep;\n  end Inner;\n  Real sibling;\nend P;");

        var nested = iface.Elements.Single(e => e.Kind == ClassElementKind.Class);
        Assert.Equal("Inner", nested.Name);
        Assert.Equal("model", nested.ClassType);
        Assert.Equal("inner model", nested.Description);

        // 'deep' belongs to the nested class and must NOT leak into P's element list.
        Assert.DoesNotContain(iface.Elements, e => e.Name == "deep");
        Assert.Contains(iface.Elements, e => e.Name == "sibling");
    }

    [Fact]
    public void ExtendsModifications_ScalarCaptured_NestedOmitted()
    {
        var iface = Extract("model M\n  extends Base(k = 5, T = 2, sub(x = 1));\nend M;");
        var ext = iface.Elements.Single(e => e.Kind == ClassElementKind.Extends);
        Assert.NotNull(ext.Modifications);
        Assert.Equal("5", ext.Modifications!["k"]);
        Assert.Equal("2", ext.Modifications["T"]);
        Assert.False(ext.Modifications.ContainsKey("sub")); // nested modification is not a scalar default
    }

    [Fact]
    public void ExtendsWithoutModifications_IsNull()
    {
        var iface = Extract("model M\n  extends Base;\nend M;");
        Assert.Null(iface.Elements.Single(e => e.Kind == ClassElementKind.Extends).Modifications);
    }

    [Fact]
    public void ReplaceablePrefix_IsCaptured()
    {
        var iface = Extract("model M\n  replaceable model Medium = Modelica.Media.Water;\nend M;");
        var nested = iface.Elements.Single(e => e.Kind == ClassElementKind.Class);
        Assert.Contains("replaceable", nested.Prefixes);
    }

    [Fact]
    public void ComponentModifier_CapturedAsDefaultValue()
    {
        var iface = Extract("model M\n  Modelica.Blocks.Continuous.Integrator integrator(k=2);\nend M;");
        Assert.Equal("(k=2)", iface.Elements.Single(e => e.Name == "integrator").DefaultValue);
    }

    [Fact]
    public void LeadingComments_AttachToFollowingElement()
    {
        var iface = Extract(
            "model M\n  // the gain parameter\n  /* block */\n  parameter Real k = 1;\n  Real x;\nend M;");
        var k = iface.Elements.Single(e => e.Name == "k");
        Assert.Equal(new[] { "// the gain parameter", "/* block */" }, k.LeadingComments);
        Assert.Empty(iface.Elements.Single(e => e.Name == "x").LeadingComments);
    }

    [Fact]
    public void EmptyOrNonClass_YieldsEmptyInterface()
    {
        Assert.Empty(Extract("// just a comment").Elements);
        Assert.Null(Extract("// just a comment").Description);
    }

    [Fact]
    public void FunctionInputsOutputs_AreCausalComponents()
    {
        var iface = Extract(
            "function f\n  input Real a;\n  input Real b;\n  output Real c;\nalgorithm\n  c := a + b;\nend f;");
        Assert.Equal(2, iface.Elements.Count(e => e.Causality == "input"));
        Assert.Equal(1, iface.Elements.Count(e => e.Causality == "output"));
    }

    [Fact]
    public void Import_ExplicitList_KeepsTheWholeList()
    {
        // `import A.{B, C}` names two classes in one clause. Reducing it to A would make both
        // unresolvable, and a name that does not resolve is reported against correct code.
        var iface = Extract("model M\n  import Modelica.Units.SI.{Voltage, Current};\n  Real x;\nend M;");

        var import = iface.Elements.Single(e => e.Kind == ClassElementKind.Import);
        Assert.Contains("Modelica.Units.SI", import.Name);
        Assert.Contains("Voltage", import.Name);
        Assert.Contains("Current", import.Name);
    }

    [Fact]
    public void NestedShortClassDefinition_IsListedByName()
    {
        // A `type` alias is a class of the package like any other, and it is what most unit
        // definitions in a library actually are.
        var iface = Extract("package P\n  type Gain = Real(min = 0) \"a gain\";\nend P;");

        var nested = iface.Elements.Single(e => e.Kind == ClassElementKind.Class);
        Assert.Equal("Gain", nested.Name);
        Assert.Equal("type", nested.ClassType);
    }

    [Fact]
    public void NestedDerivativeClassDefinition_IsListedByName()
    {
        var iface = Extract("package P\n  function df = der(f, x) \"the derivative\";\nend P;");

        Assert.Equal("df", iface.Elements.Single(e => e.Kind == ClassElementKind.Class).Name);
    }

    [Fact]
    public void AnAssignedDefault_IsReadWithoutItsOperator()
    {
        // Dymola accepts `:=` in a declaration and libraries in the field use it. Keeping the
        // operator in the value would show ":= 1" as the default everywhere it is displayed.
        var iface = Extract("function f\n  input Real x;\nprotected\n  Real t := 1;\nend f;");

        Assert.Equal("1", iface.Elements.Single(e => e.Name == "t").DefaultValue);
    }

    [Fact]
    public void AComponentThatIsOnlyDeclared_HasNoDefault()
    {
        Assert.Null(Assert.Single(Extract("model M\n  Real x;\nend M;").Elements).DefaultValue);
    }
}
