using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags an <c>import</c> that nothing in scope references.
///
/// Scope is the point. Modelica looks a simple name up in the class itself and then, if it is not
/// found there, in each lexically enclosing class in turn (§5.3.1) — and a package directory's
/// children are lexically nested inside its <c>package.mo</c> (§13.4). So an import declared in a
/// library's root package is visible to every class in the library, which is exactly how libraries
/// use it: <c>Modelica/Blocks/package.mo</c> declares <c>import Modelica.Units.SI;</c> and
/// <c>Blocks/Continuous.mo</c> writes <c>SI.Time</c> without importing anything.
///
/// That makes this rule cross-model, not per-class: the uses live in files the declaring class's own
/// parse tree never sees. Checking the declaring class alone reported every such import as unused —
/// a false positive on the top package of essentially every real library, MSL included.
///
/// The declaring class is parsed (exact: identifiers inside its own import clauses do not count).
/// Its descendants are text-scanned for the still-unaccounted-for names, which over-counts uses —
/// see <see cref="IdentifierUsageScanner"/>. Both effects push the same way: the rule under-reports
/// rather than risk claiming a live import is dead.
///
/// Known under-report: <c>encapsulated</c> stops name lookup from reaching enclosing scopes, so an
/// import an encapsulated descendant cannot actually see still counts as used here. Reporting it
/// would need the encapsulation state the graph does not currently record, and the failure mode of
/// guessing is the one this rule exists to avoid.
/// </summary>
public sealed class UnusedImportAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UnusedImport };

    /// <summary>An enclosing class whose imports are still looking for a use further down the tree.</summary>
    private sealed class Scope
    {
        public Scope(ModelNode node, IEnumerable<ImportBinding> pending)
        {
            Node = node;
            Prefix = node.Id + ".";
            Pending = pending.ToList();
        }

        public ModelNode Node { get; }

        /// <summary>Descendant ids all start with this; used to tell when the scope has closed.</summary>
        public string Prefix { get; }

        public List<ImportBinding> Pending { get; }

        /// <summary>Drops any pending import this source mentions — one use anywhere is enough.</summary>
        public void MarkMentioned(string? source)
        {
            for (var i = Pending.Count - 1; i >= 0; i--)
                if (IdentifierUsageScanner.Mentions(source, Pending[i].Alias))
                    Pending.RemoveAt(i);
        }

        public IEnumerable<Finding> UnusedFindings() => Pending.Select(Unused(Node));
    }

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();

        var reportable = context.Models
            .Where(m => m is not null && !m.IsParseFailurePlaceholder)
            .Select(m => m.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (reportable.Count == 0)
            return findings;

        // Ordinal order puts a class immediately before its descendants and nothing else between them
        // ('.' sorts below every character an identifier can continue with), so one pass with a stack
        // of open scopes visits each class once instead of re-walking a subtree per import.
        var ordered = context.Graph.ModelNodes
            .Where(n => n is not null && !n.IsParseFailurePlaceholder)
            .OrderBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        var open = new List<Scope>();

        foreach (var node in ordered)
        {
            while (open.Count > 0 && !node.Id.StartsWith(open[^1].Prefix, StringComparison.Ordinal))
            {
                findings.AddRange(open[^1].UnusedFindings());
                open.RemoveAt(open.Count - 1);
            }

            var source = node.Definition.ModelicaCode;

            // Every enclosing scope can be satisfied by this class, wherever it lives.
            foreach (var scope in open)
                scope.MarkMentioned(source);

            // Only a class that declares imports and is in the reported set needs its parse tree. The
            // text test costs nothing and skips the parse for the overwhelming majority of classes.
            if (!reportable.Contains(node.Id) ||
                source is null ||
                !source.Contains("import", StringComparison.Ordinal))
                continue;

            // Every class in this source, outermost first: the node's own class, plus any nested one
            // that cannot be stored standalone (replaceable/redeclare) and so has no node of its own.
            // An alias declared at more than one level is reported once, against the outermost.
            var pending = Extract(node).Reverse()
                .SelectMany(Unresolved)
                .GroupBy(binding => binding.Alias, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            if (pending.Count > 0)
                open.Add(new Scope(node, pending));
        }

        foreach (var scope in open)
            findings.AddRange(scope.UnusedFindings());

        return findings;
    }

    /// <summary>The scope's imports that its own source does not already account for.</summary>
    private static IEnumerable<ImportBinding> Unresolved(ImportScope scope)
        => scope.Imports.Where(i => !scope.UsedIdentifiers.Contains(i.Alias));

    private static Func<ImportBinding, Finding> Unused(ModelNode node) => binding => new Finding
    {
        RuleId = RuleIdsRef.UnusedImport,
        ModelId = node.Id,
        // Element identity only — no discriminator, matching what the per-class rule emitted before
        // this moved to the graph, so an existing baseline keeps matching these findings.
        ElementPath = binding.Alias,
        Message = $"import '{binding.Alias}' is not used in this class or any class nested inside it",
        LineNumber = binding.Line
    };

    private static IReadOnlyList<ImportScope> Extract(ModelNode node)
    {
        var tree = node.Definition.EnsureParsed();
        if (tree is null)
            return [];

        try
        {
            var lastDot = node.Id.LastIndexOf('.');
            var basePackage = lastDot > 0 ? node.Id[..lastDot] : string.Empty;
            var extractor = new ImportScopeExtractor(basePackage);
            extractor.VisitStored_definition(tree);
            return extractor.Scopes;
        }
        catch
        {
            return [];
        }
        finally
        {
            node.Definition.ParsedCode = null;   // release the parse tree to bound memory
        }
    }
}
