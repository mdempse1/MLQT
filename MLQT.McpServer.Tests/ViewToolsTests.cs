using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class ViewToolsTests
{
    private const string Package = """
        within;
        package P "P"
          connector Flange "a flange"
            Real s;
            flow Real f;
          end Flange;

          partial model Base "base"
            Real b;
          end Base;

          model Comp "a component model"
            extends P.Base;
            parameter Real k = 2 "gain";
            input Real u "the input";
            Flange flange "mechanical";
            Real internal;
          protected
            Real hidden;
          end Comp;

          function add "adds two reals"
            input Real a;
            input Real b;
            output Real c;
          algorithm
            c := a + b;
            annotation (Documentation(info="<html><p>Add two reals</p></html>",
              revisions="<html>2026 - created</html>"));
          end add;

          model Bad
            NonExistent.Thing t;
            extends Missing.Base;
          end Bad;
        end P;
        """;

    // An Integrator-like hierarchy: a partial base Gain declares the parameter k and the connectors u/y,
    // inherited by concrete blocks. Amplifier modifies k via its extends clause; Overridden redeclares k.
    private const string InheritancePackage = """
        within;
        package I "i"
          connector RealInput "in"
            input Real signal;
          end RealInput;
          connector RealOutput "out"
            output Real signal;
          end RealOutput;
          partial block Gain "gain block"
            parameter Real k = 1 "gain";
            RealInput u "the input";
            RealOutput y "the output";
          end Gain;
          block Integrator "integrator"
            extends Gain;
          end Integrator;
          block Amplifier "amplifier"
            extends Gain(k = 10);
          end Amplifier;
          block Overridden "overridden"
            extends Gain;
            parameter Real k = 99 "own gain";
          end Overridden;
        end I;
        """;

    private static ViewTools Load(TestHost h) => LoadContent(h, Package);

    private static ViewTools LoadContent(TestHost h, string content)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = content });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new ViewTools(h.Libraries);
    }

    [Fact]
    public void Interface_ParametersConnectorsExtendsDescription()
    {
        using var host = new TestHost();
        var view = ToolAssert.Ok<ClassInterfaceView>(Load(host).GetClassInterface("P.Comp"));

        Assert.Equal("a component model", view.Description);
        Assert.Contains("P.Base", view.Extends);

        var k = Assert.Single(view.Parameters);
        Assert.Equal("k", k.Name);
        Assert.Equal("2", k.Default);

        // 'u' is a causal signal connector; 'flange' is a physical connector (type resolves to a connector).
        var u = view.Connectors.Single(c => c.Name == "u");
        Assert.Equal("input", u.Causality);
        Assert.False(u.TypeIsConnector);

        var flange = view.Connectors.Single(c => c.Name == "flange");
        Assert.True(flange.TypeIsConnector);
        Assert.Null(flange.Causality);

        // 'internal' is a plain public component (neither parameter nor connector).
        Assert.Contains(view.PublicComponents, m => m.Name == "internal");
        Assert.Null(view.FunctionSignature);

        // 'b' is inherited from the P.Base base class.
        Assert.Contains(view.PublicComponents, m => m.Name == "b" && m.InheritedFrom == "P.Base");
    }

    [Fact]
    public void Interface_IncludesInheritedConnectorsAndParameters()
    {
        using var host = new TestHost();
        var view = ToolAssert.Ok<ClassInterfaceView>(LoadContent(host, InheritancePackage).GetClassInterface("I.Integrator"));

        // u, y and k are all declared in the base Gain, not in Integrator itself.
        var u = view.Connectors.Single(c => c.Name == "u");
        Assert.Equal("I.Gain", u.InheritedFrom);
        Assert.True(u.TypeIsConnector);
        Assert.Contains(view.Connectors, c => c.Name == "y" && c.InheritedFrom == "I.Gain");

        var k = view.Parameters.Single(p => p.Name == "k");
        Assert.Equal("I.Gain", k.InheritedFrom);
        Assert.Equal("1", k.Default);
    }

    [Fact]
    public void Interface_ExtendsModifierOverridesInheritedDefault()
    {
        using var host = new TestHost();
        // Amplifier: extends Gain(k = 10) -> the effective default for the inherited k is 10, not the base's 1.
        var view = ToolAssert.Ok<ClassInterfaceView>(LoadContent(host, InheritancePackage).GetClassInterface("I.Amplifier"));

        var k = view.Parameters.Single(p => p.Name == "k");
        Assert.Equal("10", k.Default);
        Assert.Equal("I.Gain", k.InheritedFrom);
    }

    [Fact]
    public void Interface_IncludeInheritedFalse_OwnOnly()
    {
        using var host = new TestHost();
        var view = ToolAssert.Ok<ClassInterfaceView>(
            LoadContent(host, InheritancePackage).GetClassInterface("I.Overridden", includeInherited: false));

        Assert.Empty(view.Connectors);                        // u/y come from the base, now excluded
        var k = view.Parameters.Single(p => p.Name == "k");   // own parameter remains
        Assert.Equal("99", k.Default);
        Assert.Null(k.InheritedFrom);
    }

    [Fact]
    public void Interface_DerivedParameterShadowsInherited()
    {
        using var host = new TestHost();
        // Overridden redeclares 'k' (=99), shadowing the inherited Gain.k (=1): exactly one k, the own one.
        var view = ToolAssert.Ok<ClassInterfaceView>(LoadContent(host, InheritancePackage).GetClassInterface("I.Overridden"));
        var k = view.Parameters.Single(p => p.Name == "k");
        Assert.Null(k.InheritedFrom);
        Assert.Equal("99", k.Default);
    }

    [Fact]
    public void ListElements_SurfacesLeadingComments()
    {
        const string pkg = """
            within;
            package K "k"
              model M
                // gain used by the controller
                parameter Real k = 1;
                Real x;
              end M;
            end K;
            """;
        using var host = new TestHost();
        var res = ToolAssert.Ok<ClassElementsResult>(LoadContent(host, pkg).ListClassElements("K.M"));
        var k = res.Elements.Single(e => e.Name == "k");
        Assert.Contains("// gain used by the controller", k.LeadingComments);
        Assert.Empty(res.Elements.Single(e => e.Name == "x").LeadingComments);
    }

    [Fact]
    public void ListElements_IncludesInheritedWithOrigin()
    {
        using var host = new TestHost();
        var tools = LoadContent(host, InheritancePackage);

        var withInherited = ToolAssert.Ok<ClassElementsResult>(tools.ListClassElements("I.Overridden"));
        Assert.Contains(withInherited.Elements, e => e.Name == "u" && e.InheritedFrom == "I.Gain");

        var ownOnly = ToolAssert.Ok<ClassElementsResult>(tools.ListClassElements("I.Overridden", includeInherited: false));
        Assert.DoesNotContain(ownOnly.Elements, e => e.Name == "u");
        Assert.Contains(ownOnly.Elements, e => e.Name == "k" && e.InheritedFrom == null); // own parameter
    }

    [Fact]
    public void Interface_FunctionSignature()
    {
        using var host = new TestHost();
        var view = ToolAssert.Ok<ClassInterfaceView>(Load(host).GetClassInterface("P.add"));

        Assert.NotNull(view.FunctionSignature);
        Assert.Equal(new[] { "a", "b" }, view.FunctionSignature!.Inputs.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { "c" }, view.FunctionSignature.Outputs.Select(o => o.Name).ToArray());
        // Function args are not also reported as connectors/parameters.
        Assert.Empty(view.Connectors);
        Assert.Empty(view.Parameters);
    }

    [Fact]
    public void ListElements_PublicByDefault_ProtectedOnRequest()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var pub = ToolAssert.Ok<ClassElementsResult>(tools.ListClassElements("P.Comp"));
        Assert.DoesNotContain(pub.Elements, e => e.Name == "hidden");
        Assert.Contains(pub.Elements, e => e.Kind == "extends" && e.Name == "P.Base");
        Assert.Contains(pub.Elements, e => e.Name == "k" && e.Variability == "parameter");

        var all = ToolAssert.Ok<ClassElementsResult>(tools.ListClassElements("P.Comp", includeProtected: true));
        var hidden = all.Elements.Single(e => e.Name == "hidden");
        Assert.Equal("protected", hidden.Visibility);
    }

    [Fact]
    public void Documentation_TextStripsHtml_HtmlIsRaw()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var text = ToolAssert.Ok<ClassDocumentationResult>(tools.GetClassDocumentation("P.add"));
        Assert.Equal("adds two reals", text.Description);
        Assert.Contains("Add two reals", text.Info);
        Assert.DoesNotContain("<html>", text.Info);

        var html = ToolAssert.Ok<ClassDocumentationResult>(tools.GetClassDocumentation("P.add", format: "html"));
        Assert.Contains("<html><p>Add two reals</p></html>", html.Info);
        Assert.Contains("created", html.Revisions);
    }

    [Fact]
    public void Validate_ReportsUnresolved_SkipsResolvedAndPredefined()
    {
        using var host = new TestHost();
        var tools = Load(host);

        // Comp references only resolvable/predefined types -> nothing unresolved.
        var comp = ToolAssert.Ok<ReferenceValidationResult>(tools.ValidateClassReferences("P.Comp"));
        Assert.Equal(0, comp.UnresolvedCount);

        // Bad references two non-existent classes.
        var bad = ToolAssert.Ok<ReferenceValidationResult>(tools.ValidateClassReferences("P.Bad"));
        Assert.Equal(2, bad.UnresolvedCount);
        Assert.Contains(bad.Unresolved, u => u.Name == "NonExistent.Thing" && u.Kind == "component-type");
        Assert.Contains(bad.Unresolved, u => u.Name == "Missing.Base" && u.Kind == "extends");
    }

    [Fact]
    public void Validate_InheritedNestedType_NotFlagged()
    {
        const string pkg = """
            within;
            package V "v"
              partial model Base
                type Voltage = Real;
                Voltage v;
              end Base;
              model Derived
                extends Base;
                Voltage v2;
                Nonexistent n;
              end Derived;
            end V;
            """;
        using var host = new TestHost();
        var view = LoadContent(host, pkg);

        var res = ToolAssert.Ok<ReferenceValidationResult>(view.ValidateClassReferences("V.Derived"));
        // 'Voltage' is inherited from Base (its nested type) -> not flagged; only 'Nonexistent' is unresolved.
        Assert.DoesNotContain(res.Unresolved, u => u.Name == "Voltage");
        Assert.Contains(res.Unresolved, u => u.Name == "Nonexistent");
    }

    [Fact]
    public void Behavior_OwnOnly_PlusBasesPointer()
    {
        const string pkg = """
            within;
            package B "b"
              partial model Base
                Real e;
              equation
                e = 1;
              end Base;
              model Der
                extends Base;
                Real d;
              equation
                d = 2*time;
              end Der;
            end B;
            """;
        using var host = new TestHost();
        var view = LoadContent(host, pkg);

        var res = ToolAssert.Ok<ClassBehaviorResult>(view.GetClassBehavior("B.Der"));
        Assert.Contains(res.Equations, e => e.Text == "d = 2*time");
        Assert.DoesNotContain(res.Equations, e => e.Text.Contains("e = 1")); // inherited behavior NOT merged
        Assert.Contains("B.Base", res.BasesWithBehavior);                    // pointer to the base instead
    }

    [Fact]
    public void Views_MissingClass_Error()
    {
        using var host = new TestHost();
        var tools = Load(host);
        Assert.IsType<ToolError>(tools.GetClassInterface("P.Nope"));
        Assert.IsType<ToolError>(tools.ListClassElements("P.Nope"));
        Assert.IsType<ToolError>(tools.GetClassDocumentation("P.Nope"));
        Assert.IsType<ToolError>(tools.ValidateClassReferences("P.Nope"));
    }
}
