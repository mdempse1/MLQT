namespace MLQT.Services;

/// <summary>
/// Every key MLQT stores through <see cref="Interfaces.ISettingsService"/>.
/// </summary>
/// <remarks>
/// <para>Written down because <b>MAUI's <c>Preferences</c> cannot be enumerated</b>. It answers for a
/// key you name and offers no way to ask what it holds, so the one-time seed of the JSON store in
/// 7b-3 can only move keys it knows about — and a key nobody wrote down is a setting that silently
/// does not survive the migration.</para>
///
/// <para>Held to the source by <c>MlqtSettingsKeysTests</c>, which reads the literals back out of the
/// codebase. That is the guard that matters: adding a seventh key is easy, remembering this file is
/// not, and the symptom would be one setting quietly reverting to its default for every existing
/// user on the day they upgrade.</para>
/// </remarks>
public static class MlqtSettingsKeys
{
    /// <summary>The UI theme and the custom palette.</summary>
    public const string Ui = "UI";

    /// <summary>The syntax highlighting theme.</summary>
    public const string SyntaxHighlighting = "SyntaxHighlighting";

    /// <summary>The project's repositories, their paths and their per-repository settings.</summary>
    public const string Repositories = "Repositories";

    /// <summary>Read-only libraries loaded to resolve references.</summary>
    public const string ReferenceLibraries = "ReferenceLibraries";

    /// <summary>Dymola's location and connection settings.</summary>
    public const string Dymola = "Dymola";

    /// <summary>OpenModelica's location and connection settings.</summary>
    public const string OpenModelica = "OpenModelica";

    /// <summary>All of them, which is what the seed walks.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Ui, SyntaxHighlighting, Repositories, ReferenceLibraries, Dymola, OpenModelica];
}
