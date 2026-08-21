using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Services.Tests;

/// <summary>
/// A dependency on the machine that is not the version the library targets resolves references against
/// classes that may have moved or changed between versions, so the findings become unreliable. This
/// is about the check's setup, not the source — hence a warning rather than a rule.
/// </summary>
public class UsesVersionCheckerTests
{
    private static DirectedGraph Graph(
        (string name, string? version, (string lib, string version)[]? uses)[] libraries)
    {
        var graph = new DirectedGraph();
        foreach (var (name, version, uses) in libraries)
        {
            graph.AddNode(new ModelNode(name, name, $"package {name} end {name};")
            {
                ClassType = "package",
                Version = version,
                Uses = uses?.ToDictionary(u => u.lib, u => u.version, StringComparer.Ordinal)
            });
        }
        return graph;
    }

    private static IReadOnlyList<UsesVersionMismatch> Check(DirectedGraph graph, string checkedRoot)
        => UsesVersionChecker.Check(graph, [graph.GetNode<ModelNode>(checkedRoot)!]);

    [Fact]
    public void MatchingVersions_ReportNothing()
    {
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.0.0")]), ("Modelica", "4.0.0", null)]);

        Assert.Empty(Check(graph, "Lib"));
    }

    [Fact]
    public void DifferentVersion_IsReported()
    {
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "3.2.2")]), ("Modelica", "4.0.0", null)]);

        var mismatch = Assert.Single(Check(graph, "Lib"));
        Assert.Equal("Modelica", mismatch.Library);
        Assert.Equal("3.2.2", mismatch.Declared);
        Assert.Equal("4.0.0", mismatch.Loaded);
        Assert.Contains("declares Modelica 3.2.2", mismatch.Describe());
    }

    [Fact]
    public void BuildSuffixOnTheLoadedCopy_IsNotADisagreement()
    {
        // MSL checkouts state versions like "4.2.0 dev"; a library targeting 4.2.0 is not in conflict.
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.2.0")]), ("Modelica", "4.2.0 dev", null)]);

        Assert.Empty(Check(graph, "Lib"));
    }

    [Fact]
    public void ShorterDeclarationMatchesALongerVersion()
    {
        // "targets 4.0" makes no claim about the patch digit.
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.0")]), ("Modelica", "4.0.0", null)]);

        Assert.Empty(Check(graph, "Lib"));
    }

    [Fact]
    public void LongerDeclarationThanTheLoadedVersion_IsReported()
    {
        // The reverse is a real disagreement: 4.0.3 was asked for, only "4.0" is on offer.
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.0.3")]), ("Modelica", "4.0", null)]);

        Assert.Single(Check(graph, "Lib"));
    }

    [Fact]
    public void DifferentMajorVersion_IsReported()
    {
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.0.0")]), ("Modelica", "3.2.3", null)]);

        Assert.Single(Check(graph, "Lib"));
    }

    [Fact]
    public void UnversionedLoadedCopy_IsReported()
    {
        // "targets 4.0.0" against a copy that states nothing cannot be verified — say so.
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.0.0")]), ("Modelica", null, null)]);

        var mismatch = Assert.Single(Check(graph, "Lib"));
        Assert.Null(mismatch.Loaded);
        Assert.Contains("states no version", mismatch.Describe());
    }

    [Fact]
    public void DeclaredButNotLoaded_IsIgnored()
    {
        // A different problem, already visible as unresolved references. Guessing a version for a
        // library that is absent says nothing useful.
        var graph = Graph([("Lib", "1.0.0", [("Modelica", "4.0.0")])]);

        Assert.Empty(Check(graph, "Lib"));
    }

    [Fact]
    public void OnlyTheCheckedLibrariesDeclarationsAreConsidered()
    {
        // A dependency's own uses(...) is not the user's problem — they did not write it.
        var graph = Graph([
            ("Lib", "1.0.0", null),
            ("Dep", "1.0.0", [("Modelica", "3.2.2")]),
            ("Modelica", "4.0.0", null)]);

        Assert.Empty(Check(graph, "Lib"));
    }

    [Fact]
    public void SeveralMismatches_AreAllReported_Sorted()
    {
        var graph = Graph([
            ("Lib", "1.0.0", [("Modelica", "3.2.2"), ("ExternData", "2.0.0")]),
            ("Modelica", "4.0.0", null),
            ("ExternData", "3.0.0", null)]);

        var mismatches = Check(graph, "Lib");

        Assert.Equal(2, mismatches.Count);
        Assert.Equal(["ExternData", "Modelica"], mismatches.Select(m => m.Library));
    }
}
