using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Services.Tests;

/// <summary>
/// Turning a finding's class-relative line into a line in the file it will be reported against.
/// </summary>
public class ClassLocationTests
{
    [Fact]
    public void ALineInsideTheClass_IsOffsetByWhereTheClassStarts()
    {
        var location = new ClassLocation(@"C:\lib\package.mo", StartLine: 120, LinesMapToFile: true);

        Assert.Equal(120, location.FileLine(1));    // the class declaration itself
        Assert.Equal(124, location.FileLine(5));
    }

    [Fact]
    public void AClassAtTheTopOfItsOwnFile_IsUnchanged()
    {
        var location = new ClassLocation(@"C:\lib\Model.mo", StartLine: 1, LinesMapToFile: true);

        Assert.Equal(7, location.FileLine(7));
    }

    [Fact]
    public void WhenTheStoredSourceIsNoLongerTheFiles_TheClassDeclarationIsReported()
    {
        // A package whose children were trimmed out, or a class the formatter re-rendered: adding
        // the offset would point confidently at a line that belongs to something else.
        var location = new ClassLocation(@"C:\lib\package.mo", StartLine: 120, LinesMapToFile: false);

        Assert.Equal(120, location.FileLine(1));
        Assert.Equal(120, location.FileLine(48));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AnUnknownStartLine_IsTreatedAsTheTopOfTheFile(int startLine)
    {
        var location = new ClassLocation(@"C:\lib\Model.mo", startLine, LinesMapToFile: true);

        Assert.Equal(1, location.FileLine(1));
        Assert.Equal(4, location.FileLine(4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALineNumberNobodySet_LandsOnTheClassDeclaration(int lineInClass)
    {
        var location = new ClassLocation(@"C:\lib\package.mo", StartLine: 50, LinesMapToFile: true);

        Assert.Equal(50, location.FileLine(lineInClass));
    }

    [Fact]
    public void ForGraph_MapsEachClassToItsFileAndStart()
    {
        var graph = new DirectedGraph();
        var file = new FileNode("f1", @"C:\lib\Fix\package.mo");
        graph.AddNode(file);

        var package = new ModelNode("Fix", "Fix", "package Fix end Fix;") { StartLine = 2 };
        var nested = new ModelNode("Fix.Late", "Late", "model Late end Late;") { StartLine = 12 };
        graph.AddNode(package);
        graph.AddNode(nested);
        graph.AddFileContainsModel("f1", "Fix");
        graph.AddFileContainsModel("f1", "Fix.Late");

        // Trimming a package rewrites its stored source, so its lines stop matching the file.
        package.SourceMatchesFile = false;

        var locations = ClassLocation.ForGraph(graph);

        Assert.Equal(12, locations["Fix.Late"].FileLine(1));
        Assert.Equal(16, locations["Fix.Late"].FileLine(5));
        Assert.Equal(2, locations["Fix"].FileLine(5));           // the fallback
        Assert.Equal(@"C:\lib\Fix\package.mo", locations["Fix"].FilePath);
    }
}
