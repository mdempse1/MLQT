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
    public void Name_WithAQueryAndLibrary_SearchesOnlyThatLibrary()
    {
        using var host = Load(LibraryA, LibraryB);
        var find = new FindTools(host.Libraries);

        Assert.Equal(2, ToolAssert.Ok<ClassSearchResult>(find.Find("er", match: "name")).Total);
        var res = ToolAssert.Ok<ClassSearchResult>(find.Find("er", match: "name", library: "A"));
        Assert.Equal("A.Reader", Assert.Single(res.Items).Id);
        ToolAssert.Error(find.Find("er", match: "name", library: "NoSuchLibrary"));
        Assert.All(host.Libraries.GetAllModels(), m => Assert.Null(m.Definition.ParsedCode));
    }

    [Fact]
    public void Text_WithAnUnknownLibrary_IsAnError()
    {
        using var host = Load(LibraryA);
        ToolAssert.Error(new FindTools(host.Libraries).Find("table", match: "text", library: "NoSuchLibrary"));
    }

    [Theory]
    [InlineData("text", "Mat File Format")]
    [InlineData("code", "Mat File Format")]
    [InlineData("interface", "Reader")]
    public void ACancelledSearch_Stops(string match, string query)
    {
        using var host = Load(LibraryA, LibraryB);
        var find = new FindTools(host.Libraries);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // The search has classes to read, so a cancelled one has work to stop. The token is checked
        // before each class, so a search cancelled part way stops once the classes it is reading are
        // done; cancelled before it starts, it reads none.
        var found = find.Find(query, match: match) switch
        {
            TextSearchResult t => t.Total,
            CodeSearchResult c => c.Total,
            InterfaceSearchResult i => i.Total,
            var other => throw new InvalidOperationException($"Unexpected {other}"),
        };
        Assert.True(found > 0, $"{match} '{query}' found nothing, so cancelling it proves nothing");
        Assert.Throws<OperationCanceledException>(() => find.Find(query, match: match, cancellationToken: cancelled.Token));
    }

    [Fact]
    public void AFailureReadingOneClass_IsThrownAsItself_NotWrapped()
    {
        var e = Assert.Throws<InvalidOperationException>(() => SearchTools.EachInParallel(50, CancellationToken.None, i =>
        {
            if (i == 7)
                throw new InvalidOperationException("class 7");
        }));
        Assert.Equal("class 7", e.Message);
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

    /// <summary>
    /// An inline class whose text also appears in the package's own description: which copy is the class
    /// cannot be told by looking, so neither is cut. Taking the first, the reduced package read
    /// "Write class _ end _;" as its description and the hit was lost.
    /// </summary>
    [Fact]
    public void AnInlineClassWrittenOutInItsPackagesDescription_IsNotCut()
    {
        using var host = Load("""
            package P "Write model A end A; to declare one"
              model A end A;
            end P;
            """);

        var everyClass = ToolAssert.Ok<TextSearchResult>(
            new SearchTools(host.Libraries) { UsePrefilter = false }.SearchText("Write model A end A"));
        var prefiltered = Text(host, "Write model A end A");

        Assert.Equal("P", Assert.Single(everyClass.Items).Id);
        Assert.Equal(everyClass.Items, prefiltered.Items);
    }

    // ---- Every word, in any order -----------------------------------------------------------------
    //
    // The whole query was one substring, so a search for a class by what it does found only the
    // documentation that used those words in that order.

    private const string Exchangers = """
        package H "heat"
          model Exchanger "Heat exchanger with constant effectiveness"
          end Exchanger;
          model Split "Exchanger described here"
            annotation (Documentation(info="<html><p>The effectiveness is fixed by a parameter.</p></html>"));
          end Split;
          model Other "Effectiveness of a pump"
          end Other;
        end H;
        """;

    [Fact]
    public void EveryWord_IsLookedFor_InAnyOrder()
    {
        using var host = Load(Exchangers);

        var hit = Assert.Single(Text(host, "effectiveness heat").Items);

        Assert.Equal(("H.Exchanger", "description"), (hit.Id, hit.MatchedIn));
    }

    [Fact]
    public void TheWords_MayBeSplitBetweenDescriptionAndDocumentation()
    {
        using var host = Load(Exchangers);

        var hit = Assert.Single(Text(host, "described parameter").Items);

        Assert.Equal(("H.Split", "description and documentation"), (hit.Id, hit.MatchedIn));
    }

    [Fact]
    public void AQuotedPhrase_IsOneTerm_MatchedAsWritten()
    {
        using var host = Load(Exchangers);

        Assert.Equal(["H.Exchanger"], Text(host, "\"constant effectiveness\"").Items.Select(i => i.Id));
        Assert.Empty(Text(host, "\"effectiveness constant\"").Items);
        Assert.Equal(["H.Exchanger"], Text(host, "heat \"constant effectiveness\"").Items.Select(i => i.Id));
        Assert.Equal(["H.Exchanger", "H.Other", "H.Split"], Text(host, "effectiveness").Items.Select(i => i.Id));
    }

    // "integrator limit" with match left at its default 'name' found nothing, and said nothing of why.
    [Theory]
    [InlineData("name")]
    [InlineData(null)]
    [InlineData("interface")]
    public void Words_WhereAnIdIsWanted_AreRefused_PointingAtText(string? match)
    {
        using var host = Load(Exchangers);
        var find = new FindTools(host.Libraries);

        var err = ToolAssert.Error(match is null ? find.Find("heat exchanger") : find.Find("heat exchanger", match: match));

        Assert.Contains("an id has no spaces", err.Error);
        Assert.Contains("use match 'text'", err.Error);
        // A word, or a quoted identifier with a space in it, is part of an id and is looked for as before.
        // Space around it is trimmed, as it was not before.
        Assert.Equal(["H.Exchanger"], ToolAssert.Ok<ClassSearchResult>(find.Find(" Exchanger ")).Items.Select(i => i.Id));
        Assert.Equal(["H.Exchanger"],
            ToolAssert.Ok<InterfaceSearchResult>(find.Find(" Exchanger ", match: "interface")).Items.Select(i => i.Id));
        ToolAssert.Ok<ClassSearchResult>(find.Find("H.'heat exchanger'"));
    }

    [Fact]
    public void AQueryOfEmptyQuotes_IsAnError()
    {
        using var host = Load(Exchangers);
        ToolAssert.Error(new FindTools(host.Libraries).Find("\"  \"", match: "text"));
    }

    [Fact]
    public void AnAndQuery_SkipsOnlyClassesThatCouldNotHaveMatched()
    {
        using var host = Load(Exchangers, LibraryA, LibraryB);
        foreach (var query in new[] { "effectiveness heat", "described parameter", "table Format \"Mat File\"" })
        {
            var everyClass = ToolAssert.Ok<TextSearchResult>(
                new SearchTools(host.Libraries) { UsePrefilter = false }.SearchText(query));
            Assert.Equal(everyClass.Items, Text(host, query).Items);
        }
    }

    [Theory]
    [InlineData("heat exchanger", "heat|exchanger")]
    [InlineData("  \"Mat File\"  table ", "Mat File|table")]
    [InlineData("a \"open quote runs on", "a|open quote runs on")]
    [InlineData("Heat heat", "Heat")]
    [InlineData("x\"y z\"w", "x|y z|w")]
    public void TheQuery_IsCutIntoWordsAndQuotedPhrases(string query, string terms)
    {
        Assert.Equal(terms, string.Join("|", TextQuery.Parse(query).Terms));
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
