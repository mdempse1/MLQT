namespace RevisionControl;

/// <summary>
/// The canonical form of a path relative to a working copy root: separators are always <c>/</c>.
/// </summary>
/// <remarks>
/// <para>Git reports <c>Lib/Thing.mo</c> and SVN on Windows reports <c>Lib\Thing.mo</c>, so anything
/// that keys, compares or joins these paths has to agree on one of the two. <b>It has to be
/// <c>/</c></b>, and that is not a coin toss: git itself only accepts <c>/</c>, and <c>/</c> is a
/// valid separator on Windows as well, so a canonical path can be handed straight to
/// <see cref="System.IO.Path.Combine(string, string)"/> and <c>File.Exists</c> on either platform.
/// The backslash form can only be used on one of them.</para>
///
/// <para><b>That is not a hypothetical.</b> <c>ChangeReview</c> canonicalised on <c>\</c> and used the
/// result both as a tree key and as a path to read the working copy with. On Linux
/// <c>Lib\Thing.mo</c> is a single file name that happens to contain backslashes, so
/// <c>File.Exists</c> answered false and the commit dialog's diff showed the HEAD side and nothing at
/// all for the modified file — the git layer normalises for itself, so only the half that touched the
/// filesystem directly was wrong. Found by comparing the Linux build against the Windows one by hand
/// (phase 7b-6, B137).</para>
///
/// <para><b>The one case this is wrong about, stated rather than hidden:</b> a backslash is a legal
/// character in a POSIX file name, so a file genuinely called <c>a\b.mo</c> on Linux would be split
/// into two segments here. It is accepted: the conversion exists for SVN on Windows, git and SVN both
/// report <c>/</c> on Linux, and a Modelica file with a backslash in its name has never been seen.
/// Handling it properly would mean asking the platform, which would make git and SVN paths behave
/// differently on the two operating systems for no benefit anyone can observe.</para>
/// </remarks>
public static class VcsRelativePath
{
    /// <summary>This path with <c>/</c> separators, whichever the VCS reported.</summary>
    public static string Canonical(string path) => path.Replace('\\', '/');
}
