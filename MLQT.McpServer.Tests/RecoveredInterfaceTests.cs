using ModelicaGraph;
using ModelicaParser.ExternalDocs;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// What the tools say about a class from an encrypted library (B179).
///
/// <para>An agent could load one and get almost nothing back: the vendor's generated help lists a
/// class's parameters, connectors and function signature, MLQT parsed them, and then
/// <c>ExternalStubBuilder</c> dropped them on the floor because a synthesized declaration has no
/// type to write. So <c>get_class_interface</c> answered "no parameters" for a class with fourteen
/// of them, which is a worse answer than "the source is encrypted".</para>
///
/// <para>These pin both halves: the members come back, and every result that carries them says they
/// were recovered from documentation rather than read from source.</para>
/// </summary>
public class RecoveredInterfaceTests
{
    private static DocumentedMember Member(string name, string? description = null, string? unit = null)
        => new(name, description, unit);

    /// <summary>A documented model with something in every table the generator emits.</summary>
    private static DocumentedClass Documented(
        string fullName,
        string kind = DocumentedClass.KindModel,
        IReadOnlyList<DocumentedMember>? parameters = null,
        IReadOnlyList<DocumentedMember>? connectors = null,
        IReadOnlyList<DocumentedMember>? inputs = null,
        IReadOnlyList<DocumentedMember>? outputs = null,
        IReadOnlyList<DocumentedMember>? contents = null,
        IReadOnlyList<string>? extends = null)
        => new(fullName, "A vendor class", extends, HasIcon: true, IconImagePath: null, kind,
            Children: [], parameters ?? [], connectors ?? [], inputs ?? [], outputs ?? [],
            contents ?? []);

    private static TestHost WithStubs(params DocumentedClass[] documented)
    {
        var host = new TestHost();
        ExternalStubBuilder.AddDocumentedClasses(
            host.Libraries.CombinedGraph, documented,
            Path.Combine(Path.GetTempPath(), "Vendor", "package.moe"));
        return host;
    }

    private static readonly DocumentedClass Restrictor = Documented(
        "Vendor.BMS.CurrentRestrictor",
        parameters: [Member("iMax", "Maximum current", "A"), Member("tau", "Time constant", "s")],
        connectors: [Member("p", "Positive pin"), Member("control", "Control signal")],
        contents: [Member("state", "Internal state")],
        extends: ["Vendor.BMS.Interfaces.BMS"]);

    private static readonly DocumentedClass Limiter = Documented(
        "Vendor.BMS.limit", kind: DocumentedClass.KindFunction,
        inputs: [Member("u", "Value to limit"), Member("uMax", "Upper bound", "A")],
        outputs: [Member("y", "The limited value")]);

    [Fact]
    public void TheInterfaceCarriesWhatTheDocumentationListed()
    {
        using var host = WithStubs(Restrictor);

        var view = ToolAssert.Ok<ClassInterfaceView>(
            new ViewTools(host.Libraries).GetClassInterface("Vendor.BMS.CurrentRestrictor"));

        Assert.True(view.RecoveredFromDocumentation);
        Assert.Equal(["iMax", "tau"], view.Parameters.Select(p => p.Name));
        Assert.Equal(["p", "control"], view.Connectors.Select(c => c.Name));
        Assert.Equal(["state"], view.PublicComponents.Select(m => m.Name));
        Assert.Equal("Maximum current", view.Parameters[0].Description);
        Assert.Equal("A", view.Parameters[0].Unit);
        Assert.Equal("A vendor class", view.Description);

        // The extends came from the synthesized source, as everything expressible in Modelica does.
        Assert.Equal(["Vendor.BMS.Interfaces.BMS"], view.Extends);
    }

    [Fact]
    public void NoTypeIsInventedForAnyOfThem()
    {
        // The rule the whole feature rests on: the generator does not publish declared types, and a
        // guessed one feeds the type and unit resolvers as though it were read from source.
        using var host = WithStubs(Restrictor, Limiter);
        var views = new ViewTools(host.Libraries);

        var model = ToolAssert.Ok<ClassInterfaceView>(views.GetClassInterface("Vendor.BMS.CurrentRestrictor"));
        Assert.All(model.Parameters, p => Assert.Null(p.Type));
        Assert.All(model.Connectors, c => Assert.Null(c.Type));
        Assert.All(model.PublicComponents, m => Assert.Null(m.Type));

        var function = ToolAssert.Ok<ClassInterfaceView>(views.GetClassInterface("Vendor.BMS.limit"));
        Assert.All(function.FunctionSignature!.Inputs, p => Assert.Null(p.Type));
    }

    [Fact]
    public void AFunctionGetsItsSignature()
    {
        using var host = WithStubs(Limiter);

        var view = ToolAssert.Ok<ClassInterfaceView>(
            new ViewTools(host.Libraries).GetClassInterface("Vendor.BMS.limit"));

        Assert.NotNull(view.FunctionSignature);
        Assert.Equal(["u", "uMax"], view.FunctionSignature!.Inputs.Select(p => p.Name));
        Assert.Equal(["y"], view.FunctionSignature.Outputs.Select(p => p.Name));
        Assert.Equal("A", view.FunctionSignature.Inputs[1].Unit);
    }

    [Fact]
    public void TheElementListSaysWhichTableEachMemberCameFrom()
    {
        using var host = WithStubs(Restrictor, Limiter);
        var views = new ViewTools(host.Libraries);

        var model = ToolAssert.Ok<ClassElementsResult>(views.ListClassElements("Vendor.BMS.CurrentRestrictor"));
        Assert.True(model.RecoveredFromDocumentation);

        var iMax = Assert.Single(model.Elements, e => e.Name == "iMax");
        Assert.Equal("parameter", iMax.Variability);
        Assert.Equal("public", iMax.Visibility);
        Assert.Null(iMax.Type);
        // Nothing was read from a file, so there is no line to send anyone to.
        Assert.Equal(0, iMax.Line);

        // The extends clause is a real declaration in the synthesized source and is listed as usual.
        Assert.Contains(model.Elements, e => e.Kind == "extends");

        var function = ToolAssert.Ok<ClassElementsResult>(views.ListClassElements("Vendor.BMS.limit"));
        Assert.Equal("input", Assert.Single(function.Elements, e => e.Name == "u").Causality);
        Assert.Equal("output", Assert.Single(function.Elements, e => e.Name == "y").Causality);
    }

    [Fact]
    public void GetClassInfoSaysWhereTheClassCameFromAndThatItCannotBeWritten()
    {
        using var host = WithStubs(Restrictor);

        var info = ToolAssert.Ok<ClassInfo>(
            new ClassQueryTools(host.Libraries).GetClassInfo("Vendor.BMS.CurrentRestrictor"));

        Assert.True(info.RecoveredFromDocumentation);

        // B85: a stub's file is the vendor's encrypted package, and offering it as editable is an
        // invitation to overwrite a library MLQT cannot read.
        Assert.Equal(false, info.Writable);
    }

    [Fact]
    public void AnOrdinaryClassSaysNoneOfThis()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Ann.mo", "model Ann \"d\"\n  parameter Real k = 1 \"gain\";\nend Ann;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();

        var info = ToolAssert.Ok<ClassInfo>(new ClassQueryTools(host.Libraries).GetClassInfo("Ann"));
        Assert.False(info.RecoveredFromDocumentation);

        var view = ToolAssert.Ok<ClassInterfaceView>(new ViewTools(host.Libraries).GetClassInterface("Ann"));
        Assert.False(view.RecoveredFromDocumentation);
        Assert.Equal("Real", Assert.Single(view.Parameters).Type);
        Assert.Null(Assert.Single(view.Parameters).Unit);

        var elements = ToolAssert.Ok<ClassElementsResult>(new ViewTools(host.Libraries).ListClassElements("Ann"));
        Assert.False(elements.RecoveredFromDocumentation);
    }

    /// <summary>
    /// A stub that extends a readable base, the usual shape for a vendor block built on MSL's
    /// <c>SISO</c>: the resolver follows the synthesized <c>extends</c> into the base and finds
    /// <c>u</c> and <c>y</c>, and the vendor's tables list them too, because generated help lists
    /// what a class inherits. Appending one to the other listed each twice and inflated the count
    /// - and with include_inherited=false the documented inherited ones still appeared (B318).
    /// </summary>
    [Fact]
    public void AMemberInheritedFromAReadableBaseIsListedOnce_AndSaysWhereItCameFrom()
    {
        using var host = WithStubs(Documented(
            "Vendor.BMS.Filter",
            parameters: [Member("k", "Gain")],
            inputs: [Member("u", "Input signal")],
            outputs: [Member("y", "Output signal")],
            extends: ["Base.SISO"]));
        var path = host.WriteMoFile("Base.mo", """
            package Base
              partial block SISO "single in, single out"
                input Real u "in";
                output Real y "out";
              protected
                Real hidden;
              end SISO;
            end Base;
            """);
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var views = new ViewTools(host.Libraries);

        var all = ToolAssert.Ok<ClassElementsResult>(views.ListClassElements("Vendor.BMS.Filter"));
        var components = all.Elements.Where(e => e.Kind == "component").ToList();

        Assert.Equal(["k", "u", "y"], components.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(all.Elements.Count, all.Count);
        // The documented view wins - it knows the role - and the base says where it came from.
        var u = Assert.Single(components, e => e.Name == "u");
        Assert.Equal("input", u.Causality);
        Assert.Equal("Base.SISO", u.InheritedFrom);
        Assert.Null(Assert.Single(components, e => e.Name == "k").InheritedFrom);

        var own = ToolAssert.Ok<ClassElementsResult>(
            views.ListClassElements("Vendor.BMS.Filter", includeInherited: false));
        Assert.Equal(["k"], own.Elements.Where(e => e.Kind == "component").Select(e => e.Name));

        // A protected member of the readable base is still only there when asked for.
        var withProtected = ToolAssert.Ok<ClassElementsResult>(
            views.ListClassElements("Vendor.BMS.Filter", includeProtected: true));
        Assert.Single(withProtected.Elements, e => e.Name == "hidden");
    }

    [Fact]
    public void AStubWithNothingDocumentedStillAnswers()
    {
        // A package, or a class whose tables the generator omitted: recovered, and empty.
        using var host = WithStubs(Documented("Vendor", kind: DocumentedClass.KindPackage));

        var view = ToolAssert.Ok<ClassInterfaceView>(new ViewTools(host.Libraries).GetClassInterface("Vendor"));

        Assert.True(view.RecoveredFromDocumentation);
        Assert.Empty(view.Parameters);
        Assert.Null(view.FunctionSignature);
    }
}
