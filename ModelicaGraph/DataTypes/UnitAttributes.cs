namespace ModelicaGraph.DataTypes;

/// <summary>
/// What a type's short-class chain fixes about a <c>Real</c>-derived quantity: whether it is
/// one at all, and the <c>unit</c>, <c>displayUnit</c> and <c>quantity</c> it carries. Each attribute
/// is taken from the <b>nearest</b> definition along the chain that sets it, so
/// <c>type Torque = Real(unit="N.m", quantity="Torque")</c> followed by
/// <c>type MyTorque = Torque(displayUnit="kN.m")</c> answers all three for <c>MyTorque</c>, and an
/// alias that sets <c>unit</c> again overrides the one below it.
///
/// <para>A value written as a string literal is that string, unquoted and unescaped (<c>"N.m"</c> is
/// <c>N.m</c>), with any parentheses round it removed (<c>("N.m")</c> is <c>N.m</c> too). A value that is any other expression — a parameter, a concatenation — is its source
/// text rebuilt from the tokens, because the attribute is still fixed there and a caller asking
/// <see cref="HasUnit"/> must not be told otherwise; a units check reading it will find it is not a
/// unit string and can say so. Every attribute is null when <see cref="IsRealDerived"/> is false.</para>
/// </summary>
public readonly record struct UnitAttributes(
    bool IsRealDerived, string? Unit, string? DisplayUnit, string? Quantity)
{
    /// <summary>Not a Real quantity, or not resolvable: nothing is known.</summary>
    public static readonly UnitAttributes None = new(false, null, null, null);

    /// <summary>A plain <c>Real</c>: Real-derived, with nothing fixed at type level.</summary>
    public static readonly UnitAttributes PlainReal = new(true, null, null, null);

    /// <summary>Whether a unit is fixed anywhere in the type chain.</summary>
    public bool HasUnit => Unit is not null;
}
