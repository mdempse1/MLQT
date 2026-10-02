namespace ModelicaParser.DataTypes;

/// <summary>Which kind of equation a nested <c>connect</c> sits inside.</summary>
public enum ConnectScopeKind
{
    For,
    If,
    When,
}

/// <summary>
/// One <c>for</c>, <c>if</c> or <c>when</c> equation enclosing a <c>connect</c>. <see cref="Header"/>
/// is the branch as written - <c>for i in 1:n</c>, <c>if useHeatPort</c>, <c>elseif k &gt; 0</c>,
/// <c>else</c>, <c>elsewhen x</c> - and <see cref="LoopIndices"/> names what a <c>for</c> binds, which
/// is what the subscripts in its connect's ports refer to.
/// </summary>
public sealed record ConnectScope(ConnectScopeKind Kind, string Header, IReadOnlyList<string> LoopIndices);

/// <summary>
/// A <c>connect(a, b)</c> equation in a class body, read by <see cref="Visitors.ConnectClauses"/>:
/// its two ports as written, the span of the <c>connect(...)</c> itself, and the equations it is
/// nested in, outermost first - empty for one written directly in an equation section.
/// </summary>
public sealed record ConnectClause(
    string PortA,
    string PortB,
    int Start,
    int Stop,
    IReadOnlyList<ConnectScope> Scopes)
{
    /// <summary>True for a connect inside a <c>for</c>, <c>if</c> or <c>when</c> equation.</summary>
    public bool IsNested => Scopes.Count > 0;

    /// <summary>True when a <c>for</c> encloses it, so its ports' subscripts may name loop indices.</summary>
    public bool InLoop => Scopes.Any(s => s.Kind == ConnectScopeKind.For);

    /// <summary>The enclosing branches' headers, outermost first - what a reader is shown.</summary>
    public IReadOnlyList<string> Within => [.. Scopes.Select(s => s.Header)];
}
