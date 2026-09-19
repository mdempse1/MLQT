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

    private List<DiffLine> _unifiedLines = new();
    private List<DiffLine> _leftLines = new();
    private List<DiffLine> _rightLines = new();
    private int _addedCount = 0;
    private int _removedCount = 0;
    private bool _isModelicaFile = false;
    private int _maxLineNumberDigits = 3;
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

    protected override void OnParametersSet()
    {
        _isModelicaFile = !string.IsNullOrEmpty(FileName) &&
            (FileName.EndsWith(".mo", StringComparison.OrdinalIgnoreCase) ||
             FileName.EndsWith(".mos", StringComparison.OrdinalIgnoreCase));

        ComputeDiff();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ViewMode == DiffViewMode.SideBySide || ViewMode == DiffViewMode.SideBySideFull)
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
        ComputeDiff();
    }

    private string GetContainerStyle()
    {
        return !string.IsNullOrEmpty(Height) ? $"height: {Height};" : "";
    }

    private string GetDiffSummary()
    {
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

    private void ComputeDiff()
    {
        _unifiedLines.Clear();
        _leftLines.Clear();
        _rightLines.Clear();
        _addedCount = 0;
        _removedCount = 0;
        _errorMessage = null;

        var originalLines = (OriginalContent ?? "").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var modifiedLines = (ModifiedContent ?? "").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        int width = Math.Max(3, Math.Max(originalLines.Length, modifiedLines.Length));
        _maxLineNumberDigits = width.ToString().Length;

        // Check if the file is too large for the O(m*n) LCS algorithm
        long lcsCells = (long)(originalLines.Length + 1) * (modifiedLines.Length + 1);
        if (lcsCells > MaxLcsCells)
        {
            var maxLines = Math.Max(originalLines.Length, modifiedLines.Length);
            _errorMessage = $"File is too large for detailed diff comparison ({maxLines:N0} lines). " +
                "Try viewing a smaller section of the file, or compare using an external diff tool.";
            return;
        }

        try
        {
            // The diff is computed on the plain text and only then dressed up: comparing markup
            // would diff the colouring as well as the code, and a line that merely changed category
            // would read as a change.
            var diffResult = ComputeLcsDiff(originalLines, modifiedLines);

            var originalDisplay = DisplayLines(OriginalContent, originalLines, _isModelicaFile);
            var modifiedDisplay = DisplayLines(ModifiedContent, modifiedLines, _isModelicaFile);

            if (ViewMode == DiffViewMode.SideBySideFull)
            {
                ComputeFullSideBySide(originalDisplay, modifiedDisplay, diffResult);
            }
            else if (ViewMode == DiffViewMode.SideBySide)
            {
                ComputeSideBySideWithContext(originalDisplay, modifiedDisplay, diffResult);
            }
            else
            {
                ComputeUnifiedWithContext(originalDisplay, modifiedDisplay, diffResult);
            }
        }
        catch (OutOfMemoryException)
        {
            _unifiedLines.Clear();
            _leftLines.Clear();
            _rightLines.Clear();
            _errorMessage = $"Not enough memory to compute diff for this file " +
                $"({originalLines.Length:N0} / {modifiedLines.Length:N0} lines). " +
                "Try viewing a smaller section, or compare using an external diff tool.";
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

    private string ParseContent(string content)
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
        if (_isModelicaFile)
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
