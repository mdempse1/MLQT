using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// <see cref="SourceElision"/> is the one mechanism behind four things that hide part of a class,
/// and the property that makes it worth having is that hiding lines is <b>invertible</b>: every line
/// still on screen can say which line of the original it is. These tests are mostly that property.
/// </summary>
public class SourceElisionTests
{
    private static readonly string[] TenLines =
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];

    /// <summary>
    /// A source string as lines. Through <c>NormalizeLineEndings</c> because these files are CRLF in
    /// the working tree and a raw string literal picks that up — so splitting on '\n' alone leaves a
    /// '\r' on the end of every line and the test compares against something the elision never saw.
    /// The production path normalises for the same reason.
    /// </summary>
    private static string[] Lines(string source) =>
        ModelicaParserHelper.NormalizeLineEndings(source).Split('\n');

    // ── applying ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NoneShowsEverything()
    {
        Assert.Equal(TenLines, SourceElision.None.Apply(TenLines));
        Assert.True(SourceElision.None.IsEmpty);
    }

    [Fact]
    public void ARangeWithNoReplacementDisappears()
    {
        var elision = SourceElision.Of([new ElidedRange(3, 5, null)]);

        Assert.Equal(["1", "2", "6", "7", "8", "9", "10"], elision.Apply(TenLines));
    }

    [Fact]
    public void ARangeWithAReplacementBecomesOneLine()
    {
        var elision = SourceElision.Of([new ElidedRange(3, 5, "…")]);

        Assert.Equal(["1", "2", "…", "6", "7", "8", "9", "10"], elision.Apply(TenLines));
    }

    [Fact]
    public void SeveralRangesApplyInOrderHoweverTheyArrive()
    {
        var elision = SourceElision.Of([
            new ElidedRange(8, 9, null),
            new ElidedRange(2, 3, "A"),
            new ElidedRange(5, 5, null),
        ]);

        Assert.Equal(["1", "A", "4", "6", "7", "10"], elision.Apply(TenLines));
    }

    [Fact]
    public void ARangeRunningToTheLastLineIsFine()
    {
        Assert.Equal(["1", "2"], SourceElision.Of([new ElidedRange(3, 10, null)]).Apply(TenLines));
    }

    // ── the map ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ADisplayedLineKnowsWhichSourceLineItIs()
    {
        var elision = SourceElision.Of([new ElidedRange(3, 5, null)]);

        Assert.Equal(1, elision.ToSourceLine(1));
        Assert.Equal(2, elision.ToSourceLine(2));
        Assert.Equal(6, elision.ToSourceLine(3));
        Assert.Equal(10, elision.ToSourceLine(7));
    }

    [Fact]
    public void AReplacementLineAnswersWithWhereTheHiddenTextBegan()
    {
        // Not the line after it: clicking the marker should go to the annotation, not past it.
        var elision = SourceElision.Of([new ElidedRange(3, 5, "…")]);

        Assert.Equal(3, elision.ToSourceLine(3));
        Assert.Equal(6, elision.ToSourceLine(4));
    }

    [Fact]
    public void AHiddenLineHasNoDisplayLineUnlessSomethingStandsInForIt()
    {
        Assert.Null(SourceElision.Of([new ElidedRange(3, 5, null)]).ToDisplayLine(4));
        Assert.Equal(3, SourceElision.Of([new ElidedRange(3, 5, "…")]).ToDisplayLine(4));
    }

    [Fact]
    public void IsHiddenAnswersForTheEndsOfARangeAsWellAsTheMiddle()
    {
        var elision = SourceElision.Of([new ElidedRange(3, 5, null)]);

        Assert.False(elision.IsHidden(2));
        Assert.True(elision.IsHidden(3));
        Assert.True(elision.IsHidden(5));
        Assert.False(elision.IsHidden(6));
    }

    /// <summary>
    /// The property, over every shape that matters: drops and replacements, at the start, in the
    /// middle, adjacent to one another and running to the end.
    /// </summary>
    [Theory]
    [MemberData(nameof(RangeSets))]
    public void EveryDisplayedLineMapsBackToItselfAgain(string _, ElidedRange[] ranges)
    {
        var elision = SourceElision.Of(ranges);
        var display = elision.Apply(TenLines);

        for (var line = 1; line <= display.Count; line++)
            Assert.Equal(line, elision.ToDisplayLine(elision.ToSourceLine(line)));

        for (var source = 1; source <= TenLines.Length; source++)
        {
            if (elision.IsHidden(source))
                continue;
            var displayed = elision.ToDisplayLine(source);
            Assert.NotNull(displayed);
            Assert.Equal(source, elision.ToSourceLine(displayed.Value));
            Assert.Equal(TenLines[source - 1], display[displayed.Value - 1]);
        }
    }

    public static TheoryData<string, ElidedRange[]> RangeSets() => new()
    {
        { "nothing", [] },
        { "one drop", [new ElidedRange(4, 6, null)] },
        { "one marker", [new ElidedRange(4, 6, "…")] },
        { "from the first line", [new ElidedRange(1, 2, null)] },
        { "to the last line", [new ElidedRange(9, 10, "…")] },
        { "the whole thing", [new ElidedRange(1, 10, "…")] },
        { "adjacent ranges", [new ElidedRange(2, 3, null), new ElidedRange(4, 5, "…")] },
        { "single lines", [new ElidedRange(2, 2, null), new ElidedRange(5, 5, "…"), new ElidedRange(9, 9, null)] },
        { "mixed", [new ElidedRange(1, 1, "…"), new ElidedRange(3, 6, null), new ElidedRange(8, 10, "…")] },
    };

    // ── merging ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MergingKeepsTheWiderOfTwoNestedRanges()
    {
        // The viewer's ordinary case: hiding a package's nested classes already hides the
        // annotations inside them, so asking for both must not produce an overlap.
        var merged = SourceElision.Merge(
            SourceElision.Of([new ElidedRange(2, 8, "class")]),
            SourceElision.Of([new ElidedRange(4, 5, "annotation")]));

        Assert.Equal(new ElidedRange(2, 8, "class"), Assert.Single(merged.Ranges));
    }

    [Fact]
    public void MergingKeepsRangesThatDoNotContainEachOther()
    {
        var merged = SourceElision.Merge(
            SourceElision.Of([new ElidedRange(2, 3, null)]),
            SourceElision.Of([new ElidedRange(7, 9, null)]));

        Assert.Equal([new ElidedRange(2, 3, null), new ElidedRange(7, 9, null)], merged.Ranges);
    }

    [Fact]
    public void MergingIdenticalRangesLeavesOneBehind_NotNone()
    {
        var merged = SourceElision.Merge(
            SourceElision.Of([new ElidedRange(2, 3, "…")]),
            SourceElision.Of([new ElidedRange(2, 3, "…")]));

        Assert.Single(merged.Ranges);
    }

    [Fact]
    public void MergingRangesThatOnlyHalfOverlapIsStillRefused()
    {
        // Neither covers the other, so there is no obvious answer and the caller hears about it.
        Assert.Throws<ArgumentException>(() => SourceElision.Merge(
            SourceElision.Of([new ElidedRange(2, 6, null)]),
            SourceElision.Of([new ElidedRange(4, 9, null)])));
    }

    [Fact]
    public void MergingNothingHidesNothing()
    {
        Assert.True(SourceElision.Merge().IsEmpty);
        Assert.True(SourceElision.Merge(null, SourceElision.None).IsEmpty);
    }

    // ── what it refuses ───────────────────────────────────────────────────────────

    [Fact]
    public void OverlappingRangesAreRefusedRatherThanMerged()
    {
        // There is no single answer for what the display shows, so a caller that produced them has a
        // bug; merging them quietly would hide it.
        var ex = Assert.Throws<ArgumentException>(() =>
            SourceElision.Of([new ElidedRange(3, 6, null), new ElidedRange(5, 8, null)]));

        Assert.Contains("overlap", ex.Message);
    }

    [Fact]
    public void RangesSharingASingleLineOverlap()
    {
        // The boundary the check turns on: 3-5 and 5-7 have line 5 in common, and one line is as
        // much of a conflict as five.
        Assert.Throws<ArgumentException>(() =>
            SourceElision.Of([new ElidedRange(3, 5, null), new ElidedRange(5, 7, null)]));

        // ...and the range starting the line after is fine, which is what makes the above a boundary
        // rather than a blanket refusal of anything adjacent.
        Assert.Equal(2, SourceElision.Of([new ElidedRange(3, 5, null), new ElidedRange(6, 7, null)]).Ranges.Count);
    }

    [Fact]
    public void ARangeThatEndsBeforeItStartsIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => SourceElision.Of([new ElidedRange(6, 3, null)]));

        // The message has to name the range, or a caller with a hundred of them learns nothing.
        Assert.Contains("6-3", ex.Message);
    }

    [Fact]
    public void ARangeBeforeLineOneIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => SourceElision.Of([new ElidedRange(0, 3, null)]));

        Assert.Contains("0", ex.Message);
        Assert.Contains("1-based", ex.Message);
    }

    [Fact]
    public void NullsAreRefusedRatherThanTreatedAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => SourceElision.Of(null!));
        Assert.Throws<ArgumentNullException>(() => SourceElision.None.Apply(null!));
    }

    [Fact]
    public void TheMapHoldsAtTheExactEdgesOfARange()
    {
        // Both loop boundaries in one test: the line just before a range, its first and last lines,
        // and the line just after. Off by one at either end and a finding lands on the wrong line.
        var elision = SourceElision.Of([new ElidedRange(4, 6, null)]);

        Assert.Equal(3, elision.ToDisplayLine(3));
        Assert.Null(elision.ToDisplayLine(4));
        Assert.Null(elision.ToDisplayLine(6));
        Assert.Equal(4, elision.ToDisplayLine(7));

        var marked = SourceElision.Of([new ElidedRange(4, 6, "…")]);

        Assert.Equal(3, marked.ToDisplayLine(3));
        Assert.Equal(4, marked.ToDisplayLine(4));
        Assert.Equal(4, marked.ToDisplayLine(6));
        Assert.Equal(5, marked.ToDisplayLine(7));
    }

    [Fact]
    public void ALineNumberBelowOneIsHandedBackUnchanged()
    {
        // Nothing should ask, but a caller that does gets its own nonsense back rather than an
        // answer computed from it.
        var elision = SourceElision.Of([new ElidedRange(4, 6, null)]);

        Assert.Equal(0, elision.ToDisplayLine(0));
        Assert.Equal(0, elision.ToSourceLine(0));
    }

    // ── finding the ranges in a class ─────────────────────────────────────────────

    private const string PackageWithNestedClasses = """
        package P "p"
          model A "first"
            Real x;
          end A;

          model B "second"
            Real y;
          end B;
        end P;
        """;

    [Fact]
    public void NestedClassesAreFoundAndCollapsed()
    {
        var tree = ModelicaParserHelper.Parse(PackageWithNestedClasses);
        var elision = ElisionFinder.NestedClasses(tree, PackageWithNestedClasses, name => $"  // {name} …");

        var display = elision.Apply(Lines(PackageWithNestedClasses));

        Assert.Equal(
            ["package P \"p\"", "  // A …", "", "  // B …", "end P;"],
            display);
    }

    /// <summary>
    /// The gate this item was given: a package shows with its nested classes collapsed, and the map
    /// round-trips every line still on screen.
    /// </summary>
    [Fact]
    public void ACollapsedPackageStillKnowsWhereEveryDisplayedLineCameFrom()
    {
        var lines = Lines(PackageWithNestedClasses);
        var tree = ModelicaParserHelper.Parse(PackageWithNestedClasses);
        var elision = ElisionFinder.NestedClasses(tree, PackageWithNestedClasses, name => $"  // {name} …");
        var display = elision.Apply(lines);

        for (var line = 1; line <= display.Count; line++)
        {
            var source = elision.ToSourceLine(line);
            Assert.Equal(line, elision.ToDisplayLine(source));
            if (!elision.IsHidden(source))
                Assert.Equal(lines[source - 1], display[line - 1]);
        }

        // "end P;" is the last line of the file and still says so, which is the point of the map.
        Assert.Equal(lines.Length, elision.ToSourceLine(display.Count));
    }

    [Fact]
    public void AMarkerIsToldWhatItIsStandingInFor()
    {
        // The name reaches the marker, so a collapsed class can say which class it was. For an
        // annotation there is only one thing it can be, and it says so.
        const string source = """
            package P "p"
              model A "a"
              end A;
              type Len = Real;
              annotation (Documentation(info="<html>x</html>"));
            end P;
            """;
        var tree = ModelicaParserHelper.Parse(source);

        var classNames = new List<string>();
        ElisionFinder.NestedClasses(tree, source, name => { classNames.Add(name); return null; });

        var annotationNames = new List<string>();
        ElisionFinder.Annotations(tree, source, name => { annotationNames.Add(name); return null; });

        // A long class specifier and a short one: the name comes off a different rule for each.
        Assert.Equal(["A", "Len"], classNames);
        Assert.Equal(["annotation"], annotationNames);
    }

    [Fact]
    public void ClassesNestedDeeperAreCoveredByTheOneAboveThem()
    {
        const string source = """
            package P "p"
              package Inner "inner"
                model Deep "deep"
                end Deep;
              end Inner;
            end P;
            """;

        var elision = ElisionFinder.NestedClasses(ModelicaParserHelper.Parse(source), source);

        Assert.Equal(new ElidedRange(2, 5, null), Assert.Single(elision.Ranges));
    }

    [Fact]
    public void AnAnnotationOnItsOwnLinesIsElided()
    {
        const string source = """
            model M "m"
              Real x;
              annotation (Icon(graphics={
                Ellipse(extent={{-1,1},{1,-1}})}));
            end M;
            """;

        var elision = ElisionFinder.Annotations(
            ModelicaParserHelper.Parse(source), source, _ => "  annotation (…);");

        Assert.Equal(
            ["model M \"m\"", "  Real x;", "  annotation (…);", "end M;"],
            elision.Apply(Lines(source)));
    }

    [Fact]
    public void AnAnnotationSharingALineWithCodeIsLeftAlone()
    {
        // Removing part of a line would leave `Real x "d" annotation (Placement(` on screen, which
        // is worse than showing the annotation. The construct goes as a unit or it stays.
        const string source = """
            model M "m"
              Real x "d" annotation (Placement(
                    transformation(extent={{-1,1},{1,-1}})));
            end M;
            """;

        var elision = ElisionFinder.Annotations(ModelicaParserHelper.Parse(source), source, _ => "…");

        Assert.True(elision.IsEmpty);
        Assert.Equal(Lines(source), elision.Apply(Lines(source)));
    }

    [Fact]
    public void AnAnnotationIsElidedWithItsStatementSemicolon()
    {
        const string source = """
            model M "m"
              annotation (Documentation(info="<html>x</html>"));
            end M;
            """;

        var elision = ElisionFinder.Annotations(ModelicaParserHelper.Parse(source), source);

        Assert.Equal(new ElidedRange(2, 2, null), Assert.Single(elision.Ranges));
        Assert.Equal(["model M \"m\"", "end M;"], elision.Apply(Lines(source)));
    }

    /// <summary>
    /// The finder reads positions from the tree and text from the source, so the two have to be the
    /// same text. When a caller passes a source that is shorter than what was parsed, the ranges it
    /// computed would index past the end — it elides nothing instead of throwing, because a viewer
    /// showing the class unelided is a far better outcome than one showing an exception.
    /// </summary>
    [Fact]
    public void ASourceShorterThanTheParsedTextElidesNothing()
    {
        const string parsed = """
            model M "m"
              annotation (Documentation(info="<html>x</html>"));
            end M;
            """;

        // Fewer lines than the tree knows about: the annotation's last line is off the end.
        Assert.True(ElisionFinder.Annotations(ModelicaParserHelper.Parse(parsed), "model M \"m\"").IsEmpty);
    }

    [Fact]
    public void ALineShorterThanTheParsedOneElidesNothing()
    {
        const string parsed = """
            model M "m"
              annotation (Documentation(info="<html>x</html>"));
            end M;
            """;

        // Right number of lines, but the annotation's line is truncated, so where the annotation
        // ends is past the end of it.
        var truncated = string.Join("\n", ["model M \"m\"", "  annotation (", "end M;"]);

        Assert.True(ElisionFinder.Annotations(ModelicaParserHelper.Parse(parsed), truncated).IsEmpty);
    }

    [Fact]
    public void AConstructEndingOnTheVeryLastLineIsStillElided()
    {
        // The bounds check has to admit lastLine == lines.Length. Tightened by one it would refuse
        // anything that runs to the end of the text, which is the ordinary case for a class whose
        // file does not end with a newline.
        const string parsed = """
            model M "m"
              annotation (Documentation(info="<html>x</html>"));
            end M;
            """;
        var truncated = string.Join("\n", Lines(parsed).Take(2));

        var elision = ElisionFinder.Annotations(ModelicaParserHelper.Parse(parsed), truncated);

        Assert.Equal(new ElidedRange(2, 2, null), Assert.Single(elision.Ranges));
    }

    [Fact]
    public void NoTreeMeansNothingIsHidden()
    {
        Assert.True(ElisionFinder.Annotations(null, "model M \"m\" end M;").IsEmpty);
        Assert.True(ElisionFinder.NestedClasses(null, "model M \"m\" end M;").IsEmpty);
    }
}
