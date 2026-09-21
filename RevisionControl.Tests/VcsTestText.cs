using System.Text;
using RevisionControl.Interfaces;

namespace RevisionControl.Tests;

/// <summary>
/// Reads a revision's content as text, for tests whose fixtures are ASCII.
/// </summary>
/// <remarks>
/// <para><c>GetFileBytesAtRevision</c> returns what was stored, because this assembly knows nothing
/// about Modelica and cannot reach the encoding funnel that decides what those bytes mean (B264).
/// Production decodes through <c>MLQT.Services.Helpers.VcsFileText</c>; these tests assert on
/// content they wrote themselves, in ASCII, where UTF-8 is exact.</para>
///
/// <para><b>Deliberately not a convenience on the production type.</b> A string-returning sibling
/// there would decode as UTF-8 for every caller that reached for the easier one, which is the defect
/// this replaced — and the two would drift the way every other pair in this repository has.</para>
/// </remarks>
internal static class VcsTestText
{
    public static string? GetFileContentAtRevision(
        this IRevisionControlSystem vcs, string repositoryPath, string filePath, string? revision = null)
    {
        var bytes = vcs.GetFileBytesAtRevision(repositoryPath, filePath, revision);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }
}
