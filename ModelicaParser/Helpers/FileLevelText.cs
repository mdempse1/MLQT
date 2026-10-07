using ModelicaParser.Visitors;

namespace ModelicaParser.Helpers;

/// <summary>
/// The text of a <c>.mo</c> file that is outside every class in it: a licence or copyright header
/// above the <c>within</c> clause, a comment between the clause and the class, and one after the
/// class's final <c>end X;</c> (B445).
///
/// <para>A class's stored <c>ModelicaCode</c> is its own span and never carries any of this, so every
/// path that rebuilds a file from stored source dropped it — one Format All or one MCP description
/// edit, and the header was gone. It is read <b>once</b>, when the file is loaded
/// (<see cref="Read"/>), carried on the class that heads the file, and put back by
/// <see cref="WithinClause.Ensure(string, string?, FileLevelText?)"/>, which every whole-file writer
/// already goes through to add the clause. It stays with that class: a rename keeps it, a move takes
/// it along, and splitting a single-file package puts it at the top of the new <c>package.mo</c>.</para>
///
/// <para>The three parts are kept <b>exactly as written</b> — whitespace included — so a writer that
/// does not reformat (the MCP edit tools) puts them back character for character. A writer that
/// reformats uses <see cref="Formatted"/>, which is what the renderer makes of the same comments,
/// so Format All and the incremental formatter (which renders the file from disk, header and all)
/// write the same file.</para>
/// </summary>
public sealed class FileLevelText
{
    /// <summary>Everything before the <c>within</c> keyword — or before the class, in a file with no
    /// clause — including the whitespace that separates it from what follows.</summary>
    public string Leading { get; }

    /// <summary>Everything between the clause's <c>;</c> and the class, whitespace included.</summary>
    public string AfterWithin { get; }

    /// <summary>Everything after the <c>;</c> that ends the file's last class.</summary>
    public string Trailing { get; }

    public FileLevelText(string leading, string afterWithin, string trailing)
    {
        Leading = leading;
        AfterWithin = afterWithin;
        Trailing = trailing;
    }

    /// <summary>
    /// The file-level text of <paramref name="fileText"/>, or null when it has none worth keeping —
    /// only whitespace, which is what almost every file has and costs nothing to rebuild.
    /// </summary>
    /// <param name="fileText">The whole file, with the line endings the class offsets were taken in.</param>
    /// <param name="classStart">Offset of the first character of the class that heads the file.</param>
    /// <param name="lastClassStop">Offset of the last character of the file's last top-level class
    /// (its closing name; the <c>;</c> after it belongs to the file).</param>
    public static FileLevelText? Read(string fileText, int classStart, int lastClassStop)
    {
        if (classStart < 0 || classStart > fileText.Length)
            return null;

        string leading;
        string afterWithin;
        var first = WithinClause.SkipIgnorable(fileText, 0, out _);
        if (first < classStart && WithinClause.Has(fileText[first..]))
        {
            var semicolon = fileText.IndexOf(';', first);
            if (semicolon < 0 || semicolon >= classStart)
                return null;
            leading = fileText[..first];
            afterWithin = fileText[(semicolon + 1)..WithinClause.SkipIgnorable(fileText, semicolon + 1, out _)];
        }
        else
        {
            // No clause of its own. A writer adds one, and the text before the class goes above it.
            leading = fileText[..first];
            afterWithin = "\n";
        }

        var trailing = string.Empty;
        if (lastClassStop >= classStart && lastClassStop < fileText.Length)
        {
            var end = WithinClause.SkipIgnorable(fileText, lastClassStop + 1, out _);
            if (end < fileText.Length && fileText[end] == ';'
                && WithinClause.SkipIgnorable(fileText, end + 1, out _) == fileText.Length)
                trailing = fileText[(end + 1)..];
        }

        return HasComment(leading) || HasComment(afterWithin) || HasComment(trailing)
            ? new FileLevelText(leading, afterWithin, trailing)
            : null;
    }

    /// <summary>
    /// The same comments as the renderer writes them: one per line, trailing whitespace trimmed, no
    /// blank lines around them, and none between them and the clause or the class. What Format All
    /// writes, because it is what the incremental formatter writes for the same file.
    /// </summary>
    public FileLevelText Formatted()
    {
        var leading = RenderComments(Leading);
        var afterWithin = RenderComments(AfterWithin);
        var trailing = RenderComments(Trailing);
        return new FileLevelText(
            leading.Length > 0 ? leading + "\n" : string.Empty,
            afterWithin.Length > 0 ? "\n" + afterWithin + "\n" : "\n",
            trailing.Length > 0 ? "\n" + trailing : string.Empty);
    }

    /// <summary>
    /// <paramref name="file"/> — a file's text opening with its within clause — with this text put
    /// back around the clause and the class. Unchanged when <paramref name="file"/> already has text
    /// above its clause, which means it is a whole file that brought its own (mlqt_format_class renders the
    /// file as it is on disk), or has no clause to place it by.
    /// </summary>
    public string ApplyTo(string file)
    {
        if (!WithinClause.OpensWithClause(file, out var clauseStart, out var hasTextAbove) || hasTextAbove)
            return file;

        var semicolon = file.IndexOf(';', clauseStart);
        if (semicolon < 0)
            return file;

        var body = file[(semicolon + 1)..].TrimStart();
        if (HasComment(Trailing))
            body = body.TrimEnd() + Trailing;

        return string.Concat(Leading, file[clauseStart..(semicolon + 1)], AfterWithin, body);
    }

    /// <summary>
    /// <paramref name="classCode"/> with this text's comments directly above and below it, for a
    /// class moved into a file another class heads: the header cannot head that file, which has its
    /// own, so it goes with the class. Blank lines are dropped; the comments are what was written.
    /// </summary>
    public string AroundNested(string classCode)
    {
        var above = string.Join("\n", new[] { RenderComments(Leading), RenderComments(AfterWithin) }
            .Where(p => p.Length > 0));
        var below = RenderComments(Trailing);
        var result = above.Length > 0 ? above + "\n" + classCode.TrimStart() : classCode;
        return below.Length > 0 ? result.TrimEnd() + "\n" + below : result;
    }

    /// <summary>True when <paramref name="text"/> holds anything but whitespace.</summary>
    private static bool HasComment(string text) => !string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// Whitespace and comments rendered the way <see cref="ModelicaRenderer"/> renders them at the top
    /// of a file — by asking it, so the two cannot drift. The comments are placed above a bare clause,
    /// the one position the grammar accepts them in for all three parts.
    /// </summary>
    private static string RenderComments(string text)
    {
        if (!HasComment(text))
            return string.Empty;

        var (tree, _) = ModelicaParserHelper.ParseWithErrors(text.TrimEnd() + "\nwithin;");
        var renderer = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true,
            excludeClassDefinitions: false, tokenStream: null, classNamesToExclude: null);
        renderer.VisitStored_definition(tree);

        var lines = renderer.Code.ToList();
        lines.RemoveAt(lines.Count - 1);   // the clause
        return string.Join("\n", lines);
    }
}
