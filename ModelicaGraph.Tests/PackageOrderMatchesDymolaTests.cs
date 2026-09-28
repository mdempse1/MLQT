using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// B195 — <c>PackageOrderMatchesDymola</c>, which narrows <c>MLQT.Structure.PackageOrder</c> to what
/// Dymola's own loader would warn about.
///
/// <para>Dymola emits, of its own accord on load: <i>"Warning: The package.order is incomplete,
/// since the class Bad in file/directory .../Sub/Bad.mo is missing."</i> It resolves a package's
/// children by file name, in exactly two places — <c>&lt;Package&gt;/Name.mo</c> and
/// <c>&lt;Package&gt;/Name/package.mo</c>. So MLQT's check is the larger one in two ways, and both
/// are tested here: it also reports stale entries, and it reports a class in a file whose name does
/// not match it, which Dymola cannot load from there and so never warns about.</para>
///
/// <para><b>B195 expected a third difference that does not exist.</b> The item said MLQT would find
/// classes in folders Dymola never descends into; it does not — <c>LibraryDataService</c> skips a
/// directory with no <c>package.mo</c> for the same reason Dymola does. Measured through the CLI
/// rather than reasoned about, and the row is corrected.</para>
///
/// <para>The setting is for a repository whose standard is "no warnings on load in Dymola", so that
/// MLQT's gate is that gate rather than a stricter one.</para>
/// </summary>
public class PackageOrderMatchesDymolaTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-package-order-dymola", Guid.NewGuid().ToString("N"));

    public PackageOrderMatchesDymolaTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A package at <c>&lt;root&gt;/P/package.mo</c> with one child at the given relative path.
    /// Paths are real, because where a class is stored is the whole of what the setting reads.
    /// </summary>
    private DirectedGraph Build(string childRelativePath, string[] packageOrder, string childName = "A")
    {
        var graph = new DirectedGraph();

        var packageFile = Path.Combine(_root, "P", "package.mo");
        graph.AddNode(new FileNode("f-package", packageFile));
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;")
        {
            ClassType = "package",
            ContainingFileId = "f-package",
            PackageOrder = packageOrder,
            StartLine = 1
        });

        var childFile = Path.Combine(_root, childRelativePath);
        graph.AddNode(new FileNode("f-child", childFile));
        graph.AddNode(new ModelNode("P." + childName, childName, $"model {childName} end {childName};")
        {
            ClassType = "model",
            ParentModelName = "P",
            ContainingFileId = "f-child"
        });

        return graph;
    }

    private static List<Finding> Analyze(DirectedGraph graph, bool matchDymola)
    {
        var settings = new StyleCheckingSettings
        {
            CheckPackageOrder = true,
            PackageOrderMatchesDymola = matchDymola
        };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(ctx).Where(f => f.RuleId == RuleIds.PackageOrder).ToList();
    }

    [Fact]
    public void AClassInTheExpectedFile_IsStillReportedWhenMissing()
    {
        // <Package>/Name.mo — the first of the two places Dymola looks, and the exact case its own
        // warning names. Narrowing must not lose it.
        var graph = Build(Path.Combine("P", "A.mo"), ["Other"]);

        var finding = Assert.Single(Analyze(graph, matchDymola: true), f => f.Discriminator == "missing");
        Assert.Equal("A", finding.ElementPath);
    }

    [Fact]
    public void AClassStoredAsItsOwnPackageDirectory_IsStillReportedWhenMissing()
    {
        // <Package>/Name/package.mo — the second place.
        var graph = Build(Path.Combine("P", "A", "package.mo"), ["Other"]);

        Assert.Single(Analyze(graph, matchDymola: true), f => f.Discriminator == "missing");
    }

    [Fact]
    public void AClassInAFileNamedSomethingElse_IsDroppedButReportedOtherwise()
    {
        // The real difference on the missing side, and the one the CLI demonstrates: right
        // directory, wrong file name. MLQT reads every .mo in a package directory and takes the
        // class's own name, so it loads A from Widget.mo; Dymola resolves by file name and cannot
        // load it from there at all, so it never warns. The setting is the only thing that decides
        // whether it is reported.
        var graph = Build(Path.Combine("P", "Widget.mo"), ["Other"]);

        Assert.DoesNotContain(Analyze(graph, matchDymola: true), f => f.Discriminator == "missing");
        Assert.Single(Analyze(graph, matchDymola: false), f => f.Discriminator == "missing");
    }

    [Fact]
    public void AClassInAFolderThatIsNotAPackage_IsAlsoDropped()
    {
        // Kept because the rule's code has a branch for it, not because a loader produces it:
        // neither MLQT nor Dymola descends into a directory with no package.mo, so this shape
        // arrives only from a graph assembled some other way. Treated the same as the case above.
        var graph = Build(Path.Combine("P", "Extras", "A.mo"), ["Other"]);

        Assert.DoesNotContain(Analyze(graph, matchDymola: true), f => f.Discriminator == "missing");
        Assert.Single(Analyze(graph, matchDymola: false), f => f.Discriminator == "missing");
    }

    [Fact]
    public void AClassHeldInsideThePackagesOwnFile_IsStillReported()
    {
        // Dymola is already reading package.mo, so it sees an inline class and can tell that
        // package.order does not list it.
        var graph = new DirectedGraph();
        var packageFile = Path.Combine(_root, "P", "package.mo");
        graph.AddNode(new FileNode("f-package", packageFile));
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;")
        {
            ClassType = "package",
            ContainingFileId = "f-package",
            PackageOrder = ["Other"],
            StartLine = 1
        });
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;")
        {
            ClassType = "model",
            ParentModelName = "P",
            ContainingFileId = "f-package"
        });

        Assert.Single(Analyze(graph, matchDymola: true), f => f.Discriminator == "missing");
    }

    [Fact]
    public void StaleEntriesAreNotReportedWhenMatchingDymola()
    {
        // Dymola reports an *incomplete* package.order and says nothing about an entry naming
        // something that is not there, so a repository asking for Dymola's answer is not asking for
        // these. With the setting off they come back, which is what makes this a narrowing rather
        // than a removal.
        var graph = Build(Path.Combine("P", "A.mo"), ["A", "Ghost"]);

        Assert.DoesNotContain(Analyze(graph, matchDymola: true), f => f.Discriminator == "stale");
        Assert.Single(Analyze(graph, matchDymola: false), f => f.Discriminator == "stale");
    }

    [Fact]
    public void AListedClassIsNeverReported_EitherWay()
    {
        // The control: with package.order complete there is nothing to report at either setting, so
        // the tests above are about the narrowing and not about the rule firing at all.
        var graph = Build(Path.Combine("P", "A.mo"), ["A"]);

        Assert.Empty(Analyze(graph, matchDymola: true));
        Assert.Empty(Analyze(graph, matchDymola: false));
    }

    [Fact]
    public void APackageWhoseFileIsUnknown_IsReportedRatherThanDropped()
    {
        // Nothing says where the package lives, so nothing can say whether Dymola would look there.
        // Reporting is the safe answer: the alternative silently drops findings from a gate.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;")
        {
            ClassType = "package",
            PackageOrder = ["Other"],
            StartLine = 1
        });
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;")
        {
            ClassType = "model",
            ParentModelName = "P"
        });

        Assert.Single(Analyze(graph, matchDymola: true), f => f.Discriminator == "missing");
    }
}
