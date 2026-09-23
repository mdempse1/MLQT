namespace ModelicaParser.Helpers;

/// <summary>One run of source lines that is hidden, and what stands in its place.</summary>
/// <param name="FirstLine">First hidden line, 1-based and inclusive.</param>
/// <param name="LastLine">Last hidden line, 1-based and inclusive.</param>
/// <param name="Replacement">
/// The single line shown instead, or <c>null</c> to show nothing at all. Whatever the caller is
/// eliding — raw source or highlight markup — the replacement has to be in the same language, since
/// it is spliced in beside the lines that were kept.
/// </param>
public readonly record struct ElidedRange(int FirstLine, int LastLine, string? Replacement)
{
    /// <summary>How many source lines this range covers.</summary>
    public int Length => LastLine - FirstLine + 1;

    /// <summary>How many display lines it leaves behind: one for a replacement, none without.</summary>
    public int Kept => Replacement is null ? 0 : 1;
}

/// <summary>
/// Hiding runs of lines, and being able to say afterwards which line of the original any displayed
/// line came from.
///
/// <para><b>Why this is one type.</b> Four things in MLQT hide part of a class and each of them is
/// the same operation: the viewer's hide-annotations toggle, the viewer hiding a package's nested
/// class definitions (which is on for every package, so it is not an optional extra), the MCP
/// <c>get_class_source</c> tool stripping annotations for an agent, and the package trimmer removing
/// inline standalone children. Today each is "run the renderer and have it not visit that subtree",
/// which rebuilds the whole text as a side effect and loses any relation to the file. Dropping the
/// lines instead keeps every line that is still shown exactly as it was written, and the relation
/// between the two is this map.</para>
///
/// <para><b>Why the map is easy here and was not in the renderer.</b> This transformation only ever
/// removes whole lines, in order, without overlapping — so it is monotone, and inverting it is
/// arithmetic rather than bookkeeping. A line map threaded through <c>ModelicaRenderer</c> would
/// have to track every layout decision that moves text, in code four other surfaces share.</para>
///
/// <para>Line numbers are 1-based throughout, matching <c>Finding.LineNumber</c> and
/// <c>ModelNode.StartLine</c>.</para>
/// </summary>
public sealed class SourceElision
{
    private readonly ElidedRange[] _ranges;

    private SourceElision(ElidedRange[] ranges) => _ranges = ranges;

    /// <summary>Hides nothing. Every line shows, and both maps are the identity.</summary>
    public static readonly SourceElision None = new([]);

    /// <summary>The ranges, in source order.</summary>
    public IReadOnlyList<ElidedRange> Ranges => _ranges;

    /// <summary>True when nothing is hidden.</summary>
    public bool IsEmpty => _ranges.Length == 0;

    /// <summary>
    /// Builds an elision from <paramref name="ranges"/>, which are sorted here and then checked for
    /// overlap.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A range starts before line 1, ends before it starts, or overlaps its neighbour. Overlapping
    /// ranges have no single answer for what the display shows, so they are refused rather than
    /// resolved — a caller that produced them has a bug, and silently merging them would hide it.
    /// </exception>
    public static SourceElision Of(IEnumerable<ElidedRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var ordered = ranges.OrderBy(r => r.FirstLine).ToArray();
        if (ordered.Length == 0)
            return None;

        for (var i = 0; i < ordered.Length; i++)
        {
            var range = ordered[i];
            if (range.FirstLine < 1)
                throw new ArgumentException(
                    $"elided range starts at line {range.FirstLine}; lines are 1-based", nameof(ranges));
            if (range.LastLine < range.FirstLine)
                throw new ArgumentException(
                    $"elided range {range.FirstLine}-{range.LastLine} ends before it starts", nameof(ranges));
            if (i > 0 && range.FirstLine <= ordered[i - 1].LastLine)
                throw new ArgumentException(
                    $"elided ranges {ordered[i - 1].FirstLine}-{ordered[i - 1].LastLine} and "
                    + $"{range.FirstLine}-{range.LastLine} overlap", nameof(ranges));
        }

        return new SourceElision(ordered);
    }

    /// <summary>
    /// One elision covering all of <paramref name="elisions"/>. A range wholly inside another is
    /// dropped — hiding a package's nested classes already hides the annotations inside them, and
    /// asking for both is the ordinary case rather than a mistake.
    ///
    /// <para>Ranges that merely <em>overlap</em> are still refused by <see cref="Of"/>: one covering
    /// the other is a question with an obvious answer, and two halves of each other is not.</para>
    /// </summary>
    public static SourceElision Merge(params SourceElision?[] elisions)
    {
        ArgumentNullException.ThrowIfNull(elisions);

        var all = elisions.Where(e => e is not null)
            .SelectMany(e => e!.Ranges)
            // Widest first, so a range is only ever dropped in favour of one that covers it — and
            // two identical ranges leave one behind rather than none.
            .OrderByDescending(r => r.Length)
            .ThenBy(r => r.FirstLine)
            .ToList();

        var kept = new List<ElidedRange>();
        foreach (var range in all)
            if (!kept.Any(k => k.FirstLine <= range.FirstLine && range.LastLine <= k.LastLine))
                kept.Add(range);

        return Of(kept);
    }

    /// <summary>
    /// The lines to display: <paramref name="lines"/> with each range replaced by its replacement,
    /// or removed where it has none.
    /// </summary>
    public List<string> Apply(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (_ranges.Length == 0)
            return [.. lines];

        var display = new List<string>(lines.Count);
        var line = 1;
        var next = 0;

        while (line <= lines.Count)
        {
            if (next < _ranges.Length && line == _ranges[next].FirstLine)
            {
                if (_ranges[next].Replacement is { } replacement)
                    display.Add(replacement);
                line = _ranges[next].LastLine + 1;
                next++;
                continue;
            }

            display.Add(lines[line - 1]);
            line++;
        }

        return display;
    }

    /// <summary>
    /// The same substitution as <see cref="Apply"/>, but every hidden line is emptied rather than
    /// removed, so the result has exactly as many lines as it was given and each one is still at its
    /// own number.
    ///
    /// <para><b>Why both exist.</b> A viewer shows the elided text next to a line map it can invert,
    /// so dropping the lines costs nothing. A caller handing the text to someone with no map — the
    /// MCP <c>get_class_source</c> tool handing a class to an agent that will go on to read findings
    /// reported against that class's own line numbers — has to keep the numbering, and a blank line
    /// is the cheapest thing that does (B218).</para>
    ///
    /// <para>A range with a replacement keeps it on the range's first line and blanks the rest.</para>
    /// </summary>
    public List<string> Blank(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var display = new List<string>(lines);
        foreach (var range in _ranges)
        {
            var last = Math.Min(range.LastLine, display.Count);
            for (var line = range.FirstLine; line <= last; line++)
                display[line - 1] = line == range.FirstLine && range.Replacement is { } replacement
                    ? replacement
                    : string.Empty;
        }

        return display;
    }

    /// <summary>
    /// Which line of the original is showing at <paramref name="displayLine"/>. A replacement line
    /// answers with the first line of the range it stands for, which is where the hidden text began
    /// — so clicking it navigates to the annotation or the class, not past it.
    /// </summary>
    public int ToSourceLine(int displayLine)
    {
        if (displayLine < 1)
            return displayLine;

        var offset = 0;
        foreach (var range in _ranges)
        {
            var displayOfRange = range.FirstLine - offset;
            if (displayLine < displayOfRange)
                break;
            if (range.Kept == 1 && displayLine == displayOfRange)
                return range.FirstLine;
            offset += range.Length - range.Kept;
        }

        return displayLine + offset;
    }

    /// <summary>
    /// Where line <paramref name="sourceLine"/> of the original is showing, or <c>null</c> if it is
    /// hidden and nothing stands in its place. A hidden line inside a range that has a replacement
    /// answers with the replacement's line, which is the nearest honest answer: it is where the user
    /// would look for it.
    /// </summary>
    public int? ToDisplayLine(int sourceLine)
    {
        if (sourceLine < 1)
            return sourceLine;

        var offset = 0;
        foreach (var range in _ranges)
        {
            if (range.FirstLine > sourceLine)
                break;
            if (sourceLine <= range.LastLine)
                return range.Kept == 0 ? null : range.FirstLine - offset;
            offset += range.Length - range.Kept;
        }

        return sourceLine - offset;
    }

    /// <summary>Whether line <paramref name="sourceLine"/> of the original is hidden.</summary>
    public bool IsHidden(int sourceLine) =>
        _ranges.Any(r => sourceLine >= r.FirstLine && sourceLine <= r.LastLine);
}
