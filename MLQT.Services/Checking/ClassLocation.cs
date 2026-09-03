using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// Where a class sits on disk, and how to turn a finding's class-relative line into a line in that
/// file.
///
/// <para>A rule is handed one class's source and reports a line within it (see
/// <see cref="Finding.LineNumber"/>). That is the right number for a surface showing the class, and
/// the wrong one for a report about files: a class two thousand lines down a <c>package.mo</c> would
/// have its findings annotated at the top of the file, on somebody else's code. Every report that
/// names a file maps through here, so console output, SARIF, JUnit, TeamCity and the JSON all agree
/// on which line they mean.</para>
/// </summary>
public sealed record ClassLocation(string FilePath, int StartLine, bool LinesMapToFile)
{
    /// <summary>
    /// Where the class's file is, always as an absolute path.
    ///
    /// <para>A <c>FileNode</c>'s path follows the library path the run was given, so
    /// <c>mlqt check Lib</c> stores <c>Lib\package.mo</c> and <c>mlqt check C:\…\Lib</c> stores an
    /// absolute one — the same class, two spellings, depending on how somebody typed the command.
    /// Every consumer then has to know that: one comparing paths as dictionary keys silently matched
    /// nothing, and phase 3 listed the mismatch as a risk before either existed. Normalising in the
    /// constructor is the one place that covers every way a location gets made.</para>
    ///
    /// <para>What a <em>report</em> shows is a separate question, and a different answer per format:
    /// relative to the library for the console and JSON, to <c>--sarif-base</c> for SARIF, to the
    /// repository root for a review comment.</para>
    /// </summary>
    public string FilePath { get; init; } =
        string.IsNullOrEmpty(FilePath) ? FilePath : Path.GetFullPath(FilePath);

    /// <summary>
    /// The line in the file for a class-relative line.
    ///
    /// <para>When the stored source is no longer the file's own text — a package whose inline
    /// children were trimmed, a class the formatter re-rendered — the offset would land on a real
    /// line that says something else, so the class declaration is reported instead. Pointing at the
    /// right class is always true; pointing at the wrong line looks precise and is not.</para>
    /// </summary>
    public int FileLine(int lineInClass)
    {
        var start = StartLine > 0 ? StartLine : 1;
        if (!LinesMapToFile)
            return start;

        return start + Math.Max(1, lineInClass) - 1;
    }

    /// <summary>Every class in the graph, keyed by model id.</summary>
    public static Dictionary<string, ClassLocation> ForGraph(DirectedGraph graph)
    {
        var locations = new Dictionary<string, ClassLocation>(StringComparer.Ordinal);

        foreach (var file in graph.FileNodes)
        {
            if (string.IsNullOrEmpty(file.FilePath))
                continue;

            foreach (var model in graph.GetModelsInFile(file.Id))
                locations[model.Id] = new ClassLocation(file.FilePath, model.StartLine, model.SourceMatchesFile);
        }

        return locations;
    }
}
