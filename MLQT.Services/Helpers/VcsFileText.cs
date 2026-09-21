using ModelicaParser.Helpers;

namespace MLQT.Services.Helpers;

/// <summary>
/// Turns bytes a version control system handed back into text.
/// </summary>
/// <remarks>
/// <para><b>Where MLQT answers a question `RevisionControl` deliberately does not.</b> That assembly
/// has no project references — it is the one part of MLQT that knows nothing about Modelica — so it
/// returns what was stored rather than decoding it. Modelica files declare no encoding and the
/// population is mixed: older libraries are single-byte Windows-1252, most files are BOM-less UTF-8.
/// Decoding as UTF-8 regardless is what turned a conflicted library's accented characters into
/// replacement characters (B240).</para>
///
/// <para>The detection is <see cref="ModelicaFileEncoding"/>'s, not a second copy of it: BOM, then
/// strict UTF-8, then Latin-1, which cannot fail. This is read-only — nothing here is written back —
/// so the worst a wrong guess costs is a diff that looks wrong, which is still the whole reason the
/// user was looking at it.</para>
/// </remarks>
public static class VcsFileText
{
    /// <summary>
    /// The text those bytes represent, or null when there were none.
    /// </summary>
    /// <remarks>
    /// Null in, null out: "there is no such version" and "that version is empty" are different
    /// answers, and the conflict dialog shows a pane for one and not for the other.
    /// </remarks>
    public static string? Decode(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        if (bytes.Length == 0)
            return string.Empty;

        var encoding = ModelicaFileEncoding.DetectFromBytes(bytes);
        var text = encoding.GetString(bytes);

        // A UTF-8 BOM decodes to a zero-width no-break space at the start of the string, which is
        // invisible in the viewer and counts as a difference in a diff.
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }
}
