using System.IO;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using DymolaInterface;
using OpenModelicaInterface;
using RevisionControl;
using ModelicaGraph;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace MLQT.Shared.Pages;

public partial class CodeReview : IAsyncDisposable
{
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ICodeReviewService CodeReviewService { get; set; } = null!;
    [Inject] private IBaselineStatusService BaselineStatus { get; set; } = null!;
    [Inject] private IStyleCheckingService StyleCheckingService { get; set; } = null!;
    [Inject] private ICustomDictionaryService CustomDictionaryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;
    [Inject] private DymolaCheckingService DymolaCheckingService { get; set; } = null!;
    [Inject] private OpenModelicaCheckingService OpenModelicaCheckingService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private AppSettings _settings = new();
    private bool _showAnnotations = true;
    private bool _showHighlighted = true;
    private List<string>? _highlightedCode = null;

    /// <summary>
    /// What the viewer is hiding, so a finding's line in the class can be turned into the line it is
    /// showing at. <see cref="SourceElision.None"/> whenever nothing is hidden, which is the common
    /// case for a model.
    /// </summary>
    private SourceElision _elision = SourceElision.None;

    private ModelNode? _currentModelNode = null;
    private int _modelsToCheck = 0;
    private int _modelsChecked = 0;
    private bool _checkProgressDialog = false;
    private readonly DialogOptions _dialogOptions = new() { FullWidth = true };
    private string _checkingModel = "";

    /// <summary>
    /// What the tool is doing before it starts counting classes - starting, opening the library -
    /// or null once it is checking them (B259).
    /// </summary>
    private string? _checkStatus;
    private string _checkingToolName = "";
    private bool _findingDetailsVisible = false;
    private LogMessage? _currentFinding = null;
    private string _searchString = "";
    private bool FindingsScopeAllModels = false;

    /// <summary>
    /// The rule the findings list is narrowed to, or null for all of them (B187). Held here rather
    /// than smuggled into the search box, which is the mistake the class scope made.
    /// </summary>
    private string? _ruleFilter;

    /// <summary>What the user is searching the <em>code</em> for, and where they are in it (B176).</summary>
    private string _codeSearch = "";

    /// <summary>
    /// The display lines carrying a match, in order. Recomputed when the term or the class changes,
    /// because it indexes into what is on screen.
    /// </summary>
    private List<int> _codeMatches = [];

    /// <summary>Which of <see cref="_codeMatches"/> the user is on, zero-based.</summary>
    private int _codeMatchIndex;

    /// <summary>
    /// Narrows the findings list to what this working copy has changed — new findings, plus standing
    /// debt in a file waiting to be committed. Off by default so nothing is hidden until asked for.
    /// </summary>
    private bool ShowChangesOnly = false;
    private CancellationTokenSource? _checkCancellationTokenSource;
    private IReadOnlyList<string>? _suggestions = null;
    private HashSet<string>? _misspelledWords = null;
    private DotNetObjectReference<CodeReview>? _spellCheckRef;
    private bool _contextMenuOpen = false;
    private double _contextMenuX;
    private double _contextMenuY;
    private string _contextWord = "";
    private string _customCorrection = "";

    // Scroll offsets (top, left) captured before an edit to the open file — a spelling correction, or
    // an __MLQT annotation from Ignore/Suppress — and restored after the file reloads and re-renders,
    // so the user stays where they were instead of jumping to the top-left. Null means "nothing
    // pending". See CaptureScrollForReloadAsync.
    private (double Top, double Left)? _pendingScroll;

    // Baseline for the scroll restore: the _highlightedCode list reference at the moment of capture
    // (i.e. the OLD content). The reload invalidates the render cache, so a brand-new list is
    // assigned when the edited content renders — we restore only on the render where
    // _highlightedCode differs from this baseline. This reliably distinguishes the new-content render from any render of the old
    // content that fires between capture and the re-render — without depending on catching the
    // transient loading spinner, which coalesces away for small/fast files.
    private List<string>? _scrollBaselineCode;

    // The misspelled word to scroll into view after a spelling finding is clicked in the findings list.
    // Consumed once the selected model's content has rendered (see OnAfterRenderAsync).
    private string? _pendingScrollWord;

    /// <summary>
    /// The line in the class a clicked finding is about, waiting for that class to be on screen.
    /// Mapped through <see cref="_elision"/> at the last moment rather than when it is armed,
    /// because the class may not be the one currently shown and the map is the new one's.
    /// </summary>
    private int? _pendingScrollLine;

    // Set when the correction context menu opens with a provisional position. On the next after-render
    // OnAfterRenderAsync re-measures the now-rendered menu and clamps it within the viewport, writing
    // the result back into _contextMenuX/_contextMenuY so .NET stays the source of truth (later
    // keystroke re-renders keep the clamped spot). Flag-gated to run once per open, avoiding jitter.
    private bool _repositionContextMenu;

    // Code rendering state
    private bool _isLoadingCode = false;
    private record RenderCacheKey(string ModelId, bool ShowAnnotations, bool ShowHighlighted, bool ExcludeClassDefs);

    /// <summary>The lines on screen and what was hidden to produce them — one without the other
    /// cannot answer which line of the class a displayed line is.</summary>
    internal record ShownClass(List<string> Lines, SourceElision Elision);

    private readonly Dictionary<RenderCacheKey, ShownClass> _renderCache = new();

    // Diff view state
    private bool _isDiffMode = false;
    private bool _isModelModified;
    private VcsFileStatus? _currentModelFileStatus;
    private string? _originalModelCode;
    private string? _modifiedModelCode;
    private bool _isLoadingDiff;
    private DiffViewMode _diffViewMode = DiffViewMode.Unified;
    private string? _currentRepositoryId;
    private string? _currentRelativeFilePath;
    private bool _isExcludedFromFormatting;
    private bool _togglingExclusion;

    //Tool logos
    const string _dymolaLogo = @"<svg width=""24"" height=""24"" viewBox=""0 0 24 24"">
        <g>
        <rect stroke-width=""1"" height=""11"" width=""20"" y=""7"" x=""2"" stroke=""currentColor"" fill=""#fff""/>
        <rect stroke-width=""1"" height=""7"" width=""14"" y=""3"" x=""5"" stroke=""currentColor"" fill=""#fff""/>
        <rect stroke-width=""1"" height=""7"" width=""14"" y=""14"" x=""5"" stroke=""currentColor"" fill=""#fff""/>
        </g>
        </svg>";
    const string _openModelicaLogo = @"<svg width=""24"" height=""24"" viewBox=""0 0 24 24"">
        <g>
        <text xml:space=""preserve"" text-anchor=""start"" font-size=""14"" stroke-width=""0"" y=""17"" x=""1"" stroke=""currentColor"" fill=""currentColor"">OM</text>
        </g>
        </svg>";

    private void CloseCheckProgressDialog()
    {
        _checkProgressDialog = false;
        _checkCancellationTokenSource?.Cancel();

        // Also call StopChecking directly on services to ensure immediate cancellation
        DymolaCheckingService.StopChecking();
        OpenModelicaCheckingService.StopChecking();
    }

    private void CloseFindingDialog() => _findingDetailsVisible = false;

    protected override void OnInitialized()
    {
        NavState.OnChangeModel += OnModelSelected;
        NavState.OnModelContentChanged += OnModelContentChanged;
        LibraryDataService.OnLibrariesChanged += OnLibrariesChanged;
        NavState.OnSaveSettings += OnSettingsSaved;
        NavState.OnDeferredAnalysisCompleted += OnDeferredAnalysisCompleted;
        CodeReviewService.OnLogMessagesChanged += OnLogMessagesChanged;
        BaselineStatus.OnChanged += OnBaselineStatusChanged;
        // Note: background style-checking findings are added to CodeReviewService by MainLayout (the
        // always-mounted layout), so they are captured even when this page isn't open. This page just
        // reacts to OnLogMessagesChanged to refresh.

        // Subscribe to model checking service events
        DymolaCheckingService.OnProgressChanged += OnCheckProgressChanged;
        DymolaCheckingService.OnModelChecked += OnModelChecked;
        DymolaCheckingService.OnCheckingComplete += OnCheckingComplete;
        OpenModelicaCheckingService.OnProgressChanged += OnCheckProgressChanged;
        OpenModelicaCheckingService.OnModelChecked += OnModelChecked;
        OpenModelicaCheckingService.OnCheckingComplete += OnCheckingComplete;

        base.OnInitialized();
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadSettings();
        await ApplySyntaxHighlightingStyles();

        // Pick up the current baselines and pending changes when the tab opens. The service keeps
        // itself current after that, from library loads and file activity.
        BaselineStatus.Refresh();
    }

    private async void OnBaselineStatusChanged() => await InvokeAsync(StateHasChanged);

    private async void OnDeferredAnalysisCompleted()
    {
        // Parser errors found by the deferred pass are surfaced by MainLayout; just re-render.
        await InvokeAsync(StateHasChanged);
    }

    private async Task RunStyleCheckingAsync()
    {
        try
        {
            await NavState.RerunStyleCheckingAsync();
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// The findings list changed. Raised on whichever thread changed it — a check's workers deliver
    /// findings directly now — so everything this touches has to happen on the dispatcher, not just
    /// the render: <c>RecomputeMisspelledWords</c> writes component state and used to run on the
    /// caller (B190).
    /// </summary>
    private async void OnLogMessagesChanged()
    {
        try
        {
            await InvokeAsync(() =>
            {
                RecomputeMisspelledWords();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The page was torn down between the change being announced and this reaching the
            // dispatcher. That window opened when findings started arriving on a worker's thread
            // rather than the dispatcher's, and the event can now outrun a tab switch. There is
            // nothing to render and nothing to report; the unsubscribe in Dispose is what normally
            // prevents this, and it cannot close the gap entirely.
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _spellCheckRef = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync("spellCheck.init", _spellCheckRef);
        }
        if (_currentModelNode == null && ((NavState.ModelID != null && NavState.ModelID.Length > 0) || NavState.SelectedModelIDs.Count > 0))
        {
            if (NavState.ModelID != null && NavState.ModelID.Length > 0)
                OnModelSelected();
            else
                NavState.ChangeModelID(NavState.SelectedModelIDs.First());
        }

        // Restore the scroll position captured before an edit to the open file, but only once the
        // *edited* content has rendered. The reload assigns a brand-new _highlightedCode list,
        // so a reference change from the captured baseline means the new content is now mounted.
        // Any render of the old content that fires between capture and the re-render keeps the same
        // reference and is correctly skipped. (Catching the transient loading spinner instead is
        // unreliable: for small files the spinner render coalesces away before OnAfterRender runs.)
        if (_pendingScroll is { } scroll && !_isLoadingCode && _highlightedCode is { Count: > 0 }
            && !ReferenceEquals(_highlightedCode, _scrollBaselineCode))
        {
            _pendingScroll = null;
            _scrollBaselineCode = null;
            try
            {
                await JSRuntime.InvokeVoidAsync("spellCheck.setScroll", ".code-viewer", scroll.Top, scroll.Left);
            }
            catch (Exception)
            {
                // View may have been torn down; ignore.
            }
        }

        // After clicking a spelling finding in the findings list, scroll the highlighted misspelled
        // word into view once the (possibly newly selected) model's content has rendered. The JS
        // helper retries across frames, so it tolerates the highlight spans appearing slightly
        // after the code lines (RecomputeMisspelledWords runs just after the render).
        if (_pendingScrollWord is { Length: > 0 } word && !_isLoadingCode && _highlightedCode is { Count: > 0 })
        {
            _pendingScrollWord = null;
            try
            {
                await JSRuntime.InvokeVoidAsync("spellCheck.scrollWordIntoView", ".code-viewer", word);
            }
            catch (Exception)
            {
                // View may have been torn down; ignore.
            }
        }

        // After clicking any other finding, scroll the line it is about into view. This is only
        // possible now that the viewer shows the class itself: a finding's line is counted against
        // the class's own source, and until B215 the page showed a reformatted copy of it that was
        // 17-24% longer, so the number pointed at whatever happened to be there (B182, B183).
        if (_pendingScrollLine is { } pending && !_isLoadingCode && _highlightedCode is { Count: > 0 })
        {
            _pendingScrollLine = null;
            var displayLine = _elision.ToDisplayLine(pending);
            if (displayLine is { } target)
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("spellCheck.scrollLineIntoView", ".code-viewer", target);
                }
                catch (Exception)
                {
                    // View may have been torn down; ignore.
                }
            }
        }

        // Reposition the correction context menu now that it has rendered and its real size is known.
        // The JS helper anchors it below the right-clicked word and clamps it within the viewport;
        // we write the clamped [left, top] back so .NET owns the final position (keystroke re-renders
        // then keep it put). Gated by the flag so it runs once per open and never loops.
        if (_repositionContextMenu && _contextMenuOpen)
        {
            _repositionContextMenu = false;
            try
            {
                var pos = await JSRuntime.InvokeAsync<double[]>("spellCheck.positionContextMenu", ".spell-context-menu", 4);
                if (pos is { Length: >= 2 })
                {
                    _contextMenuX = pos[0];
                    _contextMenuY = pos[1];
                    StateHasChanged();
                }
            }
            catch (Exception)
            {
                // Menu may have been closed before measuring; ignore.
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private async Task LoadSettings()
    {
        try
        {
            // Load each settings category
            _settings.SyntaxHighlighting = await SettingsService.GetAsync("SyntaxHighlighting", new SyntaxHighlightingSettings());
            _settings.Dymola = await SettingsService.GetAsync("Dymola", new DymolaSettings());
            _settings.OpenModelica = await SettingsService.GetAsync("OpenModelica", new OpenModelicaSettings());

            DymolaCheckingService.UpdateSettings(_settings.Dymola);            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }
    }

    private async void OnSettingsSaved()
    {
        await InvokeAsync(async () =>
        {
            await LoadSettings();
            await ApplySyntaxHighlightingStyles();
            StateHasChanged();
        });
    }

    protected void OnDispose()
    {
        NavState.OnChangeModel -= OnModelSelected;
        NavState.OnModelContentChanged -= OnModelContentChanged;
        NavState.OnSaveSettings -= OnSettingsSaved;
        LibraryDataService.OnLibrariesChanged -= OnLibrariesChanged;
        NavState.OnDeferredAnalysisCompleted -= OnDeferredAnalysisCompleted;
        CodeReviewService.OnLogMessagesChanged -= OnLogMessagesChanged;
        BaselineStatus.OnChanged -= OnBaselineStatusChanged;

        // Unsubscribe from model checking service events
        DymolaCheckingService.OnProgressChanged -= OnCheckProgressChanged;
        DymolaCheckingService.OnModelChecked -= OnModelChecked;
        DymolaCheckingService.OnCheckingComplete -= OnCheckingComplete;
        OpenModelicaCheckingService.OnProgressChanged -= OnCheckProgressChanged;
        OpenModelicaCheckingService.OnModelChecked -= OnModelChecked;
        OpenModelicaCheckingService.OnCheckingComplete -= OnCheckingComplete;

        _checkCancellationTokenSource?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        OnDispose();

        if (_spellCheckRef != null)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("spellCheck.dispose");
            }
            catch (Exception)
            {
                // JS runtime may already be torn down during app shutdown — ignore.
            }
            _spellCheckRef.Dispose();
            _spellCheckRef = null;
        }
    }

    /// <summary>
    /// Invoked from JavaScript when a highlighted misspelled word is right-clicked. Opens the
    /// correction context menu at the cursor with suggestions for the word.
    /// </summary>
    [JSInvokable]
    public async Task OnMisspelledWordRightClick(string word, double x, double y)
    {
        if (string.IsNullOrEmpty(word))
            return;

        _contextWord = word;
        _customCorrection = word;
        _contextMenuX = x;
        _contextMenuY = y;

        // Track a representative finding for this word so Add to Dictionary / Ignore can reuse it.
        _currentFinding = CodeReviewService.LogMessages.FirstOrDefault(m =>
            m.ModelName == NavState.ModelID && IsSpellingFinding(m) && ExtractMisspelledWord(m) == word);

        // Suggestions come from the language dictionaries, so they do not depend on the class
        // belonging to a repository — only recording a word does. Gating them on that left the menu
        // saying "No suggestions found" for every word whose class MLQT could not tie to a
        // repository, which is not the same statement at all. The repository is still used when
        // there is one, for its languages and its accepted words.
        var repository = RepositoryForCurrentFinding();
        _suggestions = StyleCheckingService
            .EnsureSpellChecker(repository?.LocalPath, repository?.StyleSettings?.SpellCheckLanguages)
            .Suggest(word)
            .ToList();
        _contextMenuOpen = true;
        // Position is provisional (the word's bottom-left from JS). Once the menu has rendered and its
        // real size is known, OnAfterRenderAsync re-measures and clamps it within the viewport.
        _repositionContextMenu = true;

        await InvokeAsync(StateHasChanged);
    }

    private void CloseContextMenu()
    {
        _contextMenuOpen = false;
        _suggestions = null;
        StateHasChanged();
    }

    private async Task OnCustomCorrectionKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await ApplyCorrection(_customCorrection);
    }

    private async Task ToggleAnnotations()
    {
        _showAnnotations = !_showAnnotations;
        OnModelSelected();

        // If in diff mode, force reload with new annotation settings
        if (_isDiffMode)
        {
            _originalModelCode = null;
            await LoadModelDiffAsync();
        }
    }

    /// <summary>
    /// Takes the current class out of formatting, or puts it back — by writing
    /// <c>__MLQT(format=false)</c> into its source rather than by adding its name to
    /// <c>FormattingExcludedModels</c> (B175).
    ///
    /// <para><b>Why the annotation.</b> The name list does not survive the class being renamed or
    /// moved: the entry stays behind naming nothing, the class comes back under the formatter, and
    /// the next save reorders code somebody had deliberately left alone. The annotation travels with
    /// the class, is committed with it, and is the mechanism the documentation already steers people
    /// to. Both are honoured everywhere (B39, B65), so this changes which one the button writes and
    /// nothing about what reads it.</para>
    ///
    /// <para><b>Re-including clears both.</b> A class excluded by an earlier MLQT is in the name
    /// list and has no annotation, so the button would otherwise be unable to undo its own past
    /// behaviour.</para>
    /// </summary>
    private async Task ToggleFormattingExclusionAsync()
    {
        if (_currentModelNode == null || string.IsNullOrEmpty(_currentRepositoryId) || _togglingExclusion)
            return;

        var repository = RepositoryService.GetRepository(_currentRepositoryId);
        if (repository?.StyleSettings == null)
            return;

        var target = ResolveClassSourceTarget(_currentModelNode.Id);
        if (target is null)
            return;

        var modelId = _currentModelNode.Id;
        _togglingExclusion = true;

        // Timed per step, because the first attempt at explaining why this button took ten
        // seconds was a guess. The log already carries the write and the reload, and they were
        // under a millisecond; what was missing was everything after them (B253).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var last = 0L;
        void Step(string what)
        {
            LoggingService.Debug("CodeReview",
                $"  exclusion {what}: {sw.ElapsedMilliseconds - last}ms (total {sw.ElapsedMilliseconds}ms)");
            last = sw.ElapsedMilliseconds;
        }

        try
        {
            if (_isExcludedFromFormatting)
            {
                if (!await WriteFormattingOptOutAsync(target, add: false))
                    return;
                Step("remove annotation");

                // The list entry too: a class excluded before B175, or one carrying both.
                repository.StyleSettings.FormattingExcludedModels.Remove(modelId);
                await RepositoryService.SaveRepositorySettingsAsync();
                Step("save settings");
            }
            else
            {
                // Reverting comes first. It discards the formatting the class has already had
                // applied, which is the point of the button — and doing it afterwards would discard
                // the annotation along with it.
                if (_isModelModified && !string.IsNullOrEmpty(_currentRelativeFilePath))
                {
                    await RepositoryService.RevertFilesAsync(_currentRepositoryId, [_currentRelativeFilePath]);
                    Step("revert");
                    await LibraryDataService.ReloadFileAsync(target.FilePath);
                    Step("reload after revert");
                    _currentModelNode = LibraryDataService.CombinedGraph.GetNode<ModelNode>(modelId);

                    // The reverted file is what the annotation has to be spliced into.
                    target = ResolveClassSourceTarget(modelId);
                    if (target is null)
                        return;
                }

                if (!await WriteFormattingOptOutAsync(target, add: true))
                    return;
                Step("write annotation");
            }

            // Re-fetched because saving the file reloads it, which replaces the node.
            var reloaded = LibraryDataService.GetModelById(modelId) ?? target.Node;
            _currentModelNode = reloaded;
            _isExcludedFromFormatting = FormattingExclusion.Excludes(reloaded, repository.StyleSettings);
            Step("re-read exclusion state");

            // Not CheckModelVcsStatus() directly: it asks the VCS for the whole working copy, and
            // the write above has just invalidated the cached answer — so on a library the size of
            // MSL it is a scan of thousands of files, and calling it here ran that on the UI thread.
            // The button took about ten seconds to come back on a 172-line file, of which the write
            // and the reload were under a millisecond (B253).
            //
            // OnModelSelected runs the same check in the background, as every other path on this
            // page does, and updates the toolbar when it finishes. The exclusion state is already
            // set above, so the button itself flips immediately.
            OnModelSelected();

            Step("re-render kicked off");

            Snackbar.Add(
                _isExcludedFromFormatting
                    ? "This class is now excluded from formatting, recorded in its own source so it survives a rename."
                    : "This class is back under the formatter.",
                MudBlazor.Severity.Success);
        }
        finally
        {
            _togglingExclusion = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Adds or removes the class's <c>format=false</c> directive and saves the file.</summary>
    private async Task<bool> WriteFormattingOptOutAsync(ClassSourceTarget target, bool add)
    {
        var fileContent = await ReadTargetFileAsync(target);
        if (fileContent is null)
            return false;

        var written = add
            ? MlqtSuppressionWriter.TryAddFormattingOptOutToFile(
                fileContent, target.ClassPath, out var newContent, out var error)
            : MlqtSuppressionWriter.TryRemoveFormattingOptOutFromFile(
                fileContent, target.ClassPath, out newContent, out error);

        if (!written)
        {
            Snackbar.Add($"Could not change the formatting exclusion: {error}", MudBlazor.Severity.Error);
            return false;
        }

        // Nothing to write when the class already said what was asked of it — which is the ordinary
        // case for re-including a class that was only ever in the name list.
        return string.Equals(newContent, fileContent, StringComparison.Ordinal)
            || await SaveAnnotatedFileAsync(target, newContent);
    }

    #region Diff View Methods

    /// <summary>
    /// Checks if the current model's file has uncommitted VCS changes.
    /// Sets _isModelModified, _currentModelFileStatus, _currentRepositoryId,
    /// and _currentRelativeFilePath for use by the diff view.
    /// </summary>
    private void CheckModelVcsStatus()
    {
        _isModelModified = false;
        _currentModelFileStatus = null;
        _currentRepositoryId = null;
        _currentRelativeFilePath = null;
        _isExcludedFromFormatting = false;

        if (_currentModelNode == null)
            return;

        // Find the file containing this model
        var fileId = _currentModelNode.ContainingFileId;
        if (string.IsNullOrEmpty(fileId))
            return;

        var fileNode = LibraryDataService.CombinedGraph.GetNode<FileNode>(fileId);
        if (fileNode == null)
            return;

        // Find the library and repository for this model. GetOwningLibrary, not a search of every
        // library's ModelIds: a class checked out as source and also shipped in a tool's library
        // folder is claimed by both entries, and the first of them is whichever load happened to
        // finish first. Picking the vendor's read-only copy left this deciding the user's own
        // class was not under version control, and disabling all three diff views for it.
        var library = LibraryDataService.GetOwningLibrary(_currentModelNode.Id);
        if (library == null || string.IsNullOrEmpty(library.RepositoryId))
            return;

        var repository = RepositoryService.GetRepository(library.RepositoryId);
        if (repository is not { VcsType: not RepositoryVcsType.Local })
            return;

        _currentRepositoryId = repository.Id;
        // FormattingExclusion, not the name list alone: a class carrying __MLQT(format=false)
        // is excluded and the toggle has to show it that way, or the button offers to exclude a
        // class that already is.
        _isExcludedFromFormatting = FormattingExclusion.Excludes(_currentModelNode, repository.StyleSettings);

        // Compute the path relative to VcsRootPath — this matches what GetWorkingCopyChanges
        // returns (paths are always relative to VcsRootPath, not LocalPath).
        // Normalize to forward slashes to match LibGit2Sharp's FilePath format.
        var absolutePath = fileNode.FilePath;
        if (absolutePath.StartsWith(repository.VcsRootPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentRelativeFilePath = absolutePath
                .Substring(repository.VcsRootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
        }

        if (string.IsNullOrEmpty(_currentRelativeFilePath))
            return;

        // Check working copy changes for this file. Normalize change.Path separators
        // to forward slashes to match both Git (always '/') and SVN (OS-native).
        var changes = RepositoryService.GetWorkingCopyChanges(repository.Id);
        var matchingChange = changes?.FirstOrDefault(c =>
            c.Path.Replace('\\', '/').Equals(_currentRelativeFilePath, StringComparison.OrdinalIgnoreCase));

        if (matchingChange != null)
        {
            _isModelModified = true;
            _currentModelFileStatus = matchingChange.Status;
        }
    }

    /// <summary>
    /// Handles the diff mode toggle button click.
    /// Loads the diff on first toggle to avoid unnecessary work.
    /// </summary>
    private async Task SetViewMode(bool useDiffView, DiffViewMode mode)
    {
        _isDiffMode = useDiffView;        
        if (_isDiffMode && _originalModelCode == null)
        {
            await LoadModelDiffAsync();
        }
        if (_isDiffMode)
            _diffViewMode = mode;
        StateHasChanged();
    }

    /// <summary>
    /// Loads the diff for the current model by fetching the file content at HEAD
    /// and using the raw source code for both versions. The DiffViewer component
    /// handles its own syntax highlighting on raw Modelica text.
    /// </summary>
    private async Task LoadModelDiffAsync()
    {
        if (_currentModelNode == null || !_isModelModified ||
            string.IsNullOrEmpty(_currentRepositoryId) ||
            string.IsNullOrEmpty(_currentRelativeFilePath))
            return;

        _isLoadingDiff = true;
        StateHasChanged();

        try
        {
            await Task.Run(() =>
            {
                // Get the file content at HEAD
                var headFileContent = RepositoryService.GetFileContentAtRevision(
                    _currentRepositoryId, _currentRelativeFilePath, "HEAD");

                // Extract the raw original model code from HEAD
                if (!string.IsNullOrEmpty(headFileContent))
                {
                    try
                    {
                        // Parse the HEAD file and find the specific model by name
                        var headModels = ModelicaParserHelper.ExtractModels(headFileContent);
                        var matchingModel = headModels.FirstOrDefault(m =>
                            m.Name == _currentModelNode.Definition.Name);

                        if (matchingModel != null)
                        {
                            var prefix = matchingModel.ElementPrefix;
                            _originalModelCode = string.IsNullOrEmpty(prefix)
                                ? matchingModel.SourceCode
                                : prefix + " " + matchingModel.SourceCode;
                        }
                        else
                        {
                            _originalModelCode = headFileContent;
                        }
                    }
                    catch
                    {
                        // HEAD version may not parse correctly
                        _originalModelCode = headFileContent;
                    }
                }
                else
                {
                    // File doesn't exist at HEAD (new file)
                    _originalModelCode = "";
                }

                _modifiedModelCode = WorkingCopyText(_currentModelNode, LibraryDataService.CombinedGraph);
            });
        }
        catch (Exception ex)
        {
            _originalModelCode = $"Error loading HEAD version: {ex.Message}";
            _modifiedModelCode = _currentModelNode?.Definition.ModelicaCode ?? "";
        }
        finally
        {
            _isLoadingDiff = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    #endregion

    private async void OnModelSelected()
    {
        if (string.IsNullOrEmpty(NavState.ModelID))
            return;

        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        _currentModelNode = LibraryDataService.GetModelById(NavState.ModelID);
        _originalModelCode = null;
        _modifiedModelCode = null;

        if (_currentModelNode == null)
            return;

        var selectedModelId = NavState.ModelID;
        bool excludeClassDefs = _currentModelNode.ClassType == "package";
        var cacheKey = new RenderCacheKey(selectedModelId, _showAnnotations, _showHighlighted, excludeClassDefs);

        // A model with parser errors needs no special case any more. ModelicaTokenClassifier colours
        // what the lexer recovered and copies the rest through, so a class that does not parse is
        // shown exactly and in colour, where it used to be shown in no colour at all.

        if (_renderCache.TryGetValue(cacheKey, out var cachedCode))
        {
            // Cache hit — set code and render immediately BEFORE any await.
            // Any await would yield to the Blazor sync context which is blocked
            // by MainLayout/LibraryBrowser re-rendering 27K tree nodes.
            _highlightedCode = cachedCode.Lines;
            _elision = cachedCode.Elision;
            _isLoadingCode = false;
            RecomputeCodeMatches();

            LoggingService.Debug("CodeReview", $"Cache hit for {selectedModelId}");

            // Update UI synchronously — no await means no sync context queue delay
            OnFindingsScopeChanged(FindingsScopeAllModels);
            StateHasChanged();

            // Run VCS check in background — will update UI when done
            _ = Task.Run(() => CheckModelVcsStatus()).ContinueWith(async _ =>
            {
                LoggingService.Debug("CodeReview",
                    $"  VCS ContinueWith fired at {totalSw.ElapsedMilliseconds}ms");

                if (NavState.ModelID != selectedModelId)
                    return;

                var invokeAsyncSw = System.Diagnostics.Stopwatch.StartNew();
                await InvokeAsync(async () =>
                {
                    LoggingService.Debug("CodeReview",
                        $"  VCS InvokeAsync started after {invokeAsyncSw.ElapsedMilliseconds}ms wait");

                    if (!_isModelModified)
                        _isDiffMode = false;
                    await SetViewMode(_isDiffMode, _diffViewMode);
                    StateHasChanged();
                });

                LoggingService.Debug("CodeReview",
                    $"  VCS (background): Total: {totalSw.ElapsedMilliseconds}ms");
            }, TaskScheduler.Default);
        }
        else
        {
            var codeLength = _currentModelNode.Definition.ModelicaCode?.Length ?? 0;
            var hadParsedCode = _currentModelNode.Definition.ParsedCode != null;
            LoggingService.Debug("CodeReview",
                $"Rendering {selectedModelId}: {codeLength} chars, ParsedCode cached={hadParsedCode}, classType={_currentModelNode.ClassType}");

            _isLoadingCode = true;
            StateHasChanged();

            var modelNode = _currentModelNode;
            var showHighlighted = _showHighlighted;
            var showAnnotations = _showAnnotations;
            var graph = LibraryDataService.CombinedGraph;

            // Render parse/format on a background thread. Display the code as soon as this
            // completes — crucially, do NOT gate code display on the VCS status check.
            // CheckModelVcsStatus calls GetWorkingCopyChanges, which shells out to the
            // git/svn CLI and can be slow or block; coupling the two via Task.WhenAll meant
            // a stalled VCS call left the loading spinner spinning forever even though the
            // rendered code was ready in milliseconds. The VCS check now runs independently
            // (mirroring the cache-hit path) and only updates the modified/diff indicator.
            // A class big enough that a stall would be noticed is painted from the lexer first, so
            // it appears at once instead of after however long the parse takes — which for a class
            // carrying a run of comments inside an equation section is quadratic, and was measured
            // at 69 seconds for 4,000 of them (B185, and B235 for the cause). The tree's colouring
            // replaces it when the parse lands.
            if ((modelNode.Definition.ModelicaCode?.Length ?? 0) > PaintBeforeParsingAbove)
            {
                _ = Task.Run(() => Show(modelNode, graph, showHighlighted, showAnnotations,
                                        excludeClassDefs, parse: false))
                    .ContinueWith(async quick =>
                    {
                        // Dropped if the user has moved on, or if the parse beat it here.
                        if (NavState.ModelID != selectedModelId || !_isLoadingCode || !quick.IsCompletedSuccessfully)
                            return;

                        await InvokeAsync(() =>
                        {
                            if (NavState.ModelID != selectedModelId || !_isLoadingCode)
                                return;

                            _highlightedCode = quick.Result.Lines;
                            _elision = quick.Result.Elision;

                            // Clearing this is the point of the exercise: while it is set the page
                            // shows a spinner in place of the viewer, so painting the lines without
                            // it would change nothing the user can see.
                            _isLoadingCode = false;

                            LoggingService.Debug("CodeReview",
                                $"  First paint from the lexer: {quick.Result.Lines.Count} lines");
                            StateHasChanged();
                        });
                    }, TaskScheduler.Default);
            }

            var renderTask = Task.Run(() => Show(modelNode, graph, showHighlighted, showAnnotations, excludeClassDefs));

            _ = renderTask.ContinueWith(async _ =>
            {
                LoggingService.Debug("CodeReview",
                    $"  ContinueWith fired at {totalSw.ElapsedMilliseconds}ms");

                if (NavState.ModelID != selectedModelId)
                    return;

                try
                {
                    var shown = renderTask.Result;

                    LoggingService.Debug("CodeReview",
                        $"  Lines: {shown.Lines.Count}, hidden ranges: {shown.Elision.Ranges.Count}");

                    var invokeAsyncSw = System.Diagnostics.Stopwatch.StartNew();
                    await InvokeAsync(() =>
                    {
                        LoggingService.Debug("CodeReview",
                            $"  InvokeAsync started after {invokeAsyncSw.ElapsedMilliseconds}ms wait");

                        _highlightedCode = shown.Lines;
                        _elision = shown.Elision;
                        _isLoadingCode = false;
                        RecomputeCodeMatches();

                        // Store in cache for future clicks
                        _renderCache[cacheKey] = shown;

                        OnFindingsScopeChanged(FindingsScopeAllModels);
                        StateHasChanged();

                        LoggingService.Debug("CodeReview",
                            $"  Total: {totalSw.ElapsedMilliseconds}ms");
                    });
                }
                catch (Exception ex)
                {
                    // A render fault must never strand the loading spinner.
                    LoggingService.Error("CodeReview",
                        $"Failed to render {selectedModelId}", ex);
                    await InvokeAsync(() =>
                    {
                        if (NavState.ModelID == selectedModelId)
                        {
                            _isLoadingCode = false;
                            StateHasChanged();
                        }
                    });
                }
            }, TaskScheduler.Default);

            // VCS status check runs independently — never blocks code display. When it
            // finishes it refreshes the modified/diff indicator for the current model.
            _ = Task.Run(() => CheckModelVcsStatus()).ContinueWith(async _ =>
            {
                if (NavState.ModelID != selectedModelId)
                    return;

                await InvokeAsync(async () =>
                {
                    if (!_isModelModified)
                        _isDiffMode = false;
                    await SetViewMode(_isDiffMode, _diffViewMode);
                    StateHasChanged();
                });
            }, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// The working-copy side of the class diff: the class as it is on disk now, to compare against
    /// the same class at HEAD.
    ///
    /// <para>This used to be <c>Definition.ModelicaCode</c> directly, which is the same text for
    /// most classes and <b>a different document</b> for two populations: a package whose inline
    /// standalone children the trimmer removed, and any class the formatter rewrote since it was
    /// read. For those, the diff compared a rewrite against the file and reported changes the user
    /// had not made — a trimmed package showed every standalone child as deleted (B217). Both sides
    /// now come from the file.</para>
    /// </summary>
    internal static string WorkingCopyText(ModelNode model, DirectedGraph graph)
    {
        var code = ClassSource.For(model, graph);
        return string.IsNullOrEmpty(model.ElementPrefix) ? code : model.ElementPrefix + " " + code;
    }

    /// <summary>
    /// The size of a class above which it is painted from the lexer first and the parse is allowed
    /// to catch up (B185).
    ///
    /// <para><b>Measured, and it is not what the backlog guessed.</b> Neither the highlighting nor
    /// the reformat is what made a large class take minutes — B215 removed the reformat, lexing is
    /// 11–79 ms and classifying 20–148 ms at every size tried. It is the <b>parse</b>, and the
    /// trigger is a specific shape rather than size: a run of comment lines inside an
    /// <c>equation</c> section is quadratic. 500 of them parse in 1.5 s, 1,000 in 4.4 s, 2,000 in
    /// 17 s and 4,000 in 69 s, at only 323 KB — four times the work for twice the text, which
    /// extrapolates to the five minutes reported. The input is legal Modelica and parses without a
    /// single error, so this is the parser's prediction rather than its error recovery, and it costs
    /// the checker and the CLI as much as it costs this page. That is <b>B235</b>, and it is the
    /// real fix.</para>
    ///
    /// <para>This threshold is therefore not a prediction of slowness and must not be read as one:
    /// 4,000 <em>annotated</em> declarations are 554 KB and parse in 367 ms, while the 69-second
    /// case is a third of that size. It is a judgement about when a stall would be <em>noticed</em>.
    /// Below it the parse is over before anyone could see a spinner; above it the lexer paints the
    /// class at once and the tree's colouring — which differs only in telling a type or a call from
    /// a plain identifier — arrives when it arrives. 64 KB leaves all but 52 classes in the Modelica
    /// Standard Library and 13 in Buildings on the direct path.</para>
    /// </summary>
    internal const int PaintBeforeParsingAbove = 64 * 1024;

    /// <summary>
    /// The class as the viewer shows it: <b>the user's own text</b>, coloured in place, with
    /// whatever is hidden taken out by whole lines and a map back to where those lines were.
    ///
    /// <para>This replaced a pass through <c>ModelicaRenderer</c>. The renderer rebuilds the text in
    /// order to colour it, so for a repository that has not accepted MLQT's formatting roughly 95%
    /// of what was on screen was not where the user's editor puts it, and the document was 17–24%
    /// longer than their file — which is why a finding's line number never matched it (B182). The
    /// colouring never needed the rewrite: it comes from the parse tree, and
    /// <see cref="ModelicaTokenClassifier"/> reads the same tree without touching the text.</para>
    ///
    /// <para>With <paramref name="parse"/> false the categories come from the token stream alone:
    /// the text is identical and only the tree's knowledge is missing, so an identifier is not yet
    /// known to be a type or a call and nothing can be hidden. That is the first paint of a class
    /// big enough for the parse to be worth not waiting for.</para>
    ///
    /// <para>Static, and everything it needs is passed in, because it runs on a background thread
    /// and must not read component state that the UI thread is changing underneath it.</para>
    /// </summary>
    internal static ShownClass Show(
        ModelNode model, DirectedGraph graph, bool showHighlighted, bool showAnnotations,
        bool hideClassDefinitions, bool parse = true)
    {
        // The stored text while it is still the file's, otherwise the file sliced again — for a
        // package whose inline children the trimmer removed, or a class the formatter rewrote.
        var source = ClassSource.For(model, graph);

        modelicaParser.Stored_definitionContext? tree = null;
        BufferedTokenStream stream;
        if (parse)
            (tree, stream) = ModelicaParserHelper.ParseWithTokens(source);
        else
            stream = ModelicaTokenClassifier.TokensOnly(source);

        // Annotations come out of the text before it is coloured, because the ones that matter share
        // a line with code and cannot be taken out by dropping whole lines (B233). Line numbers
        // survive it, so the elision below still maps a finding to where it lives in the file — what
        // ran across lines is joined onto its first and the rest come back as lines to drop.
        //
        // The cost is one more parse when annotations are hidden. Colouring is driven by the tree,
        // and the tree has to describe the text on screen or the two disagree about where a token
        // begins — which is the whole of what ModelicaTokenClassifier guarantees.
        var annotationElision = SourceElision.None;
        if (!showAnnotations && tree is not null)
        {
            (source, annotationElision) = ElisionFinder.WithoutAnnotations(tree, source);
            (tree, stream) = ModelicaParserHelper.ParseWithTokens(source);
        }

        var lines = showHighlighted
            ? ModelicaTokenClassifier.Highlight(tree, stream, source)
            : ModelicaTokenClassifier.Plain(source);

        // Both hiding operations are the same one, and a package that hides its nested classes has
        // already hidden the annotations inside them — Merge is what keeps those from colliding.
        var elision = SourceElision.Merge(
            hideClassDefinitions ? ElisionFinder.NestedClasses(tree, source, ClassMarker(showHighlighted)) : null,
            annotationElision);

        var display = elision.Apply(lines);

        // The class slice excludes `replaceable` / `redeclare`, which sit before it in the file.
        PrependElementPrefix(display, model.ElementPrefix, showHighlighted);

        return new ShownClass(display, elision);
    }

    /// <summary>
    /// What stands in for a hidden nested class: its declaration, so the package still reads as a
    /// list of what it contains rather than as a hole.
    /// </summary>
    private static Func<string, string?> ClassMarker(bool showHighlighted) => name => showHighlighted
        ? $"  <COMMENT>// {name} …</COMMENT>"
        : $"  // {name} …";

    // Nothing stands in for a hidden annotation any more. There used to be a `// annotation …`
    // marker, so the reader could see that something was hidden rather than silently reading a
    // class missing parts — but it cost a line per annotation and was noisier than the thing it
    // hid, which is what the user said when asking for B233. The toolbar's Bookmark button is
    // filled while annotations are hidden, and that is the signal.
    //
    // A hidden nested class still leaves one, and that is not the same decision: it carries the
    // class's *name*, so the package still reads as a list of what it contains.

    /// <summary>
    /// Formats element prefix keywords (e.g., "redeclare", "inner replaceable") as a
    /// syntax-highlighted line that can be prepended to the rendered code output.
    /// </summary>
    private static string FormatElementPrefixLine(string elementPrefix, bool showHighlighted)
    {
        if (!showHighlighted)
            return elementPrefix;

        var words = elementPrefix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => $"<KEYWORD>{w}</KEYWORD>"));
    }

    /// <summary>
    /// Prepends the element prefix to the class definition line in the rendered code,
    /// skipping any leading "within" clause so the prefix appears on the correct line.
    /// </summary>
    private static void PrependElementPrefix(List<string> code, string elementPrefix, bool showHighlighted)
    {
        if (string.IsNullOrEmpty(elementPrefix) || code.Count == 0)
            return;

        var prefixText = FormatElementPrefixLine(elementPrefix, showHighlighted);
        for (int i = 0; i < code.Count; i++)
        {
            var stripped = Regex.Replace(code[i], "<[^>]+>", "").TrimStart();
            if (!stripped.StartsWith("within"))
            {
                code[i] = prefixText + " " + code[i];
                return;
            }
        }
        // Fallback: prepend to first line if no non-within line found
        code[0] = prefixText + " " + code[0];
    }

    #region Model Checking Service Event Handlers

    private async void OnCheckProgressChanged(ModelCheckProgress progress)
    {
        await InvokeAsync(() =>
        {
            _modelsToCheck = progress.TotalModels;
            _modelsChecked = progress.ModelsChecked;
            _checkingModel = progress.CurrentModel;
            _checkStatus = progress.Status;
            StateHasChanged();
        });
    }

    private async void OnModelChecked(ModelCheckResult result)
    {
        await InvokeAsync(() =>
        {
            // Kept whatever the outcome, so the dialog at the end can report what the tool said
            // about every class rather than only the ones that failed (B170).
            _checkResults.Add(result);

            if (!result.Success)
            {
                var finding = new LogMessage(
                    result.ModelId,
                    "Error",
                    0,
                    result.Summary ?? "Check Failed",
                    result.ErrorMessage ?? "");
                CodeReviewService.AddLogMessage(finding);
            }
            StateHasChanged();
        });
    }

    private async void OnCheckingComplete(ModelCheckProgress progress)
    {
        await InvokeAsync(() =>
        {
            _checkProgressDialog = false;
            _checkCancellationTokenSource?.Dispose();
            _checkCancellationTokenSource = null;
            _checkWasCancelled = progress.WasCancelled;

            // Say what happened, always. A check that passed used to produce nothing at all: no
            // window, no dialog, no finding — so the only evidence an OpenModelica check had run was
            // that the button had been pressed, and the only evidence for Dymola was that Dymola's
            // own window appeared. That also made the answer depend on a vendor window being
            // visible, which is not something MLQT controls (B170).
            _checkResultDialog = true;
            StateHasChanged();
        });
    }

    /// <summary>What the tool said about each class, for the dialog that reports it.</summary>
    private readonly List<ModelCheckResult> _checkResults = new();

    private bool _checkResultDialog;
    private bool _checkWasCancelled;

    private IEnumerable<ModelCheckResult> FailedChecks => _checkResults.Where(r => !r.Success);

    /// <summary>
    /// The results worth listing under the headline: every failure, and any clean check the tool
    /// still had something to say about.
    ///
    /// <para>A package of two hundred classes that all passed silently has nothing to list, and the
    /// sentence above is the whole answer. One that passed with warnings has the warnings, which is
    /// what <c>checkModel</c> returning true hides.</para>
    /// </summary>
    private IEnumerable<ModelCheckResult> ReportedChecks =>
        _checkResults.Where(r => !r.Success || !string.IsNullOrWhiteSpace(r.Log));

    private int PassedCheckCount => _checkResults.Count(r => r.Success);

    /// <summary>
    /// The progress dialog's title: the tool, and the count once there is one.
    /// </summary>
    /// <remarks>
    /// Both counts are zero until the tool has started and the library is open, so the old
    /// wording announced "0 checked out of 0" for the several seconds a user is most likely to
    /// be wondering whether anything is happening. A single class is not counted either: one of
    /// one is a progress bar with nothing to say, and the class is named in the dialog anyway.
    /// </remarks>
    internal static string CheckProgressTitle(string tool, int checkedCount, int total) =>
        total <= 1 ? $"{tool} check" : $"{tool} check - {checkedCount} of {total} classes checked";

    /// <summary>
    /// The headline: what was checked and how it went, in one sentence a user can act on.
    /// </summary>
    internal static string CheckOutcomeSummary(string tool, int passed, int failed, bool cancelled)
    {
        static string Classes(int n) => n == 1 ? "1 class" : $"{n} classes";

        var checkedCount = passed + failed;
        if (cancelled)
            return $"{tool} check stopped after {Classes(checkedCount)}.";
        if (checkedCount == 0)
            return $"{tool} checked nothing.";
        if (failed == 0)
            return $"{tool} checked {Classes(checkedCount)} with no problems reported.";
        if (passed == 0)
            return $"{tool} reported a problem with {(failed == 1 ? "it" : $"all {failed}")}.";
        return $"{tool} reported problems with {failed} of {Classes(checkedCount)}.";
    }

    #endregion

    private void CheckInDymola()
    {
        if (_currentModelNode == null)
            return;

        _checkingToolName = DymolaCheckingService.ToolName;
        _checkCancellationTokenSource = new CancellationTokenSource();
        _checkResults.Clear();
        _checkWasCancelled = false;

        // Shown for one class as well as for a package. Nothing happens for several seconds
        // after the button is pressed - the tool has to start and the library has to be opened -
        // and for a single class there was nothing at all on screen during it. That is worse for
        // OpenModelica, which has no window of its own to appear, so the only evidence the check
        // was running was that the button had been pressed (B259).
        _modelsToCheck = 0;
        _modelsChecked = 0;
        _checkingModel = "";
        _checkStatus = null;
        _checkProgressDialog = true;

        // StartCheckingAsync runs on background thread and returns immediately
        _ = DymolaCheckingService.StartCheckingAsync(
            _currentModelNode,
            LibraryDataService.CombinedGraph,
            _checkCancellationTokenSource.Token);
    }

    private void CheckInOpenModelica()
    {
        if (_currentModelNode == null)
            return;

        _checkingToolName = OpenModelicaCheckingService.ToolName;
        _checkCancellationTokenSource = new CancellationTokenSource();
        _checkResults.Clear();
        _checkWasCancelled = false;

        // Shown for one class as well as for a package. Nothing happens for several seconds
        // after the button is pressed - the tool has to start and the library has to be opened -
        // and for a single class there was nothing at all on screen during it. That is worse for
        // OpenModelica, which has no window of its own to appear, so the only evidence the check
        // was running was that the button had been pressed (B259).
        _modelsToCheck = 0;
        _modelsChecked = 0;
        _checkingModel = "";
        _checkStatus = null;
        _checkProgressDialog = true;

        // StartCheckingAsync runs on background thread and returns immediately
        _ = OpenModelicaCheckingService.StartCheckingAsync(
            _currentModelNode,
            LibraryDataService.CombinedGraph,
            _checkCancellationTokenSource.Token);
    }

    /// <summary>
    /// The findings in a stable order: by class, then by line within it, then by rule. The check
    /// runs in parallel and its list comes back in completion order, so without this the same
    /// library reviewed twice showed the same findings in two different orders and a user could not
    /// pick up where they left off (B183).
    /// </summary>
    private IEnumerable<LogMessage> OrderedFindings =>
        CodeReviewService.LogMessages
            .OrderBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.LineNumber)
            .ThenBy(m => m.RuleId, StringComparer.Ordinal);

    private void RowClickEvent(TableRowClickEventArgs<LogMessage> args)
    {
        _currentFinding = args.Item;
        if (_currentFinding == null) return;

        _suggestions = null;

        if (IsSpellingFinding(_currentFinding))
        {
            // Arm a scroll so the misspelled word is brought into view once the model renders.
            _pendingScrollWord = ExtractMisspelledWord(_currentFinding);
        }
        else if (!string.IsNullOrEmpty(_currentFinding.Details))
        {
            _findingDetailsVisible = true;
        }

        // Every other finding scrolls to the line it names. A finding that carries no line (a
        // whole-class one, say) leaves this alone rather than scrolling to the top, because the
        // class declaration is already where the viewer opens.
        if (_pendingScrollWord is null && _currentFinding.LineNumber > 0)
            _pendingScrollLine = _currentFinding.LineNumber;

        NavState.ChangeModelID(_currentFinding.ModelName);
    }

    private async Task ApplySyntaxHighlightingStyles()
    {
        var css = $@"
.code-viewer {{
background-color: {_settings.SyntaxHighlighting.BackgroundColor} !important;
color: {_settings.SyntaxHighlighting.TextColor} !important;
border: 1px solid {_settings.SyntaxHighlighting.BorderColor} !important;
border-radius: 4px;
overflow: auto;
font-family: 'Consolas', 'Monaco', 'Courier New', monospace !important;
font-size: var(--mud-typography-body1-size) !important;
line-height: 1.5;
padding: 4px;
}}

.code-viewer-content {{
display: flex;
flex-direction: column;
}}

.code-line {{
white-space: pre;
font-family: inherit;
}}

.code-keyword {{
color: {_settings.SyntaxHighlighting.KeywordColor} !important;
font-weight: 600;
}}

.code-type {{
color: {_settings.SyntaxHighlighting.TypeColor} !important;
}}

.code-ident {{
color: {_settings.SyntaxHighlighting.IdentColor} !important;
}}

.code-name {{
color: {_settings.SyntaxHighlighting.NameColor} !important;
}}

.code-function {{
color: {_settings.SyntaxHighlighting.FunctionColor} !important;
}}

.code-operator {{
color: {_settings.SyntaxHighlighting.OperatorColor} !important;
}}

.code-number {{
color: {_settings.SyntaxHighlighting.NumberColor} !important;
}}

.code-string {{
color: {_settings.SyntaxHighlighting.StringColor} !important;
}}

.code-comment {{
color: {_settings.SyntaxHighlighting.CommentColor} !important;
font-style: italic;
}}

.code-linenumber {{
color: {_settings.SyntaxHighlighting.LineNumberColor} !important;
}}

.code-misspell {{
text-decoration: underline wavy #d32f2f;
text-decoration-skip-ink: none;
cursor: context-menu;
}}
";

        await JSRuntime.InvokeVoidAsync("eval", $@"
var styleId = 'dynamic-syntax-highlighting';
var existingStyle = document.getElementById(styleId);
if (existingStyle) {{
existingStyle.remove();
}}
var style = document.createElement('style');
style.id = styleId;
style.textContent = `{css}`;
document.head.appendChild(style);
");
    }

    private void OnModelContentChanged(IReadOnlyCollection<string> modelIds)
    {
        // Remove stale cache entries so the next ChangeModelID call re-renders from fresh content
        foreach (var id in modelIds)
        {
            var keysToRemove = _renderCache.Keys.Where(k => k.ModelId == id).ToList();
            foreach (var key in keysToRemove)
                _renderCache.Remove(key);
        }
    }

    /// <summary>
    /// The set of loaded libraries changed (a library was added or removed, or the project was
    /// switched). The render cache is keyed on model id alone, so an id that exists in both the old
    /// and the new set would otherwise render from the previous library's source — drop all of it.
    ///
    /// This page stays mounted across a project switch, so it cannot rely on being recreated. Finding
    /// messages are cleared by MainLayout's project-changed handler, and parser errors are surfaced
    /// there too — the always-mounted layout is where anything that must happen regardless of the
    /// open tab belongs.
    /// </summary>
    private async void OnLibrariesChanged()
    {
        _renderCache.Clear();
        await InvokeAsync(StateHasChanged);
    }

    private string MakeShortName(string name)
    {
        var idx = name.IndexOf(".") + 1;
        return name.Length <= 40 || name.IndexOf(".") == -1 || name.Count(c => c == '.') <= 1 ?
                name :
                name.Substring(0, name.IndexOf(".", idx)) + "..." + name.Substring(name.LastIndexOf("."));
    }

    private bool FilterFunc1(LogMessage element)
    {
        // Hide accepted debt first: the point of the toggle is that the search box then works over
        // what is left rather than over the whole ledger.
        if (ShowChangesOnly && !BaselineStatus.Snapshot.IsChangedFromBaseline(element))
            return false;

        return Matches(
            element,
            _searchString,
            onlyModelId: FindingsScopeAllModels && NavState.ModelID.Length > 0 ? NavState.ModelID : null,
            ruleId: _ruleFilter);
    }

    /// <summary>
    /// Whether a finding survives the three things the user can narrow by: the class, the rule, and
    /// the words in the search box.
    ///
    /// <para><b>The terms are AND, not OR</b> (B187). Typing two things into one box means "both",
    /// which is what anyone doing it expects and what makes a second word useful — under OR each
    /// word could only ever widen the result, so the box got less precise the more you told it. Each
    /// term still matches across any field, so a partial class name and a keyword work together.</para>
    ///
    /// <para><b>The class scope is a parameter, not a word in the search string.</b> It used to be
    /// appended to it (<c>_searchString + " " + NavState.ModelID</c>) and then stripped back out
    /// inside the filter, which is why AND could not simply be swapped in: with the scope smuggled
    /// through as a term, requiring every term to match would have excluded every finding whose text
    /// did not also contain the class name — which is all of them.</para>
    /// </summary>
    internal static bool Matches(LogMessage finding, string? search, string? onlyModelId, string? ruleId)
    {
        if (!string.IsNullOrEmpty(onlyModelId) && finding.ModelName != onlyModelId)
            return false;

        if (!string.IsNullOrEmpty(ruleId) && finding.RuleId != ruleId)
            return false;

        if (string.IsNullOrWhiteSpace(search))
            return true;

        foreach (var term in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!MatchesAnyField(finding, term))
                return false;
        }

        return true;
    }

    /// <summary>One term against every field the list shows, case-insensitively.</summary>
    private static bool MatchesAnyField(LogMessage finding, string term) =>
        Contains(finding.ModelName, term)
        || Contains(finding.Summary, term)
        || Contains(finding.Details, term)
        || Contains(finding.Severity, term)
        || Contains(finding.RuleId, term);

    private static bool Contains(string? field, string term) =>
        !string.IsNullOrEmpty(field) && field.Contains(term, StringComparison.OrdinalIgnoreCase);

    private void OnChangesOnlyChanged(bool value)
    {
        ShowChangesOnly = value;
        StateHasChanged();
    }

    /// <summary>
    /// The rules the current findings actually use, with their catalogue titles, for the rule filter
    /// to offer.
    ///
    /// <para>Built from the findings rather than from <c>RuleCatalog</c> so the list is what is in
    /// front of the user: offering all forty-odd rules, most of which produced nothing here, makes
    /// the control something to search rather than something to pick from. A rule the catalogue does
    /// not know — an external tool's output — falls back to its id.</para>
    /// </summary>
    private IEnumerable<(string Id, string Title)> RulesInFindings => RulesIn(CodeReviewService.LogMessages);

    /// <summary>
    /// The same, over a given set of findings, so it can be tested.
    ///
    /// <para><b>No list to maintain, which is the point.</b> A rule added to MLQT appears here the
    /// first time it produces a finding, with the title the catalogue gives it — there is no
    /// registration step to forget. What a test can still hold is that the title comes from the
    /// catalogue at all: drop that lookup and the filter goes on working while offering
    /// <c>MLQT.Structure.SingleFilePackage</c> where it used to say "Packages are stored as
    /// directories", and nothing would fail.</para>
    /// </summary>
    internal static IEnumerable<(string Id, string Title)> RulesIn(IEnumerable<LogMessage> findings) =>
        findings
            .Select(m => m.RuleId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id!, Title: RuleCatalog.BuiltIn.TryGetValue(id!, out var def) ? def.Title : id!))
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase);

    private void OnRuleFilterChanged(string? ruleId)
    {
        _ruleFilter = string.IsNullOrEmpty(ruleId) ? null : ruleId;
        StateHasChanged();
    }

    #region Going into a class this one uses (B197)

    /// <summary>
    /// The classes <paramref name="modelId"/> uses, in name order, for the "go to" menu.
    /// </summary>
    /// <remarks>
    /// <para><b>Empty until dependency analysis has run</b>, and that is a real state rather than a
    /// guard: the edges are what this reads, they are built by that pass, and for a large repository
    /// the pass is deferred until the user asks for it. <c>DirectedGraph.DependenciesAnalyzed</c> is
    /// the one way to ask — a model happening to have no edges is not the same answer, and the menu
    /// has to say "not analysed yet" rather than "uses nothing".</para>
    ///
    /// <para>A class does not lead to itself: a self-reference is possible in the graph and is not
    /// somewhere to navigate to.</para>
    /// </remarks>
    internal static IReadOnlyList<ModelNode> UsedClassesOf(DirectedGraph graph, string? modelId)
    {
        if (graph is null || string.IsNullOrEmpty(modelId) || !graph.DependenciesAnalyzed)
            return [];

        return [.. graph.GetUsedModels(modelId)
            .Where(m => m is not null && m.Id != modelId)
            .DistinctBy(m => m.Id, StringComparer.Ordinal)
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<ModelNode> UsedClasses =>
        UsedClassesOf(LibraryDataService.CombinedGraph, _currentModelNode?.Id);

    private string UsedClassesTooltip =>
        !LibraryDataService.CombinedGraph.DependenciesAnalyzed
            ? "Run dependency analysis to see what this class uses"
            : UsedClasses.Count == 0
                ? "This class uses nothing else"
                : "Go to a class this one uses — the back arrow beside this returns";

    /// <summary>
    /// Names the class the back button would return to, so the user knows before pressing it. A
    /// plain "Back" says nothing after three or four moves, which is when it is actually wanted.
    /// </summary>
    private string BackTooltip =>
        NavState.Back.Count == 0 ? "Back" : $"Back to {NavState.Back[0]}";

    private void GoBack() => NavState.GoBack();

    private void GoForward() => NavState.GoForward();

    /// <summary>
    /// Opens a class this one uses. Goes through <c>ChangeModelID</c> like every other way of
    /// selecting a class, so it is recorded in the history and the back arrow returns here (B197).
    /// </summary>
    private void GoToUsedClass(string modelId) => NavState.ChangeModelID(modelId);

    #endregion

    #region Searching the code (B176)

    /// <summary>
    /// The 1-based display lines containing <paramref name="term"/>, searched over the code the way
    /// the user reads it — tags stripped, entities decoded.
    ///
    /// <para>Searching the markup instead would find <c>KEYWORD</c> in every line and miss
    /// <c>&lt;html&gt;</c> in a documentation string, which is written <c>&amp;lt;html&amp;gt;</c>
    /// there. It is also why a match spanning two tokens is found here even though
    /// <c>CodeViewer</c> cannot tint it: this reads the line, not its spans.</para>
    /// </summary>
    internal static List<int> FindMatchingLines(IReadOnlyList<string>? displayLines, string? term)
    {
        if (displayLines is null || string.IsNullOrWhiteSpace(term))
            return [];

        var matches = new List<int>();
        for (var i = 0; i < displayLines.Count; i++)
        {
            if (PlainTextOf(displayLines[i]).Contains(term, StringComparison.OrdinalIgnoreCase))
                matches.Add(i + 1);
        }

        return matches;
    }

    private static readonly Regex MarkupTagRegex =
        new(@"</?(KEYWORD|IDENT|NAME|TYPE|OPERATOR|NUMBER|STRING|COMMENT|FUNCTION)>", RegexOptions.Compiled);

    private static string PlainTextOf(string markupLine) =>
        System.Net.WebUtility.HtmlDecode(MarkupTagRegex.Replace(markupLine, ""));

    /// <summary>
    /// Re-finds the matches against whatever is now on screen. Called whenever the displayed lines
    /// change — a different class, the annotations toggled — because the match list is line numbers
    /// into that list, and stale ones would scroll to whatever happens to be at them now.
    /// </summary>
    private void RecomputeCodeMatches()
    {
        _codeMatches = FindMatchingLines(_highlightedCode, _codeSearch);
        _codeMatchIndex = 0;
    }

    private async Task OnCodeSearchChanged(string value)
    {
        _codeSearch = value ?? "";
        _codeMatches = FindMatchingLines(_highlightedCode, _codeSearch);
        _codeMatchIndex = 0;

        // Jump to the first hit as the user types, so the box is useful before they reach for the
        // next button at all.
        if (_codeMatches.Count > 0)
            await ScrollToCurrentMatchAsync();
    }

    private async Task StepCodeMatch(int by)
    {
        if (_codeMatches.Count == 0)
            return;

        // Wraps in both directions: the alternative is a button that stops working at the ends,
        // which reads as broken rather than as finished.
        _codeMatchIndex = (_codeMatchIndex + by + _codeMatches.Count) % _codeMatches.Count;
        await ScrollToCurrentMatchAsync();
    }

    private async Task ScrollToCurrentMatchAsync()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync(
                "spellCheck.scrollLineIntoView", ".code-viewer", _codeMatches[_codeMatchIndex]);
        }
        catch (Exception)
        {
            // View may have been torn down; ignore.
        }
    }

    /// <summary>
    /// What to show beside the find-in-code box: which match of how many, or that there are none.
    ///
    /// <para><b>Empty is a state, not a missing value.</b> The element that shows this is always
    /// rendered and sizes to its content, so an empty string is what keeps the arrows against the
    /// field when nothing has been searched for (B248). It used to hold 84px open regardless.</para>
    /// </summary>
    private string CodeSearchStatus =>
        CodeSearchStatusText(_codeSearch, _codeMatches.Count, _codeMatchIndex);

    /// <inheritdoc cref="CodeSearchStatus"/>
    internal static string CodeSearchStatusText(string search, int matchCount, int matchIndex) =>
        search.Length == 0
            ? ""
            : matchCount == 0 ? "no matches" : $"{matchIndex + 1} of {matchCount}";

    #endregion

    private bool _exporting;

    /// <summary>
    /// Writes the finding list to a JSON file, in the same shape as the CLI's <c>--format json</c>
    /// findings array so the two can be compared directly.
    ///
    /// <para>Exports the <b>complete</b> list, deliberately ignoring the search box and the
    /// scope/baseline switches. An export that silently honoured the on-screen filters would be the
    /// worst possible answer to "why do the app and CI disagree on the count" — it would look like
    /// evidence while quietly reproducing the filter as a difference.</para>
    /// </summary>
    private async Task ExportFindingsAsync()
    {
        if (_exporting)
            return;

        var messages = CodeReviewService.LogMessages.ToList();
        if (messages.Count == 0)
        {
            Snackbar.Add("There are no findings to export.", MudBlazor.Severity.Info);
            return;
        }

        _exporting = true;
        try
        {
            var folder = await FilePickerService.PickFolderAsync("Select a folder to export the finding list to");
            if (string.IsNullOrEmpty(folder))
                return;

            // Where each class sits on disk, and which library it belongs to: the export writes a
            // finding's line and path the way the CLI's report does, so the two can be diffed.
            var locations = ClassLocation.ForGraph(LibraryDataService.CombinedGraph);
            var libraryRootByModel = FindingExport.LibraryRootsByModel(LibraryDataService.Libraries);

            var exportedAt = DateTime.Now;
            var json = FindingExport.ToJson(
                messages, locations, libraryRootByModel,
                RepositoryService.GetActiveProject()?.Name,
                exportedAt,
                statusOf: m => BaselineStatus.HasBaseline ? BaselineStatus.StatusOf(m)?.ToString() : null);

            var target = Path.Combine(folder, FindingExport.FileNameFor(exportedAt));

            await File.WriteAllTextAsync(target, json);
            Snackbar.Add($"Exported {messages.Count} findings to {target}", MudBlazor.Severity.Success);
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", "Failed to export the finding list", ex);
            Snackbar.Add($"Could not export the finding list: {ex.Message}", MudBlazor.Severity.Error);
        }
        finally
        {
            _exporting = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// The line in the file, for a finding that carries a line within its class. Mirrors
    /// <c>CheckReport.LineFor</c>, including its fallback for a class the map does not know.
    /// </summary>
    internal static int FileLineOf(LogMessage m, IReadOnlyDictionary<string, ClassLocation> locations)
        => ReportLocation.LineIn(locations.GetValueOrDefault(m.ModelName), m.LineNumber);

    /// <summary>
    /// The file as the report shows it: relative to the library the class belongs to, with forward
    /// slashes. The rule is <see cref="ReportLocation.RelativeFile(ClassLocation?,string?)"/>, which
    /// is also what the CLI's <c>File</c> field carries — this used to have its own copy of it, and
    /// was for a while the second field the two exports were said to share and did not agree on.
    /// </summary>
    internal static string? ReportPathOf(
        LogMessage m,
        IReadOnlyDictionary<string, ClassLocation> locations,
        IReadOnlyDictionary<string, string> libraryRootByModel)
        => ReportLocation.RelativeFile(
            locations.GetValueOrDefault(m.ModelName),
            libraryRootByModel.GetValueOrDefault(m.ModelName));

    private string FindingsHeading
    {
        get
        {
            var all = CodeReviewService.LogMessages;

            // The same predicate the table filters by, so the number in the heading is the number of
            // rows and cannot drift from it (B247).
            //
            // **Only walked when something is actually narrowing the list.** This runs on every
            // render, and a check re-renders this page for each batch of findings it produces, so an
            // unconditional pass over tens of thousands of findings is a cost paid thousands of
            // times for a number that has not moved (B190). With no filter set the answer is the
            // count itself.
            var filtered = ShowChangesOnly
                || _searchString.Length > 0
                || _ruleFilter is { Length: > 0 }
                || (FindingsScopeAllModels && NavState.ModelID.Length > 0);

            var shown = filtered ? all.Count(FilterFunc1) : all.Count;

            return FindingsHeadingText(
                shown,
                all.Count,
                BaselineStatus.HasBaseline ? all.Count(BaselineStatus.Snapshot.IsChangedFromBaseline) : null,
                ShowChangesOnly);
        }
    }

    /// <summary>
    /// What the findings table is showing, and out of how many when those differ.
    ///
    /// <para>B247: four things narrow that list — the class scope, the rule, the search box and the
    /// baseline toggle — and the heading counted none of them. It reported the whole ledger however
    /// much of it was on screen, so <b>a filter that matched nothing and a filter that matched three
    /// looked the same</b>: a table with rows scrolled out of sight and a number above it that had
    /// not moved.</para>
    ///
    /// <para>Pure, and separate from the component, because the interesting part is which of six
    /// wordings applies. Counting is the easy half.</para>
    /// </summary>
    /// <param name="shown">Findings surviving every filter — the row count.</param>
    /// <param name="total">Findings held, before any of them.</param>
    /// <param name="changed">
    /// Findings this working copy changed, or null when no repository has a baseline. Reported
    /// alongside rather than as the total, because it is a property of the findings rather than
    /// something the user switched on — except when they did, which is <paramref name="showChangesOnly"/>.
    /// </param>
    /// <param name="showChangesOnly">Whether the baseline toggle is the reason some are hidden.</param>
    internal static string FindingsHeadingText(int shown, int total, int? changed, bool showChangesOnly)
    {
        if (changed is not { } changedCount)
        {
            return shown == total
                ? $"{total} Findings to review"
                : $"{shown} of {total} findings";
        }

        if (showChangesOnly)
        {
            // The toggle's own count is the honest denominator here: with it on, the findings it
            // left are what the other filters then narrowed, and saying "of {total}" would credit
            // the search box with hiding everything the toggle did.
            return shown == changedCount
                ? $"{changedCount} changed of {total} findings"
                : $"{shown} of {changedCount} changed findings";
        }

        return shown == total
            ? $"{total} Findings to review ({changedCount} changed vs baseline)"
            : $"{shown} of {total} findings ({changedCount} changed vs baseline)";
    }

    private string ChangesOnlyTooltip => BaselineStatus.HasBaseline
        ? "Show only findings this working copy changed: new ones, plus accepted debt in a file " +
          $"waiting to be committed ({BaselineStatus.TouchedFileCount} such file(s))."
        : "No .mlqt/baseline.json in the loaded repositories — run `mlqt baseline create` to enable this.";

    private static string BaselineStatusLabel(FindingStatus status) => status switch
    {
        FindingStatus.New => "new",
        FindingStatus.TouchedDebt => "touched",
        _ => "accepted"
    };

    private static Color BaselineStatusColour(FindingStatus status) => status switch
    {
        FindingStatus.New => Color.Error,
        FindingStatus.TouchedDebt => Color.Warning,
        _ => Color.Default
    };


    private void ResolveFinding()
    {
        if (_currentFinding != null)
            CodeReviewService.RemoveLogMessage(_currentFinding);
        _findingDetailsVisible = false;
        StateHasChanged();
    }

    private bool _suppressing;
    private bool _splitting;

    /// <summary>
    /// A finding whose fix is to restructure the package it names (B242).
    ///
    /// <para>Offered from the findings list because that is where the user meets the problem. The
    /// alternative MLQT had was <b>Format All Files</b>, which restructures the whole repository —
    /// thousands of files rewritten to correct the one package that arrived from another tool this
    /// morning.</para>
    /// </summary>
    internal static bool CanSplitPackage(LogMessage? finding)
        => finding is { Source: LogMessage.StyleCheckingSource }
           && finding.RuleId == RuleIds.SingleFilePackage;

    /// <summary>
    /// Writes the package this finding names as a directory with a file per class, and reloads the
    /// files that changed.
    /// </summary>
    private async Task SplitPackageForFinding(LogMessage? finding)
    {
        if (!CanSplitPackage(finding) || _splitting)
            return;

        var package = LibraryDataService.GetModelById(finding!.ModelName);
        if (package is null)
        {
            Snackbar.Add("Cannot locate that package any more; refresh and try again.", MudBlazor.Severity.Warning);
            return;
        }

        var library = LibraryDataService.Libraries.FirstOrDefault(l => l.ModelIds.Contains(package.Id));
        var repository = string.IsNullOrEmpty(library?.RepositoryId)
            ? null : RepositoryService.GetRepository(library.RepositoryId);
        if (repository is null || library is null)
        {
            Snackbar.Add("That package is not in a repository MLQT can write to.", MudBlazor.Severity.Warning);
            return;
        }

        // It creates a directory and deletes a file, so it asks first. Everything it does is
        // recoverable from version control, but not by pressing the button again.
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Split into files",
            $"Write {package.Definition.Name} as a directory with one file per class, and delete "
            + $"{Path.GetFileName(CurrentFilePathOf(package) ?? "the single file")}?",
            yesText: "Split", cancelText: "Cancel");
        if (confirmed != true)
            return;

        _splitting = true;
        try
        {
            var settings = repository.StyleSettings ?? new StyleCheckingSettings();

            // MLQT's own write, so the monitor is paused across it exactly as it is for a save —
            // otherwise the new files come back as external changes to process.
            FileMonitoringService.StopMonitoring(repository.Id);
            PackageSplitter.SplitResult result;
            try
            {
                result = PackageSplitter.Split(
                    LibraryDataService.CombinedGraph, package, settings.ToFormattingOptions());

                if (result.Succeeded || result.WrittenFiles.Count > 0)
                {
                    var changed = result.WrittenFiles.Concat(result.RemovedFiles).ToList();
                    await LibraryDataService.UpdateChangedFilesAsync(changed, library.SourcePath);
                }
            }
            finally
            {
                FileMonitoringService.ClearPendingChanges(repository.Id);
                if (!string.IsNullOrEmpty(repository.VcsRootPath))
                    FileMonitoringService.StartMonitoring(repository.Id, repository.VcsRootPath);
            }

            if (!result.Succeeded)
            {
                Snackbar.Add(result.Error!, MudBlazor.Severity.Error);
                return;
            }

            // The finding described the old arrangement, so it goes with it. A re-check would not
            // raise it again — which is the test this is held to.
            CodeReviewService.RemoveLogMessagesByPredicate(
                m => m.RuleId == RuleIds.SingleFilePackage
                     && string.Equals(m.ModelName, package.Id, StringComparison.Ordinal));

            NavState.VcsModelsChanged(repository.Id, library.ModelIds.ToList());
            OnModelSelected();
            Snackbar.Add(
                $"{package.Definition.Name} is now a directory with {result.WrittenFiles.Count} file(s).",
                MudBlazor.Severity.Success);
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", $"Failed to split {package.Id}", ex);
            Snackbar.Add($"Could not split the package: {ex.Message}", MudBlazor.Severity.Error);
        }
        finally
        {
            _splitting = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private string? CurrentFilePathOf(ModelNode model) =>
        LibraryDataService.CombinedGraph.GetNode<FileNode>(model.ContainingFileId ?? "")?.FilePath;

    // A style finding that carries a rule id can be waived in source with a __MLQT annotation.
    // Spelling findings are excluded — a blanket waiver of the spelling rule would silence every
    // other misspelling in the class, so they get the word-scoped Ignore in the correction menu.
    // Diagnostics are excluded too: MLQT.Check.Failed reaches this list with the style source, and
    // offering Suppress on it wrote an annotation into the user's file that nothing reads and then
    // reported success. suppress_rule has refused a diagnostic since B26; this is the surface an
    // author is more likely to be sitting in front of.
    internal static bool CanSuppressRule(LogMessage? finding)
        => finding is { Source: LogMessage.StyleCheckingSource } && !string.IsNullOrEmpty(finding.RuleId)
           && !RuleIds.IsDiagnostic(finding.RuleId) && !IsSpellingFinding(finding);

    /// <summary>
    /// A class as it exists on disk: the node, the class that owns the file it lives in, that file's
    /// path, and the path from the file's top class down to this one.
    /// </summary>
    private sealed record ClassSourceTarget(ModelNode Node, ModelNode FileOwner, string FilePath, string[]? ClassPath);

    /// <summary>
    /// Locates the source file and in-file class path for a model, or returns null having said in a
    /// snackbar why it could not.
    ///
    /// <para>The annotation is placed onto the target class within the on-disk file text — the ground
    /// truth, which always holds the full nested structure. We deliberately do NOT edit the in-memory
    /// class slice: a package node's stored ModelicaCode can be a formatting "shell" that omits nested
    /// standalone classes, so a nested type (e.g. Modelica.Units.SI.Molarity) would not be found there.
    /// Locating by name path also avoids any substring matching and works for short-class `type`
    /// definitions.</para>
    /// </summary>
    private ClassSourceTarget? ResolveClassSourceTarget(string? modelId)
    {
        var targetNode = string.IsNullOrEmpty(modelId) ? null : LibraryDataService.GetModelById(modelId);
        if (targetNode is null)
        {
            Snackbar.Add("Cannot locate the model to annotate.", MudBlazor.Severity.Warning);
            return null;
        }

        var graph = LibraryDataService.CombinedGraph;
        var fileId = targetNode.ContainingFileId;
        var fileNode = string.IsNullOrEmpty(fileId) ? null : graph.GetNode<FileNode>(fileId);
        if (fileNode == null || string.IsNullOrEmpty(fileNode.FilePath))
        {
            Snackbar.Add("Cannot locate the file on disk to edit.", MudBlazor.Severity.Warning);
            return null;
        }

        var fileOwner = graph.GetModelsInFile(fileId!)
            .Where(m => ModelicaName.IsInSubtree(targetNode.Id, m.Id))
            .OrderBy(m => m.Id.Length)
            .FirstOrDefault() ?? targetNode;

        string[]? classPath = null;
        if (fileOwner.Id != targetNode.Id)
        {
            var prefix = fileOwner.Id + ".";
            if (!targetNode.Id.StartsWith(prefix, StringComparison.Ordinal))
            {
                Snackbar.Add("Could not locate the class within its file; reload the library and retry.",
                    MudBlazor.Severity.Warning);
                return null;
            }
            classPath = targetNode.Id[prefix.Length..].Split('.');
        }

        return new ClassSourceTarget(targetNode, fileOwner, fileNode.FilePath, classPath);
    }

    /// <summary>
    /// Reads a target's file as it is on disk, so an annotation can be spliced in without rewriting
    /// the whole file (which would otherwise show as hundreds of unrelated line-ending changes on a
    /// CRLF library). Returns null having reported the failure.
    /// </summary>
    private async Task<string?> ReadTargetFileAsync(ClassSourceTarget target)
    {
        try
        {
            return await ModelicaFileEncoding.ReadAllTextOnlyAsync(target.FilePath);
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", $"Failed to read {target.FilePath} for annotation", ex);
            Snackbar.Add($"Failed to read the file: {ex.Message}", MudBlazor.Severity.Error);
            return null;
        }
    }

    /// <summary>
    /// Validates the annotated file text, saves it, and reloads the file so the graph matches what is
    /// on disk. The monitor is paused across the write so MLQT's own edit does not come back as a
    /// change to process. Returns false having reported the failure.
    /// </summary>
    private async Task<bool> SaveAnnotatedFileAsync(ClassSourceTarget target, string newContent)
    {
        // Never persist broken code.
        var (_, parseErrors) = ModelicaParserHelper.ParseWithErrors(newContent);
        if (parseErrors.Any(e => e.Severity == ParserErrorSeverity.FatalParseFailure))
        {
            Snackbar.Add("The annotation was not applied: the result failed to parse.", MudBlazor.Severity.Error);
            return false;
        }

        var library = LibraryDataService.Libraries.FirstOrDefault(l => l.ModelIds.Contains(target.FileOwner.Id));
        var repository = string.IsNullOrEmpty(library?.RepositoryId)
            ? null : RepositoryService.GetRepository(library.RepositoryId);
        var repoId = repository?.Id;
        var monitoredRoot = repository?.VcsRootPath;
        bool monitorPaused = false;

        try
        {
            if (!string.IsNullOrEmpty(repoId))
            {
                FileMonitoringService.StopMonitoring(repoId);
                monitorPaused = true;
            }
            await ModelicaFileEncoding.WriteAllTextAsync(target.FilePath, newContent);
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", $"Failed to write annotation to {target.FilePath}", ex);
            Snackbar.Add($"Failed to save: {ex.Message}", MudBlazor.Severity.Error);
            if (monitorPaused && !string.IsNullOrEmpty(monitoredRoot))
                FileMonitoringService.StartMonitoring(repoId!, monitoredRoot);
            return false;
        }

        // Re-parse the file from disk so all model nodes are rebuilt from the saved content.
        var affected = await LibraryDataService.ReloadFileAsync(target.FilePath);
        if (monitorPaused && !string.IsNullOrEmpty(monitoredRoot))
        {
            FileMonitoringService.StartMonitoring(repoId!, monitoredRoot);
            FileMonitoringService.NotifyFileActivity(repoId!);
        }

        // The annotation goes in at the end of the class, so everything the user can see is where it
        // was — put them back there instead of at the top.
        await CaptureScrollForReloadAsync();

        NavState.ModelContentChanged(affected);
        _currentModelNode = LibraryDataService.GetModelById(NavState.ModelID);
        return true;
    }

    /// <summary>
    /// Waive the current finding's rule by writing a <c>__MLQT(suppress="…")</c> annotation onto the
    /// class (or the finding's component) and saving the file — the same in-source suppression the CLI
    /// and MCP server honour. Persists through the single-file save path used by spelling corrections.
    /// </summary>
    private async Task SuppressRuleForFinding(LogMessage? finding)
    {
        if (finding is null || string.IsNullOrEmpty(finding.RuleId) || _suppressing)
            return;

        var target = ResolveClassSourceTarget(finding.ModelName);
        if (target is null)
            return;

        var component = SuppressionScope.ComponentFor(finding.ElementPath);

        _suppressing = true;
        try
        {
            var fileContent = await ReadTargetFileAsync(target);
            if (fileContent is null)
                return;

            if (!MlqtSuppressionWriter.TryAddSuppressionToFile(fileContent, target.ClassPath, component, finding.RuleId!, null, out var newContent, out var writeError))
            {
                // A component target that can't be located (e.g. an inherited element) falls back to
                // suppressing at the class level so the action still succeeds.
                if (component is null ||
                    !MlqtSuppressionWriter.TryAddSuppressionToFile(fileContent, target.ClassPath, null, finding.RuleId!, null, out newContent, out writeError))
                {
                    Snackbar.Add($"Could not add the suppression: {writeError}", MudBlazor.Severity.Error);
                    return;
                }
                component = null;
            }

            if (!await SaveAnnotatedFileAsync(target, newContent))
                return;

            // Drop the now-waived finding(s) — a re-check would not report them. What "waived"
            // reaches is SuppressionScope's to say, and it is tested there.
            CodeReviewService.RemoveLogMessagesByPredicate(
                SuppressionScope.WaivedBy(finding.ModelName, finding.RuleId!, component));

            _findingDetailsVisible = false;
            OnModelSelected();   // re-render with the annotated content and refresh VCS status
            Snackbar.Add(SuppressionScope.Describe(finding.RuleId!, component), MudBlazor.Severity.Success);
        }
        finally
        {
            _suppressing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// How long the scroll position is worth waiting for. Remembering where the user was looking is
    /// a courtesy; making them wait for it is not, and this call has been measured taking eleven
    /// seconds while the file write it follows took under a millisecond (B253). Past this, the
    /// re-render lands at the top and the edit completes.
    /// </summary>
    private static readonly TimeSpan ScrollCaptureBudget = TimeSpan.FromMilliseconds(250);
    /// <summary>
    /// Remembers where the user is looking, so the re-render that follows an edit to the open file
    /// puts them back rather than at the top of the class.
    ///
    /// <para>Call it past every early return and immediately before the re-render is triggered: the
    /// offsets are then the ones on screen, and a pending offset can never leak into ordinary
    /// navigation. <c>_scrollBaselineCode</c> records the current (old) content list so the restore
    /// fires on the render that mounts the new content — see OnAfterRenderAsync.</para>
    ///
    /// <para>The offsets survive because every edit that uses this adds or changes text without
    /// moving what is above it: a correction swaps one word, and an <c>__MLQT</c> annotation is
    /// written at the end of the class.</para>
    /// </summary>

    private async Task CaptureScrollForReloadAsync()
    {
        var timer = new CancellationTokenSource(ScrollCaptureBudget);
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var offsets = await JSRuntime.InvokeAsync<double[]>(
                "spellCheck.getScroll", timer.Token, ".code-viewer");
            _pendingScroll = offsets is { Length: >= 2 } ? (offsets[0], offsets[1]) : null;
            _scrollBaselineCode = _highlightedCode;
        }
        catch (Exception)
        {
            // No view to read (not rendered yet, or torn down), or it did not answer in time — land
            // wherever the re-render lands.
            _pendingScroll = null;
        }
        finally
        {
            timer.Dispose();
            if (started.ElapsedMilliseconds > 50)
                LoggingService.Debug("CodeReview",
                    $"  scroll capture took {started.ElapsedMilliseconds}ms"
                    + (_pendingScroll is null ? " and gave up" : ""));
        }
    }

    private void OnFindingsScopeChanged(bool value) {
        FindingsScopeAllModels = value;
        RecomputeMisspelledWords();
        StateHasChanged();
    }

    /// <summary>
    /// Rebuilds the set of misspelled words for the current model from the spelling findings,
    /// assigning a fresh instance so CodeViewer re-renders its highlight overlay.
    /// </summary>
    private void RecomputeMisspelledWords()
    {
        var modelId = NavState.ModelID;
        if (string.IsNullOrEmpty(modelId))
        {
            _misspelledWords = null;
            return;
        }

        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var msg in CodeReviewService.LogMessages)
        {
            if (msg.ModelName == modelId && IsSpellingFinding(msg))
            {
                var word = ExtractMisspelledWord(msg);
                if (!string.IsNullOrEmpty(word))
                    words.Add(word);
            }
        }

        _misspelledWords = words.Count > 0 ? words : null;
    }

    // Both of these, and the rule for what an accepted word covers, are SpellingAcceptance's - see
    // there for why the word is read from the message rather than the finding's discriminator.
    private static bool IsSpellingFinding(LogMessage? finding) =>
        SpellingAcceptance.IsSpellingFinding(finding);

    private static string? ExtractMisspelledWord(LogMessage? finding) =>
        SpellingAcceptance.WordOf(finding);

    /// <summary>
    /// The style settings of the repository a class belongs to, or null when it belongs to none —
    /// a library loaded only for reference, which has no rules of its own.
    /// </summary>
    private StyleCheckingSettings? StyleSettingsForModel(string modelId) =>
        DictionaryScope.RepositoryForModel(LibraryDataService, RepositoryService, modelId)?.StyleSettings;

    /// <summary>
    /// The repository whose word list an accepted spelling belongs in. Null when the class is not in
    /// a repository, in which case there is nowhere to record the word and the action is offered as
    /// disabled rather than silently doing nothing.
    /// </summary>
    private string? DictionaryRootForCurrentFinding() => RepositoryForCurrentFinding()?.LocalPath;

    /// <summary>The repository owning the class the current finding is about, or null if it has none.</summary>
    private Repository? RepositoryForCurrentFinding()
    {
        var modelId = _currentFinding?.ModelName;
        if (string.IsNullOrEmpty(modelId))
            modelId = NavState.ModelID;

        return string.IsNullOrEmpty(modelId)
            ? null
            : DictionaryScope.RepositoryForModel(LibraryDataService, RepositoryService, modelId);
    }

    private bool CanAddToDictionary => DictionaryRootForCurrentFinding() is not null;

    private const string NoRepositoryDictionaryTooltip =
        "This class is not in a repository, so there is nowhere to record the word. Accepted " +
        "spellings live in the repository's .mlqt/dictionary.txt so the app and CI agree on them.";

    private async Task AddToDictionary()
    {
        var word = ExtractMisspelledWord(_currentFinding) ?? (_contextMenuOpen ? _contextWord : null);
        if (string.IsNullOrEmpty(word))
            return;

        var repositoryRoot = DictionaryRootForCurrentFinding();
        if (repositoryRoot is null)
        {
            Snackbar.Add(NoRepositoryDictionaryTooltip, MudBlazor.Severity.Warning);
            return;
        }

        var accepted = SpellingAcceptance.WordToRecord(word);
        await CustomDictionaryService.AddWordAsync(repositoryRoot, accepted);

        // Clear the findings this now covers, and only within the repository that accepted it: it
        // stays a finding elsewhere, which is the point of the list being per repository.
        var repositoryModelIds = LibraryDataService.Libraries
            .Where(l => DictionaryScope.RootForLibrary(RepositoryService, l) == repositoryRoot)
            .SelectMany(l => l.ModelIds)
            .ToHashSet(StringComparer.Ordinal);

        CodeReviewService.RemoveLogMessagesByPredicate(
            SpellingAcceptance.ClearedBy(accepted, repositoryModelIds));

        _contextMenuOpen = false;
        _suggestions = null;
        _findingDetailsVisible = false;
        RecomputeMisspelledWords();
        StateHasChanged();
    }

    /// <summary>
    /// Accept the right-clicked word as spelled correctly <em>in this class</em>, by writing
    /// <c>__MLQT(spelling="…")</c> into the source and saving the file.
    ///
    /// <para>Recorded in the source rather than remembered in the session, so the word stays accepted
    /// through the next check and is honoured wherever findings are produced — the app, the CLI and
    /// the MCP server all read the annotation. It is scoped to the word and the class, which is what
    /// sits between the two blunter options: suppressing the spelling rule silences every other
    /// misspelling in the class, and adding the word to the dictionary accepts it everywhere in the
    /// repository.</para>
    ///
    /// <para>A class MLQT cannot locate on disk (a snippet, an encrypted library's reconstruction)
    /// has nowhere to record it, so the finding is dismissed for now and the user is told it will
    /// come back.</para>
    /// </summary>
    private async Task IgnoreSpellingFinding()
    {
        if (_suppressing)
            return;

        var word = ExtractMisspelledWord(_currentFinding) ?? (_contextMenuOpen ? _contextWord : null);
        var modelId = _currentFinding?.ModelName;
        if (string.IsNullOrEmpty(modelId))
            modelId = NavState.ModelID;

        if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(modelId))
        {
            DismissSpellingFindingWithoutRecording();
            return;
        }

        // Record the word itself, not a possessive of it — "Stodola's" is accepted once "Stodola" is,
        // and the annotation is something the team reads in the source. Same rule as the dictionary.
        var accepted = ModelicaParser.SpellChecking.SpellChecker.PossessiveBaseOf(word) ?? word;

        var target = ResolveClassSourceTarget(modelId);
        if (target is null)
        {
            DismissSpellingFindingWithoutRecording();
            Snackbar.Add(
                $"'{accepted}' could not be recorded in the source, so it will be reported again on the next check.",
                MudBlazor.Severity.Warning);
            return;
        }

        _suppressing = true;
        try
        {
            var fileContent = await ReadTargetFileAsync(target);
            if (fileContent is null)
                return;

            if (!MlqtSuppressionWriter.TryAddSpellingExceptionToFile(
                    fileContent, target.ClassPath, accepted, null, out var newContent, out var writeError))
            {
                Snackbar.Add($"Could not record the word: {writeError}", MudBlazor.Severity.Error);
                return;
            }

            if (!await SaveAnnotatedFileAsync(target, newContent))
                return;

            // Clear what the annotation now covers: this word, and its possessive, in this class.
            // Other classes still report it — the waiver is this class's, which is the whole point of
            // recording it here rather than in the repository's word list.
            CodeReviewService.RemoveLogMessagesByPredicate(
                SpellingAcceptance.ClearedInClass(accepted, modelId));

            CloseSpellingMenu();
            _findingDetailsVisible = false;
            OnModelSelected();   // re-render with the annotated content and refresh VCS status
            Snackbar.Add($"'{accepted}' is now accepted in {modelId}.", MudBlazor.Severity.Success);
        }
        finally
        {
            _suppressing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Removes the finding from the list without recording anything — it returns on the next check.</summary>
    private void DismissSpellingFindingWithoutRecording()
    {
        if (_currentFinding != null)
            CodeReviewService.RemoveLogMessage(_currentFinding);

        CloseSpellingMenu();
        RecomputeMisspelledWords();
        StateHasChanged();
    }

    private void CloseSpellingMenu()
    {
        _contextMenuOpen = false;
        _suggestions = null;
        RecomputeMisspelledWords();
    }

    /// <summary>
    /// Replaces the right-clicked misspelled word with the chosen correction throughout the
    /// containing file's descriptions and documentation, writes the file to disk immediately,
    /// reloads it, and refreshes the view. The file is otherwise left exactly as it was — the word
    /// is the only change — and occurrences inside links/hrefs and code blocks are left untouched
    /// (see <see cref="SpellingCorrector"/>).
    /// </summary>
    private async Task ApplyCorrection(string newWord)
    {
        // Anything that escapes here is swallowed by the renderer: the menu stays open, the file is
        // untouched, and the only trace is a line in a debug window the user does not have. A failed
        // correction has to say so, and has to leave something in the log to act on.
        try
        {
            await ApplyCorrectionCore(newWord);
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", $"Correcting '{_contextWord}' failed", ex);
            Snackbar.Add(
                $"Correcting '{_contextWord}' failed: {ex.GetType().Name}: {ex.Message}",
                MudBlazor.Severity.Error);
            CloseContextMenu();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ApplyCorrectionCore(string newWord)
    {
        var oldWord = _contextWord;
        if (_currentModelNode == null || string.IsNullOrEmpty(oldWord)
            || string.IsNullOrWhiteSpace(newWord) || newWord.Trim() == oldWord)
        {
            CloseContextMenu();
            return;
        }
        newWord = newWord.Trim();

        var graph = LibraryDataService.CombinedGraph;
        var fileId = _currentModelNode.ContainingFileId;
        var fileNode = string.IsNullOrEmpty(fileId) ? null : graph.GetNode<FileNode>(fileId);
        if (fileNode == null || string.IsNullOrEmpty(fileNode.FilePath))
        {
            Snackbar.Add("Cannot locate the file on disk to correct.", MudBlazor.Severity.Warning);
            CloseContextMenu();
            return;
        }

        var filePath = fileNode.FilePath;
        if (!File.Exists(filePath))
        {
            Snackbar.Add("Cannot locate the file on disk to correct.", MudBlazor.Severity.Warning);
            CloseContextMenu();
            return;
        }

        // Correct the file as it is on disk, not the source the graph holds for the class. Style
        // checking trims a package's inline standalone children out of its stored ModelicaCode —
        // each child has its own node — so the word being corrected is usually not in the package's
        // code at all, and rewriting the file from it would drop those classes from the file. The
        // file is the whole truth. Every occurrence in it is corrected; links/hrefs and code blocks
        // are left alone by SpellingCorrector.
        string fileText;
        try
        {
            fileText = await ModelicaFileEncoding.ReadAllTextOnlyAsync(filePath);
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", $"Failed to read {filePath} to correct spelling", ex);
            Snackbar.Add($"Failed to read the file: {ex.Message}", MudBlazor.Severity.Error);
            CloseContextMenu();
            return;
        }

        // Off the render thread: correcting parses the whole file twice — once to find the word in the
        // strings, once to check the result still parses — which is over a second each on the larger
        // generated files in a library. Done inline, the window froze for the length of it and then
        // reported the outcome, which reads as the app having hung rather than worked.
        var (correctedCode, replacements, parseFailed, sourceUnreadable) = await Task.Run(() =>
        {
            var (corrected, count) = SpellingCorrector.ReplaceWordInStrings(fileText, oldWord, newWord);
            if (count == 0)
            {
                // Say which of the two it is. A word that is genuinely absent and a file the parser
                // could not read look identical from the outside, and only one of them is the user's
                // to fix. Parsed only on this path, so a successful correction pays nothing for it.
                var (_, sourceErrors) = ModelicaParserHelper.ParseWithErrors(fileText);
                var unreadable = sourceErrors.Any(e => e.Severity == ParserErrorSeverity.FatalParseFailure);
                return (corrected, count, false, unreadable);
            }

            // Never persist broken code: abort if the correction somehow fails to parse.
            var (_, errors) = ModelicaParserHelper.ParseWithErrors(corrected);
            var failed = errors.Any(e => e.Severity == ParserErrorSeverity.FatalParseFailure);
            return (SpellingCorrector.MatchFileEnding(fileText, corrected), count, failed, false);
        });

        if (replacements == 0)
        {
            Snackbar.Add(
                sourceUnreadable
                    ? $"'{Path.GetFileName(filePath)}' has a syntax error MLQT could not parse, so " +
                      $"'{oldWord}' could not be located in it. Fix the syntax first."
                    : $"Could not find '{oldWord}' to correct in this file.",
                MudBlazor.Severity.Warning);
            LoggingService.Warn("CodeReview",
                $"No correction applied for '{oldWord}' in {filePath} " +
                $"(source parses: {!sourceUnreadable})");
            CloseContextMenu();
            return;
        }

        if (parseFailed)
        {
            Snackbar.Add("Correction was not applied: the result failed to parse.", MudBlazor.Severity.Error);
            CloseContextMenu();
            return;
        }

        // Identify the repository (if any) so file monitoring can be paused across the write,
        // preventing the watcher from echoing our own change back as a pending refresh.
        var library = LibraryDataService.Libraries.FirstOrDefault(l => l.ModelIds.Contains(_currentModelNode.Id));
        var repository = string.IsNullOrEmpty(library?.RepositoryId)
            ? null : RepositoryService.GetRepository(library.RepositoryId);
        var repoId = repository?.Id;
        var monitoredRoot = repository?.VcsRootPath;
        bool monitorPaused = false;

        try
        {
            if (!string.IsNullOrEmpty(repoId))
            {
                FileMonitoringService.StopMonitoring(repoId);
                monitorPaused = true;
            }
            var write = System.Diagnostics.Stopwatch.StartNew();
            await ModelicaFileEncoding.WriteAllTextAsync(filePath, correctedCode);
            if (write.ElapsedMilliseconds > 1000)
                LoggingService.Info("CodeReview",
                    $"Writing {Path.GetFileName(filePath)} took {write.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            LoggingService.Error("CodeReview", $"Failed to write corrected file {filePath}", ex);
            Snackbar.Add($"Failed to save correction: {ex.Message}", MudBlazor.Severity.Error);
            if (monitorPaused && !string.IsNullOrEmpty(monitoredRoot))
                FileMonitoringService.StartMonitoring(repoId!, monitoredRoot);
            CloseContextMenu();
            return;
        }

        // Re-parse the file from disk so all model nodes for it are rebuilt from the saved content.
        //
        // Timed, because this is where the time goes and nothing said so. A correction in a generated
        // file holding 4,478 classes took the better part of a minute, and the only way to find out
        // which part was to read timestamps of unrelated debug lines either side of it. The removal
        // half of this is much faster since the graph gained a bulk remove (7b-5); the log line is
        // what will show whether the rest of it needs the same treatment.
        var reload = System.Diagnostics.Stopwatch.StartNew();
        var affected = await LibraryDataService.ReloadFileAsync(filePath);
        reload.Stop();

        if (reload.ElapsedMilliseconds > 1000)
            LoggingService.Info("CodeReview",
                $"Reloading {Path.GetFileName(filePath)} after a correction took {reload.ElapsedMilliseconds} ms " +
                $"for {affected.Count} class(es)");

        if (monitorPaused && !string.IsNullOrEmpty(monitoredRoot))
        {
            FileMonitoringService.StartMonitoring(repoId!, monitoredRoot);
            FileMonitoringService.NotifyFileActivity(repoId!);   // file is now genuinely modified
        }

        // The correction changes only a word, so the offsets the user is looking at stay valid.
        await CaptureScrollForReloadAsync();

        // Invalidate cached renders for every model that lived in the file, then re-fetch the
        // current node (reload replaced the old instances).
        NavState.ModelContentChanged(affected);
        _currentModelNode = LibraryDataService.GetModelById(NavState.ModelID);

        // Drop resolved spelling findings for this word on the file's models — they're fixed now.
        var affectedSet = new HashSet<string>(affected, StringComparer.Ordinal);
        CodeReviewService.RemoveLogMessagesByPredicate(m =>
            affectedSet.Contains(m.ModelName) && IsSpellingFinding(m) && ExtractMisspelledWord(m) == oldWord);

        CloseContextMenu();
        OnModelSelected();   // re-render with corrected content and refresh VCS status
        RecomputeMisspelledWords();

        LoggingService.Info("CodeReview",
            $"Replaced '{oldWord}' with '{newWord}' {replacements} time(s) in {filePath}");
        Snackbar.Add(
            $"Replaced '{oldWord}' with '{newWord}' ({replacements} occurrence{(replacements == 1 ? "" : "s")}).",
            MudBlazor.Severity.Success);
        await InvokeAsync(StateHasChanged);
    }
}
