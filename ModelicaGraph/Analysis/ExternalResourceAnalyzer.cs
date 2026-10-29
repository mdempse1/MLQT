using ModelicaParser.DataTypes;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// A library's external resources as findings: <c>MLQT.Resource.Missing</c> for a reference that
/// resolved to nothing, and <c>MLQT.Resource.AbsolutePath</c> for one written as a path only one
/// machine has.
/// </summary>
/// <remarks>
/// <para>The External Resources tab has shown these since resource tracking was built, but as
/// warnings on one page: with no rule id they never reached <c>mlqt check</c>, could not be
/// baselined and could not fail a build, so a library could lose a file from its <c>Resources/</c>
/// folder with every gate green. The decision is <see cref="ResourceProblems"/>, which the tab reads
/// too, so the two cannot disagree.</para>
///
/// <para>The references are the resource edges the dependency analysis builds, which is why this
/// needs it. A reference into a library that is not loaded has no edge, and so is never reported.</para>
/// </remarks>
public sealed class ExternalResourceAnalyzer : IGraphAnalyzer
{
    /// <inheritdoc/>
    public string Name => "ExternalResourceAnalyzer";

    public IReadOnlyList<string> RuleIds { get; } = [RuleIdsRef.MissingResource, RuleIdsRef.AbsoluteResourcePath];

    public bool NeedsDependencyAnalysis => true;

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();
        var checkMissing = context.Settings.SeverityFor(RuleIdsRef.MissingResource) != RuleSeverity.Off;
        var checkAbsolute = context.Settings.SeverityFor(RuleIdsRef.AbsoluteResourcePath) != RuleSeverity.Off;
        if (!checkMissing && !checkAbsolute)
            return findings;

        foreach (var model in context.Models)
        {
            foreach (var problem in ResourceProblems.For(context.Graph, model.Id))
            {
                if (problem.Kind == ResourceProblemKind.Missing && !checkMissing)
                    continue;
                if (problem.Kind == ResourceProblemKind.AbsolutePath && !checkAbsolute)
                    continue;

                findings.Add(new Finding
                {
                    RuleId = problem.Kind == ResourceProblemKind.Missing
                        ? RuleIdsRef.MissingResource
                        : RuleIdsRef.AbsoluteResourcePath,
                    ModelId = model.Id,
                    // The reference as written, and how it was made. Neither moves when the file is
                    // reformatted or the library is checked out somewhere else, so neither does the
                    // fingerprint. The resolved path would: it is this machine's.
                    ElementPath = problem.RawPath,
                    Discriminator = KindOf(problem.ReferenceType),
                    Message = MessageFor(problem),
                    LineNumber = LineOf(model.Definition.ModelicaCode, problem.RawPath),
                });
            }
        }

        return findings;
    }

    private static string MessageFor(ResourceProblem problem) => problem.Kind switch
    {
        ResourceProblemKind.Missing when problem.IsDirectory =>
            $"{DescriptionOf(problem.ReferenceType)} '{problem.RawPath}' does not exist: {problem.ResolvedPath}",
        ResourceProblemKind.Missing =>
            $"{DescriptionOf(problem.ReferenceType)} '{problem.RawPath}' refers to a file that does not exist: {problem.ResolvedPath}",
        _ =>
            $"{DescriptionOf(problem.ReferenceType)} '{problem.RawPath}' is an absolute path, which exists only on the machine it was written on; use a modelica:// URI",
    };

    /// <summary>
    /// The reference kind as it goes into the fingerprint. Written out rather than taken from the
    /// enum: an obfuscated build renames enum members, and a fingerprint that moved with them would
    /// stop matching its baseline (CODING_GUIDELINES, Names That Must Survive Obfuscation).
    /// </summary>
    internal static string KindOf(ResourceReferenceType type) => type switch
    {
        ResourceReferenceType.LoadResource => "loadResource",
        ResourceReferenceType.LoadSelector => "loadSelector",
        ResourceReferenceType.LoadResourceParameter => "loadResourceParameter",
        ResourceReferenceType.UriReference => "uri",
        ResourceReferenceType.ExternalInclude => "include",
        ResourceReferenceType.ExternalLibrary => "library",
        ResourceReferenceType.ExternalIncludeDirectory => "includeDirectory",
        ResourceReferenceType.ExternalLibraryDirectory => "libraryDirectory",
        ResourceReferenceType.ExternalSourceDirectory => "sourceDirectory",
        _ => "other",
    };

    private static string DescriptionOf(ResourceReferenceType type) => type switch
    {
        ResourceReferenceType.LoadResource => "loadResource",
        ResourceReferenceType.LoadSelector => "The loadSelector parameter's file",
        ResourceReferenceType.LoadResourceParameter => "The parameter's file",
        ResourceReferenceType.UriReference => "The URI",
        ResourceReferenceType.ExternalInclude => "The Include",
        ResourceReferenceType.ExternalLibrary => "The Library",
        ResourceReferenceType.ExternalIncludeDirectory => "The IncludeDirectory",
        ResourceReferenceType.ExternalLibraryDirectory => "The LibraryDirectory",
        ResourceReferenceType.ExternalSourceDirectory => "The SourceDirectory",
        _ => "The reference",
    };

    /// <summary>
    /// The line of the class's own source where the reference first appears, counted from 1 as
    /// every class-relative line is, or 1 - the class as a whole - when it cannot be found there.
    /// The path is taken from the source token as written, escapes included, so it is normally
    /// found exactly.
    /// </summary>
    internal static int LineOf(string? source, string rawPath)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(rawPath))
            return 1;

        var at = source.IndexOf(rawPath, StringComparison.Ordinal);
        if (at < 0)
            return 1;

        var line = 1;
        for (var i = 0; i < at; i++)
            if (source[i] == '\n')
                line++;
        return line;
    }
}
