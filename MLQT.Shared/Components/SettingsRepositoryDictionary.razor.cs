namespace MLQT.Shared.Components;

public partial class SettingsRepositoryDictionary : IDisposable
{
    [Inject] private ICustomDictionaryService CustomDictionaryService { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>The repository whose accepted spellings these are.</summary>
    [Parameter] public Repository? Repository { get; set; }

    private IReadOnlyCollection<string> _words = [];
    private List<string> _filtered = new();
    private string _newWord = "";
    private string _filter = "";
    private string? _loadedRoot;

    private string? Root => string.IsNullOrEmpty(Repository?.LocalPath) ? null : Repository!.LocalPath;

    protected override void OnInitialized()
    {
        CustomDictionaryService.OnDictionaryChanged += OnDictionaryChanged;
        base.OnInitialized();
    }

    public void Dispose() => CustomDictionaryService.OnDictionaryChanged -= OnDictionaryChanged;

    protected override async Task OnParametersSetAsync()
    {
        // Re-read when pointed at a different repository, not just on first render — one instance of
        // this panel is reused as the user moves between repositories.
        if (Root is not null && Root != _loadedRoot)
        {
            _loadedRoot = Root;
            await ReloadAsync();
        }
    }

    private async void OnDictionaryChanged(string repositoryRoot)
    {
        if (!string.Equals(repositoryRoot, Root, StringComparison.OrdinalIgnoreCase))
            return;

        await ReloadAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ReloadAsync()
    {
        if (Root is null)
            return;

        _words = await CustomDictionaryService.LoadAsync(Root);
        ApplyFilter();
    }

    private void OnFilterChanged(string value)
    {
        _filter = value;
        ApplyFilter();
    }

    private void ApplyFilter() =>
        _filtered = string.IsNullOrWhiteSpace(_filter)
            ? _words.ToList()
            : _words.Where(w => w.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();

    private async Task OnAddWordKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await AddWord();
    }

    private async Task AddWord()
    {
        if (Root is null || string.IsNullOrWhiteSpace(_newWord))
            return;

        await CustomDictionaryService.AddWordAsync(Root, _newWord.Trim());
        _newWord = "";
        await ReloadAsync();
    }

    private async Task RemoveWord(string word)
    {
        if (Root is null)
            return;

        await CustomDictionaryService.RemoveWordAsync(Root, word);
        await ReloadAsync();
    }

    private async Task ImportFromFile()
    {
        if (Root is null)
            return;

        var content = await FilePickerService.PickAndReadFileAsync(".txt");
        if (content is null)
            return;

        // PickAndReadFileAsync hands back the contents, not the path, so stage them where the merge
        // can read them and clean up afterwards.
        var staged = Path.Combine(Path.GetTempPath(), $"mlqt-import-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(staged, content);
            var added = await CustomDictionaryService.MergeFromAsync(Root, staged);
            await ReloadAsync();
            Snackbar.Add(
                added == 0 ? "No new words to import." : $"Imported {added} new word(s).",
                added == 0 ? MudBlazor.Severity.Info : MudBlazor.Severity.Success);
        }
        finally
        {
            try { File.Delete(staged); } catch (IOException) { }
        }
    }

    private async Task ImportMachineDictionary()
    {
        var legacy = CustomDictionaryService.LegacyMachineDictionaryPath;
        if (Root is null || legacy is null)
            return;

        var added = await CustomDictionaryService.MergeFromAsync(Root, legacy);
        await ReloadAsync();
        Snackbar.Add(
            added == 0
                ? "The machine list has no words this repository does not already accept."
                : $"Imported {added} word(s) from the machine list. Commit .mlqt/dictionary.txt to share them.",
            added == 0 ? MudBlazor.Severity.Info : MudBlazor.Severity.Success);
    }

    private async Task ExportToFile()
    {
        if (Root is null)
            return;

        var folder = await FilePickerService.PickFolderAsync("Select a folder to export the word list to");
        if (string.IsNullOrEmpty(folder))
            return;

        var target = Path.Combine(folder, $"{Repository!.Name}-dictionary.txt");
        await CustomDictionaryService.ExportAsync(Root, target);
        Snackbar.Add($"Exported {_words.Count} word(s) to {target}", MudBlazor.Severity.Success);
    }
}
