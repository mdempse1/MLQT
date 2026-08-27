using MLQT.McpServer.Services;
using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Builds a full set of real MLQT services (with an in-memory settings store) for exercising the
/// MCP tools against temporary Modelica libraries. Cleans up temp files on dispose.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public LibraryDataService Libraries { get; }
    public FileMonitoringService FileMonitoring { get; }
    public RepositoryService Repositories { get; }
    public CodeReviewService CodeReview { get; }
    public StyleCheckingService StyleChecking { get; }
    public ImpactAnalysisService Impact { get; }
    public ExternalResourceService Resources { get; }
    public SessionState Session { get; }
    public InMemorySettingsService Settings { get; }
    public CustomDictionaryService CustomDictionary { get; }
    public DictionaryManagerService DictionaryManager { get; }

    public TestHost()
    {
        Settings = new InMemorySettingsService();
        Libraries = new LibraryDataService();
        FileMonitoring = new FileMonitoringService();
        Repositories = new RepositoryService(Libraries, Settings, FileMonitoring);
        CodeReview = new CodeReviewService();
        CustomDictionary = new CustomDictionaryService();
        DictionaryManager = new DictionaryManagerService();
        StyleChecking = new StyleCheckingService(Libraries,
            Repositories,
            CustomDictionary,
            DictionaryManager,
            CodeReview);
        Impact = new ImpactAnalysisService();
        Resources = new ExternalResourceService();
        Session = new SessionState();
    }

    /// <summary>Writes a single .mo file to a fresh temp directory and returns its full path.</summary>
    public string WriteMoFile(string fileName, string content)
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Writes a set of files (relative path -> content) into a fresh temp directory and
    /// returns the directory path.</summary>
    public string WriteLibraryDir(IDictionary<string, string> files)
    {
        var dir = NewTempDir();
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return dir;
    }

    public string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mlqt-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        Resources.Dispose();
        FileMonitoring.Dispose();
        foreach (var path in _tempPaths)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
