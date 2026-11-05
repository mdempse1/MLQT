using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace MLQT.Services.Tests.Helpers;

/// <summary>Swaps NLog's global configuration, so nothing else may log while one of these runs.</summary>
[CollectionDefinition(nameof(GlobalLogConfiguration), DisableParallelization = true)]
public class GlobalLogConfiguration;

/// <summary>
/// What <see cref="IncrementalFormatter.FormatAndWriteAsync"/> says in the log.
/// </summary>
/// <remarks>
/// It wrote one line per file it saved. A startup with 17,372 changed files wrote 17,372 of them -
/// most of the day's log - while saying nothing about how far it had got or where the time went.
/// </remarks>
[Collection(nameof(GlobalLogConfiguration))]
public class IncrementalFormatterLoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-incremental-format-log", Guid.NewGuid().ToString("N"));

    public IncrementalFormatterLoggingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Untidy one-class files, registered in a graph as a load would.</summary>
    private (DirectedGraph Graph, List<string> Paths) UntidyFiles(int count)
    {
        var graph = new DirectedGraph();
        var paths = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var name = $"M{i}";
            var source = $"model {name}\r\n      Real x;\r\nend {name};\r\n";
            var path = Path.Combine(_dir, $"{name}.mo");
            File.WriteAllText(path, source);

            var fileId = GraphBuilder.GenerateFileId(path);
            graph.AddNode(new FileNode(fileId, path));
            graph.AddNode(new ModelNode(name, name, source));
            graph.AddFileContainsModel(fileId, name);
            paths.Add(path);
        }
        return (graph, paths);
    }

    private static async Task<IList<string>> LogOf(Func<Task> work)
    {
        var target = new MemoryTarget { Layout = "${level}|${logger}|${message}" };
        var previous = LogManager.Configuration;
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, target);
        LogManager.Configuration = config;
        try
        {
            await work();
            return target.Logs.Where(l => l.Contains($"|{nameof(IncrementalFormatter)}|")).ToList();
        }
        finally
        {
            LogManager.Configuration = previous;
        }
    }

    private static StyleCheckingSettings Formatting() => new() { ApplyFormattingRules = true };

    [Fact]
    public async Task ALargeFormat_LogsATotalAndNoLinePerFile()
    {
        var count = IncrementalFormatter.LargeFormat;
        var (graph, paths) = UntidyFiles(count);
        IReadOnlyDictionary<string, DateTime>? written = null;

        var log = await LogOf(async () =>
            written = await IncrementalFormatter.FormatAndWriteAsync(graph, paths, Formatting()));

        // The positive control: the files really were written, so silence is not for lack of work.
        Assert.Equal(count, written!.Count);
        Assert.DoesNotContain(log, l => l.Contains("Saved formatted file", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("Info|", StringComparison.Ordinal)
            && l.Contains($"Wrote {count} formatted file(s) of {count} selected", StringComparison.Ordinal));
        Assert.True(log.Count < 5, $"{log.Count} lines for {count} files:\n{string.Join('\n', log)}");
    }

    [Fact]
    public async Task ASmallFormat_KeepsItsSummaryOutOfAProductionLog()
    {
        // It runs after every commit and VCS operation; a production host logs Info and above.
        var (graph, paths) = UntidyFiles(2);

        var log = await LogOf(() => IncrementalFormatter.FormatAndWriteAsync(graph, paths, Formatting()));

        var line = Assert.Single(log);
        Assert.StartsWith("Debug|", line);
        Assert.Contains("Wrote 2 formatted file(s)", line);
    }
}
