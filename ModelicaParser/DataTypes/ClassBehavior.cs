namespace ModelicaParser.DataTypes;

/// <summary>A connect(a, b) pair.</summary>
public sealed record ConnectionPair(string PortA, string PortB);

/// <summary>
/// The behaviour a class declares itself: the top-level equations, connect() statements and algorithm
/// statements in its own body (not those inherited from base classes — inherited behaviour is left in
/// its declaring class, which callers can query separately). Produced by
/// <see cref="Visitors.BehaviorExtractor"/>.
/// </summary>
public sealed record ClassBehavior(
    IReadOnlyList<string> Equations,
    IReadOnlyList<ConnectionPair> Connections,
    IReadOnlyList<string> Statements,
    bool HasEquationSection,
    bool HasAlgorithmSection)
{
    public static ClassBehavior Empty { get; } = new(
        Array.Empty<string>(), Array.Empty<ConnectionPair>(), Array.Empty<string>(), false, false);

    /// <summary>True if the class declares any equations, connections or statements of its own.</summary>
    public bool HasAny => Equations.Count > 0 || Connections.Count > 0 || Statements.Count > 0;
}
