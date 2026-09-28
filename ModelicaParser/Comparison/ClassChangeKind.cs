namespace ModelicaParser.Comparison;

/// <summary>
/// What a change to one Modelica class amounts to — the distinction the library browser's marker
/// draws between an edit that can change what is simulated and one that cannot (B191).
/// </summary>
/// <remarks>
/// <para><b>Classes, not text.</b> Every member here is decided by comparing the two parsed classes,
/// so reformatting, a re-wrapped description and a component dragged across a diagram all land in
/// <see cref="Cosmetic"/>, while a changed equation, a changed modification and a changed
/// <c>Evaluate</c> annotation all land in <see cref="AffectsSimulation"/>. Annotations are not
/// ignored wholesale: <see cref="SimulationAnnotations"/> decides which of them matter.</para>
///
/// <para><b>The ordering is deliberate.</b> The values ascend by how much they demand of a reviewer,
/// so rolling a package's descendants up is <c>Max</c> over their kinds. <see cref="Unknown"/> sits
/// at the bottom because it is the absence of an answer, and a package with one unreadable class and
/// one changed equation should still report the equation.</para>
/// </remarks>
public enum ClassChangeKind
{
    /// <summary>
    /// No answer. Either version failed to parse, or there is nothing to compare against — a
    /// repository with no version control, or a file the system has no committed copy of.
    /// Shown as the plain "modified" marker, which is what MLQT did for everything before B191.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The class's own text is identical to the committed version. A class in a modified file that
    /// is not itself what changed — the usual case for a package whose nested class was edited.
    /// </summary>
    Unchanged = 1,

    /// <summary>
    /// The text changed but the meaning did not: layout, comments, descriptions, documentation and
    /// graphical annotations. Nothing here can change a simulation result.
    /// </summary>
    Cosmetic = 2,

    /// <summary>
    /// The class means something different: an equation, a declaration, a modification, or an
    /// annotation that a translator acts on.
    /// </summary>
    AffectsSimulation = 3,

    /// <summary>
    /// The class is not in the committed version at all. Ranked above
    /// <see cref="AffectsSimulation"/> because a new class is new behaviour with nothing to compare
    /// it against, and the reviewer has all of it to read rather than a difference.
    /// </summary>
    Added = 4,
}
