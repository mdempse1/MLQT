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

    private static ViewTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
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
