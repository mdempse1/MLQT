namespace MLQT.Shared.Components;

public partial class DiffViewer : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ISettingsService SettingsService { get; set; } = null!;

    /// <summary>
    /// Original (base) content of the file
    /// </summary>
    [Parameter]
    public string? OriginalContent { get; set; }

    /// <summary>
    /// Modified (working copy) content of the file
    /// </summary>
    [Parameter]
    public string? ModifiedContent { get; set; }

    /// <summary>
    /// File name for determining syntax highlighting
    /// </summary>
    [Parameter]
    public string? FileName { get; set; }

    /// <summary>
    /// Optional height constraint
    /// </summary>
    [Parameter]
    public string? Height { get; set; }

    /// <summary>
    /// Number of context lines to show around changes in diff mode
    /// </summary>
    [Parameter]
    public int ContextLines { get; set; } = 3;

    /// <summary>
    /// Current view mode
    /// </summary>
    [Parameter]
    public DiffViewMode ViewMode { get; set; } = DiffViewMode.Unified;

    [Parameter]
    public EventCallback<DiffViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// Show the view mode selector buttons
    /// </summary>
    [Parameter]
    public bool ShowViewModeSelectorButtons { get; set; } = true;

    /// <summary>
    /// Label for the left (original) pane in side-by-side mode
    /// </summary>
    [Parameter]
    public string OriginalLabel { get; set; } = "Original";

    /// <summary>
    /// Label for the right (modified) pane in side-by-side mode
    /// </summary>
    [Parameter]
    public string ModifiedLabel { get; set; } = "Modified";

    /// <summary>
    /// Maximum number of cells in the LCS table before falling back.
    /// 50 million cells ≈ 200 MB, allowing files up to ~7,000 lines each.
    /// </summary>
    private const long MaxLcsCells = 50_000_000;

    /// <summary>
    /// The most rows the viewer will lay out and render (B410).
    /// </summary>
    /// <remarks>
    /// <para><b>A limit on cells is not a limit on rows.</b> The LCS bound is on the product of the
    /// two lengths, so a ten-line class against a 157,852-line file passes it at 1.9M cells of 50M
    /// - and every line of the file then becomes a removed row. Side by side rendered all of them;
    /// the page said "Comparing…" and then Blazor's unhandled-error banner, with nothing in the log.
    /// The largest diff the cell limit was sized for, two ~7,000-line files, is at most 14,000 rows,
    /// so this takes away nothing that used to work.</para>
    /// </remarks>
    internal const int MaxRows = 20_000;

    private List<DiffLine> _unifiedLines = new();
    private List<DiffLine> _leftLines = new();
    private List<DiffLine> _rightLines = new();
    private int _addedCount = 0;
    private int _removedCount = 0;
    private bool _isModelicaFile = false;
    private string? _errorMessage;

    private ElementReference _leftPaneRef;
    private ElementReference _rightPaneRef;
    private bool _scrollSyncActive = false;
    private bool _isDarkMode = false;

    protected override async Task OnInitializedAsync()
    {
        var uiSettings = await SettingsService.GetAsync("UI", new UISettings());
        _isDarkMode = uiSettings.Theme == Theme.Dark;
        NavState.OnThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(UISettings uiSettings)
    {
        _isDarkMode = uiSettings.Theme == Theme.Dark;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Builds the diff when — and only when — what it is a diff <em>of</em> has changed (B341).
    ///
    /// <para>This ran the whole thing on every parameter set: an O(m·n) LCS and a full parse of each
    /// side for the colouring, on the dispatcher. The Code Review page re-renders for every findings
    /// batch, baseline change and progress update, so during a background check each of those cost
    /// two parses of an unchanged class. Now the expensive half is <see cref="Prepare"/>, keyed on
    /// the two texts and whether they are Modelica, and run on the pool; the view mode and context
    /// only re-lay-out what it produced, which is linear and stays here.</para>
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        _isModelicaFile = !string.IsNullOrEmpty(FileName) &&
            (FileName.EndsWith(".mo", StringComparison.OrdinalIgnoreCase) ||
             FileName.EndsWith(".mos", StringComparison.OrdinalIgnoreCase));

        var original = OriginalContent;
        var modified = ModifiedContent;
        var isModelica = _isModelicaFile;

        if (DescribesTheSameContent(_prepared, original, modified, isModelica))
        {
            // Back to what is already prepared: whatever was on its way is for content no longer
            // asked for, and must not land over this.
            if (_preparing is not null)
            {
                _preparing = null;
                _prepareGeneration++;
            }
        }
        else
        {
            // Already on its way: the call that started it will lay it out when it lands.
            if (_preparing is { } inFlight && DescribesTheSameContent(inFlight, original, modified, isModelica))
                return;

            var generation = ++_prepareGeneration;
            _preparing = new PreparedDiff(original, modified, isModelica, [], [], [], null);
            Preparations++;

            var prepared = await Task.Run(() => Prepare(original, modified, isModelica));

            // Superseded while it ran - by different content, which will lay itself out.
            if (generation != _prepareGeneration)
                return;

            _preparing = null;
            _prepared = prepared;
        }

        LayOut();
    }

    /// <summary>Whether <paramref name="diff"/> was prepared from exactly these inputs.</summary>
    private static bool DescribesTheSameContent(PreparedDiff? diff, string? original, string? modified, bool isModelica) =>
        diff is not null
        && diff.IsModelica == isModelica
        && string.Equals(diff.Original, original, StringComparison.Ordinal)
        && string.Equals(diff.Modified, modified, StringComparison.Ordinal);

    /// <summary>
    /// How many times the diff has been prepared, for a test to hold the memoisation to.
    /// </summary>
    internal int Preparations { get; private set; }

    /// <summary>Whether the diff for the current content is still being prepared.</summary>
    internal bool IsPreparing => _preparing is not null;

    /// <summary>
    /// Whether the two scrollable panes are on screen — which is not the same question as whether
    /// the view mode asks for them.
    /// </summary>
    /// <remarks>
    /// <para><b>The markup and <see cref="OnAfterRenderAsync"/> have to agree about this, and they
    /// did not.</b> A file too big for the diff, or one with nothing in either version, renders a
    /// message instead of the panes — so <c>@ref</c> never runs and both references stay default.
    /// A default <see cref="ElementReference"/> still serialises to an object, so it arrives in
    /// JavaScript as something truthy that is not an element, past the <c>if (!leftEl)</c> guard
    /// there, and <c>leftEl.addEventListener is not a function</c> comes back out of
    /// <c>OnAfterRenderAsync</c> as an unhandled exception. That is the whole of the red "An
    /// unhandled error has occurred" banner a user got for opening a large FMU model in diff mode.</para>
    ///
    /// <para>Both the markup's last branch and the interop call now ask this one property, so the
    /// two cannot come apart again.</para>
    /// </remarks>
    internal bool ShowsPanes =>
        !IsPreparing
        && string.IsNullOrEmpty(_errorMessage)
        && !(string.IsNullOrEmpty(OriginalContent) && string.IsNullOrEmpty(ModifiedContent))
        && ViewMode is DiffViewMode.SideBySide or DiffViewMode.SideBySideFull;

    /// <summary>
    /// How many after-render passes have finished that began with the diff already prepared — so a
    /// test can wait for the interop decision about the content it gave, rather than asserting
    /// before it has been made (B401).
    /// </summary>
    /// <remarks>
    /// A render notification reaches a test before <see cref="OnAfterRenderAsync"/> runs, and a
    /// finished after-render renders nothing, so neither a wait on the markup nor one on
    /// <see cref="IsPreparing"/> can see it. Asserting "no interop call" at that point passes
    /// whether or not the call was about to be made. Written on the dispatcher, read from the test's
    /// thread, hence the interlocked access.
    /// </remarks>
    internal int SettledAfterRenders => Volatile.Read(ref _settledAfterRenders);
    private int _settledAfterRenders;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var settled = !IsPreparing;

        // Wiring the panes together is a convenience. Losing the window is not, and an exception out
        // of OnAfterRenderAsync takes the whole app down with a banner whose only offer is Reload.
        try
        {
            if (ShowsPanes)
            {
                await JS.InvokeVoidAsync("diffViewer.initSyncScroll", _leftPaneRef, _rightPaneRef);
                _scrollSyncActive = true;
            }
            else if (_scrollSyncActive)
            {
                await JS.InvokeVoidAsync("diffViewer.dispose");
                _scrollSyncActive = false;
            }
        }
        catch (JSException ex)
        {
            // The panes still scroll; they just stop following each other.
            LoggingService.Warn("DiffViewer", $"Could not synchronise the diff panes' scrolling: {ex.Message}");
            _scrollSyncActive = false;
        }
        finally
        {
            if (settled)
                Interlocked.Increment(ref _settledAfterRenders);
        }
    }

    public async ValueTask DisposeAsync()
    {
        NavState.OnThemeChanged -= OnThemeChanged;
        if (_scrollSyncActive)
            await JS.InvokeVoidAsync("diffViewer.dispose");
    }

    public void SetViewMode(DiffViewMode mode)
    {
        ViewMode = mode;
        ViewModeChanged.InvokeAsync(mode);
        LayOut();
    }

    private string GetContainerStyle()
    {
        return !string.IsNullOrEmpty(Height) ? $"height: {Height};" : "";
    }

    private string GetDiffSummary()
    {
        if (IsPreparing)
            return "";
        if (_addedCount == 0 && _removedCount == 0)
            return "No changes";

        var parts = new List<string>();
        if (_addedCount > 0)
            parts.Add($"+{_addedCount}");
        if (_removedCount > 0)
            parts.Add($"-{_removedCount}");
        return string.Join(" / ", parts) + " lines";
    }

    /// <summary>
    /// Each line as it will be shown: Modelica coloured by the same classifier the code viewer uses,
    /// anything else left as it is.
    ///
    /// <para>This is what closes B178. The diff used to colour its own panes with a keyword regex
    /// while the single-file viewer was coloured from the parse tree, so the same code was coloured
    /// two ways in two panes of the same page and only one of them followed the user's chosen
    /// scheme. The two highlighters could not be merged while one of them rebuilt the text —
    /// a diff hunk is not parseable — but now that the classifier emits the source in place, both
    /// panes can be the same one.</para>
    ///
    /// <para>Falls back to the plain lines <b>HTML-encoded</b> whenever the classifier does not
    /// return exactly one markup line per source line. It cannot, by construction: the emitter
    /// round-trips the source and so preserves its line count. But the diff indexes these two arrays
    /// in step, and being wrong about that would mean showing one line's colouring on another line's
    /// text — worse than showing no colouring at all. The encoding is done here because for a
    /// Modelica file nothing downstream encodes: the classifier's own output is already safe outside
    /// its tags, and encoding it a second time would turn a stray <c>&amp;</c> into
    /// <c>&amp;amp;</c> on screen.</para>
    /// </summary>
    internal static string[] DisplayLines(string? content, string[] plainLines, bool isModelicaFile)
    {
        if (!isModelicaFile)
            return plainLines;

        if (!string.IsNullOrEmpty(content))
        {
            try
            {
                var markup = ModelicaTokenClassifier.Highlight(content);
                if (markup.Count == plainLines.Length)
                    return [.. markup];
            }
            catch
            {
                // Fall through to the plain, encoded lines.
            }
        }

        return [.. plainLines.Select(l => System.Web.HttpUtility.HtmlEncode(l) ?? "")];
    }

    /// <summary>
    /// The expensive, mode-independent half of a diff: the edit script, and each side's lines as
    /// finished HTML. Carries the inputs it was made from, so it can say whether it is still the
    /// answer.
    /// </summary>
    internal sealed record PreparedDiff(
        string? Original, string? Modified, bool IsModelica,
        string[] OriginalHtml, string[] ModifiedHtml, List<DiffOp> Ops, string? Error);

    private PreparedDiff? _prepared;
    private PreparedDiff? _preparing;
    private int _prepareGeneration;
    private (PreparedDiff Diff, DiffViewMode Mode, int Context)? _laidOut;

    /// <summary>
    /// Diffs the two texts and colours both sides. Static and pure, because it runs on the pool
    /// (B341): everything it needs is passed in and everything it makes is returned.
    /// </summary>
    internal static PreparedDiff Prepare(string? original, string? modified, bool isModelica)
    {
        var originalLines = (original ?? "").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var modifiedLines = (modified ?? "").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Check if the file is too large for the O(m*n) LCS algorithm
        long lcsCells = (long)(originalLines.Length + 1) * (modifiedLines.Length + 1);
        if (lcsCells > MaxLcsCells)
        {
            var maxLines = Math.Max(originalLines.Length, modifiedLines.Length);
            return new PreparedDiff(original, modified, isModelica, [], [], [],
                $"File is too large for detailed diff comparison ({maxLines:N0} lines). " +
                "Try viewing a smaller section of the file, or compare using an external diff tool.");
        }

        try
        {
            // The diff is computed on the plain text and only then dressed up: comparing markup
            // would diff the colouring as well as the code, and a line that merely changed category
            // would read as a change.
            var ops = ComputeLcsDiff(originalLines, modifiedLines);

            var originalHtml = DisplayLines(original, originalLines, isModelica)
                .Select(l => ParseContent(l, isModelica)).ToArray();
            var modifiedHtml = DisplayLines(modified, modifiedLines, isModelica)
                .Select(l => ParseContent(l, isModelica)).ToArray();

            return new PreparedDiff(original, modified, isModelica, originalHtml, modifiedHtml, ops, null);
        }
        catch (OutOfMemoryException)
        {
            return new PreparedDiff(original, modified, isModelica, [], [], [],
                $"Not enough memory to compute diff for this file " +
                $"({originalLines.Length:N0} / {modifiedLines.Length:N0} lines). " +
                "Try viewing a smaller section, or compare using an external diff tool.");
        }
        catch (Exception ex)
        {
            // Off the dispatcher now, so an exception here would leave the viewer saying
            // "Comparing…" for ever rather than taking the page down; say what happened instead.
            LoggingService.Error("DiffViewer", "Could not compute the diff", ex);
            return new PreparedDiff(original, modified, isModelica, [], [], [],
                $"Could not compute the diff: {ex.Message}");
        }
    }

    /// <summary>
    /// Lays the prepared diff out for the current view mode — linear in its length, and skipped when
    /// neither the diff, the mode nor the context has changed since the last time.
    /// </summary>
    private void LayOut()
    {
        if (_prepared is not { } diff)
            return;
        if (_laidOut is { } last && ReferenceEquals(last.Diff, diff)
            && last.Mode == ViewMode && last.Context == ContextLines)
            return;
        _laidOut = (diff, ViewMode, ContextLines);

        _unifiedLines.Clear();
        _leftLines.Clear();
        _rightLines.Clear();
        _addedCount = 0;
        _removedCount = 0;
        _errorMessage = diff.Error;
        if (diff.Error is not null)
            return;

        if (ViewMode == DiffViewMode.SideBySideFull)
            ComputeFullSideBySide(diff.OriginalHtml, diff.ModifiedHtml, diff.Ops);
        else if (ViewMode == DiffViewMode.SideBySide)
            ComputeSideBySideWithContext(diff.OriginalHtml, diff.ModifiedHtml, diff.Ops);
        else
            ComputeUnifiedWithContext(diff.OriginalHtml, diff.ModifiedHtml, diff.Ops);

        // Counted after laying out, which is linear and cheap, rather than guessed from the edit
        // script: with context, a long script can still be a short diff. The counts stay, so the
        // summary still says how big the change is.
        var rows = Math.Max(_unifiedLines.Count, _leftLines.Count);
        if (rows > MaxRows)
        {
            _unifiedLines.Clear();
            _leftLines.Clear();
            _rightLines.Clear();
            _errorMessage = $"This diff is {rows:N0} rows long, too many to show here (the limit is {MaxRows:N0}). " +
                "Compare the file using an external diff tool.";
        }
    }

    /// <summary>
    /// The edit script between two sets of lines: which lines match, which were inserted, which
    /// were deleted, in file order.
    /// </summary>
    /// <remarks>
    /// Static and pure — it reads no component state, and the three side-by-side and unified
    /// renderings are all built on top of whatever this returns, so an error here is an error in
    /// every diff the user sees.
    /// </remarks>
    internal static List<DiffOp> ComputeLcsDiff(string[] original, string[] modified)
    {
        // Simple LCS-based diff algorithm
        int m = original.Length;
        int n = modified.Length;

        // Build LCS table
        int[,] lcs = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (original[i - 1] == modified[j - 1])
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                else
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            }
        }

        // Backtrack to find diff operations
        var ops = new List<DiffOp>();
        int x = m, y = n;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && original[x - 1] == modified[y - 1])
            {
                ops.Add(new DiffOp { Type = DiffOpType.Equal, OriginalIndex = x - 1, ModifiedIndex = y - 1 });
                x--; y--;
            }
            else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
            {
                ops.Add(new DiffOp { Type = DiffOpType.Insert, ModifiedIndex = y - 1 });
                y--;
            }
            else
            {
                ops.Add(new DiffOp { Type = DiffOpType.Delete, OriginalIndex = x - 1 });
                x--;
            }
        }

        ops.Reverse();
        return ops;
    }

    private void ComputeFullSideBySide(string[] original, string[] modified, List<DiffOp> ops)
    {
        int leftLineNum = 1, rightLineNum = 1;
        int opIndex = 0;

        while (opIndex < ops.Count)
        {
            var op = ops[opIndex];

            if (op.Type == DiffOpType.Equal)
            {
                _leftLines.Add(new DiffLine { LineNumber = leftLineNum.ToString(), Content = original[op.OriginalIndex], Type = DiffLineType.Unchanged });
                _rightLines.Add(new DiffLine { LineNumber = rightLineNum.ToString(), Content = modified[op.ModifiedIndex], Type = DiffLineType.Unchanged });
                leftLineNum++;
                rightLineNum++;
                opIndex++;
            }
            else if (op.Type == DiffOpType.Delete)
            {
                _leftLines.Add(new DiffLine { LineNumber = leftLineNum.ToString(), Content = original[op.OriginalIndex], Type = DiffLineType.Removed });
                _rightLines.Add(new DiffLine { LineNumber = "", Content = "", Type = DiffLineType.Empty });
                leftLineNum++;
                _removedCount++;
                opIndex++;
            }
            else // Insert
            {
                _leftLines.Add(new DiffLine { LineNumber = "", Content = "", Type = DiffLineType.Empty });
                _rightLines.Add(new DiffLine { LineNumber = rightLineNum.ToString(), Content = modified[op.ModifiedIndex], Type = DiffLineType.Added });
                rightLineNum++;
                _addedCount++;
                opIndex++;
            }
        }
    }

    private void ComputeSideBySideWithContext(string[] original, string[] modified, List<DiffOp> ops)
    {
        // Mark which lines are changed or near changes
        var changedOriginal = new HashSet<int>();
        var changedModified = new HashSet<int>();

        foreach (var op in ops)
        {
            if (op.Type == DiffOpType.Delete)
            {
                for (int i = Math.Max(0, op.OriginalIndex - ContextLines); i <= Math.Min(original.Length - 1, op.OriginalIndex + ContextLines); i++)
                    changedOriginal.Add(i);
            }
            else if (op.Type == DiffOpType.Insert)
            {
                for (int i = Math.Max(0, op.ModifiedIndex - ContextLines); i <= Math.Min(modified.Length - 1, op.ModifiedIndex + ContextLines); i++)
                    changedModified.Add(i);
            }
            else
            {
                // Mark context around unchanged lines that are near changes
                if (changedOriginal.Contains(op.OriginalIndex - 1) || changedModified.Contains(op.ModifiedIndex - 1))
                {
                    changedOriginal.Add(op.OriginalIndex);
                    changedModified.Add(op.ModifiedIndex);
                }
            }
        }

        // Re-traverse to include context for equal lines
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (op.Type == DiffOpType.Equal)
            {
                // Check if within context of a change
                bool nearChange = false;
                for (int j = Math.Max(0, i - ContextLines); j <= Math.Min(ops.Count - 1, i + ContextLines); j++)
                {
                    if (ops[j].Type != DiffOpType.Equal)
                    {
                        nearChange = true;
                        break;
                    }
                }
                if (nearChange)
                {
                    changedOriginal.Add(op.OriginalIndex);
                    changedModified.Add(op.ModifiedIndex);
                }
            }
        }

        // Build the side-by-side view with separators
        int leftLineNum = 1, rightLineNum = 1;
        bool inHunk = false;

        foreach (var op in ops)
        {
            if (op.Type == DiffOpType.Equal)
            {
                if (changedOriginal.Contains(op.OriginalIndex))
                {
                    if (!inHunk)
                    {
                        AddSeparator();
                        inHunk = true;
                    }
                    _leftLines.Add(new DiffLine { LineNumber = leftLineNum.ToString(), Content = original[op.OriginalIndex], Type = DiffLineType.Unchanged });
                    _rightLines.Add(new DiffLine { LineNumber = rightLineNum.ToString(), Content = modified[op.ModifiedIndex], Type = DiffLineType.Unchanged });
                }
                else
                {
                    inHunk = false;
                }
                leftLineNum++;
                rightLineNum++;
            }
            else if (op.Type == DiffOpType.Delete)
            {
                if (!inHunk)
                {
                    AddSeparator();
                    inHunk = true;
                }
                _leftLines.Add(new DiffLine { LineNumber = leftLineNum.ToString(), Content = original[op.OriginalIndex], Type = DiffLineType.Removed });
                _rightLines.Add(new DiffLine { LineNumber = "", Content = "", Type = DiffLineType.Empty });
                leftLineNum++;
                _removedCount++;
            }
            else // Insert
            {
                if (!inHunk)
                {
                    AddSeparator();
                    inHunk = true;
                }
                _leftLines.Add(new DiffLine { LineNumber = "", Content = "", Type = DiffLineType.Empty });
                _rightLines.Add(new DiffLine { LineNumber = rightLineNum.ToString(), Content = modified[op.ModifiedIndex], Type = DiffLineType.Added });
                rightLineNum++;
                _addedCount++;
            }
        }
    }

    private void AddSeparator()
    {
        if (_leftLines.Count > 0)
        {
            _leftLines.Add(new DiffLine { LineNumber = "", Content = "...", Type = DiffLineType.Separator });
            _rightLines.Add(new DiffLine { LineNumber = "", Content = "...", Type = DiffLineType.Separator });
        }
    }

    private void ComputeUnifiedWithContext(string[] original, string[] modified, List<DiffOp> ops)
    {
        // Build unified diff with context
        int leftLineNum = 1, rightLineNum = 1;
        bool inHunk = false;

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];

            // Check if within context of a change
            bool nearChange = false;
            for (int j = Math.Max(0, i - ContextLines); j <= Math.Min(ops.Count - 1, i + ContextLines); j++)
            {
                if (ops[j].Type != DiffOpType.Equal)
                {
                    nearChange = true;
                    break;
                }
            }

            if (op.Type == DiffOpType.Equal)
            {
                if (nearChange)
                {
                    if (!inHunk && _unifiedLines.Count > 0)
                    {
                        _unifiedLines.Add(new DiffLine { LineNumber = "", Content = "...", Type = DiffLineType.Separator, Prefix = " " });
                    }
                    inHunk = true;
                    _unifiedLines.Add(new DiffLine
                    {
                        LineNumber = $"{leftLineNum}/{rightLineNum}",
                        Content = original[op.OriginalIndex],
                        Type = DiffLineType.Unchanged,
                        Prefix = " "
                    });
                }
                else
                {
                    inHunk = false;
                }
                leftLineNum++;
                rightLineNum++;
            }
            else if (op.Type == DiffOpType.Delete)
            {
                if (!inHunk && _unifiedLines.Count > 0)
                {
                    _unifiedLines.Add(new DiffLine { LineNumber = "", Content = "...", Type = DiffLineType.Separator, Prefix = " " });
                }
                inHunk = true;
                _unifiedLines.Add(new DiffLine
                {
                    LineNumber = leftLineNum.ToString(),
                    Content = original[op.OriginalIndex],
                    Type = DiffLineType.Removed,
                    Prefix = "-"
                });
                leftLineNum++;
                _removedCount++;
            }
            else // Insert
            {
                if (!inHunk && _unifiedLines.Count > 0)
                {
                    _unifiedLines.Add(new DiffLine { LineNumber = "", Content = "...", Type = DiffLineType.Separator, Prefix = " " });
                }
                inHunk = true;
                _unifiedLines.Add(new DiffLine
                {
                    LineNumber = rightLineNum.ToString(),
                    Content = modified[op.ModifiedIndex],
                    Type = DiffLineType.Added,
                    Prefix = "+"
                });
                rightLineNum++;
                _addedCount++;
            }
        }
    }

    private string GetLineClass(DiffLineType type)
    {
        return type switch
        {
            DiffLineType.Added => "diff-line-added",
            DiffLineType.Removed => "diff-line-removed",
            DiffLineType.Separator => "diff-line-separator",
            DiffLineType.Empty => "diff-line-empty",
            _ => ""
        };
    }

    /// <summary>
    /// A laid-out line's HTML. The file's own lines were turned into HTML when the diff was
    /// prepared; what is left is the layout's own filler — an empty cell, or the <c>...</c> between
    /// hunks.
    /// </summary>
    private static string Html(string content) => string.IsNullOrEmpty(content) ? "&nbsp;" : content;

    private static string ParseContent(string content, bool isModelicaFile)
    {
        if (string.IsNullOrEmpty(content))
            return "&nbsp;";

        // Count leading spaces before any encoding/highlighting
        int leadingSpaces = 0;
        while (leadingSpaces < content.Length && content[leadingSpaces] == ' ')
        {
            leadingSpaces++;
        }

        // Apply Modelica syntax highlighting (includes HTML encoding) or plain encoding
        if (isModelicaFile)
        {
            content = ApplyModelicaSyntaxHighlighting(content);
        }
        else if (!content.Contains("<span"))
        {
            content = System.Web.HttpUtility.HtmlEncode(content);
        }

        // Preserve leading spaces as non-breaking spaces
        if (leadingSpaces > 0)
        {
            // After encoding/highlighting, the leading spaces may have been encoded or
            // wrapped in spans. Strip the original leading spaces worth of characters
            // from the processed content and prepend non-breaking spaces.
            var processedWithoutLeading = content.TrimStart('\u00A0', ' ');
            content = new string('\u00A0', leadingSpaces) + processedWithoutLeading;
        }

        return content;
    }

    private static readonly System.Text.RegularExpressions.Regex _tagRegex =
        new(@"<(KEYWORD|IDENT|NAME|TYPE|OPERATOR|NUMBER|STRING|COMMENT|FUNCTION|LINENUMBER)>(.*?)</\1>",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Turns the classifier's tags into the <c>code-*</c> spans the stylesheet colours, which is the
    /// same conversion <c>CodeViewer</c> makes — so the diff and the single-file view now follow one
    /// scheme (B178).
    ///
    /// <para>A line with no tags is HTML-encoded and shown plain. That covers a file that is not
    /// Modelica, the <c>...</c> that marks elided context, and the fallback in
    /// <see cref="DisplayLines"/>. It replaced a second highlighter — a keyword-list regex over the
    /// raw line, with its own palette — which coloured the same code differently from the pane
    /// beside it and ignored the user's chosen preset entirely.</para>
    /// </summary>
    internal static string ApplyModelicaSyntaxHighlighting(string line) =>
        _tagRegex.Replace(line, match =>
            $"<span class=\"code-{match.Groups[1].Value.ToLower()}\">"
            + System.Web.HttpUtility.HtmlEncode(match.Groups[2].Value)
            + "</span>");

    private class DiffLine
    {
        public string LineNumber { get; set; } = "";
        public string Content { get; set; } = "";
        public DiffLineType Type { get; set; }
        public string Prefix { get; set; } = "";
    }

    private enum DiffLineType
    {
        Unchanged,
        Added,
        Removed,
        Separator,
        Empty
    }

    internal sealed class DiffOp
    {
        public DiffOpType Type { get; set; }
        public int OriginalIndex { get; set; }
        public int ModifiedIndex { get; set; }
    }

    internal enum DiffOpType
    {
        Equal,
        Insert,
        Delete
    }
}
