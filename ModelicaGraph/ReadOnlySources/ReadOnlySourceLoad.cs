namespace ModelicaGraph;

/// <summary>What <see cref="ReadOnlySourceLoader.Load"/> added.</summary>
/// <param name="ModelIds">The classes this library supplies to the graph.</param>
/// <param name="Superseded">Classes left alone because a copy that outranks them is already loaded.</param>
public sealed record ReadOnlySourceLoad(IReadOnlyList<string> ModelIds, int Superseded);
