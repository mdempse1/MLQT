using ModelicaParser.Comparison;
using MudBlazor;
using RevisionControl;

namespace MLQT.Shared.Helpers;

/// <summary>How a change marker is drawn.</summary>
public enum ChangeMarkerShape
{
    /// <summary>Nothing has changed here or below here.</summary>
    None,

    /// <summary>A lettered chip beside the name — this class itself changed.</summary>
    Chip,

    /// <summary>A coloured dot — something below this changed, but this did not.</summary>
    Dot,
}

/// <summary>
/// What the library browser draws beside a model, and why.
/// </summary>
/// <remarks>
/// <para><b>One decision, asked twice.</b> The browser renders two nearly identical trees — one per
/// repository, one flat across all libraries — and the marker was written out in both. Every rule
/// about it therefore existed twice, which is how a repository-mode-only fix gets made (B200). The
/// rules live here, and both templates ask.</para>
///
/// <para><b>Why the letter changes for a modified file (B191).</b> Every other status is a fact
/// about the <i>file</i>, and every class in that file shares it. "Modified" is the one that is not:
/// a package's file changes because one class in it was edited, and the other three hundred are
/// unaffected. So for a modified file the marker comes from the comparison of the class against its
/// committed self — which is also what lets a graphical edit look different from one that changes
/// what is simulated.</para>
/// </remarks>
/// <param name="Shape">Whether to draw a chip, a dot, or nothing.</param>
/// <param name="Text">The chip's letter. Empty for the other shapes.</param>
/// <param name="Color">The MudBlazor colour for whichever shape is drawn.</param>
/// <param name="Tooltip">
/// What the marker means, in words. The single-letter chips predate B191 and had none, so the only
/// way to learn what "R" meant was to guess.
/// </param>
public readonly record struct ChangeMarker(ChangeMarkerShape Shape, string Text, Color Color, string Tooltip)
{
    /// <summary>Nothing to draw.</summary>
    public static readonly ChangeMarker None = new(ChangeMarkerShape.None, "", MudBlazor.Color.Default, "");

    /// <summary>
    /// The marker for one model.
    /// </summary>
    /// <param name="status">
    /// The VCS status of the file the model is in, or null when that file has no uncommitted change.
    /// </param>
    /// <param name="kind">
    /// What the comparison made of this class. <see cref="ClassChangeKind.Unknown"/> covers both
    /// "could not be compared" and "was never asked" — a repository outside version control, and one
    /// whose committed version could not be read.
    /// </param>
    /// <param name="descendants">
    /// The strongest kind among the classes below this one, or <see cref="ClassChangeKind.Unchanged"/>
    /// when nothing below it changed. Only consulted when the model itself has nothing to show.
    /// </param>
    public static ChangeMarker For(VcsFileStatus? status, ClassChangeKind kind, ClassChangeKind descendants)
    {
        if (status == VcsFileStatus.Modified)
        {
            var own = ForModifiedClass(kind);
            if (own.Shape != ChangeMarkerShape.None)
                return own;
        }
        else if (status is { } fileStatus)
        {
            return new ChangeMarker(
                ChangeMarkerShape.Chip,
                VcsStatusHelper.GetStatusText(fileStatus),
                VcsStatusHelper.GetStatusColor(fileStatus),
                TooltipFor(fileStatus));
        }

        return ForDescendants(descendants);
    }

    /// <summary>
    /// The marker for a class in a modified file — the one status that is about the class rather
    /// than the file it happens to live in.
    /// </summary>
    private static ChangeMarker ForModifiedClass(ClassChangeKind kind) => kind switch
    {
        ClassChangeKind.AffectsSimulation => new ChangeMarker(
            ChangeMarkerShape.Chip, "M", MudBlazor.Color.Warning,
            "Changed in a way that can affect simulation"),

        ClassChangeKind.Cosmetic => new ChangeMarker(
            ChangeMarkerShape.Chip, "G", MudBlazor.Color.Info,
            "Changed, but only its layout, comments, documentation or graphics"),

        ClassChangeKind.Added => new ChangeMarker(
            ChangeMarkerShape.Chip, "A", MudBlazor.Color.Success,
            "A new class, not in the committed version of this file"),

        // The file changed and MLQT could not say how — the committed version would not parse, or
        // there is nothing to compare against. The plain modified marker, which is what every
        // changed class showed before B191.
        ClassChangeKind.Unknown => new ChangeMarker(
            ChangeMarkerShape.Chip, "M", MudBlazor.Color.Warning,
            "Modified — MLQT could not compare it with the committed version"),

        // The file changed but this class did not. Whatever is below it may still have.
        _ => None,
    };

    /// <summary>The dot that says the change is further down.</summary>
    private static ChangeMarker ForDescendants(ClassChangeKind descendants) => descendants switch
    {
        ClassChangeKind.AffectsSimulation or ClassChangeKind.Added => new ChangeMarker(
            ChangeMarkerShape.Dot, "", MudBlazor.Color.Warning,
            "Something below this has changed in a way that can affect simulation"),

        ClassChangeKind.Cosmetic => new ChangeMarker(
            ChangeMarkerShape.Dot, "", MudBlazor.Color.Info,
            "Something below this has changed, but only its layout, comments, documentation or graphics"),

        ClassChangeKind.Unknown => new ChangeMarker(
            ChangeMarkerShape.Dot, "", MudBlazor.Color.Warning,
            "Something below this has uncommitted changes"),

        _ => None,
    };

    private static string TooltipFor(VcsFileStatus status) => status switch
    {
        VcsFileStatus.Added => "Added, not yet committed",
        VcsFileStatus.Deleted => "Deleted, not yet committed",
        VcsFileStatus.Renamed => "Renamed, not yet committed",
        VcsFileStatus.Untracked => "Not under version control",
        VcsFileStatus.Conflicted => "Conflicted — resolve it before committing",
        _ => "Uncommitted changes",
    };
}
