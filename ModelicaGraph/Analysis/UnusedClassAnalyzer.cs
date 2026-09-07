using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags nested classes that nothing references (empty <c>UsedByModelIds</c>), in two confidence tiers:
/// <list type="bullet">
/// <item><b><c>MLQT.Unused.Class</c></b> — a <b>protected</b> nested class. Being protected it cannot be
/// used from outside its enclosing class, so this is high-confidence with no external-visibility false
/// positives.</item>
/// <item><b><c>MLQT.Unused.PublicClass</c></b> — a <b>public</b> nested class. Lower confidence (Info by
/// default): a downstream library we cannot see may use it, so this is only meaningful on an application
/// library, not a foundational one. Opt-in and off by default.</item>
/// </list>
/// Needs dependency analysis (the reverse edges are analyzer-only). Never flagged: top-level classes
/// (a library root is external API by definition), partial classes (extended, not instantiated),
/// packages, classes carrying an <c>experiment(...)</c> annotation (simulation entry points — they are
/// meant to be run, not referenced), and an <c>ExternalObject</c>'s <c>constructor</c>/<c>destructor</c>
/// (called implicitly by the language). Each tier only runs when its own rule is enabled.
/// </summary>
public sealed class UnusedClassAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UnusedClass, RuleIdsRef.UnusedPublicClass };

    public bool NeedsDependencyAnalysis => true;

    /// <summary>
    /// True for the <c>constructor</c>/<c>destructor</c> of an <c>ExternalObject</c>. Modelica
    /// reserves those two names inside an ExternalObject for the create/destroy protocol and calls
    /// them implicitly, so they are never referenced by name.
    /// </summary>
    private static bool IsExternalObjectLifecycleFunction(ModelNode node) =>
        node.ClassType == "function" &&
        (node.Definition.Name == "constructor" || node.Definition.Name == "destructor");

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();
        var checkProtected = context.Settings.SeverityFor(RuleIdsRef.UnusedClass) != RuleSeverity.Off;
        var checkPublic = context.Settings.SeverityFor(RuleIdsRef.UnusedPublicClass) != RuleSeverity.Off;
        if (!checkProtected && !checkPublic)
            return findings;

        foreach (var node in context.Models)
        {
            if (node.IsParseFailurePlaceholder || node.IsPartial || node.ClassType == "package")
                continue;
            if (!node.IsNested || string.IsNullOrEmpty(node.ParentModelName))
                continue;
            // An experiment(...) annotation marks a simulation entry point: the class exists to be
            // run, not to be instantiated by something else, so "nothing references it" is the
            // expected state rather than a finding. Without this, a library's whole test/example
            // package reports as dead code.
            if (node.HasExperimentAnnotation)
                continue;
            // An ExternalObject's constructor/destructor are the language's own protocol: the tool
            // calls them implicitly when the object is created and destroyed, so no code anywhere
            // references them by name. Reporting them is never right, in any library.
            if (IsExternalObjectLifecycleFunction(node))
                continue;
            if (node.UsedByModelIds.Count > 0)
                continue;

            // Visibility comes from the node, captured when the class was parsed. It used to be
            // re-derived by re-parsing the parent package's stored source, which is trimmed of its
            // standalone children as a memory optimisation — so a child the trim had removed matched
            // neither tier and was silently never reported, and the count changed depending on
            // whether the library had just been loaded or just been reloaded.
            if (!node.IsPublic)
            {
                if (checkProtected)
                    findings.Add(new Finding
                    {
                        RuleId = RuleIdsRef.UnusedClass,
                        ModelId = node.Id,
                        Message = $"protected class {node.Definition.Name} is never used",
                        // The class as a whole; finding lines are class-relative (see Finding.LineNumber).
                        LineNumber = 1
                    });
            }
            else if (checkPublic)
            {
                findings.Add(new Finding
                {
                    RuleId = RuleIdsRef.UnusedPublicClass,
                    ModelId = node.Id,
                    Message = $"public class {node.Definition.Name} may be unused — nothing in the loaded libraries references it",
                    LineNumber = 1
                });
            }
        }

        return findings;
    }
}
