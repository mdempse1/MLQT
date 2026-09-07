using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// The names that count as valid words while a class is spell checked: everything the class declares
/// (whatever the order it is written in) and everything it inherits.
/// </summary>
public class SpellCheckInheritedNamesTests
{
    private static SpellChecker CreateSpellChecker() => SpellChecker.Create();

    private static Func<string, IReadOnlySet<string>> Inherited(params string[] names)
        => _ => new HashSet<string>(names, StringComparer.Ordinal);

    private const string DerivedModel = """
        model Derived "Sets the wibbler position"
          extends Base;
          Real x "Offset from the wibbler";
          annotation (Documentation(info="<html><p>The wibbler is driven directly.</p></html>"));
        end Derived;
        """;

    [Fact]
    public void Description_InheritedComponentName_IsAWordWhenTheChainIsFollowed()
    {
        var parseTree = ModelicaParserHelper.Parse(DerivedModel);
        var visitor = new SpellCheckDescriptions(
            CreateSpellChecker(), inheritedElementNames: Inherited("wibbler"));
        visitor.Visit(parseTree);

        Assert.Empty(visitor.Findings);
    }

    [Fact]
    public void Description_InheritedComponentName_IsMisspelledWithoutTheChain()
    {
        // Guards the fix: with no lookup only the class's own declarations are known, which is the
        // behaviour that reported every reference to an inherited member as a misspelling.
        var parseTree = ModelicaParserHelper.Parse(DerivedModel);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.All(visitor.Findings, f => Assert.Equal("wibbler", f.Discriminator));
        Assert.Equal(2, visitor.Findings.Count);   // the class description and the component's
    }

    [Fact]
    public void Documentation_InheritedComponentName_IsAWordWhenTheChainIsFollowed()
    {
        var parseTree = ModelicaParserHelper.Parse(DerivedModel);
        var visitor = new SpellCheckDocumentation(
            CreateSpellChecker(), inheritedElementNames: Inherited("wibbler"));
        visitor.Visit(parseTree);

        Assert.Empty(visitor.Findings);
    }

    [Fact]
    public void Documentation_InheritedComponentName_IsMisspelledWithoutTheChain()
    {
        var parseTree = ModelicaParserHelper.Parse(DerivedModel);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Single(visitor.Findings);
    }

    [Theory]
    [InlineData("Lib.Sizing", 0)]
    [InlineData("Other.Sizing", 2)]
    public void InheritedNames_AreLookedUpByTheCheckedClassFullName(string knownClassId, int expectedFindings)
    {
        // The lookup is keyed by the fully qualified name the visitor tracks, so a class whose id the
        // caller does not recognise gets no inherited names rather than another class's.
        var code = """
            model Sizing "Scaled by the wibbler"
              Real x "Fraction of the wibbler";
            end Sizing;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(
            CreateSpellChecker(), basePackage: "Lib",
            inheritedElementNames: id => id == knownClassId
                ? new HashSet<string>(["wibbler"], StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal));
        visitor.Visit(parseTree);

        Assert.Equal(expectedFindings, visitor.Findings.Count);
    }

    [Fact]
    public void Description_ComponentDeclaredLaterInTheClass_IsAWord()
    {
        // The class description is written before any declaration, so names collected as the walk
        // reaches them arrive too late to be of any use to it.
        var code = """
            model Sizing "Scaled by the wibbler"
              Real wibbler "Scale factor";
            end Sizing;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.Findings);
    }

    [Fact]
    public void Documentation_NestedClassName_IsAWord()
    {
        var code = """
            model Holder
              model Wibbler "A nested class"
              end Wibbler;
              annotation (Documentation(info="<html><p>Holds a Wibbler.</p></html>"));
            end Holder;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDocumentation(CreateSpellChecker());
        visitor.Visit(parseTree);

        Assert.Empty(visitor.Findings);
    }

    [Fact]
    public void Description_PossessiveOfAnInheritedName_IsAWord()
    {
        var code = """
            model Derived "Follows the wibbler's position"
              extends Base;
            end Derived;
            """;

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new SpellCheckDescriptions(
            CreateSpellChecker(), inheritedElementNames: Inherited("wibbler"));
        visitor.Visit(parseTree);

        Assert.Empty(visitor.Findings);
    }
}
