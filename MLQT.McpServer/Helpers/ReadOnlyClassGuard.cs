using ModelicaGraph;
using MLQT.McpServer.Dtos;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The first thing every tool that edits a class asks: is this class from a read-only library?
///
/// <para><b>First, and by the class rather than its file.</b> <see cref="FileWritability"/> still
/// refuses a read-only file at the moment of writing, and stays as the backstop. But reached that
/// late, a tool has already composed its edit against the class's text, and a read-only class can
/// fail that first — a supplied class's stored source opens with a provenance banner its file
/// owner's does not repeat, and a recovered one has nothing in it to edit — so the caller was told
/// the cache was stale, or the component was missing, instead of the one thing that is true.
/// Asked here, every editing tool gives the same answer, and <c>SuppliedLibraryToolTests</c> can
/// require exactly that answer of every tool that does not simply read.</para>
/// </summary>
internal static class ReadOnlyClassGuard
{
    /// <summary>
    /// The refusal for editing <paramref name="classId"/>, or null when it may be edited (or does not
    /// exist, which the tool reports in its own words).
    /// </summary>
    /// <param name="libraries">The loaded libraries.</param>
    /// <param name="classId">The class the tool would change, or a package it would add to.</param>
    /// <param name="operation">What the tool was asked to do, for the message: "update this class".</param>
    public static ToolError? Refuse(ILibraryDataService libraries, string? classId, string operation)
    {
        if (string.IsNullOrEmpty(classId) || libraries.GetModelById(classId) is not { } node)
            return null;

        return ReadOnlySources.KindOf(node) switch
        {
            ReadOnlySourceKind.Supplied => new ToolError(
                $"Cannot {operation}: '{classId}' belongs to a read-only library supplied from memory. Its " +
                "classes describe a library whose source is not available — they are not the vendor's " +
                "source and can never be edited. Edit your own classes, which may use them. Nothing was changed."),
            ReadOnlySourceKind.RecoveredFromDocumentation => new ToolError(
                $"Cannot {operation}: '{classId}' belongs to an encrypted library. Its classes are " +
                "reconstructions MLQT built from the vendor's documentation so that references into the " +
                "library resolve — there is no source to edit. Nothing was changed."),
            _ => null
        };
    }
}
