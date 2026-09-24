using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

/// <summary>
/// <see cref="SectionKeyword"/>: a class body's keywords read from its terminals, and nothing read
/// from anything else — which is the whole of the saving, since the text of a rule node is its entire
/// source rebuilt.
/// </summary>
public class SectionKeywordTests
{
    private static List<string> KeywordsOf(string code)
    {
        var tree = ModelicaParserHelper.Parse(code);
        var composition = tree.class_definition(0).class_specifier().long_class_specifier().composition();
        return composition.children.Select(SectionKeyword.Of).ToList();
    }

    [Fact]
    public void TheKeywordsAreReadFromTheirTerminals()
    {
        var keywords = KeywordsOf("""
            model M
              Real a;
            public
              Real b;
            protected
              Real c;
            equation
              a = b;
            end M;
            """);

        Assert.Contains("public", keywords);
        Assert.Contains("protected", keywords);
    }

    [Fact]
    public void ARuleNode_IsNoKeywordAtAll()
    {
        // The element lists and the equation section are rule nodes. Their text is their whole source,
        // and none of it is asked for.
        var tree = ModelicaParserHelper.Parse("""
            model M
              Real a;
            equation
              a = 1;
            end M;
            """);
        var composition = tree.class_definition(0).class_specifier().long_class_specifier().composition();

        Assert.Equal(string.Empty, SectionKeyword.Of(composition.element_list(0)));
        Assert.Equal(string.Empty, SectionKeyword.Of(composition.equation_section(0)));
    }

    [Fact]
    public void ExternalIsReadToo()
    {
        // The renderer asks for it to find an external function's clause.
        var keywords = KeywordsOf("""
            function f
              input Real x;
              output Real y;
            external "C" y = f_impl(x);
            end f;
            """);

        Assert.Contains("external", keywords);
    }
}
