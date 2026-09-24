using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags a declaration that silently shadows a same-named member inherited through <c>extends</c> — a
/// common bug where a derived class re-declares a base member without <c>redeclare</c>, so the two
/// coexist confusingly. Uses the shared resolvers to walk the inheritance chain, so it needs no
/// dependency analysis (<c>extends</c> is resolved by parsing). An explicit <c>redeclare</c> is an
/// intentional override and is not flagged; a base that cannot be resolved is skipped (no false
/// positive from an invisible base).
/// </summary>
public sealed class ShadowingAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.ShadowingInheritedMember };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        // Every class here reaches for its base classes, and those overlap heavily - the same base is
        // on the chain of hundreds of others. Shared for the length of this analysis so it is
        // extracted once rather than once per class that inherits it (B128). It is safe to share
        // between threads, which is what lets the classes be analysed in parallel.
        var interfaces = new ClassElementResolver.InterfaceCache();

        // In parallel (B281): this ran on one thread for 23 seconds on Claytex, after a per-class phase
        // that used all 24 cores reading the same classes the same way. Each class's findings are kept
        // in its own slot and put back in the order given, so the report does not depend on timing.
        var models = context.Models;
        var perClass = new List<Finding>?[models.Count];
        Parallel.For(0, models.Count, i => perClass[i] = ShadowedIn(context, models[i], interfaces));

        return perClass.Where(f => f is not null).SelectMany(f => f!).ToList();
    }

    /// <summary>The declarations of one class that shadow an inherited member, or null for none.</summary>
    private static List<Finding>? ShadowedIn(
        GraphAnalysisContext context, ModelNode node, ClassElementResolver.InterfaceCache interfaces)
    {
        if (node.IsParseFailurePlaceholder)
            return null;

        List<Finding>? findings = null;
        try
        {
            // Borrowed: this walk reaches for base classes of its own, so it must not take the
            // tree of a class somebody upstream is holding. See ModelDefinition.Borrow.
            var iface = node.Definition.Borrow<ClassInterface?>(ClassInterfaceExtractor.Extract);
            if (iface is null)
                return null;

            var extends = iface.Elements.Where(e => e.Kind == ClassElementKind.Extends).ToList();
            if (extends.Count == 0)
                return null;

            var imports = iface.Elements
                .Where(e => e.Kind == ClassElementKind.Import)
                .Select(e => e.Name)
                .ToList();

            // Names of members visible through the base classes (their own + their inherited).
            var inherited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ext in extends)
            {
                var baseNode = TypeResolver.Resolve(context.Graph, node.Id, ext.Type, imports);
                if (baseNode is null)
                    continue;
                foreach (var m in ClassElementResolver.Collect(
                         context.Graph, baseNode, includeProtected: true, includeInherited: true, interfaces))
                    if (m.Element.Kind is ClassElementKind.Component or ClassElementKind.Class)
                        inherited.Add(m.Element.Name);
            }

            if (inherited.Count == 0)
                return null;

            foreach (var own in iface.Elements)
            {
                if (own.Kind is not (ClassElementKind.Component or ClassElementKind.Class))
                    continue;
                if (own.Prefixes.Contains("redeclare"))  // an intentional override, not a silent shadow
                    continue;
                if (inherited.Contains(own.Name))
                    (findings ??= []).Add(new Finding
                    {
                        RuleId = RuleIdsRef.ShadowingInheritedMember,
                        ModelId = node.Id,
                        ElementPath = own.Name,
                        Message = $"'{own.Name}' shadows a member inherited via extends",
                        LineNumber = own.Line
                    });
            }
        }
        catch
        {
            // Skip a class that fails to analyse rather than break the run.
        }

        return findings;
    }
}
