using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;
using MLQT.McpServer.Dtos;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The interface of a class whose library ships encrypted, read off the documentation it was
/// reconstructed from (B179).
///
/// <para><b>Why the view tools need this at all.</b> Everything else about a stub travels in the
/// synthesized source and is resolved by the ordinary parse-tree path — the description, the base
/// classes, the icon. Its members cannot: a Modelica declaration needs a type and the vendor's
/// generator does not publish one, so there is nothing truthful to write. Without this an agent
/// loading a commercial library got a name and an <c>extends</c> and was told the class has no
/// parameters, which is a worse answer than "the source is encrypted".</para>
///
/// <para><b>What is knowable is the role, not the type.</b> The generator prints a class's
/// parameters, connectors, function inputs and outputs in separate tables, so each member arrives
/// already knowing which it is; it carries a name, a description and sometimes a unit, and its
/// <c>type</c> is null here and always will be. Every result that includes these says so, with
/// <c>recoveredFromDocumentation</c>, so a null type reads as "not published" and not as "MLQT
/// could not work it out".</para>
/// </summary>
public static class RecoveredInterface
{
    /// <summary>
    /// The class's documentation when it is a stub and there is any, otherwise null — so a caller
    /// asks one question rather than two, and a stub built before this existed simply has none.
    /// </summary>
    public static DocumentedClass? For(ModelNode? node) =>
        node is { IsExternalStub: true } ? node.RecoveredFromDocumentation : null;

    /// <summary>The interface view of a documented class, in place of the one the parse tree cannot give.</summary>
    public static ClassInterfaceView ToInterfaceView(
        ModelNode node, DocumentedClass documented, IReadOnlyList<string> extends)
    {
        var signature = documented.Inputs.Count > 0 || documented.Outputs.Count > 0
            ? new FunctionSignatureView(
                [.. documented.Inputs.Select(m => ToParameter(m, causalArgument: true))],
                [.. documented.Outputs.Select(m => ToParameter(m, causalArgument: true))])
            : null;

        return new ClassInterfaceView(
            node.Id, node.Name, node.ClassType, node.IsPartial, documented.Description,
            extends,
            [.. documented.Parameters.Select(m => ToParameter(m, causalArgument: false))],
            [.. documented.Connectors.Select(ToConnector)],
            [.. documented.Contents.Select(ToMember)],
            signature,
            RecoveredFromDocumentation: true);
    }

    /// <summary>
    /// The documented members as raw elements, to sit beside the ones the stub's own source does
    /// give — its extends clauses, which are real declarations and are collected as usual.
    /// </summary>
    public static List<ClassElementView> ToElementViews(DocumentedClass documented)
    {
        var elements = new List<ClassElementView>();

        void Add(IReadOnlyList<DocumentedMember> members, string? variability, string? causality)
        {
            foreach (var member in members)
                elements.Add(new ClassElementView(
                    Kind: "component",
                    member.Name,
                    Type: null,
                    variability,
                    causality,
                    Connection: null,
                    // Documentation omits protected members entirely, so anything listed is public.
                    Visibility: "public",
                    Default: null,
                    TypeModification: null,
                    member.Description,
                    ClassType: null,
                    Prefixes: [],
                    LeadingComments: [],
                    // Nothing was read from a file, so there is no line to point at. Zero says that;
                    // a 1 would look like the top of the class and be wrong in a way that navigates.
                    Line: 0,
                    // Documentation does not say whether a member was conditional, and it would be
                    // a guess to imply it was not.
                    Condition: null,
                    InheritedFrom: null));
        }

        Add(documented.Parameters, variability: "parameter", causality: null);
        Add(documented.Connectors, variability: null, causality: null);
        Add(documented.Inputs, variability: null, causality: "input");
        Add(documented.Outputs, variability: null, causality: "output");
        Add(documented.Contents, variability: null, causality: null);

        return elements;
    }

    private static ParameterView ToParameter(DocumentedMember member, bool causalArgument) =>
        new(member.Name, Type: null, Variability: causalArgument ? null : "parameter",
            Default: null, TypeModification: null, member.Description, InheritedFrom: null,
            member.Unit);

    private static ConnectorView ToConnector(DocumentedMember member) =>
        // TypeIsConnector answers "does its declared type resolve to a loaded connector class?",
        // and there is no declared type - being in this list is the whole of what is known.
        new(member.Name, Type: null, Causality: null, Connection: null, TypeIsConnector: false,
            member.Description, InheritedFrom: null, member.Unit);

    private static MemberView ToMember(DocumentedMember member) =>
        new(member.Name, Type: null, member.Description, InheritedFrom: null, member.Unit);
}
