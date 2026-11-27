using System.Text;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// The <c>connect</c> equations a class writes: <c>MLQT.Connection.Incompatible</c>, two connectors
/// that cannot be joined, and <c>MLQT.Connection.Duplicate</c>, the same pair joined twice.
/// </summary>
/// <remarks>
/// <para><b>Incompatible</b> resolves each port through the class's own members and their types
/// (<see cref="ComponentReferences"/>, which follows inheritance and drops subscripts) to the connector
/// class at its end, and compares the two by <see cref="ConnectorSignature"/>. Reported only when
/// both signatures are known: a port that does not resolve, a connector that is encrypted or has a base
/// MLQT cannot read, and anything expandable are "cannot tell" - and a name that does not exist is
/// <c>MLQT.Reference.Undeclared</c>'s to report, not this rule's. Whatever a redeclaration decides is
/// left alone too: a port declared <c>replaceable</c>, one reached through a replaceable component,
/// and one whose class, or a class on the way to it, is replaceable.</para>
///
/// <para><b>Duplicate</b> is the same two ports, either way round and spaced any way, connected again
/// under branches that apply at the same time: the same <c>for</c>/<c>if</c>/<c>when</c> headers,
/// with the same earlier conditions false (<see cref="ConnectScope.Applies"/>). The same pair in two
/// branches of an <c>if</c> is two connections that never both exist, and is not reported, nor are
/// the <c>else</c> branches of two different statements. Subscripts are compared as written, so
/// <c>a[1]</c> and <c>a[2]</c> are different ports. Needs no graph at all.</para>
///
/// <para><b>DeepReference</b> (<c>MLQT.Connection.DeepReference</c>) is an argument of a form MLS 3.7
/// §9.1 does not allow. An argument is either <c>c1.c2.….cn</c>, where <c>c1</c> is a connector of the
/// class and each later name a connector element of the one before, or <c>m.c</c>, where <c>m</c> is a
/// non-connector element of the class and <c>c</c> a connector element of it; §9.3 accepts no other
/// form. So an argument whose first name is a component that is not a connector has exactly two names,
/// and <c>aero.controlBus.driverBus.x</c> - through component <c>aero</c>'s expandable bus into a
/// sub-bus - is refused by Dymola, though OpenModelica 1.27 accepts it. Only that is reported: three or
/// more names, the first a component of the class itself (inherited included, subscripts ignored)
/// whose type resolves to a <c>model</c> or <c>block</c>. What follows the first name is not resolved
/// at all, because the rule does not depend on it and an expandable connector's members need not be
/// declared. Whether a connector argument's later names are connector elements, and whether
/// <c>m.c</c>'s <c>c</c> is a connector, are left alone: a member of an expandable connector counts as
/// a connector whether or not it is declared, which makes those questions less clear-cut.</para>
///
/// <para>All three read the connects through <see cref="ConnectClauses"/>, the one reader of a class's
/// connections, nested ones included.</para>
/// </remarks>
public sealed class ConnectionIntegrityAnalyzer : IGraphAnalyzer
{
    /// <inheritdoc/>
    public string Name => "ConnectionIntegrityAnalyzer";

    public IReadOnlyList<string> RuleIds { get; } =
        [RuleIdsRef.IncompatibleConnection, RuleIdsRef.DuplicateConnection, RuleIdsRef.DeepConnectorReference];

    /// <param name="Applies">When it applies: each enclosing branch with the conditions that must be
    /// false for it (<see cref="ConnectScope.Applies"/>), which a duplicate must share.</param>
    private sealed record Connect(string PortA, string PortB, int Line, IReadOnlyList<string> Within, string Applies);

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var checkIncompatible = context.Settings.SeverityFor(RuleIdsRef.IncompatibleConnection) != RuleSeverity.Off;
        var checkDuplicate = context.Settings.SeverityFor(RuleIdsRef.DuplicateConnection) != RuleSeverity.Off;
        var checkDeep = context.Settings.SeverityFor(RuleIdsRef.DeepConnectorReference) != RuleSeverity.Off;
        if (!checkIncompatible && !checkDuplicate && !checkDeep)
            return [];

        var signatures = new ConnectorSignature(context.Graph);
        var models = context.Models;

        // In parallel, a class at a time, as UndeclaredNameAnalyzer does - the caches it shares are the
        // same thread-safe ones - with each class's findings kept in its own slot so the order does
        // not depend on the scheduling.
        var perClass = new List<Finding>?[models.Count];
        Parallel.For(0, models.Count, i =>
        {
            var model = models[i];
            if (model.IsExternalStub || model.IsParseFailurePlaceholder)
                return;

            try
            {
                var connects = ConnectsIn(model);
                if (connects.Count == 0)
                    return;

                var found = new List<Finding>();
                if (checkDuplicate)
                    found.AddRange(Duplicates(model, connects));
                if (checkIncompatible || checkDeep)
                {
                    var references = ClassElementResolver.ReferencesIn(
                        context.Graph, model, signatures.Interfaces, signatures.Ancestors);
                    if (checkIncompatible)
                        found.AddRange(Incompatible(context.Graph, model, connects, signatures, references));
                    if (checkDeep)
                        found.AddRange(TooDeep(model, connects, references));
                }
                perClass[i] = found;
            }
            catch
            {
                // A class that cannot be analysed is skipped rather than breaking the run, as the
                // other analyses do; it reports nothing it is not sure of.
            }
        });

        return perClass.Where(f => f is not null).SelectMany(f => f!).ToList();
    }

    private static List<Connect> ConnectsIn(ModelNode model) =>
        model.Definition.Borrow(
            tree => tree.class_definition() is [var c, ..]
                    && c.class_specifier()?.long_class_specifier()?.composition() is { } composition
                ? ConnectClauses.WithEquations(composition)
                    .Select(found => new Connect(
                        found.Clause.PortA, found.Clause.PortB, found.Equation.Start.Line, found.Clause.Within,
                        string.Join(" | ", found.Clause.Scopes.Select(s => s.Applies))))
                    .ToList()
                : [],
            []);

    private static IEnumerable<Finding> Duplicates(ModelNode model, List<Connect> connects)
    {
        var seen = new Dictionary<string, (Connect First, int Copies)>(StringComparer.Ordinal);
        foreach (var connect in connects)
        {
            var pair = PairOf(connect);
            var key = pair + " | " + connect.Applies;

            if (seen.TryGetValue(key, out var earlier))
            {
                var copies = earlier.Copies + 1;
                seen[key] = (earlier.First, copies);

                // The fingerprint has to tell every duplicate apart, or a baseline that accepted one
                // accepts a copy added later. So the discriminator is what decided it was a duplicate -
                // the branches by their full conditions, which two else branches of different ifs do
                // not share although their headers read the same - and, from a third copy on, which
                // copy it is.
                var discriminator = connect.Applies.Length == 0 ? null : connect.Applies;
                if (copies > 2)
                    discriminator = $"{discriminator} #{copies}";

                yield return new Finding
                {
                    RuleId = RuleIdsRef.DuplicateConnection,
                    ModelId = model.Id,
                    ElementPath = pair,
                    Discriminator = discriminator,
                    Message = $"connect({connect.PortA}, {connect.PortB}) joins the same ports as the connect on line {earlier.First.Line}",
                    LineNumber = connect.Line,
                };
                continue;
            }

            seen[key] = (connect, 1);
        }
    }

    private static IEnumerable<Finding> Incompatible(
        DirectedGraph graph, ModelNode model, List<Connect> connects, ConnectorSignature signatures,
        ComponentReferences references)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connect in connects)
        {
            if (ConnectorSignature.ConnectorOf(graph, references.Resolve(connect.PortA)) is not { } a
                || ConnectorSignature.ConnectorOf(graph, references.Resolve(connect.PortB)) is not { } b
                || signatures.Of(a) is not { } signatureA
                || signatures.Of(b) is not { } signatureB
                || string.Equals(signatureA, signatureB, StringComparison.Ordinal))
                continue;

            // Either way round is the same connection, as it is for a duplicate.
            if (!reported.Add(PairOf(connect)))
                continue;

            yield return new Finding
            {
                RuleId = RuleIdsRef.IncompatibleConnection,
                ModelId = model.Id,
                ElementPath = PairOf(connect),
                Message = $"connect({connect.PortA}, {connect.PortB}) joins {a.Id} to {b.Id}, which cannot be connected: " +
                          $"{What(signatureA)} against {What(signatureB)}",
                LineNumber = connect.Line,
            };
        }
    }

    private static IEnumerable<Finding> TooDeep(ModelNode model, List<Connect> connects, ComponentReferences references)
    {
        foreach (var connect in connects)
        {
            foreach (var (port, other) in new[] { (connect.PortA, connect.PortB), (connect.PortB, connect.PortA) })
            {
                if (ThroughComponent(model, port, references) is not { } component)
                    continue;

                var discriminator = Normalised(other);
                if (connect.Applies.Length > 0)
                    discriminator += " | " + connect.Applies;

                var bus = $"{component.Name}.{component.Next}";
                yield return new Finding
                {
                    RuleId = RuleIdsRef.DeepConnectorReference,
                    ModelId = model.Id,
                    ElementPath = Normalised(port),
                    Discriminator = discriminator,
                    Message = $"connect({connect.PortA}, {connect.PortB}) reaches through component {component.Name} " +
                              $"({component.Type}) into {Normalised(port)}. A connect argument may name a connector " +
                              $"of the class, or one inside it, or a connector of a component ({bus}), and no deeper " +
                              $"(MLS 9.1); Dymola refuses it. Connect {bus} to a connector of this class and reach into that instead",
                    LineNumber = connect.Line,
                };
            }
        }
    }

    /// <summary>
    /// The component a port reaches into, when the port has three or more names and its first is a
    /// component of the class whose type is a model or block - the one form <c>DeepReference</c> judges.
    /// Null for every other port, and for one whose first name MLQT cannot place.
    /// </summary>
    private static (string Name, string Next, string Type)? ThroughComponent(
        ModelNode model, string port, ComponentReferences references)
    {
        if (ClassElementResolver.ReferenceSegments(port) is not { Global: false, Segments: { Count: >= 3 } segments })
            return null;

        // Resolved alone: the later names are a bus's members, which need not be declared, and the
        // rule depends only on what the first one is.
        if (references.Resolve(segments[0]) is not { QualifierSegments: 0, Path.Count: 1, Type: { } type } first
            || first.Scope.Id != model.Id
            || first.Element.Element.Kind != ClassElementKind.Component
            || type.ClassType is not ("model" or "block"))
            return null;

        return (segments[0], segments[1], type.Id);
    }

    /// <summary>The two ports, normalised and in order, so either way round is the same pair.</summary>
    private static string PairOf(Connect connect)
    {
        var a = Normalised(connect.PortA);
        var b = Normalised(connect.PortB);
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}, {b}" : $"{b}, {a}";
    }

    private static string What(string signature) =>
        ConnectorSignature.HasMembers(signature)
            ? "members " + ConnectorSignature.Describe(signature)
            : ConnectorSignature.Describe(signature);

    /// <summary>A port as written with its spacing taken out, quoted identifiers left as they are.</summary>
    internal static string Normalised(string port)
    {
        var text = new StringBuilder(port.Length);
        var quoted = false;
        foreach (var c in port)
        {
            if (c == '\'')
                quoted = !quoted;
            if (quoted || !char.IsWhiteSpace(c))
                text.Append(c);
        }
        return text.ToString();
    }
}
