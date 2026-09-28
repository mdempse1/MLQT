using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// B498 - a <c>protected</c> or <c>public</c> keyword is written at the column of the class it
/// belongs to, as its <c>model</c> and <c>end</c> lines are. The keyword was inserted after its
/// element list had been written, with no indentation of its own, so in a class nested inside
/// another in the same file it stood at column 0 under correctly indented elements: MSL's
/// <c>Modelica.Fluid.Dissipation.HeatTransfer.Channel.kc_evenGapLaminar</c>, six spaces in, had its
/// <c>protected</c> at column 0 - one of 79 in that file, 978 over MSL and Buildings. A top-level
/// class's keyword was already at column 0, which is its class's column, and stays there. These
/// render the whole file, blank lines included, because indentation is the whole of what is under
/// test.
/// </summary>
public class SectionKeywordIndentTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    private static string Render(string source, FormattingOptions? formatting = null)
    {
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(source);
        var renderer = new ModelicaRenderer(tokenStream: tokenStream, formatting: formatting);
        renderer.Visit(parseTree);
        return string.Join("\n", renderer.Code);
    }

    private const string Nested = """
        package P
          model A
            Real x;
          protected
            Real y;
          public
            Real z;
            model B
              Real a;
            protected
              Real b;
            public
              Real c;
            equation
              a = 1;
            algorithm
              c := 2;
            end B;
          equation
            x = 1;
          end A;
        protected
          Real q;
          model C
          protected
            Real r;
          end C;
        end P;
        """;

    private static readonly string NestedExpected = Normalise("""
        package P

          model A
            Real x;
          protected
            Real y;
          public
            Real z;

            model B
              Real a;
            protected
              Real b;
            public
              Real c;

            equation
              a = 1;

            algorithm
              c := 2;
            end B;

          equation
            x = 1;
          end A;
        protected
          Real q;

          model C
          protected
            Real r;
          end C;
        end P;
        """);

    [Fact]
    public void ASectionKeywordIsAtItsOwnClassesColumnAtEveryDepth()
    {
        // A one level down, B two (inside A's public section, which is itself moved a level in),
        // C inside the top-level class's protected section, and P's own keyword at column 0.
        Assert.Equal(NestedExpected, Render(Normalise(Nested)));
    }

    [Fact]
    public void TheIndentedKeywordsAreStableOnARender()
    {
        var once = Render(Normalise(Nested));

        Assert.Equal(once, Render(once));
    }

    [Fact]
    public void ATopLevelClassesKeywordsStayAtColumnZero()
    {
        var rendered = Render(Normalise("""
            model M
              Real x;
            protected
              Real y;
            public
              Real z;
            end M;
            """));

        Assert.Equal(Normalise("""
            model M
              Real x;
            protected
              Real y;
            public
              Real z;
            end M;
            """), rendered);
    }

    [Fact]
    public void AProtectedKeywordIsAtItsClassesColumnWhenTheSectionsAreGathered()
    {
        // One of each section writes the protected half on its own pass; its keyword is the same
        // line and goes to the same column.
        var rendered = Render(Normalise("""
            package P
              model A
                Real x;
              protected
                Real y;
              public
                Real z;
              end A;
            end P;
            """), new FormattingOptions(OneOfEachSection: true));

        Assert.Equal(Normalise("""
            package P

              model A
                Real x;
                Real z;
              protected
                Real y;
              end A;
            end P;
            """), rendered);
    }
}
