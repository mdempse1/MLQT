using MLQT.Shared.Components;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="DiffViewer.ComputeLcsDiff"/> — the edit script every diff the user sees is built on.
///
/// <para>The unified view, the two side-by-side views and the context-collapsed variants all render
/// whatever this returns, so an error here is an error in all of them at once. It is also the piece
/// a reviewer trusts most directly: a diff that quietly drops a line is worse than no diff.</para>
/// </summary>
public class DiffViewerLcsTests
{
    /// <summary>The script as a compact string: "=" equal, "+" insert, "-" delete.</summary>
    private static string Script(string[] original, string[] modified) =>
        string.Concat(DiffViewer.ComputeLcsDiff(original, modified).Select(op => op.Type switch
        {
            DiffViewer.DiffOpType.Equal => "=",
            DiffViewer.DiffOpType.Insert => "+",
            _ => "-",
        }));

    [Fact]
    public void IdenticalFiles_AreAllEqual()
    {
        Assert.Equal("===", Script(["a", "b", "c"], ["a", "b", "c"]));
    }

    [Fact]
    public void AnInsertedLine_IsAnInsert()
    {
        Assert.Equal("=+=", Script(["a", "b"], ["a", "x", "b"]));
    }

    [Fact]
    public void ADeletedLine_IsADelete()
    {
        Assert.Equal("=-=", Script(["a", "x", "b"], ["a", "b"]));
    }

    [Fact]
    public void AChangedLine_IsADeleteThenAnInsert()
    {
        // There is no "replace" operation; a changed line is the old one gone and a new one added.
        // The order is load-bearing rather than incidental: the side-by-side renderers walk the
        // script in order and pair a removed line with the added one that follows it, so swapping
        // them puts the two halves of one edit on different rows.
        Assert.Equal("=-+=", Script(["a", "old", "b"], ["a", "new", "b"]));
    }

    [Fact]
    public void TheScriptIsMinimal_NotMerelyValid()
    {
        // Any walk that emits a delete for everything and an insert for everything is a *valid*
        // script; what makes this one worth the LCS table is that it keeps as much as possible.
        // This pair shares B, D, E and nothing longer, and the last lines differ, so the answer
        // has to come from the table rather than from the lines happening to match at the end.
        var script = Script(["A", "B", "C", "D", "E"], ["B", "D", "E", "A", "C"]);

        Assert.Equal(3, script.Count(c => c == '='));
    }

    [Fact]
    public void ReorderedLines_KeepTheLongestCommonSubsequenceNotTheFirstMatchFound()
    {
        // The case that actually exercises the LCS table rather than the equality shortcut. "a" and
        // "c" can both be kept if the walk looks ahead; a table built wrong keeps only one and
        // rewrites the rest, which on a real file turns a two-line edit into a whole-block rewrite.
        var script = Script(["a", "b", "c", "d"], ["a", "d", "c", "b"]);

        Assert.Equal(2, script.Count(c => c == '='));
    }

    [Fact]
    public void AnEmptyOriginal_IsAllInserts()
    {
        Assert.Equal("++", Script([], ["a", "b"]));
    }

    [Fact]
    public void AnEmptyModified_IsAllDeletes()
    {
        Assert.Equal("--", Script(["a", "b"], []));
    }

    [Fact]
    public void TwoEmptyFiles_ProduceNothing()
    {
        Assert.Empty(DiffViewer.ComputeLcsDiff([], []));
    }

    [Fact]
    public void NothingInCommon_DeletesEverythingAndInsertsEverything()
    {
        var script = Script(["a", "b"], ["x", "y"]);

        Assert.Equal(2, script.Count(c => c == '-'));
        Assert.Equal(2, script.Count(c => c == '+'));
    }

    [Fact]
    public void EveryOriginalLineIsAccountedFor_ExactlyOnceAndInOrder()
    {
        // The property that matters more than the exact script: nothing is dropped and nothing is
        // duplicated. A diff that loses a line is the failure a reviewer would never catch.
        string[] original = ["a", "b", "c", "d", "e"];
        string[] modified = ["a", "x", "c", "e", "f"];

        var consumed = DiffViewer.ComputeLcsDiff(original, modified)
            .Where(op => op.Type != DiffViewer.DiffOpType.Insert)
            .Select(op => op.OriginalIndex)
            .ToList();

        Assert.Equal(Enumerable.Range(0, original.Length), consumed);
    }

    [Fact]
    public void EveryModifiedLineIsAccountedFor_ExactlyOnceAndInOrder()
    {
        string[] original = ["a", "b", "c", "d", "e"];
        string[] modified = ["a", "x", "c", "e", "f"];

        var produced = DiffViewer.ComputeLcsDiff(original, modified)
            .Where(op => op.Type != DiffViewer.DiffOpType.Delete)
            .Select(op => op.ModifiedIndex)
            .ToList();

        Assert.Equal(Enumerable.Range(0, modified.Length), produced);
    }

    [Fact]
    public void ReplayingTheScript_ReproducesTheModifiedFile()
    {
        // The strongest statement of correctness available without asserting a particular script:
        // keep the equals and the inserts, and you have the new file back.
        string[] original = ["package P", "  model A", "    Real x;", "  end A;", "end P;"];
        string[] modified = ["package P", "  model A", "    Real x;", "    Real y;", "  end A;", "end P;"];

        var rebuilt = DiffViewer.ComputeLcsDiff(original, modified)
            .Where(op => op.Type != DiffViewer.DiffOpType.Delete)
            .Select(op => modified[op.ModifiedIndex])
            .ToArray();

        Assert.Equal(modified, rebuilt);
    }

    [Fact]
    public void ReplayingTheScriptBackwards_ReproducesTheOriginalFile()
    {
        string[] original = ["package P", "  model A", "    Real x;", "  end A;", "end P;"];
        string[] modified = ["package P", "  model A", "  end A;", "end P;"];

        var rebuilt = DiffViewer.ComputeLcsDiff(original, modified)
            .Where(op => op.Type != DiffViewer.DiffOpType.Insert)
            .Select(op => original[op.OriginalIndex])
            .ToArray();

        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void TheLongestCommonRunIsKept()
    {
        // The point of using LCS rather than a line-by-line walk: three shared lines out of four
        // must be recognised as shared, not rewritten as four changes.
        var script = Script(["a", "b", "c", "d"], ["a", "b", "c", "z"]);

        Assert.Equal(3, script.Count(c => c == '='));
    }

    [Fact]
    public void RepeatedLines_AreNotConfusedWithEachOther()
    {
        // Modelica is full of identical lines - "end X;", blank lines, closing parens - so a diff
        // that matches the wrong one of two identical lines shifts everything after it.
        string[] original = ["x", "end;", "y", "end;"];
        string[] modified = ["x", "end;", "y", "z", "end;"];

        var rebuilt = DiffViewer.ComputeLcsDiff(original, modified)
            .Where(op => op.Type != DiffViewer.DiffOpType.Delete)
            .Select(op => modified[op.ModifiedIndex])
            .ToArray();

        Assert.Equal(modified, rebuilt);
    }
}
