using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The number of tools the user documentation says this server exposes.
///
/// <para>It said 82 for a long time and the server had 66. Nobody had counted; the figure was
/// incremented when a tool was added and never checked against the assembly, which is the same
/// number twice with nothing holding them together — the shape this backlog has named more often
/// than any other. A count in prose is worse than no count when it is wrong, because it is the one
/// thing a reader has no way to verify for themselves.</para>
/// </summary>
public class ToolCountTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    /// <summary>
    /// Every method the server publishes as a tool, found the way the SDK finds them —
    /// <c>WithToolsFromAssembly</c> scans this assembly for <c>[McpServerToolType]</c> classes and
    /// takes their <c>[McpServerTool]</c> methods.
    /// </summary>
    private static List<string> DeclaredTools() =>
        [.. typeof(ClassQueryTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .Select(name => name!)];

    [Fact]
    public void TheDocumentationQuotesTheNumberOfToolsThereActuallyAre()
    {
        var tools = DeclaredTools();

        // Not zero, or every assertion below is trivially satisfied by a reflection query that
        // stopped matching — which is the failure this file exists to prevent.
        Assert.True(tools.Count > 30, $"only {tools.Count} tools found; the reflection query is wrong");
        Assert.Equal(tools.Count, tools.Distinct(StringComparer.Ordinal).Count());

        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "Documentation", "mcp-server.md"));
        var stated = Regex.Match(doc, @"The server exposes (?<count>\d+) tools\.");

        Assert.True(stated.Success, "Documentation/mcp-server.md no longer states how many tools there are");
        Assert.Equal(tools.Count, int.Parse(stated.Groups["count"].Value));
    }

    [Fact]
    public void EveryToolIsNamedInLowerSnakeCase()
    {
        // The naming every existing tool follows, and the only thing a client sees. Worth stating
        // once here rather than being noticed by an agent as an inconsistency in a list of 66.
        Assert.All(DeclaredTools(), name =>
            Assert.Matches("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", name));
    }
}
