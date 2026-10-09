namespace ModelicaParser.Comparison;

/// <summary>
/// Which annotations a translator acts on, and which only a person or a drawing canvas ever reads.
/// </summary>
/// <remarks>
/// <para><b>Why this is a list of the harmless ones.</b> An annotation MLQT has never heard of is
/// treated as affecting simulation. The two mistakes are not symmetric: calling a graphical edit a
/// simulation change costs the user a second look at a diagram, while calling a simulation change
/// graphical hides it from someone who was relying on the marker to tell them what to read. So the
/// list below is the set MLQT is prepared to vouch for, and everything else — including every vendor
/// annotation not named here — is significant by default.</para>
///
/// <para><b>What is deliberately <i>not</i> here</b>, because a translator acts on it:
/// <c>Evaluate</c>, <c>HideResult</c>, <c>Inline</c>, <c>LateInline</c>, <c>NoInline</c>,
/// <c>InlineAfterIndexReduction</c>, <c>GenerateEvents</c>, <c>smoothOrder</c>, <c>derivative</c>,
/// <c>inverse</c>, <c>Protection</c>, <c>experiment</c>, <c>uses</c>, <c>version</c>,
/// <c>conversion</c>, and the external-function annotations <c>Include</c>, <c>Library</c>,
/// <c>IncludeDirectory</c>, <c>LibraryDirectory</c> and <c>SourceDirectory</c>. None of those needs
/// registering anywhere — not being in the list below is what makes them significant.</para>
///
/// <para><b>Judgement calls worth knowing about.</b> <c>missingInnerMessage</c> and
/// <c>unassignedMessage</c> are here: they change the wording of a diagnostic and nothing else.
/// <c>versionBuild</c>, <c>versionDate</c> and <c>dateModified</c> are here because tools rewrite
/// them on save and they decide nothing; <c>version</c> and <c>uses</c> are not, because they decide
/// which library a reference resolves to. <c>absoluteValue</c> is <i>not</i> here — it is arguably
/// display-only, and a borderline case belongs on the side that gets looked at.</para>
/// </remarks>
public static class SimulationAnnotations
{
    /// <summary>
    /// Annotation names a translator ignores, each with what it is about. Ordinal and case
    /// sensitive, because Modelica is.
    /// </summary>
    /// <remarks>
    /// <b>Every display-only name has exactly one category.</b> A class's dropped annotations are
    /// kept per category on its <see cref="ClassSignature"/>, so a name in no category would vanish
    /// from every comparison — and a change to it would read as no change at all.
    /// </remarks>
    private static readonly Dictionary<string, CosmeticCategory> Cosmetic = new(StringComparer.Ordinal)
    {
        // Drawing: the class's own two canvases, a component's position on its parent's, and the
        // primitives inside them. The primitives cannot appear at the top level of an annotation,
        // but naming them costs nothing and says what the set is.
        ["Icon"] = CosmeticCategory.Graphics,
        ["Diagram"] = CosmeticCategory.Graphics,
        ["IconMap"] = CosmeticCategory.Graphics,
        ["DiagramMap"] = CosmeticCategory.Graphics,
        ["Placement"] = CosmeticCategory.Graphics,
        ["coordinateSystem"] = CosmeticCategory.Graphics,
        ["graphics"] = CosmeticCategory.Graphics,
        ["Line"] = CosmeticCategory.Graphics,
        ["Text"] = CosmeticCategory.Graphics,
        ["Rectangle"] = CosmeticCategory.Graphics,
        ["Polygon"] = CosmeticCategory.Graphics,
        ["Ellipse"] = CosmeticCategory.Graphics,
        ["Bitmap"] = CosmeticCategory.Graphics,

        // Documentation, and the wording of a diagnostic when one is produced at all.
        ["Documentation"] = CosmeticCategory.Documentation,
        ["DocumentationClass"] = CosmeticCategory.Documentation,
        ["revisions"] = CosmeticCategory.Documentation,
        ["obsolete"] = CosmeticCategory.Documentation,
        ["missingInnerMessage"] = CosmeticCategory.Documentation,
        ["unassignedMessage"] = CosmeticCategory.Documentation,

        // What a tool's dialogs, menus and browsers offer. None of it survives translation. The
        // vendor annotations here are the ones MLQT is prepared to vouch for as GUI-only; every
        // other vendor annotation, __Dymola_ or otherwise, is significant by default.
        ["Dialog"] = CosmeticCategory.Dialog,
        ["choices"] = CosmeticCategory.Dialog,
        ["choicesAllMatching"] = CosmeticCategory.Dialog,
        ["preferredView"] = CosmeticCategory.Dialog,
        ["defaultComponentName"] = CosmeticCategory.Dialog,
        ["defaultComponentPrefixes"] = CosmeticCategory.Dialog,
        ["__Dymola_Commands"] = CosmeticCategory.Dialog,
        ["__Dymola_Images"] = CosmeticCategory.Dialog,
        ["__Dymola_selections"] = CosmeticCategory.Dialog,
        ["__Dymola_choicesAllMatching"] = CosmeticCategory.Dialog,
        ["__Dymola_checkBox"] = CosmeticCategory.Dialog,
        ["__Dymola_colorSelector"] = CosmeticCategory.Dialog,
        ["__Dymola_editText"] = CosmeticCategory.Dialog,
        ["__Dymola_editButton"] = CosmeticCategory.Dialog,

        // Library bookkeeping a tool rewrites on save (note the absence of "version" and "uses"),
        // and MLQT's own directives, which steer MLQT's checking and formatting and are not Modelica.
        ["versionBuild"] = CosmeticCategory.Tooling,
        ["versionDate"] = CosmeticCategory.Tooling,
        ["dateModified"] = CosmeticCategory.Tooling,
        ["__MLQT"] = CosmeticCategory.Tooling,
    };

    /// <summary>
    /// Whether a change inside this annotation can change what is simulated.
    /// </summary>
    /// <param name="name">The annotation element's name, as written.</param>
    /// <returns>False only for a name this class vouches for as display-only.</returns>
    public static bool AffectsSimulation(string name) => !Cosmetic.ContainsKey(name);

    /// <summary>
    /// What a display-only annotation is about, or null for one a translator acts on — including
    /// every name this class has never heard of.
    /// </summary>
    /// <param name="name">The annotation element's name, as written.</param>
    public static CosmeticCategory? CategoryOf(string name) =>
        Cosmetic.TryGetValue(name, out var category) ? category : null;

    /// <summary>
    /// The names treated as display-only, for tests and for documentation that has to list them.
    /// </summary>
    public static IReadOnlyCollection<string> CosmeticNames => Cosmetic.Keys;
}
