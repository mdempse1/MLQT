using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph;

/// <summary>
/// The text of a class <b>as it is written in the user's file</b>, for anything that shows a class
/// rather than checks it.
///
/// <para>Usually that is simply <c>Definition.ModelicaCode</c>, which starts life as an exact slice
/// of the file. Two things change it — the formatter after a save, which is correct because the
/// file now says the same, and <see cref="PackageCodeTrimmer"/>, which excises a package's inline
/// standalone children and so leaves text that is the file's own but no longer all of the class.
/// <see cref="ModelNode.StoredSourceIsWholeClass"/> is the flag that says which, and when it is
/// false this reads the file again and slices the class back out of it.</para>
///
/// <para><b>Slicing is not as obvious as the fields make it look</b>, which is why it is here and
/// not at each call site. The offsets are into the file's text with line endings normalised, because
/// that is the text the lexer was given; slicing the file as read drifts by one character per line
/// above the class, and on a CRLF file — which every file in the Modelica Standard Library and in
/// Buildings is — a class a few thousand lines down comes back as some other class entirely. And the
/// <c>class_definition</c> rule stops at the <c>IDENT</c> of <c>end A</c>, so the statement's
/// <c>;</c> has to be taken as well. Measured over both libraries: 0 of 13,997 classes matched the
/// documented rule, and 99.5% match this one exactly.</para>
///
/// <para>The remainder are short class definitions carrying an element annotation —
/// <c>replaceable package Medium = … "…"</c> followed on the next line by
/// <c>annotation (choicesAllMatching = true);</c>. The annotation belongs to the <em>element</em>,
/// not to the <c>class_definition</c>, so the rule ends at the comment and the <c>;</c> is past it.
/// The slice therefore comes back without a terminator. Nothing is lost that the stored text keeps —
/// it ends at the same place for the same reason — so this is left alone rather than reached for
/// with a wider scan that could pick up a semicolon belonging to something else (backlog B231).</para>
/// </summary>
public static class ClassSource
{
    /// <summary>
    /// The class's source to show: the stored text while it is still the file's, otherwise the file
    /// sliced afresh. Falls back to the stored text whenever the file cannot be read, the offsets
    /// are not populated, or the file has changed since they were taken so that they no longer cut
    /// out the class — a class shown from slightly stale text beats a blank pane, and beats a chunk
    /// of some other code.
    /// </summary>
    public static string For(ModelNode model, DirectedGraph graph)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(graph);

        var stored = model.Definition.ModelicaCode ?? string.Empty;
        if (model.StoredSourceIsWholeClass)
            return stored;

        var path = graph.GetNode<FileNode>(model.ContainingFileId ?? "")?.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return stored;

        try
        {
            var slice = SliceFromFile(ModelicaFileEncoding.ReadAllTextOnly(path), model.StartIndex, model.StopIndex);
            return slice is not null && IsStillTheClass(model, slice, stored) ? slice : stored;
        }
        catch (IOException)
        {
            return stored;
        }
        catch (UnauthorizedAccessException)
        {
            return stored;
        }
    }

    /// <summary>
    /// Whether a slice of the file <em>as it is now</em>, taken at offsets recorded when it was
    /// loaded, is still the class (B343).
    ///
    /// <para>The offsets are from load time and the file is read now. After an edit outside MLQT
    /// the monitor holds the change until Refresh, and meanwhile a trimmed package was shown as an
    /// arbitrary chunk of the new file — possibly starting mid-token — and the diff compared that
    /// chunk with HEAD. Two checks, as strong as what is known allows:</para>
    /// <list type="bullet">
    /// <item>A trimmed package whose stored text is still the file's own lines has an exact answer:
    /// dropping the trimmed lines from the slice has to give back the stored text, character for
    /// character, because that is how the stored text was made.</item>
    /// <item>Text something rewrote cannot be compared that way — being different from the file is
    /// why it is read — so the slice has only to name the class on its first line and, where it ends
    /// with <c>end X</c>, to end it: a chunk from somewhere else in the file does neither.</item>
    /// </list>
    /// </summary>
    private static bool IsStillTheClass(ModelNode model, string slice, string stored)
    {
        if (model.TrimElision is { } trim && model.SourceMatchesFile)
        {
            var kept = trim.Apply(slice.Split('\n'));
            return string.Equals(string.Join("\n", kept),
                ModelicaParserHelper.NormalizeLineEndings(stored), StringComparison.Ordinal);
        }

        var name = System.Text.RegularExpressions.Regex.Escape(model.Definition.Name ?? "");
        if (name.Length == 0)
            return false;

        var text = slice.TrimStart();
        var firstLine = text.Split('\n', 2)[0];
        if (!System.Text.RegularExpressions.Regex.IsMatch(firstLine, $@"(?<![\w.']){name}(?![\w'])"))
            return false;

        var end = System.Text.RegularExpressions.Regex.Match(text, @"\bend\s+([\w.']+)\s*;?\s*$");
        return !end.Success || end.Groups[1].Value == model.Definition.Name;
    }

    /// <summary>
    /// The class at <paramref name="startIndex"/>..<paramref name="stopIndex"/> cut out of
    /// <paramref name="fileText"/>, or null when those offsets do not describe a range inside it.
    /// </summary>
    /// <param name="fileText">The file as read. Normalised here, because the offsets assume it.</param>
    public static string? SliceFromFile(string fileText, int startIndex, int stopIndex)
    {
        if (string.IsNullOrEmpty(fileText) || startIndex < 0 || stopIndex < startIndex)
            return null;

        var text = ModelicaParserHelper.NormalizeLineEndings(fileText);
        if (stopIndex >= text.Length)
            return null;

        // The rule ends at the class name in `end A`; take the `;` that closes the statement too,
        // across any spaces between them, because that is part of the class as everything else
        // understands it.
        var end = stopIndex;
        var next = end + 1;
        while (next < text.Length && (text[next] == ' ' || text[next] == '\t'))
            next++;
        if (next < text.Length && text[next] == ';')
            end = next;

        return text[startIndex..(end + 1)];
    }
}
