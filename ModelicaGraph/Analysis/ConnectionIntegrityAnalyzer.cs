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
/// <para>Both read the connects through <see cref="ConnectClauses"/>, the one reader of a class's
/// connections, nested ones included.</para>
/// </remarks>
public sealed class ConnectionIntegrityAnalyzer : IGraphAnalyzer
{
    /// <inheritdoc/>
    public string Name => "ConnectionIntegrityAnalyzer";

    public IReadOnlyList<string> RuleIds { get; } = [RuleIdsRef.IncompatibleConnection, RuleIdsRef.DuplicateConnection];

    /// <param name="Applies">When it applies: each enclosing branch with the conditions that must be
    /// false for it (<see cref="ConnectScope.Applies"/>), which a duplicate must share.</param>
    private sealed record Connect(string PortA, string PortB, int Line, IReadOnlyList<string> Within, string Applies);

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var checkIncompatible = context.Settings.SeverityFor(RuleIdsRef.IncompatibleConnection) != RuleSeverity.Off;
        var checkDuplicate = context.Settings.SeverityFor(RuleIdsRef.DuplicateConnection) != RuleSeverity.Off;
        if (!checkIncompatible && !checkDuplicate)
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
                if (checkIncompatible)
                    found.AddRange(Incompatible(context.Graph, model, connects, signatures));
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
        var seen = new Dictionary<string, Connect>(StringComparer.Ordinal);
        foreach (var connect in connects)
        {
            var pair = PairOf(connect);
            var key = pair + " | " + connect.Applies;

            if (seen.TryGetValue(key, out var first))
            {
                yield return new Finding
                {
                    RuleId = RuleIdsRef.DuplicateConnection,
                    ModelId = model.Id,
                    ElementPath = pair,
                    Discriminator = connect.Within.Count == 0 ? null : string.Join(" / ", connect.Within),
                    Message = $"connect({connect.PortA}, {connect.PortB}) joins the same ports as the connect on line {first.Line}",
                    LineNumber = connect.Line,
                };
                continue;
            }

            seen[key] = connect;
        }
    }

    private static IEnumerable<Finding> Incompatible(
        DirectedGraph graph, ModelNode model, List<Connect> connects, ConnectorSignature signatures)
    {
        var references = ClassElementResolver.ReferencesIn(graph, model, signatures.Interfaces, signatures.Ancestors);
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
