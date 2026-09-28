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
    /// Annotation names a translator ignores. Ordinal and case sensitive, because Modelica is.
    /// </summary>
    private static readonly HashSet<string> Cosmetic = new(StringComparer.Ordinal)
    {
        // Drawing: the class's own two canvases, a component's position on its parent's, and the
        // primitives inside them. The primitives cannot appear at the top level of an annotation,
        // but naming them costs nothing and says what the set is.
        "Icon",
        "Diagram",
        "IconMap",
        "DiagramMap",
        "Placement",
        "coordinateSystem",
        "graphics",
        "Line",
        "Text",
        "Rectangle",
        "Polygon",
        "Ellipse",
        "Bitmap",

        // Documentation.
        "Documentation",
        "DocumentationClass",
        "revisions",
        "obsolete",

        // What a tool's dialogs and browsers offer. None of it survives translation.
        "Dialog",
        "choices",
        "choicesAllMatching",
        "preferredView",
        "defaultComponentName",
        "defaultComponentPrefixes",

        // Wording of a diagnostic, when one is produced at all.
        "missingInnerMessage",
        "unassignedMessage",

        // Library bookkeeping a tool rewrites on save. Note the absence of "version" and "uses".
        "versionBuild",
        "versionDate",
        "dateModified",

        // Vendor annotations MLQT is prepared to vouch for as GUI-only. Every other vendor
        // annotation, __Dymola_ or otherwise, is significant by default.
        "__Dymola_Commands",
        "__Dymola_Images",
        "__Dymola_selections",
        "__Dymola_choicesAllMatching",
        "__Dymola_checkBox",
        "__Dymola_colorSelector",
        "__Dymola_editText",
        "__Dymola_editButton",

        // MLQT's own directives steer MLQT's checking and formatting. They are not Modelica.
        "__MLQT",
    };

    /// <summary>
    /// Whether a change inside this annotation can change what is simulated.
    /// </summary>
    /// <param name="name">The annotation element's name, as written.</param>
    /// <returns>False only for a name this class vouches for as display-only.</returns>
    public static bool AffectsSimulation(string name) => !Cosmetic.Contains(name);

    /// <summary>
    /// The names treated as display-only, for tests and for documentation that has to list them.
    /// </summary>
    public static IReadOnlyCollection<string> CosmeticNames => Cosmetic;
}
