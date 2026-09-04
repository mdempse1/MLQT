using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Checks a library's top-level <c>uses(...)</c> annotation against the libraries its code actually
/// references (via cross-model dependency edges — so this needs dependency analysis):
/// <list type="bullet">
/// <item><b>undeclared</b> — an external library referenced by the code but missing from <c>uses(...)</c>;</item>
/// <item><b>unused</b> — a library declared in <c>uses(...)</c> that (while loaded) nothing references.</item>
/// </list>
/// Both directions are conservative against invisible libraries: a referenced library only shows up
/// when it is loaded (its models resolve), and <b>unused</b> is reported only for a declared library
/// that is loaded but unreferenced — a declared dependency that isn't loaded is never flagged (we
/// can't tell whether it's used). Findings attach to the library's root package.
/// </summary>
public sealed class UsesHygieneAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UsesUndeclared, RuleIdsRef.UsesDeclaredUnused };

    public bool NeedsDependencyAnalysis => true;

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();
        var checkUndeclared = context.Settings.SeverityFor(RuleIdsRef.UsesUndeclared) != RuleSeverity.Off;
        var checkUnused = context.Settings.SeverityFor(RuleIdsRef.UsesDeclaredUnused) != RuleSeverity.Off;
        if (!checkUndeclared && !checkUnused)
            return findings;

        var allModels = context.Graph.ModelNodes.Where(m => m is not null && !m.IsParseFailurePlaceholder).ToList();
        var loadedLibs = new HashSet<string>(allModels.Select(m => ModelicaName.RootLibraryOf(m.Id)), StringComparer.Ordinal);

        // The set of external libraries each library references (root package name -> referenced roots).
        var referencedByLib = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var model in allModels)
        {
            var ownLib = ModelicaName.RootLibraryOf(model.Id);
            foreach (var used in model.UsedModelIds)
            {
                var usedLib = ModelicaName.RootLibraryOf(used);
                if (string.Equals(usedLib, ownLib, StringComparison.Ordinal))
                    continue;
                if (!referencedByLib.TryGetValue(ownLib, out var set))
                    referencedByLib[ownLib] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(usedLib);
            }
        }

        // Report against each checked library root (a top-level package).
        foreach (var root in context.Models)
        {
            if (root.ClassType != "package" || !string.IsNullOrEmpty(root.ParentModelName))
                continue;

            var libName = ModelicaName.RootLibraryOf(root.Id);
            var referenced = referencedByLib.TryGetValue(libName, out var r) ? r : new HashSet<string>(StringComparer.Ordinal);
            var declared = root.Uses;

            if (checkUndeclared)
                foreach (var lib in referenced)
                    if (declared is null || !declared.ContainsKey(lib))
                        findings.Add(new Finding
                        {
                            RuleId = RuleIdsRef.UsesUndeclared,
                            ModelId = root.Id,
                            ElementPath = lib,
                            Discriminator = "undeclared",
                            Message = $"library '{lib}' is referenced but not declared in the uses(...) of {root.Definition.Name}",
                            // The library root class as a whole; lines are class-relative.
                            LineNumber = 1
                        });

            if (checkUnused && declared is not null)
                foreach (var lib in declared.Keys)
                    if (loadedLibs.Contains(lib) && !referenced.Contains(lib))
                        findings.Add(new Finding
                        {
                            RuleId = RuleIdsRef.UsesDeclaredUnused,
                            ModelId = root.Id,
                            ElementPath = lib,
                            Discriminator = "unused",
                            Message = $"uses(...) of {root.Definition.Name} declares '{lib}', but nothing references it",
                            LineNumber = 1
                        });
        }

        return findings;
    }

}
