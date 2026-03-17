# C# and Blazor Coding Guidelines

This document defines coding standards and best practices for C# and Blazor development in this project.

## Table of Contents

1. [Naming Conventions](#naming-conventions)
2. [Code Organization](#code-organization)
3. [C# Best Practices](#c-best-practices)
4. [Blazor Patterns](#blazor-patterns)
5. [Async/Await Patterns](#asyncawait-patterns)
6. [Error Handling](#error-handling)
7. [Dependency Injection](#dependency-injection)
8. [Null Safety](#null-safety)
9. [Comments and Documentation](#comments-and-documentation)
10. [Testing](#testing)

---

## Naming Conventions

### General Rules

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `LibraryDataService` |
| Interfaces | IPascalCase | `ILibraryDataService` |
| Methods | PascalCase | `GetModelById()` |
| Properties | PascalCase | `CurrentRevision` |
| Public fields | PascalCase | `MaxRetryCount` |
| Private fields | _camelCase | `_isLoading` |
| Local variables | camelCase | `modelNode` |
| Parameters | camelCase | `repositoryId` |
| Constants | PascalCase | `DefaultTimeout` |
| Enums | PascalCase (singular) | `RepositoryVcsType` |
| Enum values | PascalCase | `RepositoryVcsType.Git` |
| Events | PascalCase | `OnTreeDataChanged` |
| Delegates | PascalCase + Handler/Callback | `TreeChangedHandler` |

### Specific Guidelines

```csharp
// DO: Use meaningful, descriptive names
private readonly ILibraryDataService _libraryDataService;
public string CurrentBranch { get; set; }

// DON'T: Use abbreviations or single letters (except in loops/lambdas)
private readonly ILibraryDataService _lds;  // Bad
public string CurBr { get; set; }           // Bad

// DO: Use verbs for methods that perform actions
public async Task LoadLibraryAsync(string path) { }
public bool IsValidModel(ModelNode node) { }

// DO: Use nouns/adjectives for properties
public bool IsLoading { get; private set; }
public int ModelCount { get; }
```

### Boolean Naming

```csharp
// DO: Use Is, Has, Can, Should prefixes for booleans
public bool IsLoading { get; set; }
public bool HasChildren { get; }
public bool CanExpand { get; set; }
private bool _shouldRefresh;

// DON'T: Use negative names
public bool IsNotValid { get; }  // Bad - use IsValid and negate when needed
```

---

## Code Organization

### File Structure

Each file should contain a single type (class, interface, enum) with the same name as the file.

```
Project/
├── Interfaces/           # Interface definitions
│   └── IServiceName.cs
├── Services/             # Service implementations
│   └── ServiceName.cs
├── Models/               # Data models and DTOs
│   └── ModelName.cs
├── Extensions/           # Extension methods
│   └── StringExtensions.cs
└── Helpers/              # Static helper classes
    └── FileHelper.cs
```

### Class Organization

Order members within a class consistently:

```csharp
public class ExampleService : IExampleService, IDisposable
{
    // 1. Constants
    private const int DefaultTimeout = 30000;

    // 2. Static fields
    private static readonly object _staticLock = new();

    // 3. Instance fields (readonly first, then mutable)
    private readonly ILogger _logger;
    private readonly IRepository _repository;
    private bool _isInitialized;
    private string _currentState;

    // 4. Properties
    public bool IsInitialized => _isInitialized;
    public string CurrentState { get; private set; }

    // 5. Events
    public event Action? OnStateChanged;

    // 6. Constructors
    public ExampleService(ILogger logger, IRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    // 7. Public methods
    public async Task InitializeAsync() { }

    // 8. Internal/Protected methods
    protected virtual void OnInitialized() { }

    // 9. Private methods
    private void UpdateState(string newState) { }

    // 10. Interface implementations (IDisposable, etc.)
    public void Dispose() { }
}
```

### Using Directives

Order using directives as follows:

```csharp
// System namespaces first
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Third-party namespaces
using Microsoft.Extensions.Logging;
using MudBlazor;

// Project namespaces
using MLQT.Shared.Models;
using MLQT.Services.Interfaces;
```

---

## C# Best Practices

### Expression-Bodied Members

Use expression bodies for simple, single-expression members:

```csharp
// DO: Use expression bodies for simple getters
public string FullName => $"{FirstName} {LastName}";
public bool IsEmpty => _items.Count == 0;

// DO: Use expression bodies for simple methods
public override string ToString() => Name;

// DON'T: Use expression bodies for complex logic
public string GetStatus() => _isLoading ? "Loading" : _hasError ? "Error" : _items.Any() ? "Ready" : "Empty";  // Bad - too complex
```

### Object and Collection Initialization

```csharp
// DO: Use object initializers
var node = new ModelNode
{
    Id = "model1",
    Name = "MyModel",
    IsExpanded = true
};

// DO: Use collection initializers
var list = new List<string> { "item1", "item2", "item3" };
var dict = new Dictionary<string, int>
{
    ["key1"] = 1,
    ["key2"] = 2
};

// DO: Use target-typed new
private readonly List<string> _items = new();
ModelNode node = new() { Name = "Test" };
```

### Pattern Matching

```csharp
// DO: Use pattern matching for type checks
if (node is ModelNode modelNode)
{
    ProcessModel(modelNode);
}

// DO: Use switch expressions
var icon = classType switch
{
    "function" => Icons.Material.Filled.Functions,
    "block" => Icons.Material.Filled.ViewModule,
    "package" => Icons.Material.Filled.FolderOpen,
    _ => Icons.Material.Filled.ModelTraining
};

// DO: Use property patterns
if (repository is { VcsType: RepositoryVcsType.Git, CurrentBranch: not null })
{
    // Handle Git repository with a branch
}
```

### LINQ Guidelines

```csharp
// DO: Use method syntax for simple queries
var activeModels = models.Where(m => m.IsActive).ToList();

// DO: Use query syntax for complex joins
var result = from model in models
             join file in files on model.FileId equals file.Id
             where model.IsActive
             select new { model.Name, file.Path };

// DO: Use meaningful names in lambdas for complex expressions
var grouped = models
    .Where(model => model.IsValid)
    .GroupBy(model => model.Category)
    .Select(group => new { Category = group.Key, Count = group.Count() });

// DON'T: Chain too many operations without intermediate variables
var result = items.Where(x => x.IsValid).Select(x => x.Name).OrderBy(x => x).Take(10).ToList();  // Consider breaking up

// DO: Break up long chains
var validItems = items.Where(item => item.IsValid);
var sortedNames = validItems
    .Select(item => item.Name)
    .OrderBy(name => name)
    .Take(10)
    .ToList();
```

### String Handling

```csharp
// DO: Use string interpolation
var message = $"Processing model: {modelName} in {fileName}";

// DO: Use StringBuilder for multiple concatenations in loops
var builder = new StringBuilder();
foreach (var item in items)
{
    builder.AppendLine(item.Name);
}

// DO: Use string.IsNullOrEmpty or string.IsNullOrWhiteSpace
if (!string.IsNullOrEmpty(path))
{
    // Process path
}

// DO: Use verbatim strings for paths
var path = @"C:\Projects\MLQT";

// DO: Use raw string literals for multi-line content (C# 11+)
var json = """
    {
        "name": "value",
        "count": 42
    }
    """;
```

---

## Blazor Patterns

### Component Structure

```razor
@* 1. Using directives *@
@using MLQT.Services.Interfaces
@using MudBlazor

@* 2. Dependency injection *@
@inject ILibraryDataService LibraryDataService
@inject ISnackbar Snackbar

@* 3. Interface implementations *@
@implements IDisposable

@* 4. Markup *@
<div class="component-container">
    @if (_isLoading)
    {
        <MudProgressCircular Indeterminate="true" />
    }
    else
    {
        <MudText>@_data</MudText>
    }
</div>

@* 5. Code block *@
@code {
    // Parameters first
    [Parameter]
    public string ModelId { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> OnModelSelected { get; set; }

    // Private fields
    private bool _isLoading;
    private string _data = string.Empty;

    // Lifecycle methods
    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    // Event handlers
    private async Task HandleClick()
    {
        await OnModelSelected.InvokeAsync(ModelId);
    }

    // Private methods
    private async Task LoadDataAsync()
    {
        _isLoading = true;
        try
        {
            _data = await LibraryDataService.GetDataAsync(ModelId);
        }
        finally
        {
            _isLoading = false;
        }
    }

    // IDisposable
    public void Dispose()
    {
        // Cleanup
    }
}
```

### Event Handling and StateHasChanged

```csharp
// DO: Use InvokeAsync for event handlers that modify state from external events
// When only calling StateHasChanged, use the method group form (shorter, no closure allocation)
private async void OnExternalEvent()
{
    await InvokeAsync(StateHasChanged);
}

// DO: Use the lambda form only when setting state AND calling StateHasChanged together
private async void OnExternalEventWithData()
{
    await InvokeAsync(() =>
    {
        _data = "Updated";
        StateHasChanged();
    });
}

// DO: Avoid calling StateHasChanged after awaited operations in event handlers
// (Blazor calls it automatically)
private async Task HandleButtonClick()
{
    _isLoading = true;
    StateHasChanged();  // Needed to show loading state immediately

    await LoadDataAsync();

    _isLoading = false;
    // StateHasChanged() not needed here - Blazor calls it after the handler
}

// DON'T: Call StateHasChanged unnecessarily
private void UpdateValue(string value)
{
    _value = value;
    StateHasChanged();  // Not needed - Blazor handles this for UI events
}
```

### Component Communication

```csharp
// Parent to Child: Use Parameters
[Parameter]
public string Title { get; set; } = string.Empty;

// Child to Parent: Use EventCallback
[Parameter]
public EventCallback<ModelNode> OnModelSelected { get; set; }

private async Task SelectModel(ModelNode model)
{
    await OnModelSelected.InvokeAsync(model);
}

// Cascading Values: Use for deeply nested components
// In parent:
<CascadingValue Value="@_theme">
    <ChildComponent />
</CascadingValue>

// In child:
[CascadingParameter]
public Theme? CurrentTheme { get; set; }
```

### Two-Way Binding

```csharp
// DO: Implement proper two-way binding pattern
[Parameter]
public string Value { get; set; } = string.Empty;

[Parameter]
public EventCallback<string> ValueChanged { get; set; }

private async Task UpdateValue(string newValue)
{
    Value = newValue;
    await ValueChanged.InvokeAsync(newValue);
}
```

---

## Async/Await Patterns

### Method Naming

```csharp
// DO: Suffix async methods with Async
public async Task<Model> GetModelAsync(string id) { }
public async Task SaveChangesAsync() { }

// EXCEPTION: Event handlers and lifecycle methods
protected override async Task OnInitializedAsync() { }  // Framework convention
private async void OnButtonClick() { }  // Event handler
```

### Async Best Practices

```csharp
// DO: Use async/await all the way down
public async Task ProcessAsync()
{
    var data = await GetDataAsync();
    await SaveDataAsync(data);
}

// DON'T: Block on async code
public void Process()
{
    var data = GetDataAsync().Result;  // Bad - can cause deadlocks
    SaveDataAsync(data).Wait();        // Bad
}

// DO: Use ConfigureAwait(false) in library code (not UI code)
public async Task<string> GetDataAsync()
{
    var result = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
    return result;
}

// DO: Use ValueTask for methods that often complete synchronously
public ValueTask<int> GetCachedValueAsync(string key)
{
    if (_cache.TryGetValue(key, out var value))
    {
        return ValueTask.FromResult(value);
    }
    return new ValueTask<int>(LoadValueAsync(key));
}

// DO: Cancel long-running operations
public async Task LoadDataAsync(CancellationToken cancellationToken = default)
{
    await foreach (var item in GetItemsAsync(cancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessItem(item);
    }
}
```

### Parallel Operations

```csharp
// DO: Use Task.WhenAll for independent operations
var task1 = LoadModelsAsync();
var task2 = LoadFilesAsync();
var task3 = LoadSettingsAsync();
await Task.WhenAll(task1, task2, task3);

// DO: Use Parallel.ForEachAsync for CPU-bound parallel work with async
await Parallel.ForEachAsync(items, async (item, ct) =>
{
    await ProcessItemAsync(item, ct);
});
```

---

## Error Handling

### Exception Guidelines

```csharp
// DO: Catch specific exceptions
try
{
    await File.ReadAllTextAsync(path);
}
catch (FileNotFoundException ex)
{
    _logger.LogWarning(ex, "File not found: {Path}", path);
    return null;
}
catch (UnauthorizedAccessException ex)
{
    _logger.LogError(ex, "Access denied: {Path}", path);
    throw;
}

// DON'T: Catch generic Exception unless re-throwing
catch (Exception ex)
{
    // Log and swallow - usually bad
}

// DO: Use exception filters when appropriate
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return null;
}

// DO: Throw meaningful exceptions
public void SetModel(ModelNode? model)
{
    ArgumentNullException.ThrowIfNull(model);
    // or for older C#: if (model == null) throw new ArgumentNullException(nameof(model));
}
```

### Result Pattern (Alternative to Exceptions)

```csharp
// DO: Use result types for expected failures
public record OperationResult(bool Success, string? ErrorMessage = null);

public record OperationResult<T>(bool Success, T? Value, string? ErrorMessage = null)
{
    public static OperationResult<T> Ok(T value) => new(true, value);
    public static OperationResult<T> Fail(string error) => new(false, default, error);
}

// Usage
public async Task<OperationResult<Model>> TryLoadModelAsync(string path)
{
    if (!File.Exists(path))
    {
        return OperationResult<Model>.Fail($"File not found: {path}");
    }

    var model = await ParseModelAsync(path);
    return OperationResult<Model>.Ok(model);
}
```

---

## Dependency Injection

### Registration

```csharp
// DO: Register services with appropriate lifetimes
services.AddSingleton<ISettingsService, SettingsService>();     // Shared state
services.AddScoped<IUserService, UserService>();                 // Per-request/circuit
services.AddTransient<IModelValidator, ModelValidator>();        // Stateless

// DO: Use interfaces for testability
services.AddSingleton<ILibraryDataService, LibraryDataService>();

// DON'T: Register concrete types directly (harder to test)
services.AddSingleton<LibraryDataService>();  // Avoid
```

### Constructor Injection

```csharp
// DO: Use constructor injection
public class LibraryDataService : ILibraryDataService
{
    private readonly ILogger<LibraryDataService> _logger;
    private readonly IFileService _fileService;

    public LibraryDataService(
        ILogger<LibraryDataService> logger,
        IFileService fileService)
    {
        _logger = logger;
        _fileService = fileService;
    }
}

// DON'T: Use service locator pattern
public class BadService
{
    private readonly IServiceProvider _serviceProvider;

    public void DoWork()
    {
        var service = _serviceProvider.GetService<IOtherService>();  // Avoid
    }
}
```

---

## Null Safety

### Nullable Reference Types

```csharp
// DO: Enable nullable reference types
#nullable enable

// DO: Be explicit about nullability
public string Name { get; set; } = string.Empty;  // Non-null with default
public string? Description { get; set; }           // Explicitly nullable

// DO: Use null-conditional and null-coalescing operators
var name = model?.Name ?? "Unknown";
var count = items?.Count ?? 0;

// DO: Use pattern matching for null checks
if (model is { Name: not null } validModel)
{
    ProcessModel(validModel);
}

// DO: Guard against null parameters
public void ProcessModel(ModelNode model)
{
    ArgumentNullException.ThrowIfNull(model);
    // Process
}
```

### Avoiding Null

```csharp
// DO: Return empty collections instead of null
public IReadOnlyList<ModelNode> GetModels()
{
    if (!_isInitialized)
    {
        return Array.Empty<ModelNode>();  // Not null
    }
    return _models.AsReadOnly();
}

// DO: Use TryGet pattern
public bool TryGetModel(string id, [NotNullWhen(true)] out ModelNode? model)
{
    return _models.TryGetValue(id, out model);
}
```

### Capturing Return Values as Null Proofs

When a method both sets a nullable property as a side effect AND returns that value, capture the return value and use it directly rather than re-accessing the nullable property. This gives the compiler the proof of non-nullability it needs without suppression operators.

```csharp
// DON'T: Discard the return value and re-access the nullable property
if (_model.EnsureParsed() == null)
    return;
// _model.ParsedCode is still typed as nullable here — compiler warns on use
visitor.Visit(_model.ParsedCode);  // CS8604: Possible null reference argument

// DO: Capture the return value and use it directly
var parsedCode = _model.EnsureParsed();
if (parsedCode == null)
    return;
// parsedCode is proved non-null — no warning
visitor.Visit(parsedCode);
```

This pattern avoids both compiler warnings and the null-forgiving operator (`!`), which silences warnings without adding any safety.

---

## Comments and Documentation

### XML Documentation

```csharp
/// <summary>
/// Loads a Modelica library from the specified path.
/// </summary>
/// <param name="path">The absolute path to the library root directory.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>The loaded library, or null if loading failed.</returns>
/// <exception cref="ArgumentNullException">Thrown when path is null.</exception>
/// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
public async Task<LoadedLibrary?> LoadLibraryAsync(
    string path,
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### Code Comments

```csharp
// DO: Explain WHY, not WHAT
// Cache the parsed result to avoid re-parsing on every access
private readonly Dictionary<string, ParseResult> _parseCache = new();

// DON'T: State the obvious
// Increment counter
counter++;  // Bad comment

// DO: Use TODO comments with context
// TODO: Replace with batch operation when API supports it (see issue #123)
foreach (var item in items)
{
    await SaveItemAsync(item);
}

// DO: Mark temporary code or workarounds
// HACK: Workaround for MudBlazor issue #4567 - remove when fixed
```

---

## Testing

### Test Naming

```csharp
// DO: Use descriptive test names that explain the scenario
[Fact]
public async Task GetModelById_WithValidId_ReturnsModel()
{
    // Arrange, Act, Assert
}

[Fact]
public async Task GetModelById_WithInvalidId_ReturnsNull()
{
    // Arrange, Act, Assert
}

[Fact]
public async Task SaveModel_WhenFileSystemFails_ThrowsIOException()
{
    // Arrange, Act, Assert
}
```

### Test Structure

```csharp
[Fact]
public async Task ProcessModels_WithMultipleModels_ProcessesAllInOrder()
{
    // Arrange
    var mockService = new Mock<IFileService>();
    var sut = new ModelProcessor(mockService.Object);
    var models = new[] { CreateModel("A"), CreateModel("B") };

    // Act
    var result = await sut.ProcessModelsAsync(models);

    // Assert
    Assert.Equal(2, result.ProcessedCount);
    Assert.True(result.Success);
}

// DO: Use helper methods for test data
private static ModelNode CreateModel(string name) => new()
{
    Id = Guid.NewGuid().ToString(),
    Name = name
};
```

---

## Summary Checklist

Before committing code, verify:

- [ ] Naming follows conventions (PascalCase for public, _camelCase for private fields)
- [ ] Async methods are suffixed with Async (except event handlers)
- [ ] No blocking calls on async code (.Result, .Wait())
- [ ] Proper null handling with nullable reference types — zero CS8604/CS8602/CS8600 warnings
- [ ] No use of null-forgiving operator (`!`) to suppress warnings — fix the root cause instead
- [ ] XML documentation on public APIs
- [ ] Specific exception types caught (not generic Exception)
- [ ] Services registered with appropriate lifetimes
- [ ] Blazor components properly dispose of event subscriptions
- [ ] StateHasChanged used correctly (with InvokeAsync when needed)
- [ ] No unnecessary StateHasChanged calls after awaited operations

---

## Revision History

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2026-02-12 | 1.0 | - | Initial draft |
| 2026-03-11 | 1.1 | - | Added null-proof return-value capture pattern; InvokeAsync method-group form; zero-warning checklist items |
