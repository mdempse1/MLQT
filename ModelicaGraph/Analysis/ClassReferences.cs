using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Analysis;

/// <summary>
/// What a <c>modelica://</c> link to a class leads to, in the roadmap's three states: found, missing,
/// or something MLQT cannot see.
/// </summary>
/// <remarks>
/// <para><b>Why it is not "is the name in the graph".</b> That was the whole of
/// <c>MLQT.Reference.ModelReferences</c> until phase 2, and it is wrong both ways. A link into a
/// library that is not loaded was reported broken - so a library documenting where to look in MSL
/// was all findings until MSL was loaded. And a link to a class that a package inherits -
/// <c>modelica://Modelica.Media.Water.StandardWater.ThermodynamicState</c>, which
/// <c>StandardWater</c> gets by extending <c>PartialMedium</c> - has no node of its own, so it was
/// reported broken though every tool follows it.</para>
///
/// <para>So the name is walked a segment at a time with <see cref="TypeResolver.MemberClass"/>, which
/// finds inherited classes too. Its first segment not loaded is <see cref="ClassReferenceTarget.CannotTell"/>.
/// A segment found nowhere is <see cref="ClassReferenceTarget.Missing"/> only when the class it was
/// looked for in is complete (<see cref="ClassCompleteness"/>, which <c>MLQT.Reference.Undeclared</c>
/// asks too): every base resolved. Missing from a class with an unresolved base, it could be in what
/// MLQT cannot read. A class recovered from an encrypted library's documentation counts as known
/// here, because the documentation lists the classes it holds; see <see cref="ClassCompleteness"/>.</para>
///
/// <para>One instance for one check, shared by every class's rule in parallel: the answers are cached,
/// and a library's documentation links to the same few classes many times.</para>
/// </remarks>
internal sealed class ClassReferences(DirectedGraph graph)
{
    private readonly TypeResolver.AncestorCache _ancestors = new();
    private readonly ConcurrentDictionary<string, ClassReferenceTarget> _verdicts = new(StringComparer.Ordinal);
    private ClassCompleteness? _completeness;

    public ClassReferenceTarget TargetOf(string fullName) =>
        _verdicts.GetOrAdd(fullName, Judge);

    private ClassReferenceTarget Judge(string fullName)
    {
        var segments = ModelicaName.Segments(fullName);
        if (segments.Count == 0 || graph.GetNode<ModelNode>(segments[0]) is not { } current)
            return ClassReferenceTarget.CannotTell;   // a library that is not loaded

        for (var i = 1; i < segments.Count; i++)
        {
            if (TypeResolver.MemberClass(graph, current, segments[i], _ancestors) is not { } next)
            {
                // Only which classes it holds is asked, so a class recovered from an encrypted
                // library's documentation, which lists them, counts as known.
                var completeness = LazyInitializer.EnsureInitialized(
                    ref _completeness, () => new ClassCompleteness(graph, _ancestors, trustRecoveredClassLists: true));
                return completeness.IsComplete(current) ? ClassReferenceTarget.Missing : ClassReferenceTarget.CannotTell;
            }
            current = next;
        }

        return ClassReferenceTarget.Found;
    }
}
