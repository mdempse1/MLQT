namespace MLQT.Shared.Pages;

public partial class ExternalResources : IDisposable
{
    [Inject] private IExternalResourceService ExternalResourceService { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;

    private MudTreeView<ResourceTreeNode>? _treeView;
    private List<TreeItemData<ResourceTreeNode>> _topLevelItems = new();
    private List<ExternalResourceReference> _allResources = new();
    private List<ResourceWarning> _allWarnings = new();
    private ResourceTreeNode? _selectedResource;
    private List<ExternalResourceReference> _referencingModels = new();

    // Internal tree structure: directory path -> children nodes
    private Dictionary<string, List<ResourceTreeNode>> _treeChildren = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string _commonRoot = "";

    // File type filter
    private IReadOnlyCollection<string> _selectedFileTypes = new List<string> { "data", "ccode", "lib" };
    private IReadOnlyCollection<string> _selectedWarningTypes = new List<string>();
    private int _missingCount;
    private int _absolutePathCount;
    private int _treeKey = 0; // Changing this forces MudTreeView to re-render
    private bool _isRunningAnalysis = false;

    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mat", ".csv", ".txt", ".dat", ".json", ".xml", ".sdf", ".hdf", ".h5"
    };

    private static readonly HashSet<string> CCodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cpp", ".h", ".hpp"
    };

    private static readonly HashSet<string> LibExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lib", ".dll", ".a", ".so"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".ico", ".tiff", ".webp"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".html", ".htm", ".doc", ".docx", ".md"
    };

    protected override void OnInitialized()
    {
        NavState.OnDeferredAnalysisCompleted += OnDeferredAnalysisCompleted;
        RebuildTree();
        base.OnInitialized();
    }

    public void Dispose()
    {
        NavState.OnDeferredAnalysisCompleted -= OnDeferredAnalysisCompleted;
    }

    private async void OnDeferredAnalysisCompleted()
    {
        await InvokeAsync(() =>
        {
            RebuildTree();
            StateHasChanged();
        });
    }

    private async Task RunExternalResourceAnalysisAsync()
    {
        _isRunningAnalysis = true;
        StateHasChanged();
        try
        {
            await NavState.RunDeferredExternalResourcesAsync();
        }
        finally
        {
            _isRunningAnalysis = false;
            RebuildTree();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void RebuildTree()
    {
        _allResources = ExternalResourceService.GetAllResources();
        _allWarnings = ExternalResourceService.GetWarnings();
        _missingCount = _allWarnings.Count(w => w.WarningType == ResourceWarningType.MissingFile);
        _absolutePathCount = _allWarnings.Count(w => w.WarningType == ResourceWarningType.AbsolutePath);
        BuildTreeStructure();
        _topLevelItems = GetTopLevelItems();
    }

    private void BuildTreeStructure()
    {
        _treeChildren.Clear();
        _commonRoot = "";

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Separate resolved (absolute path) resources from unresolved ones.
        // Only resolved resources participate in common root calculation.
        var resolvedFileNodes = new Dictionary<string, ResourceTreeNode>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var unresolvedFileNodes = new List<ResourceTreeNode>();

        foreach (var resource in _allResources)
        {
            if (!string.IsNullOrEmpty(resource.ResolvedPath))
            {
                var displayPath = Path.GetFullPath(resource.ResolvedPath);
                if (resolvedFileNodes.ContainsKey(displayPath))
                    continue;

                resolvedFileNodes[displayPath] = CreateFileNode(resource, displayPath, comparison);
            }
            else
            {
                // Strip modelica:// prefix and convert to display path
                var raw = resource.RawPath;
                if (raw.StartsWith("modelica://", StringComparison.OrdinalIgnoreCase))
                    raw = raw.Substring("modelica://".Length);
                var displayPath = raw.Replace('/', Path.DirectorySeparatorChar);

                if (string.IsNullOrEmpty(displayPath))
                    continue;

                // Check for duplicate unresolved paths
                if (unresolvedFileNodes.Any(n => string.Equals(n.FullPath, displayPath, comparison)))
                    continue;

                var node = CreateFileNode(resource, displayPath, comparison);
                node.HasWarning = true;
                node.IsMissing = true;
                node.WarningMessage = "Could not resolve path";
                unresolvedFileNodes.Add(node);
            }
        }

        if (resolvedFileNodes.Count == 0 && unresolvedFileNodes.Count == 0)
            return;

        // Compute common root from resolved (absolute) paths only
        if (resolvedFileNodes.Count > 0)
        {
            var allDirs = resolvedFileNodes.Keys
                .Select(p => Path.GetDirectoryName(p) ?? "")
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToList();

            _commonRoot = ResourceTreeHelper.FindCommonDirectoryRoot(allDirs);
        }

        // Insert resolved file nodes into the tree
        foreach (var (filePath, fileNode) in resolvedFileNodes)
        {
            InsertFileIntoTree(filePath, fileNode, comparison);
        }

        // Insert unresolved resources under an "Unresolved References" virtual node
        if (unresolvedFileNodes.Count > 0)
        {
            const string unresolvedRoot = "<Unresolved References>";
            var unresolvedDirNode = new ResourceTreeNode
            {
                Name = unresolvedRoot,
                FullPath = unresolvedRoot,
                IsDirectory = true,
                HasWarning = true,
                WarningMessage = "These resource paths could not be resolved to files on disk"
            };
            AddChildNode(_commonRoot, unresolvedDirNode);

            foreach (var node in unresolvedFileNodes)
            {
                AddChildNode(unresolvedRoot, node);
            }
        }

        // Sort each directory's children: directories first, then files, alphabetically
        foreach (var key in _treeChildren.Keys.ToList())
        {
            _treeChildren[key] = _treeChildren[key]
                .OrderByDescending(n => n.IsDirectory)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private ResourceTreeNode CreateFileNode(ExternalResourceReference resource, string displayPath, StringComparison comparison)
    {
        var warning = _allWarnings.FirstOrDefault(w =>
            string.Equals(w.ResourcePath, resource.ResolvedPath, comparison) ||
            string.Equals(w.ResourcePath, resource.RawPath, comparison));

        var resolvedPath = resource.ResolvedPath;
        var modelIds = !string.IsNullOrEmpty(resolvedPath)
            ? ExternalResourceService.GetModelsReferencingResource(resolvedPath)
            : _allResources.Where(r => r.RawPath == resource.RawPath).Select(r => r.ModelId).Distinct().ToList();

        // Use the IsDirectory flag from the resource, or detect if path is a directory on disk
        var isDirectory = resource.IsDirectory ||
            (!string.IsNullOrEmpty(resolvedPath) && Directory.Exists(resolvedPath) && !File.Exists(resolvedPath));

        // Map ReferenceType to DirectoryAnnotationType for directories
        var annotationType = DirectoryAnnotationType.None;
        if (isDirectory)
        {
            annotationType = resource.ReferenceType switch
            {
                ResourceReferenceType.ExternalIncludeDirectory => DirectoryAnnotationType.IncludeDirectory,
                ResourceReferenceType.ExternalLibraryDirectory => DirectoryAnnotationType.LibraryDirectory,
                ResourceReferenceType.ExternalSourceDirectory => DirectoryAnnotationType.SourceDirectory,
                _ => DirectoryAnnotationType.None
            };
        }

        var isMissing = !resource.FileExists;
        var isAbsolute = resource.IsAbsolutePath;

        return new ResourceTreeNode
        {
            Name = isDirectory ? Path.GetFileName(displayPath.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileName(displayPath),
            FullPath = displayPath,
            IsDirectory = isDirectory,
            AnnotationType = annotationType,
            ReferencingModelIds = modelIds,
            HasWarning = isMissing || isAbsolute,
            IsMissing = isMissing,
            IsAbsolutePath = isAbsolute,
            WarningMessage = warning?.Message ??
                (isMissing ? (isDirectory ? "Directory not found" : "File not found") : null),
            FileExtension = isDirectory ? "" : Path.GetExtension(displayPath),
            IsImageFile = resource.IsImageFile,
            ReferencingModelCount = modelIds.Count
        };
    }

    private void InsertFileIntoTree(string filePath, ResourceTreeNode fileNode, StringComparison comparison)
    {
        var parentDir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(parentDir))
            return;

        AddChildNode(parentDir, fileNode);

        // Walk up and create intermediate directory nodes back to the common root
        var dir = parentDir;
        while (!string.IsNullOrEmpty(dir) &&
               dir.Length > _commonRoot.Length &&
               !string.Equals(dir, _commonRoot, comparison))
        {
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent.Length < _commonRoot.Length)
                break;

            var dirNode = new ResourceTreeNode
            {
                Name = Path.GetFileName(dir),
                FullPath = dir,
                IsDirectory = true
            };
            AddChildNode(parent, dirNode);
            dir = parent;
        }
    }

    private void AddChildNode(string parentPath, ResourceTreeNode child)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!_treeChildren.TryGetValue(parentPath, out var children))
        {
            children = new List<ResourceTreeNode>();
            _treeChildren[parentPath] = children;
        }

        var existing = children.FirstOrDefault(n => string.Equals(n.FullPath, child.FullPath, comparison));
        if (existing == null)
        {
            children.Add(child);
        }
        else if (child.AnnotationType != DirectoryAnnotationType.None && existing.AnnotationType == DirectoryAnnotationType.None)
        {
            // The incoming node carries annotation info (IncludeDirectory, LibraryDirectory, etc.)
            // but an intermediate plain directory node was already inserted for the same path.
            // This happens when files inside an annotated directory are processed before the
            // annotated directory reference itself. Promote the existing node to annotated.
            existing.AnnotationType = child.AnnotationType;
            existing.ReferencingModelIds = child.ReferencingModelIds;
            existing.ReferencingModelCount = child.ReferencingModelCount;
            existing.HasWarning = child.HasWarning;
            existing.WarningMessage = child.WarningMessage;
        }
    }

    private List<TreeItemData<ResourceTreeNode>> GetTopLevelItems()
    {
        if (!_treeChildren.ContainsKey(_commonRoot))
            return new List<TreeItemData<ResourceTreeNode>>();

        return GetFilteredChildren(_commonRoot)
            .Select(node => new TreeItemData<ResourceTreeNode>
            {
                Value = node,
                Expandable = node.IsDirectory,
                Expanded = false
            })
            .ToList();
    }

    /// <summary>
    /// Persists children loaded on demand by <see cref="LoadServerData"/> back into the
    /// <c>context.Children</c> data tree. Required by MudBlazor 9.4+: with both
    /// <c>ItemTemplate</c> and <c>Items</c> set, selectable values are derived from the
    /// <c>Items</c> data tree, so a lazily-loaded child that isn't written back is treated
    /// as unselectable and clicking it resets selection to default (the click does nothing).
    /// See LibraryBrowser for the same fix.
    /// </summary>
    private void OnNodeChildrenLoaded(
        ITreeItemData<ResourceTreeNode> treeItem, IReadOnlyCollection<ITreeItemData<ResourceTreeNode>> children)
    {
        treeItem.Children = children?.ToList();
    }

    private Task<IReadOnlyCollection<TreeItemData<ResourceTreeNode>>> LoadServerData(ResourceTreeNode? parentNode)
    {
        if (parentNode == null)
        {
            var topLevel = GetTopLevelItems();
            return Task.FromResult<IReadOnlyCollection<TreeItemData<ResourceTreeNode>>>(topLevel.AsReadOnly());
        }

        if (!parentNode.IsDirectory)
            return Task.FromResult<IReadOnlyCollection<TreeItemData<ResourceTreeNode>>>(
                Array.Empty<TreeItemData<ResourceTreeNode>>());

        var children = GetFilteredChildren(parentNode.FullPath)
            .Select(node => new TreeItemData<ResourceTreeNode>
            {
                Value = node,
                Expandable = node.IsDirectory,
                Expanded = false
            })
            .ToList();

        return Task.FromResult<IReadOnlyCollection<TreeItemData<ResourceTreeNode>>>(children.AsReadOnly());
    }

    private List<ResourceTreeNode> GetFilteredChildren(string directoryPath)
    {
        if (!_treeChildren.ContainsKey(directoryPath))
            return new List<ResourceTreeNode>();

        var children = _treeChildren[directoryPath];
        var filtered = new List<ResourceTreeNode>();

        foreach (var child in children)
        {
            if (child.IsDirectory)
            {
                // When the warning filter is active, include annotated directories that match the filter
                if (IsWarningFilterActive && child.HasWarning && PassesWarningFilter(child))
                    filtered.Add(child);
                // Include directory only if it has visible descendants
                else if (HasVisibleDescendants(child.FullPath))
                    filtered.Add(child);
            }
            else
            {
                if (PassesFileTypeFilter(child))
                    filtered.Add(child);
            }
        }

        return filtered;
    }

    private bool HasVisibleDescendants(string directoryPath)
    {
        if (!_treeChildren.ContainsKey(directoryPath))
            return false;

        foreach (var child in _treeChildren[directoryPath])
        {
            if (child.IsDirectory)
            {
                if (HasVisibleDescendants(child.FullPath))
                    return true;
            }
            else if (PassesFileTypeFilter(child))
            {
                return true;
            }
        }

        return false;
    }

    private bool PassesFileTypeFilter(ResourceTreeNode node)
    {
        if (node.IsDirectory)
            return true;

        // When a warning filter is active, only show nodes matching the selected warning types
        if (IsWarningFilterActive)
        {
            if (!PassesWarningFilter(node))
                return false;
        }

        var ext = node.FileExtension.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
            return _selectedFileTypes.Contains("other");

        if (DataExtensions.Contains(ext))
            return _selectedFileTypes.Contains("data");
        if (CCodeExtensions.Contains(ext))
            return _selectedFileTypes.Contains("ccode");
        if (LibExtensions.Contains(ext))
            return _selectedFileTypes.Contains("lib");
        if (ImageExtensions.Contains(ext))
            return _selectedFileTypes.Contains("images");
        if (DocumentExtensions.Contains(ext))
            return _selectedFileTypes.Contains("documents");

        return _selectedFileTypes.Contains("other");
    }

    private bool PassesWarningFilter(ResourceTreeNode node)
    {
        if (_selectedWarningTypes.Contains("missing") && node.IsMissing)
            return true;
        if (_selectedWarningTypes.Contains("absolute") && node.IsAbsolutePath)
            return true;
        return false;
    }

    private static string GetNodeText(ResourceTreeNode? node)
    {
        if (node == null) return "";
        // Show count for files and annotated directories
        if (node.ReferencingModelCount > 0 &&
            (!node.IsDirectory || node.AnnotationType != DirectoryAnnotationType.None))
            return $"{node.Name} ({node.ReferencingModelCount})";
        return node.Name;
    }

    private static string GetNodeIcon(ResourceTreeNode? node)
    {
        if (node == null) return Icons.Material.Filled.InsertDriveFile;
        if (node.IsDirectory)
        {
            // Use different icons for annotated directories
            return node.AnnotationType switch
            {
                DirectoryAnnotationType.IncludeDirectory => Icons.Material.Filled.Code, // C header files
                DirectoryAnnotationType.LibraryDirectory => Icons.Material.Filled.LibraryBooks, // Compiled libraries
                DirectoryAnnotationType.SourceDirectory => Icons.Material.Filled.DataObject, // C/Fortran source
                _ => Icons.Material.Filled.Folder // Regular directory
            };
        }
        if (node.HasWarning) return Icons.Material.Filled.WarningAmber;
        if (node.IsImageFile) return Icons.Material.Filled.Image;
        return Icons.Material.Filled.InsertDriveFile;
    }

    private static Color GetNodeIconColor(ResourceTreeNode? node)
    {
        if (node == null) return Color.Default;
        if (node.IsDirectory)
        {
            // Annotated directories get distinct colors, regular folders get default folder color
            return node.AnnotationType switch
            {
                DirectoryAnnotationType.IncludeDirectory => Color.Primary, // Blue for includes
                DirectoryAnnotationType.LibraryDirectory => Color.Secondary, // Purple for libraries
                DirectoryAnnotationType.SourceDirectory => Color.Tertiary, // Teal for source
                _ => Color.Warning // Yellow/orange for regular folders
            };
        }
        if (node.HasWarning) return Color.Error;
        if (node.IsImageFile) return Color.Info;
        return Color.Default;
    }

    private void OnFileTypeFilterChanged(IReadOnlyCollection<string> selectedValues)
    {
        _selectedFileTypes = selectedValues;
        _topLevelItems = GetTopLevelItems();
        _treeKey++; // Force MudTreeView to fully re-render with new filter
        _selectedResource = null;
        _referencingModels.Clear();
        StateHasChanged();
    }

    private void OnWarningFilterChanged(IReadOnlyCollection<string> selectedValues)
    {
        _selectedWarningTypes = selectedValues;
        _topLevelItems = GetTopLevelItems();
        _treeKey++;
        _selectedResource = null;
        _referencingModels.Clear();
        StateHasChanged();
    }

    private bool IsWarningFilterActive => _selectedWarningTypes.Count > 0;

    private void OnResourceSelected(ResourceTreeNode? node)
    {
        if (node == null)
        {
            _selectedResource = null;
            _referencingModels.Clear();
            return;
        }

        // Handle annotated directories - show models referencing this directory
        if (node.IsDirectory && node.AnnotationType != DirectoryAnnotationType.None)
        {
            _selectedResource = node;

            // For annotated directories, get models from the pre-computed list
            var modelIds = node.ReferencingModelIds;
            _referencingModels = _allResources
                .Where(r => modelIds.Contains(r.ModelId) &&
                            r.ResolvedPath != null &&
                            string.Equals(Path.GetFullPath(r.ResolvedPath), Path.GetFullPath(node.FullPath),
                                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                .ToList();
            return;
        }

        // Skip regular (intermediate) directories
        if (node.IsDirectory)
        {
            _selectedResource = null;
            _referencingModels.Clear();
            return;
        }

        _selectedResource = node;

        // Get models referencing this resource file
        var fileModelIds = ExternalResourceService.GetModelsReferencingResource(node.FullPath);
        _referencingModels = _allResources
            .Where(r => fileModelIds.Contains(r.ModelId) &&
                        r.ResolvedPath != null &&
                        string.Equals(Path.GetFullPath(r.ResolvedPath), Path.GetFullPath(node.FullPath),
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .ToList();
    }

    private void OnModelClicked(TableRowClickEventArgs<ExternalResourceReference> args)
    {
        if (args.Item != null)
            NavState.ChangeModelID(args.Item.ModelId);
    }

    private static string GetAnnotationTypeLabel(DirectoryAnnotationType annotationType)
    {
        return annotationType switch
        {
            DirectoryAnnotationType.IncludeDirectory => "Include Directory (C headers)",
            DirectoryAnnotationType.LibraryDirectory => "Library Directory (compiled libs)",
            DirectoryAnnotationType.SourceDirectory => "Source Directory (C/Fortran)",
            _ => "Directory"
        };
    }

    private static Color GetAnnotationChipColor(DirectoryAnnotationType annotationType)
    {
        return annotationType switch
        {
            DirectoryAnnotationType.IncludeDirectory => Color.Primary,
            DirectoryAnnotationType.LibraryDirectory => Color.Secondary,
            DirectoryAnnotationType.SourceDirectory => Color.Tertiary,
            _ => Color.Default
        };
    }
}
