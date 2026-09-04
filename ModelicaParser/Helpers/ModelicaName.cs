namespace ModelicaParser.Helpers;

/// <summary>
/// Taking a fully-qualified Modelica name apart: <c>Modelica.Blocks.Sources.Ramp</c> is the class
/// <c>Ramp</c> in the package <c>Modelica.Blocks.Sources</c>, from the library <c>Modelica</c>.
///
/// <para>Three lines of string arithmetic, written out at every site that needed them — the graph
/// analyses, the checker, the metrics, the dashboard — each with its own answer for the name that has
/// no dot in it. That is the sort of thing that stays right until one copy is edited: they must agree,
/// because a base package is what the suppression extractor and every rule visitor are told the class
/// sits in, and a wrong one silently changes which annotations are read.</para>
/// </summary>
public static class ModelicaName
{
    /// <summary>
    /// The package a class sits in — everything before the last dot — or empty for a top-level name.
    ///
    /// <para>Empty rather than null on purpose: it is passed straight to
    /// <c>VisitorWithModelNameTracking</c>, whose whole constructor surface defaults it to <c>""</c>
    /// to mean "this class is not inside anything".</para>
    /// </summary>
    public static string EnclosingPackageOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var lastDot = fullName.LastIndexOf('.');
        return lastDot > 0 ? fullName[..lastDot] : string.Empty;
    }

    /// <summary>The class's own name — everything after the last dot, or the whole name if there is none.</summary>
    public static string LeafOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    /// <summary>
    /// The library a class belongs to — the first segment, which is the top-level package Modelica
    /// resolves everything else against.
    /// </summary>
    public static string RootLibraryOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var firstDot = fullName.IndexOf('.');
        return firstDot > 0 ? fullName[..firstDot] : fullName;
    }
}
