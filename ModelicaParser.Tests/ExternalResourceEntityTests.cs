using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// That a <c>modelica://</c> URI written inside HTML-escaped documentation stops at the file name
/// (B209).
///
/// <para>A Modelica documentation string carries HTML inside a Modelica string, so the HTML's own
/// quotes are commonly written as <c>&amp;quot;</c> rather than as quotes. The URI scan stopped at a
/// literal quote and at several other delimiters, but not at <c>&amp;</c> — so with no literal quote
/// present it ran past the end of the file name and kept the entity: <c>foo.png&amp;quot;</c>. That
/// path can never exist, so it is reported missing for ever.</para>
///
/// <para>The Modelica Standard Library has two of them, and one names a file that is on disk — so
/// this was two thirds of the false entries in its missing-resource list.</para>
/// </summary>
public class ExternalResourceEntityTests
{
    private static List<string> UriResourcesIn(string documentation)
    {
        var code = $$"""
            model Documented "A model"
              annotation (Documentation(info="{{documentation}}"));
            end Documented;
            """;

        var extractor = new ExternalResourceExtractor();
        extractor.Visit(ModelicaParserHelper.Parse(code));

        return extractor.Resources
            .Where(r => r.ReferenceType == ResourceReferenceType.UriReference)
            .Select(r => r.RawPath)
            .ToList();
    }

    [Fact]
    public void AnEntityQuotedImageSourceStopsAtTheFileName()
    {
        var found = UriResourcesIn(
            "&lt;img src=&quot;modelica://Modelica/Resources/Images/foo.png&quot;&gt;");

        Assert.Equal(["modelica://Modelica/Resources/Images/foo.png"], found);
    }

    [Fact]
    public void APlainQuotedImageSourceIsUnchanged()
    {
        // The case that already worked, kept so the fix is not a swap of which form breaks.
        var found = UriResourcesIn("<img src='modelica://Modelica/Resources/Images/foo.png'>");

        Assert.Equal(["modelica://Modelica/Resources/Images/foo.png"], found);
    }

    [Fact]
    public void TheEntityIsNotLeftOnTheEndOfTheName()
    {
        var found = UriResourcesIn(
            "&lt;img src=&quot;modelica://Modelica/Resources/Images/foo.png&quot;&gt;");

        Assert.DoesNotContain(found, path => path.Contains('&'));
    }
}
