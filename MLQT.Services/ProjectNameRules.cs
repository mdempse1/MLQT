using MLQT.Services.DataTypes;

namespace MLQT.Services;

/// <summary>
/// What makes a project name usable. One implementation, because two screens ask.
///
/// <para>A project can be named from the startup selector and from Settings → Manage Repositories,
/// and the second of those can also rename one. Three places, one rule — written once here rather
/// than three times, which is the shape that produced B106, B109 and B110: sibling dialogs each
/// keeping a private copy of a decision, every copy wrong in its own way.</para>
///
/// <para><b>Names are compared case-insensitively, after trimming.</b> "Work" and "work " are the
/// same project as far as a person reading a list is concerned, and a list showing both is a list
/// nobody can use. The id remains what actually identifies a project; this is about what the user
/// can tell apart.</para>
///
/// <para>The message is returned rather than a bare bool so that the screen showing it and the
/// service refusing it say the same thing.</para>
/// </summary>
public static class ProjectNameRules
{
    /// <summary>
    /// The form of a name that is compared and stored: trimmed of surrounding whitespace.
    /// </summary>
    public static string Normalise(string? name) => (name ?? string.Empty).Trim();

    /// <summary>
    /// <c>null</c> when <paramref name="name"/> may be used, otherwise why it may not — phrased for
    /// the user.
    /// </summary>
    /// <param name="name">The name as typed, before trimming.</param>
    /// <param name="existing">The projects it has to be distinct from.</param>
    /// <param name="ignoringProjectId">
    /// A project to leave out of the comparison — the one being renamed, so that confirming a rename
    /// without having changed the name is not a clash with itself.
    /// </param>
    public static string? Validate(
        string? name,
        IEnumerable<ProjectProfile> existing,
        string? ignoringProjectId = null)
    {
        var candidate = Normalise(name);
        if (candidate.Length == 0)
            return "Enter a name for the project.";

        var clash = existing.FirstOrDefault(p =>
            !string.Equals(p.Id, ignoringProjectId, StringComparison.Ordinal)
            && string.Equals(Normalise(p.Name), candidate, StringComparison.OrdinalIgnoreCase));

        return clash is null
            ? null
            : $"There is already a project called '{Normalise(clash.Name)}'. Project names must be different.";
    }

    /// <summary>
    /// Whether the name may be used. <see cref="Validate"/> where the reason is wanted.
    /// </summary>
    public static bool IsAvailable(
        string? name,
        IEnumerable<ProjectProfile> existing,
        string? ignoringProjectId = null)
        => Validate(name, existing, ignoringProjectId) is null;
}
