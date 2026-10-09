namespace ModelicaGraph;

/// <summary>What <see cref="ReadOnlySourceLoader.Load"/> added.</summary>
/// <param name="ModelIds">The classes this library supplies to the graph.</param>
/// <param name="Superseded">Classes left alone because a copy that outranks them is already loaded.</param>
public sealed record ReadOnlySourceLoad(IReadOnlyList<string> ModelIds, int Superseded)
{
    /// <summary>
    /// Classes a supplied source declared outside its own library, which were not added: a read-only
    /// library may only hold classes of its own name. Empty for a source that kept to it.
    /// </summary>
    public IReadOnlyList<string> Refused { get; init; } = [];
}
