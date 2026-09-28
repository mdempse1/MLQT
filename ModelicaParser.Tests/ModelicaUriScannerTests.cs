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
    [Fact]
    public void AnEntityQuotedUriStopsAtTheFileName()
    {
        // The defect. Documentation carries HTML inside a Modelica string, so its quotes are written
        // as entities and there is no literal quote to stop at.
        var found = ModelicaUriScanner.FindFileUris(
            "&lt;img src=&quot;modelica://Modelica/Resources/Images/foo.png&quot;&gt;").ToList();

        Assert.Equal(["modelica://Modelica/Resources/Images/foo.png"], found);
    }

    [Theory]
    [InlineData("<img src=\"modelica://Lib/Resources/a.png\">")]
    [InlineData("<img src='modelica://Lib/Resources/a.png'>")]
    [InlineData("see modelica://Lib/Resources/a.png for details")]
    [InlineData("loadResource(\"modelica://Lib/Resources/a.png\")")]
    public void TheDelimitersThatAlreadyWorkedStillDo(string text)
    {
        Assert.Equal(["modelica://Lib/Resources/a.png"], ModelicaUriScanner.FindFileUris(text).ToList());
    }

    [Fact]
    public void SeveralUrisInOneStringAreAllFound()
    {
        var found = ModelicaUriScanner.FindFileUris(
            "&quot;modelica://Lib/Resources/a.png&quot; and &quot;modelica://Lib/Resources/b.mat&quot;").ToList();

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
