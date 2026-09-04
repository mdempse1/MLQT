using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// "Must the formatter write this class back unchanged?" — asked in one place over both mechanisms,
/// because it had been asked in two places over one each. The full library save read the
/// <c>__MLQT</c> annotation and the incremental format read only the name list, so the class the
/// annotation was written on was reordered by the path that actually runs after every VCS operation
/// (backlog B65).
/// </summary>
public class FormattingExclusionTests
{
    private static ModelNode Model(string id, string code) => new(id, id, code);

    private const string Plain = """
        model A
          Real x;
        end A;
        """;

    private const string OptedOut = """
        model A
          Real x;
          annotation(__MLQT(format=false, reason="solver order matters"));
        end A;
        """;

    private const string PreservesOrder = """
        model A
          Real x;
          annotation(__MLQT(preserveOrder=true));
        end A;
        """;

    [Fact]
    public void AnOrdinaryClass_IsNotExcluded()
    {
        Assert.False(FormattingExclusion.Excludes(Model("A", Plain), new StyleCheckingSettings()));
        Assert.False(FormattingExclusion.OptsOutInSource(Model("A", Plain)));
    }

    [Fact]
    public void TheNameList_Excludes()
    {
        var settings = new StyleCheckingSettings();
        settings.FormattingExcludedModels.Add("A");

        Assert.True(FormattingExclusion.Excludes(Model("A", Plain), settings));
        Assert.False(FormattingExclusion.Excludes(Model("B", Plain), settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAnnotationExcludes_WhicheverSpellingIsUsed(bool useFormatFalse)
    {
        var model = Model("A", useFormatFalse ? OptedOut : PreservesOrder);

        Assert.True(FormattingExclusion.OptsOutInSource(model));
        // And through the combined answer, with no name list at all — which is the case the
        // incremental formatter got wrong.
        Assert.True(FormattingExclusion.Excludes(model, new StyleCheckingSettings()));
    }

    [Fact]
    public void WithNoSettingsAtAll_TheAnnotationStillCounts()
    {
        // A caller that has only the source to go on — the library save, which is handed an explicit
        // exclusion set rather than a settings object.
        Assert.True(FormattingExclusion.Excludes(Model("A", OptedOut), settings: null));
        Assert.False(FormattingExclusion.Excludes(Model("A", Plain), settings: null));
    }

    [Fact]
    public void AClassWholeSourceMentionsMlqtWithoutOptingOut_IsNotExcluded()
    {
        // The string pre-check only decides whether to look; it must not decide the answer.
        var suppressesARule = """
            model A
              parameter Real x = 1 annotation(__MLQT(suppress="Doc.ParameterDescription"));
            end A;
            """;

        Assert.False(FormattingExclusion.OptsOutInSource(Model("A", suppressesARule)));
    }

    [Fact]
    public void TheAnswerIsKeptOnTheClass()
    {
        // Through ClassSuppressions, so the checker and the coverage measurer find it done. Written
        // out here it built its own extractor and walked the tree again.
        var model = Model("A", OptedOut);
        Assert.Null(model.Definition.Suppressions);

        FormattingExclusion.OptsOutInSource(model);

        Assert.NotNull(model.Definition.Suppressions);
        Assert.True(model.Definition.Suppressions!.PreservesFormatting("A"));
    }

    [Fact]
    public void TheKeysAreFullyQualified()
    {
        // The extractor was built with no base package here, so its keys were bare names and could
        // not be matched against a finding's ModelId. Nothing noticed because the caller asked
        // HasFormattingOptOut, which does not look at the keys.
        var model = Model("Lib.Sub.A", OptedOut);

        Assert.True(FormattingExclusion.OptsOutInSource(model));
        Assert.True(model.Definition.Suppressions!.PreservesFormatting("Lib.Sub.A"));
    }

    [Fact]
    public void AnUnparseableClass_IsNotExcluded()
    {
        // Reading directives from a class that will not parse is impossible, and refusing to format
        // everything that fails to parse would be the wrong direction — the formatter has its own
        // guard for malformed source.
        Assert.False(FormattingExclusion.OptsOutInSource(Model("A", "model A __MLQT( this is not Modelica")));
    }
}
