using MLQT.Services.DataTypes;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// Which loaded library a class belongs to, when more than one of them claims it.
/// </summary>
/// <remarks>
/// <para><b>Two libraries claiming one class is ordinary, not a fault.</b> A tool's library folder
/// ships the encrypted build of a library the user also has checked out as source, and both are
/// loaded — one for reference, one to work in. Only one copy of the class survives in the graph and
/// <c>DirectedGraph.AddNode</c> decides which, readable source always beating a class reconstructed
/// from vendor documentation. <b>The library index is never told</b>, so the losing entry goes on
/// listing an id it no longer supplies.</para>
///
/// <para><b>So the answer comes from the graph, not from the list.</b> Whichever way the collision
/// was resolved, the owner is the library that could have supplied the node that is actually there:
/// an encrypted library supplies stubs and nothing else, and every other kind supplies readable
/// source. Asking the list instead returns whichever library was added first, which is a race
/// between two parallel loads — and that is a race the caller then loses silently, because a
/// perfectly ordinary class resolves to a vendor folder that is not under version control.</para>
/// </remarks>
public static class LibraryOwnership
{
    /// <summary>
    /// The library that owns <paramref name="modelId"/>, or null when none claims it.
    /// </summary>
    /// <param name="libraries">Every loaded library.</param>
    /// <param name="modelId">The class's full Modelica name.</param>
    /// <param name="lookup">Finds the class in the graph. Consulted only when two libraries claim it.</param>
    public static LoadedLibrary? Owner(
        IEnumerable<LoadedLibrary> libraries, string modelId, Func<string, ModelNode?> lookup)
    {
        LoadedLibrary? first = null;
        List<LoadedLibrary>? claimants = null;

        foreach (var library in libraries)
        {
            if (!library.ModelIds.Contains(modelId))
                continue;

            if (first is null)
                first = library;
            else
                (claimants ??= [first]).Add(library);
        }

        // The overwhelmingly common case, and the one worth not paying for: a single claimant is
        // the answer and the graph is never consulted.
        if (claimants is null)
            return first;

        var isStub = lookup(modelId)?.IsExternalStub == true;

        return claimants.FirstOrDefault(
                   l => (l.SourceType == LibrarySourceType.EncryptedDirectory) == isStub)
               ?? claimants[0];
    }
}
