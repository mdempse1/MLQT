using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Whether MLQT knows everything a class has: it is readable source, and every base it names resolves
/// to a class of which the same is true. The question the roadmap's three-state rule turns on - a
/// member missing from a complete class is missing, and one missing from an incomplete class may be in
/// what MLQT cannot see.
/// </summary>
/// <remarks>
/// <para>Asked by <c>MLQT.Reference.Undeclared</c> of every scope a name is looked up in, and by
/// <c>MLQT.Reference.ModelReferences</c> of the class a <c>modelica://</c> link stops resolving in.
/// It was private to the first until the second needed it; two copies of what "complete" means would
/// come to disagree about which misses can be reported.</para>
///
/// <para><b>An encrypted class is complete or not depending on the question.</b> MLQT recovers such a
/// class from the vendor's help HTML (<c>DymolaHelpParser</c>), which lists the classes a package
/// holds but not every member a class declares. So for "which classes are in it?" - a
/// <c>modelica://</c> link - its class list is trusted, and a link into a documented package naming
/// a class the documentation does not list is reported (the decision
/// <c>EncryptedLibraryEndToEndTests.ModelReferenceToSomethingNotInTheDocumentation_IsStillReported</c>
/// holds). For "which names does it declare?" - <c>MLQT.Reference.Undeclared</c> - it is not.
/// <paramref name="trustRecoveredClassLists"/> says which question this instance answers.</para>
///
/// <para>Thread-safe: the per-class rules ask it in parallel. The answer is cached per class for the
/// life of the instance, which is one check.</para>
/// </remarks>
/// <param name="trustRecoveredClassLists">True when the question is only which classes a class holds,
/// so a class recovered from an encrypted library's documentation counts as known.</param>
internal sealed class ClassCompleteness(
    DirectedGraph graph, TypeResolver.AncestorCache ancestors, bool trustRecoveredClassLists = false)
{
    private readonly ConcurrentDictionary<string, bool> _complete = new(StringComparer.Ordinal);

    public bool IsComplete(ModelNode node) => IsComplete(node, []);

    private bool IsComplete(ModelNode node, HashSet<string> visiting)
    {
        if (_complete.TryGetValue(node.Id, out var known))
            return known;
        if (!visiting.Add(node.Id))
            return true;  // a cycle adds nothing a member could come from

        var complete = !node.IsParseFailurePlaceholder && (trustRecoveredClassLists || !node.IsExternalStub);
        if (complete)
        {
            var imports = ClassImports.For(node.Definition);
            foreach (var written in ClassImports.BasesOf(node.Definition))
            {
                if (TypeResolver.IsPredefined(written))
                    continue;
                if (ClassElementResolver.ResolveBaseOf(graph, node, written, imports, ancestors) is not { } baseNode
                    || !IsComplete(baseNode, visiting))
                {
                    complete = false;
                    break;
                }
            }
        }

        visiting.Remove(node.Id);
        _complete[node.Id] = complete;
        return complete;
    }
}
