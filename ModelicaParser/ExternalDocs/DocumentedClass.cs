namespace ModelicaParser.ExternalDocs;

/// <summary>
/// One Modelica class recovered from a vendor's generated HTML documentation, for a library
/// whose source is encrypted and therefore unreadable.
///
/// This is deliberately a *partial* picture. Documentation records what a class is and what it
/// inherits, not how it is implemented, so equations, algorithms, annotation graphics and the
/// declared types of members are all absent and unrecoverable.
///
/// <para><b>Unknown is not the same as absent.</b> <see cref="ExtendsClasses"/> and
/// <see cref="HasIcon"/> are nullable so a source that could not determine them says so, rather
/// than claiming the class extends nothing and has no icon. Consumers must treat null as "do not
/// judge this class" — reporting a missing icon because the documentation never mentioned one is
/// exactly the false positive this whole feature exists to remove.</para>
/// </summary>
/// <param name="FullName">Fully-qualified class name, e.g. <c>Battery.BMS.Interfaces.BMS</c>.</param>
/// <param name="Description">The class description string, entity-decoded. Null when the class
/// has none.</param>
/// <param name="ExtendsClasses">Fully-qualified base classes in declaration order. Empty means
/// "known to extend nothing"; <b>null means "not known"</b>. Predefined types (<c>Real</c> and
/// friends) are excluded — they are not classes we can resolve or synthesize an extends for.</param>
/// <param name="HasIcon">Whether the class has an icon, its own or inherited. <b>Null means
/// "not known"</b>.</param>
/// <param name="IconImagePath">File name of the rendered icon image within the help directory,
/// when one was referenced. Never derived from the class name — vendors deduplicate and mangle
/// these — so it is only ever a value read out of the documentation.</param>
/// <param name="Kind">Inferred class restriction. The generator does not emit the keyword, so
/// this is a guess from which tables are present and is <c>"class"</c> whenever the evidence is
/// ambiguous. Never use it where being wrong matters.</param>
/// <param name="Children">Fully-qualified names of the classes this class contains, for a
/// package. Empty for a non-package.</param>
/// <param name="Parameters">Parameters listed for the class.</param>
/// <param name="Connectors">Connectors listed for the class.</param>
/// <param name="Inputs">Function inputs listed for the class.</param>
/// <param name="Outputs">Function outputs listed for the class.</param>
/// <param name="Contents">Members listed for a record or connector, which the generator puts
/// under its own "Contents" heading rather than in the parameter table.</param>
public sealed record DocumentedClass(
    string FullName,
    string? Description,
    IReadOnlyList<string>? ExtendsClasses,
    bool? HasIcon,
    string? IconImagePath,
    string Kind,
    IReadOnlyList<string> Children,
    IReadOnlyList<DocumentedMember> Parameters,
    IReadOnlyList<DocumentedMember> Connectors,
    IReadOnlyList<DocumentedMember> Inputs,
    IReadOnlyList<DocumentedMember> Outputs,
    IReadOnlyList<DocumentedMember> Contents)
{
    /// <summary>Class restriction values <see cref="Kind"/> can take.</summary>
    public const string KindPackage = "package";
    public const string KindFunction = "function";
    public const string KindModel = "model";

    /// <summary>The neutral Modelica restriction, used whenever the evidence is ambiguous.</summary>
    public const string KindUnknown = "class";

    /// <summary>
    /// The name of the enclosing package, or null for a top-level class.
    /// </summary>
    public string? ParentName => LastSeparator(FullName) is var dot && dot > 0 ? FullName[..dot] : null;

    /// <summary>
    /// The class's own name without its package prefix.
    /// </summary>
    public string SimpleName => SimpleNameOf(FullName);

    /// <summary>
    /// The last segment of a qualified Modelica name, respecting quoted identifiers.
    /// </summary>
    public static string SimpleNameOf(string qualifiedName) =>
        LastSeparator(qualifiedName) is var dot && dot >= 0 ? qualifiedName[(dot + 1)..] : qualifiedName;

    /// <summary>
    /// Index of the dot that separates the last segment of a qualified name, or -1 when there is
    /// none. Dots inside a Modelica quoted identifier do not separate anything — libraries really
    /// do name classes this way, most visibly the operator overloads
    /// (<c>Testing.Utilities.Time.DateTime.'&lt;='</c>) — so splitting on the last dot in the
    /// string would cut such a name in the wrong place.
    /// </summary>
    private static int LastSeparator(string qualifiedName)
    {
        var separator = -1;
        var quoted = false;
        for (var i = 0; i < qualifiedName.Length; i++)
        {
            var c = qualifiedName[i];
            if (c == '\\' && quoted)
            {
                i++;   // an escaped character inside a quoted identifier
            }
            else if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (c == '.' && !quoted)
            {
                separator = i;
            }
        }

        return separator;
    }
}
