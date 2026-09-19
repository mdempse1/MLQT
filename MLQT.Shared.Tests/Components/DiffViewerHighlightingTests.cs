using MLQT.Shared.Components;
using ModelicaParser.Helpers;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B178: the diff coloured its own panes with a keyword-list regex and its own palette, while the
/// single-file viewer was coloured from the parse tree and followed the user's chosen scheme — so
/// the same code was coloured two ways in two panes of the same page.
///
/// <para>The two could not be merged while one of them rebuilt the text to colour it; a diff hunk is
/// not parseable. Once the classifier emitted the source in place, they could: the diff classifies
/// each <b>whole side</b> and then indexes the result, rather than trying to highlight a line at a
/// time.</para>
/// </summary>
public class DiffViewerHighlightingTests
{
    private static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    private static readonly string Source = Lf("""
        model M "m"
          parameter Real k = 1 "gain";
        end M;
        """);

    private static string[] PlainLines(string source) => Lf(source).Split('\n');

    [Fact]
    public void ModelicaIsColouredFromTheParseTree()
    {
        var display = DiffViewer.DisplayLines(Source, PlainLines(Source), isModelicaFile: true);

        Assert.Equal(PlainLines(Source).Length, display.Length);
        // From the tree, not from a keyword list: `Real` is a type here and `k` is not a keyword at
        // all, neither of which a regex over the line can tell.
        Assert.Contains("<KEYWORD>parameter</KEYWORD>", display[1]);
        Assert.Contains("<IDENT>k</IDENT>", display[1]);
        Assert.Contains("<STRING>\"gain\"</STRING>", display[1]);
    }

    [Fact]
    public void EveryLineKeepsItsPlaceSoTheDiffCanIndexIt()
    {
        // The diff computes its line pairing on the plain text and then reads the markup by the same
        // index. One extra or missing markup line would show one line's colouring on another's text.
        var display = DiffViewer.DisplayLines(Source, PlainLines(Source), isModelicaFile: true);

        Assert.StartsWith("<KEYWORD>model</KEYWORD>", display[0]);
        Assert.StartsWith("<KEYWORD>end</KEYWORD>", display[2]);
    }

    [Fact]
    public void AFileThatIsNotModelicaIsLeftAlone()
    {
        var plain = PlainLines("a\nb\nc");

        Assert.Same(plain, DiffViewer.DisplayLines("a\nb\nc", plain, isModelicaFile: false));
    }

    [Fact]
    public void LinesThatDoNotPairUpFallBackToPlainEncodedText()
    {
        // The guard, forced by handing it a line count the content cannot produce. It cannot happen
        // by construction — the classifier round-trips its input — but showing no colour is the one
        // safe answer if it ever does, and a stray '<' must not become markup on the way.
        var display = DiffViewer.DisplayLines(Source, ["a < b", "c & d"], isModelicaFile: true);

        Assert.Equal(["a &lt; b", "c &amp; d"], display);
    }

    [Fact]
    public void EmptyContentIsEncodedRatherThanClassified()
    {
        Assert.Equal(["&lt;p&gt;"], DiffViewer.DisplayLines(null, ["<p>"], isModelicaFile: true));
    }

    // ── turning the tags into what the stylesheet colours ─────────────────────────

    [Fact]
    public void TagsBecomeTheSameSpansTheCodeViewerUses()
    {
        var html = DiffViewer.ApplyModelicaSyntaxHighlighting("<KEYWORD>model</KEYWORD> <IDENT>M</IDENT>");

        Assert.Equal("<span class=\"code-keyword\">model</span> <span class=\"code-ident\">M</span>", html);
    }

    [Fact]
    public void WhatIsInsideATagIsEncodedOnTheWayOut()
    {
        // Tag contents are the source's own characters, raw, exactly as CodeViewer receives them.
        var html = DiffViewer.ApplyModelicaSyntaxHighlighting("<STRING>\"<html>&</html>\"</STRING>");

        Assert.Equal("<span class=\"code-string\">&quot;&lt;html&gt;&amp;&lt;/html&gt;&quot;</span>", html);
    }

    [Fact]
    public void ALineWithNoTagsIsPassedThroughUntouched()
    {
        // It has already been made safe - either by the classifier, which encodes everything outside
        // its tags, or by the fallback above. Encoding again would show the user `&amp;amp;`.
        Assert.Equal("  &amp;  ", DiffViewer.ApplyModelicaSyntaxHighlighting("  &amp;  "));
        Assert.Equal("...", DiffViewer.ApplyModelicaSyntaxHighlighting("..."));
    }
}
