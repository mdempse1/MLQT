using System.IO;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using DymolaInterface;
using OpenModelicaInterface;
using RevisionControl;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;

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
    private string _modelicaCode { get; set; } = "";
    private int _lines = 10;
    private ModelNode? _currentModelNode = null;
    private int _modelsToCheck = 0;
    private int _modelsChecked = 0;
    private bool _checkProgressDialog = false;
    private readonly DialogOptions _dialogOptions = new() { FullWidth = true };
    private string _checkingModel = "";
    private string _checkingToolName = "";
    private bool _findingDetailsVisible = false;
    private LogMessage? _currentFinding = null;
    private string _searchString = "";
    private bool FindingsScopeAllModels = false;

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

    // Set when the correction context menu opens with a provisional position. On the next after-render
    // OnAfterRenderAsync re-measures the now-rendered menu and clamps it within the viewport, writing
    // the result back into _contextMenuX/_contextMenuY so .NET stays the source of truth (later
    // keystroke re-renders keep the clamped spot). Flag-gated to run once per open, avoiding jitter.
    private bool _repositionContextMenu;

    // Code rendering state
    private bool _isLoadingCode = false;
    private record RenderCacheKey(string ModelId, bool ShowAnnotations, bool ShowHighlighted, bool ExcludeClassDefs);
    private readonly Dictionary<RenderCacheKey, List<string>> _renderCache = new();

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

    private async void OnLogMessagesChanged()
    {
        RecomputeMisspelledWords();
        await InvokeAsync(StateHasChanged);
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

    private async Task ToggleFormattingExclusionAsync()
    {
        if (_currentModelNode == null || string.IsNullOrEmpty(_currentRepositoryId))
            return;

        var repository = RepositoryService.GetRepository(_currentRepositoryId);
        if (repository?.StyleSettings == null)
            return;

        var modelId = _currentModelNode.Id;
        var excluded = repository.StyleSettings.FormattingExcludedModels;

        if (_isExcludedFromFormatting)
        {
            // Re-include the model
            excluded.Remove(modelId);
            _isExcludedFromFormatting = false;
        }
        else
        {
            // Exclude the model
            if (!excluded.Contains(modelId))
                excluded.Add(modelId);
            _isExcludedFromFormatting = true;

            // If the file is modified in VCS, revert it to discard formatting changes
            if (_isModelModified && !string.IsNullOrEmpty(_currentRelativeFilePath))
            {
                await RepositoryService.RevertFilesAsync(_currentRepositoryId, [_currentRelativeFilePath]);

                // Reload the file to pick up the reverted content
                var fileId = _currentModelNode.ContainingFileId ?? "";
                var fileNode = LibraryDataService.CombinedGraph.GetNode<FileNode>(fileId);
                if (fileNode != null)
                {
                    await LibraryDataService.ReloadFileAsync(fileNode.FilePath);
                    // Re-fetch the model node since it may have been replaced
                    _currentModelNode = LibraryDataService.CombinedGraph.GetNode<ModelNode>(modelId);
                }

                // Refresh VCS status
                CheckModelVcsStatus();

                // Re-render
                OnModelSelected();
            }
        }

        await RepositoryService.SaveRepositorySettingsAsync();
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

        // Find the library and repository for this model
        var library = LibraryDataService.Libraries
            .FirstOrDefault(l => l.ModelIds.Contains(_currentModelNode.Id));
        if (library == null || string.IsNullOrEmpty(library.RepositoryId))
            return;

        var repository = RepositoryService.GetRepository(library.RepositoryId);
        if (repository is not { VcsType: not RepositoryVcsType.Local })
            return;

        _currentRepositoryId = repository.Id;
        _isExcludedFromFormatting = repository.StyleSettings?.IsModelExcludedFromFormatting(_currentModelNode.Id) ?? false;

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

                // Use the raw source code for the current working copy version
                var currentCode = _currentModelNode.Definition.ModelicaCode;
                var currentPrefix = _currentModelNode.ElementPrefix;
                _modifiedModelCode = string.IsNullOrEmpty(currentPrefix)
                    ? currentCode
                    : currentPrefix + " " + currentCode;
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

        // When a model has parser errors, skip ModelicaRenderer entirely — rendering a
        // partial/invalid parse tree produces misleading or truncated output. Show the raw
        // source from Definition.ModelicaCode so the user sees exactly what's in the file,
        // unmodified, with no syntax highlighting. Placeholder nodes (whole-file failures)
        // benefit from this the most — they carry the full file as ModelicaCode.
        if (_currentModelNode.HasParserErrors)
        {
            ShowRawSource(selectedModelId);
            return;
        }

        if (_renderCache.TryGetValue(cacheKey, out var cachedCode))
        {
            // Cache hit — set code and render immediately BEFORE any await.
            // Any await would yield to the Blazor sync context which is blocked
            // by MainLayout/LibraryBrowser re-rendering 27K tree nodes.
            _highlightedCode = cachedCode;
            _lines = Math.Max(10, cachedCode.Count);
            _modelicaCode = string.Join("\n", cachedCode);
            _isLoadingCode = false;

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
            // Rendering follows the rules of the repository the class belongs to, the same ones the
            // formatter would apply on save. There is no app-wide copy of these to fall back on.
            var formatting = StyleSettingsForModel(modelNode.Id)?.ToFormattingOptions() ?? FormattingOptions.None;

            // Render parse/format on a background thread. Display the code as soon as this
            // completes — crucially, do NOT gate code display on the VCS status check.
            // CheckModelVcsStatus calls GetWorkingCopyChanges, which shells out to the
            // git/svn CLI and can be slow or block; coupling the two via Task.WhenAll meant
            // a stalled VCS call left the loading spinner spinning forever even though the
            // rendered code was ready in milliseconds. The VCS check now runs independently
            // (mirroring the cache-hit path) and only updates the modified/diff indicator.
            var renderTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (parseTree, errors) =
                    ModelicaParserHelper.ParseWithErrors(modelNode.Definition.ModelicaCode ?? "");
                modelNode.Definition.ParsedCode = parseTree;
                var parseDuration = sw.ElapsedMilliseconds;

                sw.Restart();
                var visitor = new ModelicaRenderer(
                    renderForCodeEditor: showHighlighted,
                    showAnnotations: showAnnotations,
                    excludeClassDefinitions: excludeClassDefs,
                    formatting: formatting);
                visitor.VisitStored_definition(parseTree);
                var renderDuration = sw.ElapsedMilliseconds;

                return (visitor.Code, visitor.Code.Count, parseDuration, renderDuration);
            });

            _ = renderTask.ContinueWith(async _ =>
            {
                LoggingService.Debug("CodeReview",
                    $"  ContinueWith fired at {totalSw.ElapsedMilliseconds}ms");

                if (NavState.ModelID != selectedModelId)
                    return;

                try
                {
                    var (code, lineCount, parseMs, renderMs) = renderTask.Result;

                    // If the model has an element prefix (redeclare, replaceable, etc.),
                    // prepend it to the class definition line (skipping any within clause)
                    PrependElementPrefix(code, modelNode.ElementPrefix, showHighlighted);

                    LoggingService.Debug("CodeReview",
                        $"  Parse: {parseMs}ms, Render: {renderMs}ms, Lines: {lineCount}");

                    var invokeAsyncSw = System.Diagnostics.Stopwatch.StartNew();
                    await InvokeAsync(() =>
                    {
                        LoggingService.Debug("CodeReview",
                            $"  InvokeAsync started after {invokeAsyncSw.ElapsedMilliseconds}ms wait");

                        _highlightedCode = code;
                        _lines = Math.Max(10, lineCount);
                        _modelicaCode = string.Join("\n", code);
                        _isLoadingCode = false;

                        // Store in cache for future clicks
                        _renderCache[cacheKey] = code;

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
    /// Loads the full original contents of the file that contains the given model, so that
    /// models whose extraction was truncated by a parse failure still show the whole file
    /// to the user rather than just the successfully-extracted prefix. Returns <c>null</c>
    /// if the containing file can't be located on disk — the caller will then fall back to
    /// <c>Definition.ModelicaCode</c>, which for placeholder nodes is already the full file.
    /// </summary>
    private string? LoadOriginalFileContents(ModelNode? model)
    {
        if (model == null || string.IsNullOrEmpty(model.ContainingFileId))
            return null;

        var fileNode = LibraryDataService.CombinedGraph.GetNode<FileNode>(model.ContainingFileId);
        if (fileNode == null || string.IsNullOrEmpty(fileNode.FilePath) || !File.Exists(fileNode.FilePath))
            return null;

        try
        {
            return ModelicaFileEncoding.ReadAllTextOnly(fileNode.FilePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Displays the raw, unrendered source of the current model. Used when the model has
    /// parser errors — running ModelicaRenderer on a broken parse tree yields misleading
    /// output, so we show the file contents verbatim instead. For non-placeholder models
    /// the file is read fresh from disk so the user sees the *entire* original file,
    /// not just the sub-range that the extractor managed to pull out before it failed.
    /// Each line is HTML-encoded so that any <c>&lt;</c> or <c>&gt;</c> characters in the
    /// source don't get mistaken for CodeViewer markup tags; no syntax highlighting is applied.
    /// </summary>
    private void ShowRawSource(string selectedModelId)
    {
        var raw = LoadOriginalFileContents(_currentModelNode)
            ?? _currentModelNode?.Definition.ModelicaCode
            ?? string.Empty;
        var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n')
            .Select(l => System.Web.HttpUtility.HtmlEncode(l) ?? string.Empty)
            .ToList();

        _highlightedCode = lines;
        _lines = Math.Max(10, lines.Count);
        _modelicaCode = normalized;
        _isLoadingCode = false;

        // Deliberately do NOT populate _renderCache — raw content isn't tied to the
        // render-option cache key and we don't want it served to a later cache hit if the
        // model is ever cleared of errors (e.g. after a reload/fix).

        OnFindingsScopeChanged(FindingsScopeAllModels);
        StateHasChanged();

        // Run VCS check in the background so the diff toggle reflects working-copy state.
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
            StateHasChanged();
        });
    }

    private async void OnModelChecked(ModelCheckResult result)
    {
        await InvokeAsync(() =>
        {
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
            StateHasChanged();
        });
    }

    #endregion

    private void CheckInDymola()
    {
        if (_currentModelNode == null)
            return;

        _checkingToolName = DymolaCheckingService.ToolName;
        _checkCancellationTokenSource = new CancellationTokenSource();

        // Show progress dialog for packages
        if (_currentModelNode.ClassType == "package")
        {
            _checkProgressDialog = true;
        }

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

        // Show progress dialog for packages
        if (_currentModelNode.ClassType == "package")
        {
            _checkProgressDialog = true;
        }

        // StartCheckingAsync runs on background thread and returns immediately
        _ = OpenModelicaCheckingService.StartCheckingAsync(
            _currentModelNode,
            LibraryDataService.CombinedGraph,
            _checkCancellationTokenSource.Token);
    }

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

        return FilterFunc(element, _searchString + (FindingsScopeAllModels ? " " + NavState.ModelID : ""));
    }

    private void OnChangesOnlyChanged(bool value)
    {
        ShowChangesOnly = value;
        StateHasChanged();
    }

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

            // Where each class sits on disk, through the same ClassLocation the CLI's report uses, so
            // "line 42 of that file" means the same thing in both exports. Findings carry
            // class-relative lines; a report that names a file has to name the file's line.
            var graph = LibraryDataService.CombinedGraph;
            var locations = ClassLocation.ForGraph(graph);

            // And the library each class belongs to, so the path can be written relative to it — the
            // CLI writes `File` relative to the library it was pointed at (CheckReport.RelativeFileFor).
            var libraryRootByModel = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var library in LibraryDataService.Libraries)
            {
                var root = library.SourceType == LibrarySourceType.File
                    ? Path.GetDirectoryName(library.SourcePath) ?? library.SourcePath
                    : library.SourcePath;
                if (string.IsNullOrEmpty(root))
                    continue;
                foreach (var id in library.ModelIds)
                    libraryRootByModel[id] = root;
            }

            var payload = new
            {
                tool = "mlqt-gui",
                exported = DateTime.Now.ToString("o"),
                project = RepositoryService.GetActiveProject()?.Name,
                findingCount = messages.Count,
                findings = messages
                    .OrderBy(m => m.ModelName, StringComparer.Ordinal)
                    .ThenBy(m => m.LineNumber)
                    .ThenBy(m => m.RuleId ?? string.Empty, StringComparer.Ordinal)
                    // Field names AND meanings match the CLI's --format json findings array, so the
                    // two exports can be diffed without translating between them first. The names
                    // matched on their own for a while and the meanings did not: B1 gave every report
                    // the line in the FILE and left this export on the class-relative line the code
                    // viewer wants, so build/Compare-Findings.ps1 — which pairs the two up on
                    // model + rule + line — reported nearly every finding as exclusive to both sides.
                    // Both numbers are here now, named as the CLI names them.
                    .Select(m => new
                    {
                        RuleId = m.RuleId,
                        Severity = m.Severity,
                        Status = BaselineStatus.HasBaseline ? BaselineStatus.StatusOf(m)?.ToString() : null,
                        Model = m.ModelName,
                        Element = m.ElementPath,
                        Line = FileLineOf(m, locations),
                        ModelLine = m.LineNumber,
                        Message = m.Summary,
                        Fingerprint = m.Fingerprint,
                        File = ReportPathOf(m, locations, libraryRootByModel),
                        Source = m.Source,
                        Details = string.IsNullOrEmpty(m.Details) ? null : m.Details
                    })
                    .ToList()
            };

            var name = $"mlqt-findings-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var target = Path.Combine(folder, name);
            var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

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
            if (!BaselineStatus.HasBaseline)
                return $"{all.Count} Findings to review";

            var snapshot = BaselineStatus.Snapshot;
            var changed = all.Count(snapshot.IsChangedFromBaseline);
            return ShowChangesOnly
                ? $"{changed} changed of {all.Count} findings"
                : $"{all.Count} Findings to review ({changed} changed vs baseline)";
        }
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

    private bool FilterFunc(LogMessage element, string searchString)
    {
        if (string.IsNullOrWhiteSpace(searchString))
            return true;

        if (FindingsScopeAllModels && NavState.ModelID.Length > 0) {
            if (element.ModelName == NavState.ModelID) {
                //Remove the model name from the search string before continuing
                searchString = searchString.Replace(NavState.ModelID,"").Trim();
                if (searchString.Length > 0) {
                    var strings = searchString.ToLower().Split(' ');
                    if (element.Summary.Length > 0 && strings.Any(element.Summary.ToLower().Contains))
                        return true;
                    if (element.Details.Length > 0 && strings.Any(element.Details.ToLower().Contains))
                        return true;
                    if (element.Severity.Length > 0 && strings.Any(element.Severity.ToLower().Contains))
                        return true;
                    return false;
                }
                return true;
            }
        }
        else {
            var strings = searchString.Split(' ');
            if (strings.Any(element.ModelName.Contains))
                return true;

            var stringsLowerCase = searchString.ToLower().Split(' ');
            if (element.Summary.Length > 0 && stringsLowerCase.Any(element.Summary.ToLower().Contains))
                return true;
            if (element.Details.Length > 0 && stringsLowerCase.Any(element.Details.ToLower().Contains))
                return true;
            if (element.Severity.Length > 0 && stringsLowerCase.Any(element.Severity.ToLower().Contains))
                return true;
        }
        return false;
    }

    private void ResolveFinding()
    {
        if (_currentFinding != null)
            CodeReviewService.RemoveLogMessage(_currentFinding);
        _findingDetailsVisible = false;
        StateHasChanged();
    }

    private bool _suppressing;

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
            .Where(m => targetNode.Id == m.Id || targetNode.Id.StartsWith(m.Id + ".", StringComparison.Ordinal))
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

        // Scope to the component when the finding names a simple one; otherwise waive it for the class.
        var component = finding.ElementPath is { Length: > 0 } ep && !ep.Contains('.') ? ep : null;

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

            // Drop the now-waived finding(s) for this rule on this model — a re-check would not report
            // them. Scoped to the target model (a class-level waiver doesn't affect sibling classes) and,
            // for a component waiver, to that component.
            CodeReviewService.RemoveLogMessagesByPredicate(m =>
                m.ModelName == finding.ModelName && m.RuleId == finding.RuleId
                && (component is null || m.ElementPath == component));

            _findingDetailsVisible = false;
            OnModelSelected();   // re-render with the annotated content and refresh VCS status
            Snackbar.Add(
                $"Suppressed rule '{finding.RuleId}'{(component is null ? "" : $" on '{component}'")}.",
                MudBlazor.Severity.Success);
        }
        finally
        {
            _suppressing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

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
        try
        {
            var offsets = await JSRuntime.InvokeAsync<double[]>("spellCheck.getScroll", ".code-viewer");
            _pendingScroll = offsets is { Length: >= 2 } ? (offsets[0], offsets[1]) : null;
            _scrollBaselineCode = _highlightedCode;
        }
        catch (Exception)
        {
            // No view to read (not rendered yet, or torn down) — land wherever the re-render lands.
            _pendingScroll = null;
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

    private static bool IsSpellingFinding(LogMessage? finding) =>
        SpellingMessage.Is(finding?.Summary);

    /// <summary>
    /// The word the rule flagged. Read through the shared format rather than from the finding's
    /// Discriminator: that field exists to make the fingerprint unique, and for a documentation
    /// finding it carries the section as well as the word ("documentation info:tyre"), which
    /// underlined nothing because no such text appears in the source.
    /// </summary>
    private static string? ExtractMisspelledWord(LogMessage? finding) =>
        SpellingMessage.WordFrom(finding?.Summary);

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

        // Record the word itself, not a possessive of it: the checker already accepts "Stodola's"
        // once "Stodola" is accepted, and the list is a file the team reads and reviews.
        var accepted = ModelicaParser.SpellChecking.SpellChecker.PossessiveBaseOf(word) ?? word;
        await CustomDictionaryService.AddWordAsync(repositoryRoot, accepted);

        // Clear the findings this now covers — both the word and its possessive — and only within
        // the repository that accepted it. It stays a finding elsewhere, which is the point of the
        // list being per repository: another team's library has not agreed to the word.
        var repositoryModelIds = LibraryDataService.Libraries
            .Where(l => DictionaryScope.RootForLibrary(RepositoryService, l) == repositoryRoot)
            .SelectMany(l => l.ModelIds)
            .ToHashSet(StringComparer.Ordinal);

        CodeReviewService.RemoveLogMessagesByPredicate(m =>
            repositoryModelIds.Contains(m.ModelName) && IsSpellingFinding(m) &&
            IsNowAccepted(accepted, ExtractMisspelledWord(m)));

        _contextMenuOpen = false;
        _suggestions = null;
        _findingDetailsVisible = false;
        RecomputeMisspelledWords();
        StateHasChanged();
    }

    /// <summary>
    /// Whether a flagged word is covered by <paramref name="acceptedWord"/> — the word itself, or a
    /// possessive of it. Case is ignored because the word list is: a word accepted in one casing is
    /// accepted in any.
    /// </summary>
    private static bool IsNowAccepted(string acceptedWord, string? flagged) =>
        flagged is not null &&
        string.Equals(
            ModelicaParser.SpellChecking.SpellChecker.PossessiveBaseOf(flagged) ?? flagged,
            acceptedWord, StringComparison.OrdinalIgnoreCase);

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
            CodeReviewService.RemoveLogMessagesByPredicate(m =>
                m.ModelName == modelId && IsSpellingFinding(m) &&
                IsNowAccepted(accepted, ExtractMisspelledWord(m)));

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
