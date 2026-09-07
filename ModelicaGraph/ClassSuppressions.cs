using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaGraph;

/// <summary>
/// The <c>__MLQT</c> directives a class carries, read once per class and kept on its
/// <see cref="ModelDefinition"/>.
///
/// <para>One reader because there were three, each walking the same tree for the same answer in the
/// same run: the style checker filtering its findings, the coverage measurer asking whether the class
/// opted out of formatting — on the very tree the checker had just walked — and the graph analyses,
/// which re-parsed the class to ask again. Three walks and three chances to disagree about what a
/// directive means, for a question whose answer is a fact about the source.</para>
///
/// <para>Cached as <see cref="SuppressionSet.Empty"/> when the class carries nothing, which is nearly
/// every class: that is one shared instance, so keeping the answer for a library of tens of thousands
/// of classes costs a reference each rather than a set each. <see cref="ModelDefinition.ModelicaCode"/>
/// clears it, so an edited class is read again.</para>
///
/// <para>Style checking runs per class in parallel, so two threads can reach the same class at once
/// and both do the work. That is fine and deliberately not locked: the answer is a pure function of
/// the source, so the two agree, and the reference assignment is atomic — the cost of the race is one
/// wasted walk, against a lock taken tens of thousands of times. Same shape as
/// <see cref="ModelDefinition.Coverage"/>.</para>
/// </summary>
public static class ClassSuppressions
{
    /// <summary>
    /// The directives <paramref name="definition"/> carries, reading them if nobody has yet.
    ///
    /// <para>Never null and never throws: a class that will not parse has no directives anyone can
    /// see, and every caller here treats that the same as carrying none. Failing louder would mean a
    /// broken file silently gaining every waiver instead of losing them, which is the wrong direction
    /// for a check to fail in — the parse error is reported on its own account elsewhere.</para>
    /// </summary>
    /// <param name="modelId">The class's fully qualified name, which decides the package a directive
    /// on a nested class is attributed to. Callers pass the same id they report findings under, so a
    /// directive and the finding it waives agree about which class they are about.</param>
    public static SuppressionSet For(ModelDefinition definition, string modelId)
    {
        if (definition.Suppressions is { } cached)
            return cached;

        SuppressionSet extracted;
        try
        {
            // Borrowed: the caller usually holds the tree already (the checker and the measurer both
            // do), in which case this releases nothing; the graph analyses do not, and hand it back.
            extracted = definition.Borrow(
                tree =>
                {
                    var extractor = new MlqtSuppressionExtractor(ModelicaName.EnclosingPackageOf(modelId));
                    extractor.VisitStored_definition(tree);
                    return extractor.Build();
                },
                SuppressionSet.Empty);
        }
        catch
        {
            extracted = SuppressionSet.Empty;
        }

        definition.Suppressions = extracted.IsEmpty ? SuppressionSet.Empty : extracted;
        return definition.Suppressions;
    }
}
