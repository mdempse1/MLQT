using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// The one implementation of "find the <c>modelica://</c> URIs in this text" (B209).
///
/// <para>There were two, byte for byte: one in <c>ExternalResourceExtractor</c> and one in
/// <c>ModelAnalyzer</c>. The graph build uses the second, so fixing the first changed nothing a user
/// could see — the entity bug below was fixed and the Modelica Standard Library went on reporting the
/// same two phantom files. Both call this now.</para>
/// </summary>
public class ModelicaUriScannerTests
{
    [Theory]
    [InlineData("&lt;img src=&quot;modelica://Modelica/Resources/Images/foo.png&quot;&gt;")]
    [InlineData("&lt;img src=&#34;modelica://Modelica/Resources/Images/foo.png&#34;&gt;")]
    [InlineData("&lt;img src=&QUOT;modelica://Modelica/Resources/Images/foo.png&QUOT;&gt;")]
    public void AnEntityQuotedUri_IsHtmlShownAsText_NotAReference(string text)
    {
        // B209 took this for the ordinary way documentation writes an image and kept the URI, cut at
        // the entity. It is the opposite: in a Modelica string the HTML's quotes are \", and this form
        // renders as the characters <img src="..."> - an example. Over MSL and Buildings, 1,792 images
        // are written with \" and five like this, all in UsersGuide.Conventions' examples, so the
        // External Resources tab listed the guide's example picture as a missing file.
        Assert.Empty(ModelicaUriScanner.FindFileUris(text));
    }

    [Fact]
    public void AnEntityBeforeTheUri_IsWhatSaysSo_NotOneSomewhereElse()
    {
        const string text = "&quot;example&quot; then <img src=\"modelica://Lib/Resources/a.png\">";

        Assert.Equal(["modelica://Lib/Resources/a.png"], ModelicaUriScanner.FindFileUris(text).ToList());
        Assert.False(ModelicaUriScanner.IsShownAsText(text, text.IndexOf("modelica://", StringComparison.Ordinal)));
        Assert.False(ModelicaUriScanner.IsShownAsText("modelica://Lib/a.png", 0));
    }

    [Theory]
    [InlineData("<img src=\"modelica://Lib/Resources/a.png\">")]
    [InlineData("<img src='modelica://Lib/Resources/a.png'>")]
    [InlineData("see modelica://Lib/Resources/a.png for details")]
    [InlineData("loadResource(\"modelica://Lib/Resources/a.png\")")]
    [InlineData("<a href=\"modelica://Lib/Resources/a.png#part\">")]   // the anchor is no part of the file
    public void TheDelimitersThatAlreadyWorkedStillDo(string text)
    {
        Assert.Equal(["modelica://Lib/Resources/a.png"], ModelicaUriScanner.FindFileUris(text).ToList());
    }

    [Theory]
    [InlineData("<a href=\"modelica://Lib.'a b'.C\">", "modelica://Lib.'a b'.C")]
    [InlineData("<a href=\"modelica://Lib.'a#b'\">", "modelica://Lib.'a#b'")]
    [InlineData("<a href='modelica://Lib.M'>", "modelica://Lib.M")]
    [InlineData("<a href=\"modelica://Lib.M#info\">", "modelica://Lib.M")]
    [InlineData("modelica://Lib.'unclosed", "modelica://Lib.'unclosed")]
    public void AUriEnds_PastAQuotedIdentifier_AndAtAnAnchorOrAClosingQuote(string text, string expected)
    {
        // One end for a class link and a file reference alike (CheckModelReferences had its own).
        var start = text.IndexOf("modelica://", StringComparison.Ordinal);

        Assert.Equal(expected, text[start..ModelicaUriScanner.EndOf(text, start)]);
    }

    [Fact]
    public void SeveralUrisInOneStringAreAllFound()
    {
        var found = ModelicaUriScanner.FindFileUris(
            "\"modelica://Lib/Resources/a.png\" and \"modelica://Lib/Resources/b.mat\"").ToList();

        Assert.Equal(["modelica://Lib/Resources/a.png", "modelica://Lib/Resources/b.mat"], found);
    }

    [Theory]
    [InlineData("modelica://Modelica.Blocks.Continuous")]   // a class reference, not a file
    [InlineData("modelica://Modelica")]
    public void AClassReferenceIsNotAResource(string uri)
    {
        Assert.False(ModelicaUriScanner.NamesAFile(uri));
        Assert.Empty(ModelicaUriScanner.FindFileUris($"see {uri} for details"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nothing to see here")]
    public void TextWithNoUriYieldsNothing(string? text)
    {
        Assert.Empty(ModelicaUriScanner.FindFileUris(text));
    }
}
