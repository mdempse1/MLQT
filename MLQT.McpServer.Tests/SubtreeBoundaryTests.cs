using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Where one class's subtree ends — asked of the two tools that move and delete a user's files.
/// </summary>
/// <remarks>
/// <para><b>Why these cases (B274).</b> `move_class` and `delete_class` both work out which classes
/// they are acting on with <c>id == root || id.StartsWith(root + ".")</c>. The 2026-09-22 campaign
/// could empty that <c>"."</c> in all four copies with no test objecting, which makes
/// <c>Root.Src</c> a prefix of <c>Root.SrcExtra</c> — so a move or a delete aimed at one package
/// takes an unrelated one with it. The fixture here has that namesake in it, which no other test
/// fixture does; the four copies now go through
/// <see cref="ModelicaParser.Helpers.ModelicaName.IsInSubtree"/>.</para>
///
/// <para><b>And why the refusals are asserted by message.</b> `Move_Validation` already moves a
/// class into its own descendant and asserts <c>IsType&lt;ToolError&gt;</c> — but a mutant that
/// removes the guard still produces <i>an</i> error further down, from a later check, so the
/// assertion holds while the guard is gone. A destructive tool's refusal is worth naming, and worth
/// checking that nothing was written.</para>
/// </remarks>
public class SubtreeBoundaryTests
{
    // Root.Src and Root.SrcExtra are namesakes: one is not inside the other, and only the
    // separator says so.
    private const string Package = """
        within;
        package Root "root"
          package Src
            model Widget "w"
              Real x;
            end Widget;
          end Src;
          package SrcExtra
            model Keeper "k"
              Real y;
            end Keeper;
          end SrcExtra;
          package Dst
          end Dst;
        end Root;
        """;

    private static async Task<EditTools> LoadAndAnalyze(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var deps = new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session);
        await deps.AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    private static string[] Ids(TestHost h) =>
        h.Libraries.GetAllModels().Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task MovingAPackageLeavesItsNamesakeWhereItWas()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src", "Root.Dst"));

        var ids = Ids(host);
        Assert.Contains("Root.SrcExtra", ids);
        Assert.Contains("Root.SrcExtra.Keeper", ids);
        Assert.Contains("Root.Dst.Src.Widget", ids);
        Assert.DoesNotContain("Root.Dst.SrcExtra", ids);
    }

    [Fact]
    public async Task DeletingAPackageLeavesItsNamesakeWhereItWas()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        ToolAssert.Ok<DeleteClassResult>(await edit.DeleteClass("Root.Src"));

        var ids = Ids(host);
        Assert.Contains("Root.SrcExtra", ids);
        Assert.Contains("Root.SrcExtra.Keeper", ids);
        Assert.DoesNotContain("Root.Src", ids);
        Assert.DoesNotContain("Root.Src.Widget", ids);
    }

    /// <summary>
    /// The guard by name, and the library untouched — not merely "some error came back".
    /// </summary>
    [Theory]
    [InlineData("Root.Src", "Root.Src")]              // into itself
    [InlineData("Root.Src", "Root.Src.Widget")]       // into its own descendant
    public async Task MovingAClassIntoItsOwnSubtreeIsRefusedAndWritesNothing(string classId, string newParent)
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);
        var before = Ids(host);

        var error = ToolAssert.Error(await edit.MoveClass(classId, newParent));

        Assert.Contains("itself or one of its own descendants", error.Error);
        Assert.Equal(before, Ids(host));
    }

    /// <summary>
    /// A namesake is <b>not</b> its own subtree, so this move is allowed. The control for the pair
    /// above: without it, a guard that refused everything would pass them both.
    /// </summary>
    [Fact]
    public async Task MovingAClassIntoItsNamesakeIsAllowed()
    {
        using var host = new TestHost();
        var edit = await LoadAndAnalyze(host);

        ToolAssert.Ok<MoveClassResult>(await edit.MoveClass("Root.Src", "Root.SrcExtra"));

        Assert.Contains("Root.SrcExtra.Src.Widget", Ids(host));
    }
}
