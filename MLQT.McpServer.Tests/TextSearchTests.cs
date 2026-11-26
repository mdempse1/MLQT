using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_find</c>'s text search: the library it is limited to, cancellation, and that the classes
/// <see cref="TextSearchPrefilter"/> skips unparsed could never have matched.
/// </summary>
public class TextSearchTests
{
    private const string LibraryA = """
        package A "first"
          model Reader "reads a Mat File Format table"
          end Reader;
          annotation (Documentation(info="<html><p>Holds the readers.</p></html>"));
        end A;
        """;

    private const string LibraryB = """
        package B "second"
          model Writer "writes a Mat File Format table"
          end Writer;
        end B;
        """;

    private static TestHost Load(params string[] libraries)
    {
        var host = new TestHost();
        for (var i = 0; i < libraries.Length; i++)
            host.Libraries.AddLibraryFromFileAsync(host.WriteMoFile($"L{i}.mo", libraries[i])).GetAwaiter().GetResult();
        return host;
    }

    private static TextSearchResult Text(TestHost host, string query, string? library = null) =>
        ToolAssert.Ok<TextSearchResult>(new FindTools(host.Libraries).Find(query, match: "text", library: library));

    [Fact]
    public void Text_WithoutLibrary_SearchesEveryLibrary()
    {
        using var host = Load(LibraryA, LibraryB);
        Assert.Equal(new[] { "A.Reader", "B.Writer" }, Text(host, "Mat File Format").Items.Select(i => i.Id));
    }

    [Fact]
    public void Text_WithLibrary_SearchesOnlyThatLibrary()
    {
        using var host = Load(LibraryA, LibraryB);

        var res = Text(host, "Mat File Format", library: "B");

        Assert.Equal(1, res.Total);
        Assert.Equal("B.Writer", Assert.Single(res.Items).Id);
    }

    [Fact]
    public void Text_WithAnUnknownLibrary_IsAnError()
    {
        using var host = Load(LibraryA);
        ToolAssert.Error(new FindTools(host.Libraries).Find("table", match: "text", library: "NoSuchLibrary"));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("code")]
    public void ACancelledSearch_StopsBeforeReadingAClass(string match)
    {
        using var host = Load(LibraryA, LibraryB);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // The token is checked before each class, so this one is not read at all; a search cancelled
        // part way stops once the class it is reading is done.
        Assert.Throws<OperationCanceledException>(() =>
            new FindTools(host.Libraries).Find("Mat File Format", match: match, cancellationToken: cancelled.Token));
        Assert.All(host.Libraries.GetAllModels(), m => Assert.Null(m.Definition.ParsedCode));
    }

    [Fact]
    public void ASearch_KeepsNoParseTrees()
    {
        using var host = Load(LibraryA, LibraryB);
        Text(host, "table");
        Assert.All(host.Libraries.GetAllModels(), m => Assert.Null(m.Definition.ParsedCode));
    }

    /// <summary>
    /// Each way the searched text is made from more than the source says, where a skipped class
    /// would be a lost hit: tags between the words, an entity spelling a letter, a word split
    /// across joined literals, and white space the documentation collapses.
    /// </summary>
    [Theory]
    [InlineData("""model M "x" annotation (Documentation(info="<p>Mat <b>File</b> Format</p>")); end M;""")]
    [InlineData("""model M "x" annotation (Documentation(info="<p>Mat &#70;ile Format</p>")); end M;""")]
    [InlineData("""model M "x" annotation (Documentation(info="<p>Mat Fi" + "le Format</p>")); end M;""")]
    [InlineData("""model M "Mat Fi" + "le Format table" end M;""")]
    [InlineData("""model M "x" annotation (Documentation(info="<p>Mat   File&nbsp;Format</p>")); end M;""")]
    public void TheSkippedClasses_CouldNotHaveMatched(string source)
    {
        using var host = Load($"package P \"p\" {source} end P;");

        var prefiltered = Text(host, "Mat File Format");
        var everyClass = ToolAssert.Ok<TextSearchResult>(
            new SearchTools(host.Libraries) { UsePrefilter = false }.SearchText("Mat File Format"));

        Assert.Equal("P.M", Assert.Single(everyClass.Items).Id);
        Assert.Equal(everyClass.Items, prefiltered.Items);
    }

    [Fact]
    public void APackage_IsNotParsedForWhatIsWrittenInItsInlineClasses()
    {
        using var host = Load(LibraryA);
        var models = host.Libraries.GetAllModels().ToList();
        var prefilter = new TextSearchPrefilter("Mat File Format", models);

        Assert.True(prefilter.MayMatch(models.Single(m => m.Id == "A.Reader")));
        Assert.False(prefilter.MayMatch(models.Single(m => m.Id == "A")));
    }

    [Fact]
    public void APackage_IsReadWithoutItsInlineClasses_AndGivesTheSameAnswer()
    {
        using var host = Load("""
            package P "a Mat File Format package"
              model M "inside"
                Real x;
              equation
                x = 1;
              end M;
              encapsulated operator record R
                Real re;
              end R;
              annotation (Documentation(info="<html><p>Holds M.</p></html>"));
            end P;
            """);
        var models = host.Libraries.GetAllModels().ToList();
        var package = models.Single(m => m.Id == "P");

        var reduced = new TextSearchPrefilter("x", models).ReducedSource(package);

        Assert.NotNull(reduced);
        Assert.DoesNotContain("Real x", reduced);
        Assert.Empty(ModelicaParser.Helpers.ModelicaParserHelper.ParseWithErrors(reduced).errors);
        Assert.Null(new TextSearchPrefilter("x", models).ReducedSource(models.Single(m => m.Id == "P.M")));

        var hit = Assert.Single(Text(host, "Mat File Format").Items);
        Assert.Equal(("P", "description"), (hit.Id, hit.MatchedIn));
        Assert.Equal("P", Assert.Single(Text(host, "Holds M").Items).Id);
    }

    [Fact]
    public void TheRunsAClassMustHold_AreTheQuerysLettersAndDigits()
    {
        Assert.Equal(new[] { "Mat", "File", "Format" }, new TextSearchPrefilter("Mat File-Format", []).RequiredRuns);
        Assert.Equal(new[] { "Modelica", "Blocks" }, new TextSearchPrefilter("Modelica.Blocks", []).RequiredRuns);
        // Nothing to look for in the source: every class is parsed.
        Assert.Empty(new TextSearchPrefilter("ä & <", []).RequiredRuns);
    }
}
