namespace ModelicaParser.ExternalDocs;

/// <summary>
/// Result of reading one generated help HTML file.
/// </summary>
/// <param name="Classes">Classes documented in the file, in document order. The first is always
/// the class the page belongs to — a package — whose own heading carries no icon image.</param>
/// <param name="IconByClass">Class name to the small icon image the generator showed for it in a
/// package-content table. This is the only reliable route to a package's icon, since a package's
/// own heading never carries one, and the only legitimate way to name an icon file at all: the
/// generator deduplicates identical icons behind a mangled name, so one image routinely serves
/// several unrelated classes and the file name cannot be derived from the class name.</param>
public sealed record ParsedHelpFile(
    IReadOnlyList<DocumentedClass> Classes,
    IReadOnlyDictionary<string, string> IconByClass);

/// <summary>
/// Parses the HTML documentation Dymola generates for a Modelica library into structured class
/// records. This is the route by which an encrypted library — one shipping only an unreadable
/// <c>package.moe</c> — can still tell us which classes exist, what they extend and whether they
/// carry an icon.
///
/// <para>The format has been verified stable across thirteen Dymola releases (2021 through 2026x
/// Refresh 1): the same seven section headings, the same eleven CSS class names, and byte-identical
/// markup for a sampled class. Refresh releases change nothing structurally. What does move between
/// releases is icon file naming and deduplication, which is why no icon file name is ever
/// computed here.</para>
///
/// <para>All scanning is tag-oriented rather than line-oriented — see <see cref="HelpHtml"/> for
/// why that is a hard requirement and not a stylistic preference.</para>
/// </summary>
public static class DymolaHelpParser
{
    private const string GeneratorMarker = "name=\"HTML-Generator\" content=\"Dymola\"";
    private const string DescriptionClass = "class=\"ModelicaDescription\"";
    private const string BaseClassClass = "class=\"ModelicaBaseClass\"";
    private const string ExtendsPrefix = "Extends from";

    private const string TablePackageContent = "class=\"ModelicaTablePackageContent\"";
    private const string TableParameters = "class=\"ModelicaTableParameters\"";
    private const string TableConnectors = "class=\"ModelicaTableConnectors\"";
    private const string TableInputs = "class=\"ModelicaTableInputs\"";
    private const string TableOutputs = "class=\"ModelicaTableOutputs\"";
    private const string TableContents = "class=\"ModelicaTableContents\"";

    /// <summary>
    /// Modelica's predefined types. The generator prints these as base classes verbatim
    /// (<c>Modelica.Blocks.Interfaces.RealInput</c> documents as "Extends from Real."), but they
    /// are not classes we can resolve, and synthesizing <c>extends Real;</c> onto anything other
    /// than a short class definition does not parse. They are dropped from the extends list.
    /// </summary>
    private static readonly HashSet<string> PredefinedTypes = new(StringComparer.Ordinal)
    {
        "Real", "Integer", "Boolean", "String", "enumeration", "Clock", "ExternalObject"
    };

    /// <summary>
    /// Whether the content looks like Dymola-generated class documentation. Checked before
    /// parsing so an unrecognised generator degrades to "we cannot read this library" rather
    /// than to a confident wrong answer built from markers that happen to collide.
    /// </summary>
    public static bool IsDymolaGenerated(string html) =>
        html.Contains(GeneratorMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses one help HTML file. Returns an empty result when the file is not Dymola-generated
    /// class documentation.
    /// </summary>
    public static ParsedHelpFile ParseFile(string html)
    {
        if (string.IsNullOrEmpty(html) || !IsDymolaGenerated(html))
            return new ParsedHelpFile([], new Dictionary<string, string>(StringComparer.Ordinal));

        var sectionStarts = FindSectionStarts(html);
        var classes = new List<DocumentedClass>(sectionStarts.Count);
        var iconByClass = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < sectionStarts.Count; i++)
        {
            var start = sectionStarts[i];
            var limit = i + 1 < sectionStarts.Count ? sectionStarts[i + 1] : html.Length;

            // The first heading in a file is the page's own class, always a package. The
            // generator never draws an icon on it, so "no image here" carries no information —
            // its icon is resolved later from the parent's content table.
            var documented = ParseSection(html, start, limit, isPageOwner: i == 0, iconByClass);
            if (documented is not null)
                classes.Add(documented);
        }

        return new ParsedHelpFile(classes, iconByClass);
    }

    /// <summary>
    /// Positions of the headings that introduce a class.
    ///
    /// <para>Not every <c>&lt;h2&gt;</c> is one. A class's own <c>Documentation(info=…)</c> is
    /// author-written HTML that the generator emits verbatim, and vendors do put headings in it —
    /// ClaRaPlus opens a package's documentation with an <c>&lt;h2&gt;</c> of its own. Treating
    /// those as class boundaries cuts a section short, and everything after the intruding heading
    /// (in that case the package's entire content table) is attributed to a section that names no
    /// class and is thrown away. A class heading is distinguished by carrying the anchor that
    /// states the class's name; prose headings never do.</para>
    /// </summary>
    private static List<int> FindSectionStarts(string html)
    {
        var starts = new List<int>();
        var index = HelpHtml.FindTag(html, 0, "h2");
        while (index >= 0)
        {
            var headingEnd = html.IndexOf("</h2", index, StringComparison.OrdinalIgnoreCase);
            if (headingEnd < 0)
                headingEnd = html.Length;

            if (!string.IsNullOrEmpty(ReadAnchorName(html, index, headingEnd)))
                starts.Add(index);

            index = HelpHtml.FindTag(html, HelpHtml.EndOfTag(html, index), "h2");
        }

        return starts;
    }

    private static DocumentedClass? ParseSection(
        string html, int start, int limit, bool isPageOwner, Dictionary<string, string> iconByClass)
    {
        var headingEnd = html.IndexOf("</h2", start, StringComparison.OrdinalIgnoreCase);
        if (headingEnd < 0 || headingEnd > limit)
            headingEnd = limit;

        var fullName = ReadAnchorName(html, start, headingEnd);
        if (string.IsNullOrEmpty(fullName))
            return null;

        var (hasIcon, iconPath) = ReadHeadingIcon(html, start, headingEnd, fullName, isPageOwner);
        if (iconPath is not null)
            iconByClass[fullName] = iconPath;

        var body = headingEnd;
        var description = ReadSpanText(html, body, limit, DescriptionClass);
        var extends = ReadBaseClasses(html, body, limit);

        var children = ReadPackageContent(html, body, limit, iconByClass);
        var parameters = ReadMemberTable(html, body, limit, TableParameters);
        var connectors = ReadMemberTable(html, body, limit, TableConnectors);
        var inputs = ReadMemberTable(html, body, limit, TableInputs);
        var outputs = ReadMemberTable(html, body, limit, TableOutputs);
        var contents = ReadMemberTable(html, body, limit, TableContents);

        var kind = InferKind(children.Count > 0, inputs.Count > 0 || outputs.Count > 0, connectors.Count > 0);

        return new DocumentedClass(
            fullName, description, extends, hasIcon, iconPath, kind,
            children, parameters, connectors, inputs, outputs, contents);
    }

    /// <summary>
    /// The class name from the heading's <c>&lt;a name="…"&gt;</c> anchor — the generator's own
    /// statement of the fully-qualified name, preferred over reassembling it from the visible
    /// breadcrumb text.
    /// </summary>
    private static string? ReadAnchorName(string html, int start, int headingEnd)
    {
        var index = HelpHtml.FindTag(html, start, "a");
        while (index >= 0 && index < headingEnd)
        {
            var name = HelpHtml.ReadAttribute(HelpHtml.TagTextAt(html, index), "name");
            if (!string.IsNullOrEmpty(name))
                return name;

            index = HelpHtml.FindTag(html, HelpHtml.EndOfTag(html, index), "a");
        }

        return null;
    }

    /// <summary>
    /// Whether the heading carries the class's rendered icon, and the image it names.
    ///
    /// <para>A heading image means the class has an icon, its own or inherited. Its absence means
    /// the class has none — verified against source: <c>Modelica.Mechanics.MultiBody.Interfaces.Frame</c>,
    /// documented as "(no icon)" and carrying no <c>Icon</c> annotation, is rendered without one.
    /// The exception is the page-owning package, whose heading never carries an image at all, so
    /// for it the answer is "not known" and must be resolved elsewhere.</para>
    /// </summary>
    private static (bool? HasIcon, string? IconPath) ReadHeadingIcon(
        string html, int start, int headingEnd, string fullName, bool isPageOwner)
    {
        var index = HelpHtml.FindTag(html, start, "img");
        while (index >= 0 && index < headingEnd)
        {
            var tag = HelpHtml.TagTextAt(html, index);
            // Match on alt rather than position: alt states which class the image is for, while
            // the file name is a deduplicated, mangled artefact that says nothing reliable.
            if (HelpHtml.ReadAttribute(tag, "alt") == fullName)
                return (true, HelpHtml.ReadAttribute(tag, "src"));

            index = HelpHtml.FindTag(html, HelpHtml.EndOfTag(html, index), "img");
        }

        return isPageOwner ? (null, null) : (false, null);
    }

    private static string? ReadSpanText(string html, int start, int limit, string spanClass)
    {
        var inner = FindClassedElement(html, start, limit, "span", spanClass);
        if (inner is null)
            return null;

        var text = HelpHtml.StripTags(inner);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Base classes from the "Extends from …" span, fully qualified.
    ///
    /// <para>Returns null when the span is absent — the class's inheritance is then <b>not known</b>,
    /// which is a different answer from "extends nothing" and must stay distinguishable.</para>
    /// </summary>
    private static IReadOnlyList<string>? ReadBaseClasses(string html, int start, int limit)
    {
        var inner = FindClassedElement(html, start, limit, "span", BaseClassClass);
        if (inner is null)
            return null;

        var text = inner.TrimStart();
        var prefix = text.IndexOf(ExtendsPrefix, StringComparison.OrdinalIgnoreCase);
        if (prefix < 0)
            return null;

        var list = text[(prefix + ExtendsPrefix.Length)..];
        var names = new List<string>();
        foreach (var entry in HelpHtml.SplitTopLevelCommas(list))
        {
            var name = ReadBaseClassEntry(entry);
            if (name is not null && !PredefinedTypes.Contains(name) && !names.Contains(name, StringComparer.Ordinal))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// One base-class entry. A link's href fragment is preferred over its visible text: it is the
    /// generator's own resolved name, and it stays correct for cross-library links whose href is a
    /// relative path into another library's help directory.
    /// </summary>
    private static string? ReadBaseClassEntry(string entry)
    {
        var anchor = HelpHtml.FindTag(entry, 0, "a");
        if (anchor >= 0)
        {
            var href = HelpHtml.ReadAttribute(HelpHtml.TagTextAt(entry, anchor), "href");
            var hash = href?.LastIndexOf('#') ?? -1;
            if (href is not null && hash >= 0 && hash + 1 < href.Length)
                return href[(hash + 1)..];
        }

        return HelpHtml.LeadingQualifiedName(HelpHtml.StripTags(entry));
    }

    /// <summary>
    /// Child class names from the package-content table, harvesting each row's icon image on the
    /// way — that map is what lets a package's own icon be determined later.
    /// </summary>
    private static IReadOnlyList<string> ReadPackageContent(
        string html, int start, int limit, Dictionary<string, string> iconByClass)
    {
        var children = new List<string>();
        var table = FindClassedElement(html, start, limit, "table", TablePackageContent);
        if (table is null)
            return children;

        foreach (var row in ReadRows(table))
        {
            var cells = ReadCells(row);
            if (cells.Count == 0)
                continue;

            var name = ReadContentRowName(cells[0], iconByClass);
            if (name is not null)
                children.Add(name);
        }

        return children;
    }

    private static string? ReadContentRowName(string cell, Dictionary<string, string> iconByClass)
    {
        string? name = null;

        var img = HelpHtml.FindTag(cell, 0, "img");
        if (img >= 0)
        {
            var tag = HelpHtml.TagTextAt(cell, img);
            name = HelpHtml.ReadAttribute(tag, "alt");
            var src = HelpHtml.ReadAttribute(tag, "src");
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(src))
                iconByClass[name] = src;
        }

        if (!string.IsNullOrEmpty(name))
            return name;

        // A row without an image still names its class through the link's fragment.
        var anchor = HelpHtml.FindTag(cell, 0, "a");
        if (anchor < 0)
            return null;

        var href = HelpHtml.ReadAttribute(HelpHtml.TagTextAt(cell, anchor), "href");
        var hash = href?.LastIndexOf('#') ?? -1;
        return href is not null && hash >= 0 && hash + 1 < href.Length ? href[(hash + 1)..] : null;
    }

    private static IReadOnlyList<DocumentedMember> ReadMemberTable(
        string html, int start, int limit, string tableClass)
    {
        var members = new List<DocumentedMember>();
        var table = FindClassedElement(html, start, limit, "table", tableClass);
        if (table is null)
            return members;

        foreach (var row in ReadRows(table))
        {
            // Group and tab headings span both columns and name no member.
            if (row.Contains("colspan", StringComparison.OrdinalIgnoreCase))
                continue;

            var cells = ReadCells(row);
            if (cells.Count == 0)
                continue;

            var name = HelpHtml.StripTags(cells[0]);
            if (string.IsNullOrEmpty(name))
                continue;

            var description = cells.Count > 1 ? HelpHtml.StripTags(cells[1]) : string.Empty;
            var (text, unit) = SplitTrailingUnit(description);
            members.Add(new DocumentedMember(name, string.IsNullOrEmpty(text) ? null : text, unit));
        }

        return members;
    }

    /// <summary>
    /// Separates the unit the generator appends in square brackets ("Cut-force […] [N]") from the
    /// description proper.
    /// </summary>
    private static (string Text, string? Unit) SplitTrailingUnit(string description)
    {
        var trimmed = description.TrimEnd();
        if (!trimmed.EndsWith(']'))
            return (trimmed, null);

        var open = trimmed.LastIndexOf('[');
        if (open < 0)
            return (trimmed, null);

        var unit = trimmed[(open + 1)..^1].Trim();
        if (unit.Length == 0 || unit.Contains('['))
            return (trimmed, null);

        return (trimmed[..open].TrimEnd(), unit);
    }

    /// <summary>
    /// Inner HTML of the first element of <paramref name="tagName"/> whose opening tag carries
    /// <paramref name="classAttribute"/>, searching only within [start, limit).
    /// </summary>
    private static string? FindClassedElement(
        string html, int start, int limit, string tagName, string classAttribute)
    {
        var marker = html.IndexOf(classAttribute, start, StringComparison.Ordinal);
        if (marker < 0 || marker >= limit)
            return null;

        // Walk back to the '<' that opens the tag holding this class attribute.
        var open = html.LastIndexOf('<', marker);
        if (open < 0 || open < start)
            return null;

        var contentStart = HelpHtml.EndOfTag(html, open);
        var close = html.IndexOf("</" + tagName, contentStart, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
            return null;

        // A table may legitimately run past the section limit only if the file is malformed;
        // clamp rather than reading into the next class's content.
        return close > limit ? html[contentStart..limit] : html[contentStart..close];
    }

    private static IEnumerable<string> ReadRows(string table)
    {
        var index = HelpHtml.FindTag(table, 0, "tr");
        while (index >= 0)
        {
            var contentStart = HelpHtml.EndOfTag(table, index);
            var next = HelpHtml.FindTag(table, contentStart, "tr");
            var close = table.IndexOf("</tr", contentStart, StringComparison.OrdinalIgnoreCase);
            var end = close >= 0 && (next < 0 || close < next) ? close : next < 0 ? table.Length : next;

            // Include the opening tag so colspan/class on <tr> stays visible to the caller.
            yield return table[index..end];
            index = next;
        }
    }

    private static List<string> ReadCells(string row)
    {
        var cells = new List<string>();
        var index = HelpHtml.FindTag(row, 0, "td");
        while (index >= 0)
        {
            var contentStart = HelpHtml.EndOfTag(row, index);
            var close = row.IndexOf("</td", contentStart, StringComparison.OrdinalIgnoreCase);
            var next = HelpHtml.FindTag(row, contentStart, "td");
            var end = close >= 0 && (next < 0 || close < next) ? close : next < 0 ? row.Length : next;

            cells.Add(row[contentStart..end]);
            index = next;
        }

        return cells;
    }

    /// <summary>
    /// Best guess at the class restriction, which the generator never states outright. Only the
    /// unambiguous cases are claimed; everything else stays <see cref="DocumentedClass.KindUnknown"/>
    /// so no consumer mistakes a guess for a fact.
    /// </summary>
    private static string InferKind(bool hasChildren, bool hasInputsOrOutputs, bool hasConnectors)
    {
        if (hasChildren)
            return DocumentedClass.KindPackage;
        if (hasInputsOrOutputs)
            return DocumentedClass.KindFunction;
        if (hasConnectors)
            return DocumentedClass.KindModel;

        return DocumentedClass.KindUnknown;
    }
}
