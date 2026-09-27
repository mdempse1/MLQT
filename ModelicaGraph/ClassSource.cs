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
/// Only the second is a reason to read the file: a trimmed package whose stored text is still the
/// file's own lines (<see cref="ModelNode.TrimElision"/> set, <see cref="ModelNode.SourceMatchesFile"/>
/// true) is sliced back out of it, and everything else is shown as stored.</para>
///
/// <para><b>Never after a save</b> (B400). A save rewrites the file and stores what it wrote on the
/// class, clearing <see cref="ModelNode.SourceMatchesFile"/>; nothing updates the offsets, so a slice
/// taken at them is a cut through a file they no longer describe. The stored text is what was
/// written, so it is the answer. Slicing anyway left only a check that the slice named the class on
/// its first line between that and a chunk of some other code: a class a few characters further
/// along after a save shortened the one above it began mid-declaration, ran into the next class,
/// and passed.</para>
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
    /// The class's source to show: the stored text, except for a trimmed package that has not been
    /// saved since, whose file is sliced afresh. Falls back to the stored text whenever the file cannot be read, the offsets
    /// are not populated, or the file has changed since they were taken so that they no longer cut
    /// out the class — a class shown from slightly stale text beats a blank pane, and beats a chunk
    /// of some other code.
    /// </summary>
    public static string For(ModelNode model, DirectedGraph graph)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(graph);

        var stored = model.Definition.ModelicaCode ?? string.Empty;
        if (model.TrimElision is not { } trim || !model.SourceMatchesFile)
            return stored;

        var path = graph.GetNode<FileNode>(model.ContainingFileId ?? "")?.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return stored;

        try
        {
            var slice = SliceFromFile(ModelicaFileEncoding.ReadAllTextOnly(path), model.StartIndex, model.StopIndex);
            return slice is not null && IsStillTheClass(trim, slice, stored) ? slice : stored;
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
    /// loaded, is still the trimmed package (B343).
    ///
    /// <para>The offsets are from load time and the file is read now. After an edit outside MLQT
    /// the monitor holds the change until Refresh, and meanwhile a trimmed package was shown as an
    /// arbitrary chunk of the new file — possibly starting mid-token — and the diff compared that
    /// chunk with HEAD. A trimmed package whose stored text is still the file's own lines has an
    /// exact answer: dropping the trimmed lines from the slice has to give back the stored text,
    /// character for character, because that is how the stored text was made.</para>
    /// </summary>
    private static bool IsStillTheClass(SourceElision trim, string slice, string stored)
    {
        var kept = trim.Apply(slice.Split('\n'));
        return string.Equals(string.Join("\n", kept),
            ModelicaParserHelper.NormalizeLineEndings(stored), StringComparison.Ordinal);
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
