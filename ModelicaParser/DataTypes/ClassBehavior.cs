namespace ModelicaParser.DataTypes;

/// <summary>An equation or statement, with any // or /* */ comments written immediately before it.</summary>
public sealed record BehaviorLine(string Text, IReadOnlyList<string> LeadingComments);

/// <summary>
/// The behaviour a class declares itself: the top-level equations and algorithm statements in its own
/// body, and every connect() in it - those inside a for, if or when equation too, which also stay in
/// that equation's text (not those inherited from base classes — inherited behaviour is left in
/// its declaring class, which callers can query separately). Produced by
/// <see cref="Visitors.BehaviorExtractor"/>.
/// </summary>
public sealed record ClassBehavior(
    IReadOnlyList<BehaviorLine> Equations,
    IReadOnlyList<ConnectClause> Connections,
    IReadOnlyList<BehaviorLine> Statements,
    bool HasEquationSection,
    bool HasAlgorithmSection)
{
    public static ClassBehavior Empty { get; } = new(
        Array.Empty<BehaviorLine>(), Array.Empty<ConnectClause>(), Array.Empty<BehaviorLine>(), false, false);

    /// <summary>True if the class declares any equations, connections or statements of its own.</summary>
    public bool HasAny => Equations.Count > 0 || Connections.Count > 0 || Statements.Count > 0;
}
