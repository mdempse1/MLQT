using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Tests for <see cref="ReadOnlySourceLoader"/> and <see cref="ReadOnlySources"/>: a read-only
/// library's classes reach the graph marked, bannered, in a file no write path will take, and
/// ranked against every other copy of the same class in whichever order they arrive.
/// </summary>
public class ReadOnlySourceLoaderTests
{
    private const string Note = "Supplied by a test host - this is NOT the vendor's source.\nRead-only.";

    private const string PackageText = """
        within;
        package Lib "A supplied library"
          model Inline "nested in the package file"
            parameter Real k = 1 "gain";
          end Inline;
          annotation (version = "2.1.0");
        end Lib;
        """;

    private const string StandaloneText = """
        within Lib;
        model Pump "a pump"
          parameter Real dp = 1e5 "pressure rise";
        equation
          dp = dp;
        end Pump;
        """;

    private sealed class TestSource(
        ReadOnlySourceKind kind, ReadOnlySourceContent content, string? location = null) : IReadOnlyClassSource
    {
        public string LibraryName => "Lib";
        public string? LibraryVersion => "2.1.0";
        public ReadOnlySourceKind Kind => kind;
        public string? Location => location;
        public string ProvenanceNote => Note;
        public ReadOnlySourceContent Read(CancellationToken cancellationToken) => content;
    }

    private static TestSource Supplied(params SuppliedText[] texts) =>
        new(ReadOnlySourceKind.Supplied, new ReadOnlySourceContent { Texts = texts });

    private static TestSource SuppliedLibrary() => Supplied(
        new SuppliedText("Lib/package.mo", PackageText),
        new SuppliedText("Lib/Pump.mo", StandaloneText),
        new SuppliedText("Lib/package.order", "Pump\nInline\n"));

    private static ReadOnlySourceLoad Load(DirectedGraph graph, TestSource source) =>
        ReadOnlySourceLoader.Load(graph, source, source.Read(CancellationToken.None));

    private static DocumentedClass Documented(string fullName, IReadOnlyList<string>? children = null) =>
        new(fullName, "recovered", [], true, null,
            children is null ? DocumentedClass.KindModel : DocumentedClass.KindPackage,
            children ?? [], [], [], [], [], []);

    private static void LoadRecovered(DirectedGraph graph, params DocumentedClass[] classes) =>
        ExternalStubBuilder.AddDocumentedClasses(graph, classes, "C:/vendor/Lib/package.moe");

    #region Supplied classes

    [Fact]
    public void EverySuppliedClass_IsAStubWithTheSourcesBanner()
    {
        var graph = new DirectedGraph();

        var load = Load(graph, SuppliedLibrary());

        Assert.Equal(["Lib", "Lib.Inline", "Lib.Pump"], load.ModelIds.Order());
        Assert.Equal(0, load.Superseded);
        foreach (var id in load.ModelIds)
        {
            var node = graph.GetNode<ModelNode>(id)!;
            Assert.True(node.IsExternalStub, id);
            // Supplied, not recovered: the view tools read its declarations, not a documentation record.
            Assert.Null(node.RecoveredFromDocumentation);
            Assert.Equal(ReadOnlySourceKind.Supplied, ReadOnlySources.KindOf(node));
            Assert.StartsWith(
                "// Supplied by a test host - this is NOT the vendor's source.\n// Read-only.\n",
                node.Definition.ModelicaCode);
        }
    }

    [Fact]
    public void ASuppliedClass_KeepsWhatItsTextDeclares()
    {
        var graph = new DirectedGraph();
        Load(graph, SuppliedLibrary());

        var pump = graph.GetNode<ModelNode>("Lib.Pump")!;
        Assert.Equal("model", pump.ClassType);
        Assert.Equal("Lib", pump.ParentModelName);
        Assert.Contains("parameter Real dp = 1e5", pump.Definition.ModelicaCode);
        // The equation the vendor made visible is there to be read.
        Assert.Contains("dp = dp;", pump.Definition.ModelicaCode);
        Assert.Equal("2.1.0", graph.GetNode<ModelNode>("Lib")!.Version);
    }

    [Fact]
    public void EverySuppliedFile_IsUnderTheInMemoryRoot_AndIsReadOnly()
    {
        var graph = new DirectedGraph();
        var load = Load(graph, SuppliedLibrary());

        foreach (var id in load.ModelIds)
        {
            var file = graph.GetNode<FileNode>(graph.GetNode<ModelNode>(id)!.ContainingFileId!)!;
            Assert.StartsWith("mlqt-readonly://Lib/Lib/", file.FilePath);
            Assert.True(ReadOnlySources.IsReadOnlyPath(file.FilePath), file.FilePath);
        }
    }

    [Fact]
    public void APackageOrder_OrdersThePackageItsPackageMoDefines()
    {
        var graph = new DirectedGraph();
        Load(graph, SuppliedLibrary());

        Assert.Equal(["Pump", "Inline"], graph.GetNode<ModelNode>("Lib")!.PackageOrder!);
    }

    [Fact]
    public void AClassWithSyntaxErrors_IsStillReadOnly()
    {
        var graph = new DirectedGraph();

        var load = Load(graph, Supplied(new SuppliedText("Lib/Broken.mo", "within Lib;\nmodel Broken\n  Real x\nend")));

        var broken = graph.GetNode<ModelNode>(Assert.Single(load.ModelIds))!;
        Assert.NotEmpty(broken.Definition.ParserErrors);
        Assert.True(broken.IsExternalStub);
    }

    [Fact]
    public void AFileThatDoesNotParse_IsStillAReadOnlyClass()
    {
        // The placeholder a broken file becomes stands in the tree like a class, so it must be as
        // read-only as the rest of the library it came from.
        var graph = new DirectedGraph();

        var load = Load(graph, Supplied(new SuppliedText("Lib/Broken.mo", "within Lib;\n@@@ this is not Modelica @@@")));

        var placeholder = graph.GetNode<ModelNode>(Assert.Single(load.ModelIds))!;
        Assert.True(placeholder.IsParseFailurePlaceholder);
        Assert.True(placeholder.IsExternalStub);
    }

    [Fact]
    public void ASuppliedLocation_MustBeInMemory()
    {
        var source = new TestSource(
            ReadOnlySourceKind.Supplied, new ReadOnlySourceContent { Texts = [new("Lib/package.mo", PackageText)] },
            location: "C:/vendor/Lib");

        var error = Assert.Throws<ArgumentException>(() => Load(new DirectedGraph(), source));
        Assert.Contains("mlqt-readonly://", error.Message);
    }

    [Fact]
    public void ASourceMayNotNameAWritableLocation()
    {
        var source = new TestSource(
            ReadOnlySourceKind.RecoveredFromDocumentation,
            new ReadOnlySourceContent { Documented = [Documented("Lib")] },
            location: "C:/vendor/Lib/package.mo");

        Assert.Throws<ArgumentException>(() => Load(new DirectedGraph(), source));
    }

    [Fact]
    public void ContentOfTheOtherKind_IsRefused()
    {
        var suppliedWithDocumentation = new TestSource(
            ReadOnlySourceKind.Supplied, new ReadOnlySourceContent { Documented = [Documented("Lib")] });
        var recoveredWithText = new TestSource(
            ReadOnlySourceKind.RecoveredFromDocumentation,
            new ReadOnlySourceContent { Texts = [new("Lib/package.mo", PackageText)] },
            location: "C:/vendor/Lib/package.moe");

        Assert.Throws<ArgumentException>(() => Load(new DirectedGraph(), suppliedWithDocumentation));
        Assert.Throws<ArgumentException>(() => Load(new DirectedGraph(), recoveredWithText));
    }

    [Fact]
    public void ARecoveredSource_LoadsThroughTheStubBuilder_WithItsOwnBanner()
    {
        var graph = new DirectedGraph();
        var source = new TestSource(
            ReadOnlySourceKind.RecoveredFromDocumentation,
            new ReadOnlySourceContent { Documented = [Documented("Lib")] },
            location: "C:/vendor/Lib/package.moe");

        var load = Load(graph, source);

        var node = graph.GetNode<ModelNode>(Assert.Single(load.ModelIds))!;
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, ReadOnlySources.KindOf(node));
        Assert.StartsWith("// Supplied by a test host", node.Definition.ModelicaCode);
        Assert.Equal("2.1.0", node.Version);
    }

    #endregion

    #region Precedence between copies of a class

    [Fact]
    public void ReadableSource_KeepsItsPlace_WhenASuppliedCopyArrivesAfterIt()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "C:/work/Lib/Pump.mo", StandaloneText);

        var load = Load(graph, Supplied(new SuppliedText("Lib/Pump.mo", StandaloneText)));

        var pump = graph.GetNode<ModelNode>("Lib.Pump")!;
        Assert.False(pump.IsExternalStub);
        Assert.Empty(load.ModelIds);
        Assert.Equal(1, load.Superseded);
        // And it is not pointed at the in-memory file, which no write would then reach.
        Assert.Equal("C:/work/Lib/Pump.mo", graph.GetNode<FileNode>(pump.ContainingFileId!)!.FilePath);
    }

    [Fact]
    public void ReadableSource_ReplacesASuppliedCopy_LoadedBeforeIt()
    {
        var graph = new DirectedGraph();
        Load(graph, Supplied(new SuppliedText("Lib/Pump.mo", StandaloneText)));

        GraphBuilder.LoadModelicaFile(graph, "C:/work/Lib/Pump.mo", StandaloneText);

        var pump = graph.GetNode<ModelNode>("Lib.Pump")!;
        Assert.False(pump.IsExternalStub);
        Assert.Equal("C:/work/Lib/Pump.mo", graph.GetNode<FileNode>(pump.ContainingFileId!)!.FilePath);
    }

    [Fact]
    public void ASuppliedClass_ReplacesOneRecoveredFromDocumentation()
    {
        var graph = new DirectedGraph();
        LoadRecovered(graph, Documented("Lib.Pump"));

        var load = Load(graph, Supplied(new SuppliedText("Lib/Pump.mo", StandaloneText)));

        var pump = graph.GetNode<ModelNode>("Lib.Pump")!;
        Assert.Equal(ReadOnlySourceKind.Supplied, ReadOnlySources.KindOf(pump));
        Assert.Equal(["Lib.Pump"], load.ModelIds);
        Assert.True(ReadOnlySources.IsInMemoryPath(graph.GetNode<FileNode>(pump.ContainingFileId!)!.FilePath));
    }

    [Fact]
    public void AClassRecoveredFromDocumentation_LeavesASuppliedOneAlone()
    {
        var graph = new DirectedGraph();
        Load(graph, Supplied(new SuppliedText("Lib/Pump.mo", StandaloneText)));

        var added = ExternalStubBuilder.AddDocumentedClasses(
            graph, [Documented("Lib.Pump")], "C:/vendor/Lib/package.moe", out var superseded);

        var pump = graph.GetNode<ModelNode>("Lib.Pump")!;
        Assert.Equal(ReadOnlySourceKind.Supplied, ReadOnlySources.KindOf(pump));
        Assert.Empty(added);
        Assert.Equal(1, superseded);
        // Never moved into the encrypted package, where it would read as recovered.
        Assert.True(ReadOnlySources.IsInMemoryPath(graph.GetNode<FileNode>(pump.ContainingFileId!)!.FilePath));
    }

    #endregion

    #region ReadOnlySources

    [Theory]
    [InlineData("C:/vendor/Lib/package.moe", true)]
    [InlineData("mlqt-readonly://Lib/Lib/package.mo", true)]
    [InlineData("MLQT-READONLY://Lib/x.mo", true)]
    [InlineData("C:/work/Lib/package.mo", false)]
    [InlineData("mlqt-readonly-not://x.mo", false)]
    [InlineData(null, false)]
    public void IsReadOnlyPath_CoversBothKindsOfReadOnlyFile(string? path, bool readOnly) =>
        Assert.Equal(readOnly, ReadOnlySources.IsReadOnlyPath(path));

    [Fact]
    public void Precedence_RanksSourceThenSuppliedThenRecovered()
    {
        Assert.True(ReadOnlySources.Precedence(null) > ReadOnlySources.Precedence(ReadOnlySourceKind.Supplied));
        Assert.True(ReadOnlySources.Precedence(ReadOnlySourceKind.Supplied)
                    > ReadOnlySources.Precedence(ReadOnlySourceKind.RecoveredFromDocumentation));
    }

    [Fact]
    public void KindOf_TellsTheThreeKindsOfClassApart()
    {
        var readable = new ModelNode("A", "A", "model A end A;");
        var supplied = new ModelNode("B", "B", "model B end B;") { IsExternalStub = true };
        var recovered = new ModelNode("C", "C", "model C end C;")
        {
            IsExternalStub = true,
            RecoveredFromDocumentation = Documented("C")
        };

        Assert.Null(ReadOnlySources.KindOf(readable));
        Assert.Equal(ReadOnlySourceKind.Supplied, ReadOnlySources.KindOf(supplied));
        Assert.Equal(ReadOnlySourceKind.RecoveredFromDocumentation, ReadOnlySources.KindOf(recovered));
    }

    [Fact]
    public void Banner_CommentsEveryLine_AndEndsWithANewline()
    {
        Assert.Equal("// one\n//\n// two\n", ReadOnlySources.Banner("one\r\n\r\ntwo\n"));
    }

    [Fact]
    public void TheRecoveredBanner_IsTheHeaderStubsAlwaysHad()
    {
        // Word for word: an encrypted library's classes read exactly as they did before the note
        // became the source's to give.
        Assert.Equal(
            "// Reconstructed by MLQT from this library's documentation — this is NOT the vendor's source.\n" +
            "// The library ships encrypted, so only what its documentation states is known here: the name,\n" +
            "// the description, the base classes and whether there is an icon. Read-only.\n",
            ReadOnlySources.Banner(ExternalStubBuilder.RecoveredProvenanceNote));
    }

    #endregion
}
