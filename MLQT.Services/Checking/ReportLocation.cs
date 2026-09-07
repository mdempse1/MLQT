using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// Where a report sends a reader: the file, spelled the way a report should spell it, and the line
/// in that file.
///
/// <para><b>Why this exists.</b> A finding carries a class-relative line and a model id; every
/// surface that reports one has to turn those into "this file, this line", and each of them used to
/// do it itself. The CLI's <c>CheckReport</c> had <c>RelativeFileFor</c> and <c>LineFor</c>; the Code
/// Review page's export had <c>ReportPathOf</c> and <c>FileLineOf</c>, character for character the
/// same rules. They were meant to agree — the GUI export's doc comment said so, and recorded that
/// they once did not, because it had been writing the absolute path while the CLI wrote a relative
/// one. A promise that two copies match is the shape this codebase has had to fix repeatedly
/// (backlog B105). One implementation is not a promise; it is the same answer.</para>
///
/// <para>Deliberately takes the resolved <see cref="ClassLocation"/> and root rather than a
/// dictionary: the CLI has one library path for the whole run, the GUI has a root per model, and the
/// rule does not care which.</para>
/// </summary>
public static class ReportLocation
{
    /// <summary>
    /// The line in the file for a finding's class-relative line, or the finding's own number when
    /// the class has no known location — a snippet, or a class whose file is unknown. Never less
    /// than 1: a report has to open the file somewhere valid.
    /// </summary>
    public static int LineIn(ClassLocation? location, int lineInClass) =>
        location is not null ? location.FileLine(lineInClass) : Math.Max(1, lineInClass);

    /// <summary>
    /// The file as a report shows it: relative to the library it belongs to, with forward slashes.
    ///
    /// <para>Absolute paths are an accident of how the command was typed, and noise in a report that
    /// already names the library it checked; a path relative to the library is the one a reader can
    /// act on. Returns null when there is no file to name.</para>
    ///
    /// <para>A file <em>outside</em> the library — a dependency checked alongside it — comes back as
    /// a relative path that walks up, <c>../../other/Dep.mo</c>, which is still resolvable against
    /// the library the report names. Only paths that cannot be related at all fall back to the
    /// absolute one, which on Windows means a different drive. Both copies of this rule carried a
    /// comment claiming the outside-the-library case fell back to absolute too; it never did, and
    /// writing <c>ReportLocationTests</c> is what showed it. The behaviour is left as it was and the
    /// description corrected to match, because the paths in question are resolvable and changing
    /// what a report prints is not a refactor.</para>
    /// </summary>
    public static string? RelativeFile(string? filePath, string? libraryRoot)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        if (string.IsNullOrEmpty(libraryRoot))
            return filePath;

        try
        {
            var relative = Path.GetRelativePath(libraryRoot, filePath);
            return Path.IsPathRooted(relative) ? filePath : relative.Replace('\\', '/');
        }
        catch
        {
            return filePath;
        }
    }

    /// <summary>
    /// <see cref="RelativeFile(string?,string?)"/> for a finding whose class location has already
    /// been looked up.
    /// </summary>
    public static string? RelativeFile(ClassLocation? location, string? libraryRoot) =>
        RelativeFile(location?.FilePath, libraryRoot);
}
