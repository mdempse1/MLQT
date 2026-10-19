using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// What an agent is told about this server before it picks a tool: the names, the instructions and
/// the first sentence of each description.
///
/// <para>Agents were not reaching for these tools to read and edit Modelica, and when a simulator's
/// server was connected beside this one they confused the two: both had a <c>load_library</c> and a
/// <c>list_classes</c>, and <c>check_class</c> sat next to <c>check_model</c> meaning something else
/// entirely. Every tool now carries the <c>mlqt_</c> prefix, and the prose that names them was
/// rewritten to match — which is a rename of some 600 mentions, and the reason the second test here
/// exists.</para>
/// </summary>
public class ToolNamingTests
{
    private static readonly Regex MentionedTool = new(@"\bmlqt_[a-z][a-z0-9_]*\b", RegexOptions.Compiled);

    [Fact]
    public void EveryToolCarriesTheMlqtPrefix()
    {
        // Clients that do not namespace tools by server put both servers' load_library in one list,
        // and the prose that names a tool is ambiguous in every client that does.
        Assert.All(ToolCountTests.DeclaredTools(), name => Assert.StartsWith("mlqt_", name));
    }

    /// <summary>
    /// Every <c>mlqt_</c> name the server's own text gives an agent — instructions, guidance, tool
    /// descriptions and error messages, all in its source — and every one the user documentation
    /// gives a person, is a tool there is. A name that is no tool sends an agent to call something
    /// that does not exist, and reads to it as a server that is broken.
    /// </summary>
    [Fact]
    public void EveryToolNameMentionedIsAToolThereIs()
    {
        var tools = ToolCountTests.DeclaredTools().ToHashSet(StringComparer.Ordinal);
        var root = ToolCountTests.RepositoryRoot();

        var files = Directory.EnumerateFiles(Path.Combine(root, "MLQT.McpServer"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Append(Path.Combine(root, "MLQT.McpServer", "README.md"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Documentation"), "*.md"))
            .ToList();
        Assert.True(files.Count > 20, $"only {files.Count} files found; the search is wrong");

        var unknown = (from file in files
                       from Match m in MentionedTool.Matches(File.ReadAllText(file))
                       where !tools.Contains(m.Value)
                       select $"{Path.GetRelativePath(root, file)}: {m.Value}")
                      .Distinct()
                      .ToList();

        Assert.True(unknown.Count == 0, "Names that are no tool:\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void TheInstructionsFitInWhatAClientKeeps()
    {
        // Claude Code keeps about the first 2,048 characters of a server's instructions. The version
        // these replaced ran to 5,000 and was cut off before it said anything about editing.
        Assert.True(ServerInstructions.Text.Length <= ServerInstructions.Budget,
            $"The instructions are {ServerInstructions.Text.Length} characters; the budget is " +
            $"{ServerInstructions.Budget}. Move detail into mlqt_get_guidance.");
    }

    [Fact]
    public void TheInstructionsSayWhenToUseTheseToolsAndPointToEveryTopic()
    {
        var text = ServerInstructions.Text;

        // The first thing said is when to reach for these tools at all: an agent's own file tools
        // are always available, and without a reason to prefer these it does not.
        var firstParagraph = text[..text.IndexOf("\n\n", StringComparison.Ordinal)];
        Assert.Contains("Modelica", firstParagraph);
        Assert.Contains("rather than", firstParagraph);

        Assert.Contains("mlqt_get_guidance", text);
        Assert.All(GuidanceTools.Topics, topic => Assert.Contains(topic, text));
    }

    [Fact]
    public void TheInstructionsSayMlqtHasASessionOfItsOwn()
    {
        // An agent loaded the MSL with a simulator's load_library and then searched with MLQT, taking
        // one server's session for the other's. Said second, right after when to use these tools.
        var paragraphs = ServerInstructions.Text.Split("\n\n");
        Assert.Contains("starts EMPTY", paragraphs[1]);
        Assert.Contains("does NOT load anything here", paragraphs[1]);
        Assert.Contains("getLoadedLibraries()", paragraphs[1]);
    }

    [Fact]
    public void TheGuidanceToolListsEveryTopic()
    {
        var method = typeof(GuidanceTools).GetMethod(nameof(GuidanceTools.GetGuidance))!;
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
        var parameter = method.GetParameters().Single().GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.All(GuidanceTools.Topics, topic =>
        {
            Assert.Contains(topic, description);
            Assert.Contains(topic, parameter);
        });
    }

    /// <summary>
    /// A client that defers tools until searched for finds them by keyword, and an agent looking for
    /// a way to edit a model searches for "Modelica". So every description says it in its first
    /// sentence, which is also the part a client is least likely to cut.
    /// </summary>
    [Fact]
    public void EveryDescriptionNamesModelicaInItsFirstSentence()
    {
        var missing = (from type in typeof(ClassQueryTools).Assembly.GetTypes()
                       where type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null
                       from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                       let tool = method.GetCustomAttribute<McpServerToolAttribute>()
                       where tool is not null
                       let description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""
                       let first = Regex.Split(description, @"(?<=\.)\s", RegexOptions.None)[0]
                       where !first.Contains("Modelica", StringComparison.Ordinal)
                       select $"{tool.Name}: {first}")
                      .ToList();

        Assert.True(missing.Count == 0, "First sentences without 'Modelica':\n" + string.Join("\n", missing));
    }
}
