using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// B175 — what the exclusion button writes is what the formatter reads.
///
/// <para>The button used to add a name to <c>FormattingExcludedModels</c>; it now writes
/// <c>__MLQT(format=false)</c> into the class. Those are two different mechanisms answered by two
/// different pieces of code, and this is the test that they meet:
/// <see cref="MlqtSuppressionWriter"/> writes it and <see cref="FormattingExclusion.Excludes"/> —
/// the single answer to "must the formatter leave this class alone?" — has to agree.</para>
///
/// <para>It is worth its own test because the two live in different assemblies and neither one's
/// own tests can see the other. That is the shape of B39, B50 and B65, where an exclusion taught to
/// one reader was not taught to the next.</para>
/// </summary>
public class FormattingOptOutRoundTripTests
{
    private static ModelNode NodeFor(string code) =>
        new("M", "M", code) { ClassType = "model" };

    [Fact]
    public void WhatTheWriterWrites_IsWhatTheFormatterReadsAsExcluded()
    {
        const string Source = "model M\n  Real x;\nend M;\n";
        var settings = new StyleCheckingSettings();

        Assert.False(FormattingExclusion.Excludes(NodeFor(Source), settings));

        Assert.True(MlqtSuppressionWriter.TryAddFormattingOptOutToFile(
            Source, null, out var annotated, out var error), error);

        Assert.True(FormattingExclusion.Excludes(NodeFor(annotated), settings));
    }

    [Fact]
    public void RemovingIt_PutsTheClassBackUnderTheFormatter()
    {
        const string Source = "model M\n  Real x;\nend M;\n";
        var settings = new StyleCheckingSettings();

        MlqtSuppressionWriter.TryAddFormattingOptOutToFile(Source, null, out var annotated, out _);
        Assert.True(MlqtSuppressionWriter.TryRemoveFormattingOptOutFromFile(
            annotated, null, out var cleared, out var error), error);

        Assert.False(FormattingExclusion.Excludes(NodeFor(cleared), settings));
        Assert.Equal(Source, cleared);
    }

    [Fact]
    public void TheNameListStillWorks_SoAnOlderRepositoryIsUnaffected()
    {
        // Both mechanisms remain honoured. A class excluded by an earlier MLQT stays excluded
        // without anyone editing its source.
        var settings = new StyleCheckingSettings();
        settings.FormattingExcludedModels.Add("M");

        Assert.True(FormattingExclusion.Excludes(NodeFor("model M\nend M;\n"), settings));
    }

    [Fact]
    public void AnAnnotatedClassIsExcludedEvenWithNoSettingsAtAll()
    {
        // The point of preferring the annotation: it travels with the class, so it holds wherever
        // the class is read from — including a library loaded outside any repository, which has no
        // settings for a name list to live in.
        MlqtSuppressionWriter.TryAddFormattingOptOutToFile(
            "model M\nend M;\n", null, out var annotated, out _);

        Assert.True(FormattingExclusion.Excludes(NodeFor(annotated), settings: null));
    }
}
