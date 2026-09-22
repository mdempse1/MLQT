using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// What removing something from a class must leave behind: <b>every other line exactly as it
/// was</b>.
/// </summary>
/// <remarks>
/// <para><b>Why this and not more cases (B274).</b> The 2026-09-22 mutation campaign put 140
/// surviving mutants in <c>StructureEditTools</c>, and the ones worth acting on were not the
/// message strings — they were <i>equality and arithmetic</i> mutations in the two places that
/// compute which characters to delete. That is index arithmetic over the lines of a file an agent
/// is about to rewrite, where an off-by-one deletes the wrong line.</para>
///
/// <para><b>The existing tests could not see it.</b> They assert that the removed thing is gone
/// (<c>DoesNotContain("Real x")</c>) and that the class still ends — which stays true when the
/// line above is swallowed with it, when a blank line is left in its place, or when the newline is
/// eaten and two lines are merged. The property that catches all three is a comparison of the whole
/// class before and after: the result must be the original lines minus exactly one.</para>
/// </remarks>
public class StructureRemovalTests
{
    private const string Package = """
        within;
        package P "p"
          model A "a"
            Real first;
            Real target;
            Real last;
          equation
            first = target;
          end A;
          model Shared "s"
            Real a, b, c;
            Real keep;
          end Shared;
          model Solo "solo"
            Real only;
          end Solo;
        end P;
        """;

    private static StructureEditTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new StructureEditTools(h.Libraries, h.Resources, h.Session);
    }

    private static string[] Lines(TestHost h, string id) =>
        h.Libraries.GetModelById(id)!.Definition.ModelicaCode!
            .Replace("\r\n", "\n").Split('\n');

    /// <summary>
    /// The whole contract in one assertion: what is left is the original, minus the one line.
    /// </summary>
    private static void AssertOnlyLineRemoved(string[] before, string[] after, string removed)
    {
        var expected = before.Where(line => !line.Contains(removed, StringComparison.Ordinal)).ToArray();

        Assert.Equal(before.Length - 1, expected.Length);   // the fixture has exactly one such line
        Assert.Equal(expected, after);
    }

    [Fact]
    public async Task RemovingAComponentLeavesEveryOtherLineExactlyAsItWas()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Lines(host, "P.A");

        ToolAssert.Ok<StructureEditResult>(await tools.RemoveComponent("P.A", "target"));

        AssertOnlyLineRemoved(before, Lines(host, "P.A"), "Real target;");
    }

    /// <summary>
    /// The last declaration before a section keyword, where the character after the semicolon is
    /// the newline that ends the class body's last statement rather than one of many.
    /// </summary>
    [Fact]
    public async Task RemovingTheLastComponentBeforeASectionLeavesTheSectionWhereItWas()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Lines(host, "P.A");

        ToolAssert.Ok<StructureEditResult>(await tools.RemoveComponent("P.A", "last"));

        AssertOnlyLineRemoved(before, Lines(host, "P.A"), "Real last;");
    }

    [Fact]
    public async Task RemovingTheFirstComponentDoesNotTakeTheClassHeaderWithIt()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Lines(host, "P.A");

        ToolAssert.Ok<StructureEditResult>(await tools.RemoveComponent("P.A", "first"));

        AssertOnlyLineRemoved(before, Lines(host, "P.A"), "Real first;");
    }

    /// <summary>
    /// A class whose only element is the one being removed: the line is both the first and the last
    /// of the body, so both ends of the arithmetic are at a boundary at once.
    /// </summary>
    [Fact]
    public async Task RemovingTheOnlyComponentLeavesAnEmptyClassRatherThanABlankLine()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Lines(host, "P.Solo");

        ToolAssert.Ok<StructureEditResult>(await tools.RemoveComponent("P.Solo", "only"));

        AssertOnlyLineRemoved(before, Lines(host, "P.Solo"), "Real only;");
    }

    /// <summary>
    /// Removing one name from <c>Real a, b, c;</c> rewrites that line and must leave the line count
    /// alone — the other branch of the same method, with its own comma arithmetic.
    /// </summary>
    [Theory]
    [InlineData("a", "Real b, c;")]
    [InlineData("b", "Real a, c;")]
    [InlineData("c", "Real a, b;")]
    public async Task RemovingOneNameFromASharedClauseRewritesOnlyThatLine(string name, string expected)
    {
        using var host = new TestHost();
        var tools = Load(host);
        var before = Lines(host, "P.Shared");

        ToolAssert.Ok<StructureEditResult>(await tools.RemoveComponent("P.Shared", name));
        var after = Lines(host, "P.Shared");

        Assert.Equal(before.Length, after.Length);

        var rewritten = before
            .Select(line => line.Contains("Real a, b, c;", StringComparison.Ordinal)
                ? line.Replace("Real a, b, c;", expected, StringComparison.Ordinal)
                : line)
            .ToArray();

        Assert.Equal(rewritten, after);
    }

    /// <summary>
    /// <c>remove_connection</c> goes through the one shared <c>RemoveWholeLine</c> that
    /// <c>RemoveComponent</c>'s sole-on-line branch now also uses, so it is the same promise about
    /// a different statement.
    /// </summary>
    [Fact]
    public async Task RemovingAConnectionLeavesEveryOtherLineExactlyAsItWas()
    {
        using var host = new TestHost();
        var tools = Load(host);

        ToolAssert.Ok<StructureEditResult>(
            await tools.AddConnection("P.A", "first", "target"));
        var before = Lines(host, "P.A");

        ToolAssert.Ok<StructureEditResult>(
            await tools.RemoveConnection("P.A", "first", "target"));

        AssertOnlyLineRemoved(before, Lines(host, "P.A"), "connect(first, target)");
    }
}
