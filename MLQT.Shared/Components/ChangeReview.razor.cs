using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;
using RevisionControl;

namespace MLQT.Shared.Components;

public partial class ChangeReview
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;

    [Parameter]
    public string RepositoryId { get; set; } = "";

    [Parameter]
    public int SelectedFilesCount { get; set; }= new();
    [Parameter]
    public EventCallback<int> SelectedFilesCountChanged { get; set; }
    private HashSet<string> _selectedFiles = new();
    
    [Parameter]
    public List<VcsWorkingCopyFile> ChangedFiles { get; set; }= new();
    [Parameter]
    public EventCallback<List<VcsWorkingCopyFile>> ChangedFilesChanged { get; set; }

    [Parameter]
    public string? ErrorMessage {get;set;}
    [Parameter]
    public EventCallback<string?> ErrorMessageChanged { get; set; }

    [Parameter]
    public bool IsLoading {get;set;} = true;
    [Parameter]
    public EventCallback<bool> IsLoadingChanged { get; set; }

    private Repository? _repository;

    // Tree structure for MudTreeView
    private List<TreeItemData<FileTreeNode>> _treeItems = new();

    // Diff view state
    private string? _selectedFilePath;
    private string? _originalContent;
    private string? _modifiedContent;
    private bool _isLoadingDiff = false;
    private DiffViewMode _diffViewMode = DiffViewMode.Unified;

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(RepositoryId))
        {
            _repository = RepositoryService.GetRepository(RepositoryId);
            await LoadChanges();
        }
    }

    public async Task LoadChanges()
    {
        IsLoading = true;
        await IsLoadingChanged.InvokeAsync(IsLoading);
        ErrorMessage = null;
        await ErrorMessageChanged.InvokeAsync(ErrorMessage);
        StateHasChanged();

        try
        {
            // Use local variables to avoid race condition with parameter binding
            List<VcsWorkingCopyFile> loadedFiles = new();
            HashSet<string> selectedFiles = new();

            List<TreeItemData<FileTreeNode>> treeItems = new();

            await Task.Run(() =>
            {
                loadedFiles = RepositoryService.GetWorkingCopyChanges(RepositoryId);
                // Same canonical form as the tree node FullPath values - see VcsRelativePath.
                selectedFiles = new HashSet<string>(loadedFiles.Select(f => VcsRelativePath.Canonical(f.Path)));
                treeItems = BuildFileTree(loadedFiles);
            });

            // Update both parameters and notify parent together
            ChangedFiles = loadedFiles;
            _selectedFiles = selectedFiles;
            _treeItems = treeItems;
            SelectedFilesCount = _selectedFiles.Count;
            await Task.WhenAll(
                ChangedFilesChanged.InvokeAsync(ChangedFiles),
                SelectedFilesCountChanged.InvokeAsync(_selectedFiles.Count)
            );

            _selectedFilePath = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await ErrorMessageChanged.InvokeAsync(ErrorMessage);
        }
        finally
        {
            IsLoading = false;
            await IsLoadingChanged.InvokeAsync(IsLoading);
            StateHasChanged();
        }
    }

    /// <summary>
    /// The changed files as a folder tree, folders before files and alphabetical within each level.
    /// </summary>
    /// <remarks>
    /// <para>The separator normalisation is the part that has to hold: Git reports paths with
    /// <c>/</c> and SVN on Windows with <c>\</c>, and a tree that treats them differently shows one
    /// repository's changes as a flat list of long names and the other's as a tree.</para>
    ///
    /// <para><b>It canonicalises on <c>/</c>, and which way round that goes is not cosmetic.</b> It
    /// used to canonicalise on <c>\</c>, and <see cref="FileTreeNode.FullPath"/> is not only a tree
    /// key — it is handed to <c>Path.Combine</c> to read the working copy. On Linux
    /// <c>Lib\Thing.mo</c> is one file name containing backslashes, not a relative path, so
    /// <c>File.Exists</c> answered false and the commit dialog showed **no modified content at all**
    /// while the HEAD side still worked, because the git layer normalises for itself. See
    /// <see cref="VcsRelativePath"/>.</para>
    /// </remarks>
    internal static List<TreeItemData<FileTreeNode>> BuildFileTree(List<VcsWorkingCopyFile> changedFiles)
    {
        var roots = new List<FileTreeNode>();
        var nodeMap = new Dictionary<string, FileTreeNode>();

        foreach (var file in changedFiles.OrderBy(f => f.Path))
        {
            var normalizedPath = VcsRelativePath.Canonical(file.Path);
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";
            FileTreeNode? parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isFile = i == parts.Length - 1;
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                if (!nodeMap.TryGetValue(currentPath, out var node))
                {
                    node = new FileTreeNode
                    {
                        Name = part,
                        FullPath = currentPath,
                        IsFile = isFile,
                        Status = isFile ? file.Status : null
                    };
                    nodeMap[currentPath] = node;

                    if (parent != null)
                    {
                        parent.Children.Add(node);
                    }
                    else
                    {
                        roots.Add(node);
                    }
                }

                parent = node;
            }
        }

        // Sort children: folders first, then files, alphabetically
        SortTreeNodes(roots);

        // Convert to TreeItemData
        return ConvertToTreeItemData(roots);
    }

    private static void SortTreeNodes(List<FileTreeNode> nodes)
    {
        nodes.Sort((a, b) =>
        {
            if (a.IsFile != b.IsFile)
                return a.IsFile ? 1 : -1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var node in nodes)
        {
            if (node.Children.Count > 0)
                SortTreeNodes(node.Children);
        }
    }

    private static List<TreeItemData<FileTreeNode>> ConvertToTreeItemData(List<FileTreeNode> nodes, int depth = 0)
    {
        return nodes.Select(node =>
        {
            // Auto-expand the top 2 directory levels; deeper directories start collapsed.
            // Children are only pre-populated for depth < 2; deeper nodes use ServerData
            // lazy loading so the dialog renders quickly even with thousands of files.
            var hasChildren = !node.IsFile && node.Children.Count > 0;
            var expanded = hasChildren && depth < 2;
            return new TreeItemData<FileTreeNode>
            {
                Value = node,
                Expanded = expanded,
                Expandable = hasChildren,
                Children = hasChildren && depth < 2
                    ? ConvertToTreeItemData(node.Children, depth + 1)
                    : null
            };
        }).ToList();
    }

    private Task<IReadOnlyCollection<TreeItemData<FileTreeNode>>> LoadServerData(FileTreeNode? parentNode)
    {
        if (parentNode == null)
            return Task.FromResult<IReadOnlyCollection<TreeItemData<FileTreeNode>>>(_treeItems);

        var children = parentNode.Children.Select(node =>
        {
            var hasChildren = !node.IsFile && node.Children.Count > 0;
            return new TreeItemData<FileTreeNode>
            {
                Value = node,
                Expanded = false,
                Expandable = hasChildren,
                Children = null  // Always lazy-load deeper nodes
            };
        }).ToList();

        return Task.FromResult<IReadOnlyCollection<TreeItemData<FileTreeNode>>>(children);
    }

    private Task OnExpandNode(ITreeItemData<FileTreeNode> node, bool expanded)
    {
        node.Expanded = expanded;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists children loaded on demand by <see cref="LoadServerData"/> back into the
    /// <c>node.Children</c> data tree. Required by MudBlazor 9.4+: with both
    /// <c>ItemTemplate</c> and <c>Items</c> set, selectable values are derived from the
    /// <c>Items</c> data tree, so a lazily-loaded child that isn't written back is treated
    /// as unselectable and clicking it resets selection to default (the click does nothing).
    /// See LibraryBrowser for the same fix.
    /// </summary>
    private void OnNodeChildrenLoaded(
        ITreeItemData<FileTreeNode> node, IReadOnlyCollection<ITreeItemData<FileTreeNode>> children)
    {
        node.Children = children?.ToList();
    }

    private List<string> GetAllChildFiles(FileTreeNode node)
    {
        var files = new List<string>();
        if (node.IsFile)
        {
            files.Add(node.FullPath);
        }
        else
        {
            foreach (var child in node.Children)
            {
                files.AddRange(GetAllChildFiles(child));
            }
        }
        return files;
    }

    private bool? GetFolderCheckState(FileTreeNode folder)
    {
        var childFiles = GetAllChildFiles(folder);
        var selectedCount = childFiles.Count(f => _selectedFiles.Contains(f));
        if (selectedCount == childFiles.Count && childFiles.Count > 0) return true;
        if (selectedCount > 0) return null;
        return false;
    }

    private async Task OnFolderSelectionChanged(FileTreeNode folder, bool selected)
    {
        var childFiles = GetAllChildFiles(folder);
        foreach (var file in childFiles)
        {
            if (selected)
                _selectedFiles.Add(file);
            else
                _selectedFiles.Remove(file);
        }
        SelectedFilesCount = _selectedFiles.Count;
        await SelectedFilesCountChanged.InvokeAsync(SelectedFilesCount);
    }

    private async Task OnTreeNodeSelected(FileTreeNode? node)
    {
        if (node != null && node.IsFile)
        {
            await SelectFile(node.FullPath);
        }
    }

    private async Task SelectFile(string path)
    {
        if (_selectedFilePath == path)
            return;

        _selectedFilePath = path;
        _isLoadingDiff = true;
        _originalContent = null;
        _modifiedContent = null;
        StateHasChanged();

        try
        {
            await Task.Run(() =>
            {
                if (_repository == null)
                    return;

                // Get original content from HEAD (may be null for new files)
                // Use raw content — do NOT pass through ModelicaRenderer, as that would
                // normalize formatting and hide the actual differences being committed.
                _originalContent = RepositoryService.GetFileContentAtRevision(RepositoryId, path, "HEAD");

                // Get modified content from working copy.
                // path is relative to VcsRootPath, which may be a parent of LocalPath.
                var fullPath = Path.Combine(_repository.VcsRootPath, path);
                if (File.Exists(fullPath))
                {
                    _modifiedContent = ModelicaFileEncoding.ReadAllTextOnly(fullPath);
                }
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load diff: {ex.Message}";
            await ErrorMessageChanged.InvokeAsync(ErrorMessage);
        }
        finally
        {
            _isLoadingDiff = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnFileSelectionChanged(string path, bool selected)
    {
        if (selected)
        {
            _selectedFiles.Add(path);
        }
        else
        {
            _selectedFiles.Remove(path);
        }
        SelectedFilesCount = _selectedFiles.Count;
        await SelectedFilesCountChanged.InvokeAsync(SelectedFilesCount);
    }

    private async Task SelectAllFiles()
    {
        _selectedFiles = new HashSet<string>(ChangedFiles.Select(f => VcsRelativePath.Canonical(f.Path)));
        SelectedFilesCount = _selectedFiles.Count;
        await SelectedFilesCountChanged.InvokeAsync(SelectedFilesCount);
    }

    private async Task DeselectAllFiles()
    {
        _selectedFiles.Clear();
        SelectedFilesCount = _selectedFiles.Count;
        await SelectedFilesCountChanged.InvokeAsync(SelectedFilesCount);
    }

    internal sealed class FileTreeNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsFile { get; set; }
        public VcsFileStatus? Status { get; set; }
        public List<FileTreeNode> Children { get; set; } = new();
    }

    public List<string> GetSelectedFiles()
    {
        return _selectedFiles.ToList();
    }
}
