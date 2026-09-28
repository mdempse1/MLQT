using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// The shape of the <b>file</b> the renderer writes, and the order of the sections inside a class —
/// the two things about its output that are written down somewhere other than in the renderer.
/// </summary>
/// <remarks>
/// <para><b>Why these and not the other survivors (B227).</b> Mutation testing found 272 surviving
/// mutants in <c>ModelicaRenderer</c>, and the instruction with the item was to read them in groups
/// and decide per group whether the behaviour is specified anywhere — because 272 read end to end
/// is how a pass like this produces tests that assert the renderer's current output instead of its
/// contract. <c>build/survivor-map.py</c> is the sampling tool that made that possible; it groups a
/// Stryker report's survivors by the method they sit in. The groups below had a specification to
/// test against. The largest single group — the six-condition heuristic in
/// <c>VisitClass_or_inheritence_modification</c> that decides when a modification goes multi-line —
/// did not, and is deliberately left alone: its thresholds (<c>numArguments &gt; 5</c>,
/// <c>maxNestingDepth &gt;= 2</c>) are accumulated rather than chosen, and a test for them would
/// freeze today's bytes under the name of a contract.</para>
///
/// <para><b>Why 25 mutants in <c>VisitStored_definition</c> survived is the harness, not an
/// oversight.</b> <see cref="TestHelpers.AssertClass"/> ends with <c>actualOutput.RemoveAt(0)</c>
/// and strips trailing blank lines — so every renderer test written through it discards the within
/// clause and the blank lines between classes before it asserts anything. The file's shape was not
/// under-tested; it was structurally invisible. These tests render directly for that reason.</para>
///
/// <para>The same applies to <see cref="FormattingOptions.InitialSectionsLast"/>, which
/// <c>AssertClass</c> has no parameter for, so no test could set it.</para>
/// </remarks>
public class FileAndSectionShapeTests
{
    /// <summary>Renders a whole file, as it would be written to disk.</summary>
    private static List<string> RenderFile(string source, FormattingOptions? formatting = null)
    {
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(source);
        var renderer = new ModelicaRenderer(tokenStream: tokenStream, formatting: formatting);
        renderer.Visit(parseTree);
        return renderer.Code;
    }

    private static string RenderFileText(string source, FormattingOptions? formatting = null) =>
        string.Join("\n", RenderFile(source, formatting));

    // ── the file's shape ─────────────────────────────────────────────────────────

    /// <summary>
    /// A within clause belongs to the file rather than to the class, and CLAUDE.md is explicit about
    /// what losing it costs: the file re-parses with no package context and its classes come back
    /// with detached ids. The only existing assertion about it is <c>Contains("within")</c>, which
    /// the name and the semicolon can both go missing under.
    /// </summary>
    [Fact]
    public void AWithinClauseKeepsItsNameAndItsSemicolon()
    {
        var rendered = RenderFile("""
            within Claytex.Blocks.Discrete;
            model M
              Real x;
            end M;
            """);

        Assert.Equal("within Claytex.Blocks.Discrete;", rendered[0]);
    }

    /// <summary>A top-level file says `within;` — the clause is there, the package name is not.</summary>
    [Fact]
    public void ATopLevelFileGetsAWithinClauseWithNoName()
    {
        var rendered = RenderFile("""
            within;
            package Claytex
            end Claytex;
            """);

        Assert.Equal("within;", rendered[0]);
    }

    /// <summary>
    /// Two classes in one file are separated by a blank line, and the last is not followed by one —
    /// which is what the renderer's own comment says, and what a `package.mo` holding several
    /// nested classes depends on for readability.
    /// </summary>
    [Fact]
    public void ClassesInAFileAreSeparatedByABlankLineButNotFollowedByOne()
    {
        var rendered = RenderFile("""
            within P;
            model A
            end A;
            model B
            end B;
            model C
            end C;
            """);

        var text = string.Join("\n", rendered).TrimEnd('\n');

        Assert.Equal("""
            within P;
            model A
            end A;

            model B
            end B;

            model C
            end C;
            """.Replace("\r\n", "\n"), text);
    }

    [Fact]
    public void AFileWithOneClassHasNoBlankLineInIt()
    {
        var rendered = RenderFile("""
            within P;
            model Only
            end Only;
            """);

        Assert.DoesNotContain("", rendered.Select(l => l.TrimEnd()).SkipLast(1).Skip(1));
    }

    // ── merging sections: the OneOfEachSection contract ──────────────────────────

    private const string TwoOfEachSection = """
        within P;
        model M
          Real a;
        protected
          Real p1;
        public
          Real b;
        protected
          Real p2;
        end M;
        """;

    /// <summary>
    /// "Merges multiple sections of the same kind into one" — <c>Documentation/code-formatting.md</c>
    /// on <c>MLQT.Style.OneOfEachSection</c>. A second `protected` keyword is the visible form of
    /// that rule not being kept, and it is written by a different call than the first.
    /// </summary>
    [Fact]
    public void MergingSectionsWritesProtectedExactlyOnce()
    {
        var rendered = RenderFile(TwoOfEachSection, new FormattingOptions(OneOfEachSection: true));

        Assert.Equal(1, rendered.Count(line => line.Trim() == "protected"));
    }

    /// <summary>
    /// The same promise on the other path. With imports first, the renderer writes the protected
    /// section through a different sequence of calls - extends, then components and classes, each
    /// passing on whether the marker has been written yet - so "exactly once" is a separate claim
    /// about a separate piece of bookkeeping, not a second spelling of the test above.
    /// </summary>
    [Fact]
    public void MergingSectionsWritesProtectedExactlyOnceWithImportsFirstToo()
    {
        var rendered = RenderFile(MixedComposition, new FormattingOptions(
            OneOfEachSection: true, ImportsFirst: true, ComponentsBeforeClasses: true));

        Assert.Equal(1, rendered.Count(line => line.Trim() == "protected"));
        Assert.DoesNotContain(rendered, line => line.Trim() == "public");
    }

    /// <summary>
    /// ...and every protected element is still under it. With imports first the protected import
    /// is lifted to the top of the file, above the marker, and the rest stay below it - so this is
    /// the one case where "under the protected keyword" is not true of everything in the section.
    /// </summary>
    [Fact]
    public void WithImportsFirstOnlyTheProtectedImportIsLiftedAboveTheMarker()
    {
        var rendered = RenderFile(MixedComposition, new FormattingOptions(
            OneOfEachSection: true, ImportsFirst: true, ComponentsBeforeClasses: true));

        var marker = rendered.FindIndex(line => line.Trim() == "protected");
        Assert.InRange(marker, 0, rendered.Count - 1);

        Assert.True(rendered.FindIndex(line => line.Trim() == "import Other.Thing;") < marker,
            $"the protected import was not lifted:\n{string.Join("\n", rendered)}");
        Assert.True(rendered.FindIndex(line => line.Trim() == "Real p;") > marker,
            $"a protected component was written above the marker:\n{string.Join("\n", rendered)}");
    }

    /// <summary>
    /// `public` is the default section in Modelica, so a merged class writes its public elements
    /// without announcing them. Writing the keyword would be valid and is not what MLQT does.
    /// </summary>
    [Fact]
    public void MergingSectionsDoesNotWriteAPublicKeyword()
    {
        var rendered = RenderFile(TwoOfEachSection, new FormattingOptions(OneOfEachSection: true));

        Assert.DoesNotContain(rendered, line => line.Trim() == "public");
    }

    [Fact]
    public void MergingSectionsKeepsEveryDeclaration()
    {
        var text = RenderFileText(TwoOfEachSection, new FormattingOptions(OneOfEachSection: true));

        foreach (var declaration in new[] { "Real a;", "Real b;", "Real p1;", "Real p2;" })
            Assert.Contains(declaration, text);
    }

    /// <summary>
    /// Both protected declarations end up under the one `protected` keyword — the point of merging,
    /// and not implied by the keyword appearing once.
    /// </summary>
    [Fact]
    public void EveryProtectedDeclarationEndsUpUnderTheOneKeyword()
    {
        var rendered = RenderFile(TwoOfEachSection, new FormattingOptions(OneOfEachSection: true));

        var marker = rendered.FindIndex(line => line.Trim() == "protected");
        Assert.InRange(marker, 0, rendered.Count - 1);

        Assert.All(new[] { "Real p1;", "Real p2;" }, declaration =>
            Assert.True(rendered.FindIndex(line => line.Trim() == declaration) > marker,
                $"{declaration} was written above the protected keyword"));
    }

    /// <summary>
    /// "With <see cref="FormattingOptions.OneOfEachSection"/> off it does not reorder anything at
    /// all" — <c>FormattingOptions</c>'s own summary, and the reason
    /// <see cref="FormattingOptions.None"/> is what a caller passes when it only wants the text
    /// re-rendered.
    /// </summary>
    [Fact]
    public void WithoutTheMasterSwitchNothingIsMoved()
    {
        var rendered = RenderFile(TwoOfEachSection, FormattingOptions.None);

        Assert.Equal(2, rendered.Count(line => line.Trim() == "protected"));
        Assert.Equal(1, rendered.Count(line => line.Trim() == "public"));
    }

    // ── imports first, and components before classes ─────────────────────────────

    private const string MixedComposition = """
        within P;
        model M
          Real a;
          extends Base;
          import SI = Modelica.SIunits;
          model Nested
          end Nested;
        protected
          import Other.Thing;
          Real p;
        end M;
        """;

    /// <summary>
    /// "Moves `import` statements to the top of each section, then `extends` clauses" —
    /// <c>code-formatting.md</c>. Both sections' imports go above, which is why the renderer calls
    /// for the protected imports before writing anything public.
    /// </summary>
    [Fact]
    public void ImportsFirstPutsEveryImportAboveEveryExtends()
    {
        var rendered = RenderFile(MixedComposition,
            new FormattingOptions(OneOfEachSection: true, ImportsFirst: true));

        var lastImport = rendered.FindLastIndex(line => line.TrimStart().StartsWith("import "));
        var firstExtends = rendered.FindIndex(line => line.TrimStart().StartsWith("extends "));

        Assert.InRange(lastImport, 0, rendered.Count - 1);
        Assert.InRange(firstExtends, 0, rendered.Count - 1);
        Assert.True(lastImport < firstExtends,
            $"an import was written below the extends clause:\n{string.Join("\n", rendered)}");
    }

    /// <summary>
    /// "Sorts component declarations before nested class definitions. Only does anything when
    /// *imports first* is also on" — <c>code-formatting.md</c>, which is why this passes both.
    /// </summary>
    [Fact]
    public void ComponentsBeforeClassesPutsDeclarationsAboveNestedClasses()
    {
        var rendered = RenderFile(MixedComposition, new FormattingOptions(
            OneOfEachSection: true, ImportsFirst: true, ComponentsBeforeClasses: true));

        var declaration = rendered.FindIndex(line => line.Trim() == "Real a;");
        var nested = rendered.FindIndex(line => line.TrimStart().StartsWith("model Nested"));

        Assert.InRange(declaration, 0, rendered.Count - 1);
        Assert.InRange(nested, 0, rendered.Count - 1);
        Assert.True(declaration < nested,
            $"a nested class was written above a component:\n{string.Join("\n", rendered)}");
    }

    // ── initial equation and algorithm placement ─────────────────────────────────

    private const string BothKindsOfSection = """
        within P;
        model M
          Real x;
        initial equation
          x = 0;
        equation
          der(x) = 1;
        initial algorithm
          x := 0;
        algorithm
          x := x;
        end M;
        """;

    /// <summary>
    /// Off means before, which is the convention <c>MLQT.Style.InitialEqAlgoFirst</c> checks.
    /// </summary>
    [Fact]
    public void InitialSectionsComeBeforeTheOrdinaryOnesByDefault()
    {
        var rendered = RenderFile(BothKindsOfSection, new FormattingOptions(OneOfEachSection: true));

        Assert.True(IndexOfSection(rendered, "initial equation") < IndexOfSection(rendered, "equation"),
            $"initial equation was not written first:\n{string.Join("\n", rendered)}");
        Assert.True(IndexOfSection(rendered, "initial algorithm") < IndexOfSection(rendered, "algorithm"),
            $"initial algorithm was not written first:\n{string.Join("\n", rendered)}");
    }

    /// <summary>
    /// On matches <c>MLQT.Style.InitialEqAlgoLast</c>. <c>code-formatting.md</c> records this
    /// failing before: "the formatter used to write initial blocks first whatever the setting said,
    /// so a repository that chose 'last' had the finding reintroduced on every save and the rule
    /// could never be satisfied." Nothing has held it since, because
    /// <see cref="TestHelpers.AssertClass"/> has no parameter for this option.
    /// </summary>
    [Fact]
    public void InitialSectionsLastPutsThemAfterTheOrdinaryOnes()
    {
        var rendered = RenderFile(BothKindsOfSection,
            new FormattingOptions(OneOfEachSection: true, InitialSectionsLast: true));

        Assert.True(IndexOfSection(rendered, "initial equation") > IndexOfSection(rendered, "equation"),
            $"initial equation was not written last:\n{string.Join("\n", rendered)}");
        Assert.True(IndexOfSection(rendered, "initial algorithm") > IndexOfSection(rendered, "algorithm"),
            $"initial algorithm was not written last:\n{string.Join("\n", rendered)}");
    }

    /// <summary>
    /// Whichever order they are written in, an initial section stays initial and an ordinary one
    /// stays ordinary. Losing the keyword turns initialisation into a permanent equation, which is
    /// a change to what the model does rather than to how it looks.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASectionKeepsItsKindWhicheverOrderItIsWrittenIn(bool initialLast)
    {
        var rendered = RenderFile(BothKindsOfSection,
            new FormattingOptions(OneOfEachSection: true, InitialSectionsLast: initialLast));

        Assert.Equal(1, rendered.Count(line => line.Trim() == "initial equation"));
        Assert.Equal(1, rendered.Count(line => line.Trim() == "equation"));
        Assert.Equal(1, rendered.Count(line => line.Trim() == "initial algorithm"));
        Assert.Equal(1, rendered.Count(line => line.Trim() == "algorithm"));
    }

    private static int IndexOfSection(List<string> rendered, string header)
    {
        var index = rendered.FindIndex(line => line.Trim() == header);
        Assert.True(index >= 0, $"no `{header}` section was written:\n{string.Join("\n", rendered)}");
        return index;
    }

    // ── the one line-length decision that is a rule rather than a threshold ──────

    /// <summary>
    /// A modification long enough to push its line past the maximum is broken across lines. This is
    /// the one condition in the renderer's six-part multi-line heuristic that is a stated rule
    /// rather than a tuned threshold - the rest count arguments and nesting depth against numbers
    /// nobody chose, and are left to survive deliberately (B227).
    /// </summary>
    /// <remarks>
    /// <b>Two arguments, not three.</b> A modification with three goes multi-line on the argument
    /// count alone, whatever the line length, so a test written with three passes at every width
    /// and says nothing about the length rule. Two is the largest number that leaves the length
    /// the only thing deciding.
    /// </remarks>
    [Fact]
    public void AModificationTooLongForTheLineIsBrokenAcrossLines()
    {
        const string source = """
            within P;
            model M
              extends SomeRatherLongBaseClassName(aVeryLongParameterNameIndeed = 1, anotherEquallyLongOne = 2);
            end M;
            """;

        var narrow = RenderFileWithWidth(source, 40);
        var wide = RenderFileWithWidth(source, 200);

        // The wide render keeps the whole clause on its one line; the narrow one spreads the
        // arguments over several. The opening paren stays at the end of the extends line in
        // both, which is why this counts lines rather than looking for one.
        Assert.Equal(4, wide.Count);
        Assert.True(narrow.Count > wide.Count,
            Report("narrow", narrow) + Report("wide", wide));
    }

    private static string Report(string label, List<string> rendered) =>
        label + " (" + rendered.Count + " lines):" + Environment.NewLine
        + string.Join(Environment.NewLine, rendered) + Environment.NewLine;

    /// <summary>
    /// The control for the pair above: at the wide setting the whole clause is one line, so the
    /// narrow case is the length rule firing rather than the renderer always breaking this shape.
    /// </summary>
    [Fact]
    public void AModificationThatFitsStaysOnOneLine()
    {
        const string source = """
            within P;
            model M
              extends Base(a = 1, b = 2);
            end M;
            """;

        var rendered = RenderFileWithWidth(source, 200);

        Assert.Contains(rendered, line => line.Trim() == "extends Base(a=1, b=2);");
    }

    private static List<string> RenderFileWithWidth(string source, int maxLineLength)
    {
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(source);
        var renderer = new ModelicaRenderer(tokenStream: tokenStream, maxLineLength: maxLineLength);
        renderer.Visit(parseTree);
        return renderer.Code;
    }
}
