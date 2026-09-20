using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Tests;

/// <summary>
/// B258 — a class's icon is rendered once and thrown away when its code changes.
///
/// <para><b>Why it is cached.</b> Rendering an icon resolves the class's base classes and parses
/// them to do it, and the library browser rebuilds its tree on every change to it — so this ran on
/// the dispatcher, for every top-level class, every time. Measured on a real project, one refresh
/// of one repository's tree spent <b>1,477ms of its 1,522ms</b> there, repeatedly. That is the
/// startup stutter.</para>
///
/// <para><b>Why the invalidation is the part worth testing.</b> A cache that is merely slow is a
/// disappointment; one that keeps an answer past the question is a wrong icon on screen, and the
/// only reason this is safe to keep at all is that it now sits beside the parse tree, the coverage
/// facts and the suppressions — everything else derived from the code, discarded by the one setter
/// that knows the code has moved.</para>
/// </summary>
public class IconCacheInvalidationTests
{
    private const string WithIcon = """
        model M "m"
          annotation (Icon(graphics={Rectangle(extent={{-10,-10},{10,10}})}));
        end M;
        """;

    [Fact]
    public void ChangingTheCodeDiscardsTheRenderedIcon()
    {
        var definition = new ModelDefinition("M", WithIcon);
        definition.IconSvg = "<svg>old</svg>";
        definition.IconRendered = true;

        definition.ModelicaCode = "model M \"m\"\nend M;";

        Assert.Null(definition.IconSvg);
        Assert.False(definition.IconRendered,
            "the icon has to be re-rendered after the code changes, not just blanked — otherwise a "
            + "class that gained an icon never shows it");
    }

    [Fact]
    public void ChangingTheCodeDiscardsTheOtherDerivedAnswersToo()
    {
        // The company it keeps, asserted so that a later reader can see the icon is not a special
        // case bolted on: it is one of four things derived from the code and dropped with it.
        var definition = new ModelDefinition("M", WithIcon);
        definition.Suppressions = ModelicaParser.StyleRules.SuppressionSet.Empty;
        definition.EnsureParsed();
        definition.IconRendered = true;

        definition.ModelicaCode = "model M \"m\"\nend M;";

        Assert.Null(definition.Suppressions);
        Assert.Null(definition.ParsedCode);
        Assert.False(definition.IconRendered);
    }

    [Fact]
    public void TheNodeAndItsDefinitionAgreeAboutTheIcon()
    {
        // ModelNode.IconSvg is the name everything already uses and is now a facade. If the two ever
        // came apart, the browser would read one and the invalidation would clear the other.
        var node = new ModelNode("M", "M", WithIcon);

        node.IconSvg = "<svg>rendered</svg>";
        Assert.Equal("<svg>rendered</svg>", node.Definition.IconSvg);

        node.Definition.ModelicaCode = "model M \"m\"\nend M;";
        Assert.Null(node.IconSvg);
    }

    [Fact]
    public void NoIconIsAnAnswerWorthRemembering()
    {
        // Most classes have none, so "asked, and there is none" must be as cheap to remember as an
        // answer — which is why IconRendered is separate from the SVG being null.
        var definition = new ModelDefinition("M", "model M \"m\"\nend M;");

        definition.IconRendered = true;

        Assert.True(definition.IconRendered);
        Assert.Null(definition.IconSvg);
    }
}
