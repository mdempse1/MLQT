using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// The class a component of a class <b>is</b>, after every redeclaration that reaches it - the class a
/// tool draws for it, takes its connectors from, and names on its icon.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Libraries built the template way declare their components
/// <c>replaceable</c>, usually constrained by a partial class with a plain icon, and a model extends
/// the template and redeclares them. <see cref="ClassElementResolver.Collect"/> carries an extends
/// clause's scalar modifications down to what it inherits, not its redeclarations, and
/// <see cref="TypeResolver"/> resolves a name where it is written - so every inherited component of
/// such a model kept its declared class, and a diagram of it was a page of blank boxes: the template,
/// as Dymola shows it before anything is redeclared.</para>
/// <para><b>What reaches a component</b>, the most derived winning:</para>
/// <list type="bullet">
/// <item>a <c>redeclare</c> of it in the body of a class on the way - which
/// <see cref="ClassElementResolver.Collect"/> already gives, since the redeclaring element shadows the
/// one it replaces;</item>
/// <item>a <c>redeclare</c> of it in an extends clause's modification, or a short class's, on the way
/// from the class drawn down to the class that declared it:
/// <c>extends Template(redeclare Concrete engine)</c>;</item>
/// <item>and when the class it is declared with - as written, or as redeclared - is itself a
/// replaceable class (<c>replaceable model Engine = PartialEngine; Engine engine;</c>), that class's
/// redeclaration on the way down to the class declaring it: a nested <c>redeclare model Engine =
/// Concrete</c> in a class on the way, or one in an extends clause's modification.</item>
/// </list>
/// <para>A redeclaration is resolved in the class that wrote it, as its type is a name written there.
/// A redeclaration of a component of a component (<c>extends T(sub(redeclare X part))</c>) changes what
/// <c>sub</c> holds, not what <c>sub</c> is, so it does not reach here.</para>
/// </remarks>
/// <param name="Class">The class, or null when it is not loaded.</param>
/// <param name="Written">The class as the declaration or redeclaration that decided it wrote it -
/// <c>Diesel</c> for <c>redeclare Diesel engine</c> - which names it even when it is not loaded.</param>
/// <param name="Redeclared">Whether a redeclaration decided it, rather than the declaration.</param>
/// <param name="Modifications">The scalar modifications the component is given, keyed as
/// <see cref="ClassElement.Modifications"/> are: the redeclaration's when it is redeclared, which
/// replace the declaration's, over those of a class redeclaration, which are defaults.</param>
public sealed record ComponentClass(
    ModelNode? Class, string? Written, bool Redeclared, IReadOnlyDictionary<string, string>? Modifications);

public static class RedeclaredClass
{
    private const int MaxDepth = 32;

    /// <summary>
    /// The class <paramref name="member"/> - a component of <paramref name="within"/>, as
    /// <see cref="ClassElementResolver.Collect"/> gives it - is in <paramref name="within"/>: its
    /// declared class when nothing redeclares it. Null when that class is not loaded, a redeclaration's
    /// included, or <paramref name="member"/> is not a component. <see cref="For"/> says more.
    /// </summary>
    public static ModelNode? Of(DirectedGraph graph, ModelNode within, ResolvedElement member)
        => For(graph, within, member)?.Class;

    /// <summary>
    /// What decides the class <paramref name="member"/> is in <paramref name="within"/>: the class,
    /// the name it was written as, whether a redeclaration chose it, and the modifications the
    /// component is given. Null when <paramref name="member"/> is not a component.
    /// </summary>
    public static ComponentClass? For(DirectedGraph graph, ModelNode within, ResolvedElement member)
    {
        if (member.Element.Kind != ClassElementKind.Component)
            return null;

        // A redeclared component is the redeclaration's whole declaration, modifications included:
        // the replaced one's are not carried over (MLS 7.3).
        var result = ComponentRedeclaration(graph, within, member)
            ?? new ComponentClass(
                TypeResolver.Resolve(graph, member.OwnerId, member.Element.Type, member.OwnerImports),
                member.Element.Type, Redeclared: false, member.Element.Modifications);
        if (result.Class is null || ClassRedeclaration(graph, within, result.Class) is not { } replaced)
            return result;

        // A class redeclared with modifications (`redeclare model Gearbox = Manual(r = 4)`) gives
        // defaults, which what the instance is given overrides.
        return result with
        {
            Class = replaced.Class,
            Written = replaced.Written,
            Redeclared = true,
            Modifications = Merge(replaced.Modifications, result.Modifications),
        };
    }

    // A redeclare of the component in an extends clause between `within` and the class declaring it.
    private static ComponentClass? ComponentRedeclaration(DirectedGraph graph, ModelNode within, ResolvedElement member)
    {
        if (string.Equals(member.OwnerId, within.Id, StringComparison.Ordinal)
            || PathTo(graph, within, member.OwnerId) is not { } path)
            return null;

        foreach (var (step, redeclarations) in path)
        {
            if (redeclarations?.TryGetValue(member.Element.Name, out var r) == true
                && r.ClassType is null && r.Type is { Length: > 0 } written)
                return new ComponentClass(
                    TypeResolver.Resolve(graph, step.Id, written, null), written, Redeclared: true, r.Modifications);
        }
        return null;
    }

    // When `declared` is a replaceable class, its redeclaration in a class between `within` and the
    // class declaring it: a nested class of the same name there, or a class redeclare in its clause.
    // A nested class carries its own modifications, which resolving it reads; a clause's are returned.
    private static ComponentClass? ClassRedeclaration(DirectedGraph graph, ModelNode within, ModelNode declared)
    {
        if (!IsReplaceable(declared) || declared.ParentModelName is not { } parentId
            || PathTo(graph, within, parentId) is not { } path)
            return null;

        foreach (var (step, redeclarations) in path)
        {
            if (graph.GetNode<ModelNode>(step.Id + "." + declared.Name) is { } own
                && !string.Equals(own.Id, declared.Id, StringComparison.Ordinal))
                return new ComponentClass(own, WrittenAs(own), Redeclared: true, Modifications: null);
            if (redeclarations?.TryGetValue(declared.Name, out var r) == true
                && r.ClassType is not null && r.Type is { Length: > 0 } written)
                return new ComponentClass(
                    TypeResolver.Resolve(graph, step.Id, written, null), written, Redeclared: true, r.Modifications);
        }
        return null;
    }

    // How a nested redeclared class names what it is: the base of `redeclare model Gearbox = Manual`,
    // or its own name for a long one (`redeclare model extends Gearbox ... end Gearbox`).
    private static string WrittenAs(ModelNode node) =>
        ClassElementResolver.InterfaceCache.Extract(node)?.ShortClassBase?.Trim() is { Length: > 0 } shortBase
            ? shortBase
            : node.Name;

    private static IReadOnlyDictionary<string, string>? Merge(
        IReadOnlyDictionary<string, string>? defaults, IReadOnlyDictionary<string, string>? given)
    {
        if (defaults is null || defaults.Count == 0)
            return given;
        if (given is null || given.Count == 0)
            return defaults;
        var merged = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
        foreach (var (key, value) in given)
            merged[key] = value;
        return merged;
    }

    private static bool IsReplaceable(ModelNode node) =>
        node.ElementPrefix.Contains("replaceable", StringComparison.Ordinal)
        || node.ElementPrefix.Contains("redeclare", StringComparison.Ordinal);

    /// <summary>
    /// The extends clauses from <paramref name="from"/> down to the class <paramref name="toId"/>,
    /// outermost first: each class on the way, with the redeclarations its clause toward the next
    /// makes. Empty when they are the same class; null when <paramref name="from"/> does not inherit it.
    /// </summary>
    private static List<(ModelNode Step, IReadOnlyDictionary<string, Redeclaration>? Redeclarations)>? PathTo(
        DirectedGraph graph, ModelNode from, string toId)
    {
        var path = new List<(ModelNode, IReadOnlyDictionary<string, Redeclaration>?)>();
        if (string.Equals(from.Id, toId, StringComparison.Ordinal))
            return path;
        return Walk(graph, from, toId, path, new HashSet<string>(StringComparer.Ordinal), 0) ? path : null;
    }

    private static bool Walk(
        DirectedGraph graph, ModelNode node, string toId,
        List<(ModelNode, IReadOnlyDictionary<string, Redeclaration>?)> path, HashSet<string> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(node.Id))
            return false;

        ClassInterface? iface = null;
        foreach (var (written, baseNode) in ClassElementResolver.DirectBases(graph, node))
        {
            iface ??= ClassElementResolver.InterfaceCache.Extract(node);
            path.Add((node, RedeclarationsOf(iface, written)));
            if (string.Equals(baseNode.Id, toId, StringComparison.Ordinal)
                || Walk(graph, baseNode, toId, path, visited, depth + 1))
                return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    // The redeclarations of the clause naming `written`: an extends clause's, or a short class's.
    private static IReadOnlyDictionary<string, Redeclaration>? RedeclarationsOf(ClassInterface? iface, string written)
    {
        if (iface is null)
            return null;
        if (iface.ShortClassBase is { } shortBase && SameName(shortBase, written))
            return iface.ShortClassRedeclarations;
        return iface.Elements
            .FirstOrDefault(e => e.Kind == ClassElementKind.Extends && SameName(e.Name, written))
            ?.Redeclarations;
    }

    private static bool SameName(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
}
