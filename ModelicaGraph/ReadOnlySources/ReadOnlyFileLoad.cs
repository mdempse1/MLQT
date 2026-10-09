using System.Collections.Concurrent;
using ModelicaParser.Helpers;

namespace ModelicaGraph;

/// <summary>
/// How <see cref="GraphBuilder"/> loads one file of a supplied read-only library: each class marked
/// <see cref="DataTypes.ModelNode.IsExternalStub"/> and opening with <paramref name="Banner"/>, and
/// only the classes inside <paramref name="LibraryName"/> accepted.
/// </summary>
/// <param name="Banner">The comment block every class opens with.</param>
/// <param name="LibraryName">The library the source says it is. A class outside it is refused before
/// it reaches the graph: precedence is decided per library name, so a supplied file declaring
/// <c>within Modelica.Blocks;</c> or <c>within MyLib;</c> would otherwise put read-only classes into
/// another library's namespace — displacing that library's recovered classes, or adding to the
/// user's own — without any precedence rule ever being asked.</param>
/// <param name="Refused">Where the ids of refused classes are collected, from parallel loads.</param>
internal sealed record ReadOnlyFileLoad(string Banner, string LibraryName, ConcurrentBag<string> Refused)
{
    /// <summary>Whether a class of this id may come from this library.</summary>
    public bool Accepts(string modelId) =>
        string.Equals(ModelicaName.RootLibraryOf(modelId), LibraryName, StringComparison.Ordinal);
}
