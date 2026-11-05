using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Whether the formatter has to leave a class's source exactly as the author wrote it.
///
/// <para>There are two ways to say so and they have to be asked together, which is the whole reason
/// this is a method: <see cref="StyleCheckingSettings.FormattingExcludedModels"/> is a name list in
/// the settings, and <c>__MLQT(format=false)</c> / <c>preserveOrder=true</c> is a fact about the
/// source. Phase 5b calls the annotation the rename-safe successor to the list and the documentation
/// steers new usage to it — and the full library save honoured it while the incremental format,
/// which is the one that runs at startup, after every VCS operation, before a commit and on Refresh,
/// asked only the name list and reordered the class anyway.</para>
///
/// <para><b>This is a different question from the one the checker and the dashboard ask</b>, and the
/// two answers are deliberately not the same shape. Writing is per <em>rendered definition</em>: the
/// renderer rewrites a class's whole source or none of it, nested <c>replaceable</c>/<c>redeclare</c>
/// classes included, so one opt-out anywhere inside it takes all of it out — hence
/// <see cref="ModelicaParser.StyleRules.SuppressionSet.HasFormattingOptOut"/> here. Reporting is per
/// <em>class</em>: <c>StyleChecking</c> skips a class's layout rules and
/// <c>CoverageDimensions.ForClass</c> drops its layout rows only for the class that carries the
/// directive, through
/// <see cref="ModelicaParser.StyleRules.SuppressionSet.PreservesFormatting(string)"/>, because
/// waiving a nested class's declaration order is not a request to stop judging its parent's. The
/// residual is small and intended: a class whose nested <c>replaceable</c> member opted out is still
/// reported on layout the formatter will now decline to fix.</para>
/// </summary>
public static class FormattingExclusion
{
    /// <summary>
    /// Whether <paramref name="model"/>'s source must be written back unchanged, by either mechanism.
    /// </summary>
    /// <param name="settings">The repository's settings, for the name list. Null for a caller that
    /// has only the source to go on.</param>
    public static bool Excludes(ModelNode model, StyleCheckingSettings? settings)
        => (settings?.IsModelExcludedFromFormatting(model.Id) ?? false) || OptsOutInSource(model);

    /// <summary>
    /// Whether the class says so in its own source — <c>__MLQT(format=false)</c> or
    /// <c>__MLQT(preserveOrder=true)</c>, on it or on a class nested inside it.
    ///
    /// <para>Read through <see cref="ClassSuppressions.For"/> like every other <c>__MLQT</c> question,
    /// so the answer is kept on the class and the base package is the one the finding ids use. Written
    /// out here it had neither: the extractor was built with no base package, so its keys were bare
    /// names, and the walk was repeated for a class the checker had already read.</para>
    ///
    /// <para>The string pre-check is what keeps this affordable on a library of tens of thousands of
    /// classes that has not been checked yet — a save can be the first thing that runs, and parsing
    /// every class to find the handful carrying an annotation is the wrong shape. A class whose answer
    /// is already known skips it.</para>
    /// </summary>
    /// <remarks>
    /// <b>A class Dymola generated from an FMU is left alone too</b>, with no annotation: Dymola
    /// writes it again whenever the FMU is imported again, so reformatting it is churn in the
    /// repository that the next import throws away, and nothing in it is checked to report the
    /// layout against. Like an opt-out, anywhere in the rendered definition takes all of it out.
    /// </remarks>
    public static bool OptsOutInSource(ModelNode model)
    {
        var definition = model.Definition;

        if (definition.Suppressions is null && !MayCarryDirective(definition.ModelicaCode))
            return false;

        var suppressions = ClassSuppressions.For(definition, model.Id);
        return suppressions.HasFormattingOptOut || suppressions.HasGeneratedImport;
    }

    /// <summary>
    /// Whether the source could hold anything <see cref="OptsOutInSource"/> looks for - the cheap
    /// test before the parse. Both strings: a generated class carries no <c>__MLQT</c> at all.
    /// </summary>
    private static bool MayCarryDirective(string? code) =>
        code is not null
        && (code.Contains("__MLQT", StringComparison.Ordinal)
            || code.Contains("__Dymola_FMU", StringComparison.Ordinal));
}
