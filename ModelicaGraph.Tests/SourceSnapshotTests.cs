using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Swapping a class's stored source out and back again, without losing anything on the way.
///
/// <para>Two MCP tools do this — one formats a file from its text on disk, the other trims a
/// package's inline children before checking it — and both wrote the snapshot by hand as
/// <c>(ModelicaCode, ParsedCode)</c>. What a class carries beside its source has grown since: setting
/// <see cref="ModelDefinition.ModelicaCode"/> now also drops the coverage facts and the suppression
/// set, and trimming sets two flags on the node. Neither hand-written copy restored any of that, and
/// nothing said whether the omissions were deliberate (backlog B93).</para>
/// </summary>
public class SourceSnapshotTests
{
    private static ModelNode Node() => new("A", "A", "model A \"a\"\n  Real x;\nend A;");

    [Fact]
    public void ARoundTripLeavesTheClassAsItWas()
    {
        var node = Node();
        node.Definition.EnsureParsed();
        node.Definition.Coverage = new CoverageFacts(true, false, 1, 0, 0, 0, 0);
        node.Definition.Suppressions = SuppressionSet.Empty;

        var snapshot = node.TakeSourceSnapshot();
        var tree = node.Definition.ParsedCode;

        // What a caller does in between: replace the source with the file's own text.
        node.Definition.ModelicaCode = "model A \"different\" end A;";
        node.SourceMatchesFile = false;
        node.ChildrenTrimmed = true;

        node.RestoreSource(snapshot);

        Assert.Equal("model A \"a\"\n  Real x;\nend A;", node.Definition.ModelicaCode);
        Assert.Same(tree, node.Definition.ParsedCode);
        Assert.NotNull(node.Definition.Coverage);
        Assert.Same(SuppressionSet.Empty, node.Definition.Suppressions);
        Assert.True(node.SourceMatchesFile);
        Assert.False(node.ChildrenTrimmed);
    }

    [Fact]
    public void TheCachesAreRestoredDespiteTheSetterClearingThem()
    {
        // The specific reason the two hand-written versions had gone stale: assigning ModelicaCode
        // clears Coverage and Suppressions, so a restore that sets the source last would undo itself.
        var node = Node();
        node.Definition.Coverage = new CoverageFacts(true, false, 1, 0, 0, 0, 0);
        node.Definition.Suppressions = SuppressionSet.Empty;

        var snapshot = node.TakeSourceSnapshot();
        node.Definition.ModelicaCode = "model A end A;";

        Assert.Null(node.Definition.Coverage);       // the setter did clear them
        Assert.Null(node.Definition.Suppressions);

        node.RestoreSource(snapshot);

        Assert.NotNull(node.Definition.Coverage);
        Assert.NotNull(node.Definition.Suppressions);
    }

    [Fact]
    public void ASnapshotOfAnUnparsedClassRestoresAnUnparsedClass()
    {
        // Restoring must not resurrect a tree that was not there — the point of releasing one is that
        // it stays released.
        var node = Node();
        var snapshot = node.TakeSourceSnapshot();

        node.Definition.EnsureParsed();
        Assert.NotNull(node.Definition.ParsedCode);

        node.RestoreSource(snapshot);

        Assert.Null(node.Definition.ParsedCode);
    }

    [Fact]
    public void TheSnapshotNamesEveryFieldTheSourceSetterClears()
    {
        // The guard: if ModelicaCode's setter learns to clear a third thing, the snapshot has to
        // learn it too, and this fails until it does.
        var cleared = new[] { "ParsedCode", "Coverage", "Suppressions" };
        var captured = typeof(ModelNode.SourceSnapshot)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(cleared, name => Assert.Contains(name, captured));
    }
}
