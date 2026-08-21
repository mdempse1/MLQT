using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// One library declared in a <c>uses(...)</c> annotation whose version does not match the copy that
/// was actually loaded. <see cref="Loaded"/> is null when the loaded library states no version.
/// </summary>
public sealed record UsesVersionMismatch(string Library, string DeclaredBy, string Declared, string? Loaded)
{
    public string Describe() => Loaded is null
        ? $"{DeclaredBy} declares {Library} {Declared}, but the loaded copy states no version"
        : $"{DeclaredBy} declares {Library} {Declared}, but {Loaded} is loaded";
}

/// <summary>
/// Compares each checked library's declared dependency versions against the versions actually loaded.
///
/// This is a statement about the check's setup, not about the code: when the copy of a dependency on
/// the machine is not the one the library says it targets, references resolve against classes that may
/// have moved, been renamed or changed signature between versions — so the findings themselves become
/// unreliable. It is reported as a warning rather than a rule for that reason: there is nothing here
/// to baseline or to fix in the source, only a checkout to correct.
/// </summary>
public static class UsesVersionChecker
{
    /// <summary>
    /// Mismatches between what <paramref name="checkedRoots"/> declare and what the graph holds.
    /// A declared library that is not loaded at all is ignored — that is a different problem, already
    /// visible as unresolved references, and guessing a version for something absent says nothing.
    /// </summary>
    public static IReadOnlyList<UsesVersionMismatch> Check(
        DirectedGraph graph, IEnumerable<ModelNode> checkedRoots)
    {
        var loadedVersions = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var node in graph.ModelNodes)
            if (IsLibraryRoot(node))
                loadedVersions[node.Definition.Name] = node.Version;

        var mismatches = new List<UsesVersionMismatch>();
        foreach (var root in checkedRoots)
        {
            if (!IsLibraryRoot(root) || root.Uses is null)
                continue;

            foreach (var (library, declared) in root.Uses)
            {
                if (string.IsNullOrWhiteSpace(declared) || !loadedVersions.TryGetValue(library, out var loaded))
                    continue;
                if (VersionsAgree(declared, loaded))
                    continue;

                mismatches.Add(new UsesVersionMismatch(library, root.Definition.Name, declared, loaded));
            }
        }

        return mismatches
            .OrderBy(m => m.DeclaredBy, StringComparer.Ordinal)
            .ThenBy(m => m.Library, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsLibraryRoot(ModelNode node) =>
        node.ClassType == "package" && string.IsNullOrEmpty(node.ParentModelName) && !node.IsParseFailurePlaceholder;

    /// <summary>
    /// Whether a declared and a loaded version should be treated as the same.
    ///
    /// Compared segment by segment, and a shorter declaration matches a longer version: "4.0" covers
    /// "4.0.0", because a library that says it targets 4.0 is not making a claim about the patch
    /// digit. Anything the loaded version appends beyond the declared segments (a build suffix such as
    /// "4.2.0 dev") is likewise not a disagreement about what was declared.
    /// </summary>
    private static bool VersionsAgree(string declared, string? loaded)
    {
        if (loaded is null)
            return false;   // reported, since "targets 4.0.0" against an unversioned copy is unverifiable

        var declaredParts = Segments(declared);
        var loadedParts = Segments(loaded);
        if (declaredParts.Length == 0 || loadedParts.Length == 0)
            return string.Equals(declared.Trim(), loaded.Trim(), StringComparison.OrdinalIgnoreCase);

        if (declaredParts.Length > loadedParts.Length)
            return false;

        for (var i = 0; i < declaredParts.Length; i++)
            if (!string.Equals(declaredParts[i], loadedParts[i], StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    // "4.2.0 dev" -> ["4","2","0"]: the numeric dotted part, with any trailing build text dropped.
    private static string[] Segments(string version) =>
        version.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } words
            ? words[0].Split('.', StringSplitOptions.RemoveEmptyEntries)
            : [];
}
