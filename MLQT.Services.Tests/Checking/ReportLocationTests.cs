using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// <see cref="ReportLocation"/> — the one answer to "which file and which line does this finding
/// point at", now that the CLI's <c>CheckReport</c> and the Code Review page's export both ask it
/// here instead of each answering for themselves (backlog B105).
/// </summary>
public class ReportLocationTests
{
    private static string Abs(params string[] parts) => Path.GetFullPath(Path.Combine(parts));

    // ---- LineIn -------------------------------------------------------------------------------

    [Fact]
    public void LineIn_AddsTheClassStartToAClassRelativeLine()
    {
        var location = new ClassLocation(Abs("C:", "lib", "Thing.mo"), StartLine: 100, LinesMapToFile: true);

        Assert.Equal(104, ReportLocation.LineIn(location, 5));
    }

    [Fact]
    public void LineIn_ForTheFirstLineOfAClass_IsTheClassStart()
    {
        var location = new ClassLocation(Abs("C:", "lib", "Thing.mo"), StartLine: 100, LinesMapToFile: true);

        Assert.Equal(100, ReportLocation.LineIn(location, 1));
    }

    [Fact]
    public void LineIn_WhenTheStoredSourceIsNotTheFilesText_PointsAtTheClassDeclaration()
    {
        // A trimmed package or a re-rendered class: the offset would land on a real line saying
        // something else. Pointing at the right class is always true.
        var location = new ClassLocation(Abs("C:", "lib", "package.mo"), StartLine: 42, LinesMapToFile: false);

        Assert.Equal(42, ReportLocation.LineIn(location, 17));
    }

    [Fact]
    public void LineIn_WithNoLocation_FallsBackToTheFindingsOwnLine()
    {
        Assert.Equal(7, ReportLocation.LineIn(null, 7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void LineIn_NeverReturnsALineBeforeTheFirst(int lineInClass)
    {
        // A report has to open the file somewhere valid even for a finding that carries no line.
        Assert.Equal(1, ReportLocation.LineIn(null, lineInClass));
    }

    // ---- RelativeFile -------------------------------------------------------------------------

    [Fact]
    public void RelativeFile_IsRelativeToTheLibraryRoot()
    {
        var path = ReportLocation.RelativeFile(Abs("C:", "libs", "Lib", "Sub", "Thing.mo"), Abs("C:", "libs", "Lib"));

        Assert.Equal("Sub/Thing.mo", path);
    }

    [Fact]
    public void RelativeFile_UsesForwardSlashes()
    {
        // The report is read on whatever platform opens it, and a backslash in a path field is not
        // something a Linux reader or a CI annotation can act on.
        var path = ReportLocation.RelativeFile(Abs("C:", "libs", "Lib", "A", "B", "Thing.mo"), Abs("C:", "libs", "Lib"));

        Assert.NotNull(path);
        Assert.DoesNotContain('\\', path);
        Assert.Equal("A/B/Thing.mo", path);
    }

    [Fact]
    public void RelativeFile_ForAFileOutsideTheLibrary_WalksUpRatherThanGoingAbsolute()
    {
        // A dependency, checked alongside but not part of this library. Both copies of this rule
        // carried a comment saying this case fell back to the absolute path; it never did, and this
        // test is what showed that. The walking-up path is still resolvable against the library the
        // report names, so the behaviour stands and the comment was corrected instead.
        var path = ReportLocation.RelativeFile(Abs("C:", "other", "Dep.mo"), Abs("C:", "libs", "Lib"));

        Assert.Equal("../../other/Dep.mo", path);
    }

    [Fact]
    public void RelativeFile_WithNoLibraryRoot_KeepsTheFullPath()
    {
        var file = Abs("C:", "libs", "Lib", "Thing.mo");

        Assert.Equal(file, ReportLocation.RelativeFile(file, libraryRoot: null));
        Assert.Equal(file, ReportLocation.RelativeFile(file, libraryRoot: ""));
    }

    [Fact]
    public void RelativeFile_WithNoFile_IsNull()
    {
        Assert.Null(ReportLocation.RelativeFile((string?) null, Abs("C:", "libs", "Lib")));
        Assert.Null(ReportLocation.RelativeFile("", Abs("C:", "libs", "Lib")));
    }

    [Fact]
    public void RelativeFile_ForTheLibraryRootFileItself_IsJustTheFileName()
    {
        var path = ReportLocation.RelativeFile(Abs("C:", "libs", "Lib", "package.mo"), Abs("C:", "libs", "Lib"));

        Assert.Equal("package.mo", path);
    }

    [Fact]
    public void RelativeFile_FromAClassLocation_ReadsItsPath()
    {
        var location = new ClassLocation(Abs("C:", "libs", "Lib", "Sub", "Thing.mo"), 1, true);

        Assert.Equal("Sub/Thing.mo", ReportLocation.RelativeFile(location, Abs("C:", "libs", "Lib")));
    }

    [Fact]
    public void RelativeFile_FromNoClassLocation_IsNull()
    {
        Assert.Null(ReportLocation.RelativeFile((ClassLocation?) null, Abs("C:", "libs", "Lib")));
    }
}
