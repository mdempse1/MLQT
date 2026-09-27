using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Reports a file whose <c>within</c> clause does not name the package whose directory it is stored
/// in (B458).
///
/// <para>MLS 3.6 section 13.4.3: "A non-top-level entity shall begin with a within-clause which for
/// the class defined in the entity specifies the location in the Modelica class hierarchy", and "For
/// a sub-entity of an enclosing structured entity, the within-clause shall designate the class of the
/// enclosing entity". So <c>Lib/R.mo</c> must say <c>within Lib;</c> and <c>Lib/Sub/package.mo</c>
/// must too, the enclosing entity of a <c>package.mo</c> being its directory's parent.</para>
///
/// <para>MLQT names a class from its <c>within</c> clause and nothing else, so a file that says
/// <c>within Lib.Q;</c> loads as <c>Lib.Q.R</c> and is shown there — where no other Modelica tool
/// will look for it, since they find a class by walking the directories. And <b>Format All Files</b>
/// cannot place it: when <c>Q</c> is written inline in <c>Lib/package.mo</c>, nothing is written
/// that holds R, and B441's guard refuses the whole library rather than delete <c>R.mo</c> (B449).
/// Before this rule that was the first a user heard of it.</para>
///
/// <para><b>What it judges.</b> Only a file whose directory is a package MLQT has loaded — one with
/// a <c>package.mo</c> in the graph. A file whose directory is not a package is a top-level entity,
/// and a top-level entity's <c>within</c> clause cannot be judged from where it is: a repository may
/// hold one sub-package of a larger library, whose root says <c>within Modelica;</c> quite properly.
/// For the same reason the expected name is built from the root package's <em>loaded</em> name, not
/// from its directory, and from the class names in each nested <c>package.mo</c> rather than their
/// directories' names — so one wrong <c>within</c> in a <c>package.mo</c> is one finding, not one on
/// every file below it that is right.</para>
///
/// <para>Structural only, so it needs no dependency analysis. On by default, for the reason
/// <see cref="SingleFilePackageAnalyzer"/> is: the files that trip it are written by other tools and
/// by hand, and a user who has not heard of the rule is the user whose class has gone missing.</para>
/// </summary>
public sealed class WithinClauseAnalyzer : IGraphAnalyzer
{
    private const string PackageFile = "package.mo";

    public IReadOnlyList<string> RuleIds { get; } =
        new[] { ModelicaParser.StyleRules.RuleIds.WithinClause };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();

        // Per run, per directory: what a file stored in it must say. Only the directories the checked
        // classes' files are in and their ancestors are ever asked, so this scales with the checked
        // set rather than the graph.
        var expectedByDirectory = new Dictionary<string, string?>(PathComparer);

        foreach (var model in context.Models)
        {
            // The class that heads its file: the one the within clause is about. A nested class's
            // parent is the class around it, not a clause.
            if (model.IsNested || model.IsParseFailurePlaceholder || model.Definition is null
                || string.IsNullOrEmpty(model.ContainingFileId))
                continue;

            if (context.Graph.GetNode<FileNode>(model.ContainingFileId) is not { } file
                || string.IsNullOrEmpty(file.FilePath))
                continue;

            var enclosing = EnclosingDirectory(file.FilePath);
            if (enclosing is null)
                continue;

            var expected = ExpectedWithin(context.Graph, enclosing, expectedByDirectory);
            if (expected is null)
                continue;

            var actual = model.ParentModelName ?? string.Empty;
            if (string.Equals(actual, expected, StringComparison.Ordinal))
                continue;

            var says = actual.Length == 0 ? "has no within clause" : $"says 'within {actual};'";
            findings.Add(new Finding
            {
                RuleId = ModelicaParser.StyleRules.RuleIds.WithinClause,
                ModelId = model.Id,
                ElementPath = model.Definition.Name,
                Message = $"{file.FileName} {says} but is stored in the directory of package {expected}, "
                        + $"so it must say 'within {expected};'. It loads as {model.Id}, where other "
                        + $"Modelica tools will not look for it",
                // The clause is in the file, before the class, so there is no class-relative line
                // for it; the class as a whole is what is misplaced.
                LineNumber = 1
            });
        }

        return findings;
    }

    /// <summary>
    /// The directory whose package a file is a sub-entity of: its own directory, or for a
    /// <c>package.mo</c> the directory above, since that file <em>is</em> its directory's package.
    /// </summary>
    private static string? EnclosingDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (directory is null)
            return null;

        return string.Equals(Path.GetFileName(filePath), PackageFile, StringComparison.Ordinal)
            ? Path.GetDirectoryName(directory)
            : directory;
    }

    /// <summary>
    /// The within clause a file stored in <paramref name="directory"/> must carry, or null when that
    /// directory is not a loaded package and there is nothing to judge against.
    /// </summary>
    private static string? ExpectedWithin(
        DirectedGraph graph, string directory, Dictionary<string, string?> cache)
    {
        if (cache.TryGetValue(directory, out var known))
            return known;

        string? expected = null;
        if (PackageDefinedIn(graph, directory) is { } package)
        {
            var parent = Path.GetDirectoryName(directory);
            var above = parent is null ? null : ExpectedWithin(graph, parent, cache);

            // The root of the directory tree is trusted to be what it says it is; everything below
            // it is named from the root down.
            expected = above is null ? package.Id : $"{above}.{package.Definition.Name}";
        }

        cache[directory] = expected;
        return expected;
    }

    /// <summary>The class <c>directory/package.mo</c> defines, if that file is loaded and parsed.</summary>
    private static ModelNode? PackageDefinedIn(DirectedGraph graph, string directory)
    {
        var fileId = GraphBuilder.GenerateFileId(Path.Combine(directory, PackageFile));
        if (graph.GetNode<FileNode>(fileId) is null)
            return null;

        return graph.GetModelsInFile(fileId)
            .FirstOrDefault(m => !m.IsNested && !m.IsParseFailurePlaceholder && m.Definition is not null);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
