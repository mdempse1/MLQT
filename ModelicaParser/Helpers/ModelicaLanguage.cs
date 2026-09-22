namespace ModelicaParser.Helpers;

/// <summary>
/// The names Modelica defines for itself: its predefined types, its built-in operators and functions,
/// and the pseudo-packages a tool provides rather than a library. None of them is ever a class in a
/// loaded library, so none of them is ever a dependency, and no <c>uses(...)</c> annotation can
/// declare one.
///
/// <para>Written down because leaving it implicit is what made <c>MLQT.Structure.UsesUndeclared</c>
/// unusable: name resolution binds a simple name to any node it finds, so a library that happened to
/// hold a class called <c>rooted</c> or <c>Connections</c> collected an edge to it from every model
/// that called the operator, and the rule then reported a library of that name as undeclared -
/// against a name no <c>uses(...)</c> can contain (B246).</para>
///
/// <para><b>Ordinal, because Modelica is.</b> The first version of this list compared
/// case-insensitively, which quietly cost real edges: <c>Modelica.Blocks.Math</c> alone contains
/// <c>Sum</c>, <c>Product</c>, <c>Min</c>, <c>Max</c>, <c>Abs</c>, <c>Sign</c>, <c>Sqrt</c>,
/// <c>Sin</c>, <c>Cos</c>, <c>Tan</c>, <c>Exp</c> and <c>Log</c>, every one of which collides with a
/// built-in function under that comparison, so a component typed by one of them was dropped from the
/// graph.</para>
///
/// <para>Graphical primitives - <c>Line</c>, <c>Rectangle</c> and the rest - are <b>not</b> here.
/// They are annotation grammar, not language names, and a library may legitimately define a class
/// called <c>Line</c>; what keeps them out of the graph is that annotation content is not collected
/// as references at all. See <c>ModelAnalyzer</c>.</para>
/// </summary>
public static class ModelicaLanguage
{
    /// <summary>
    /// True when <paramref name="reference"/> names something the language provides. The <b>first</b>
    /// segment decides: <c>Connections.branch</c> is the operator, not a class in a package called
    /// <c>Connections</c>.
    /// </summary>
    public static bool IsBuiltInName(string? reference)
        => !string.IsNullOrWhiteSpace(reference) && Names.Contains(reference.Split('.')[0]);

    /// <summary>The whole set, for a caller that wants to ask something else of it.</summary>
    public static IReadOnlySet<string> Names => _names;

    /// <summary>
    /// The predefined types alone (§4.8) — the narrower question "is this a type that is not a class
    /// anywhere?", which is what a resolver asks before deciding a name is unresolvable. Kept apart
    /// from <see cref="Names"/> because a resolver must not treat <c>sum</c> or <c>time</c> as a
    /// type, and here because three copies of this four-to-seven name list had drifted apart.
    /// </summary>
    public static IReadOnlySet<string> PredefinedTypes => _predefinedTypes;

    private static readonly HashSet<string> _predefinedTypes = new(StringComparer.Ordinal)
    {
        "Real", "Integer", "Boolean", "String", "StateSelect", "AssertionLevel", "Clock",
        // ExternalObject is the language's own base class for an external object (§12.9), and
        // 'enumeration' is how a vendor's generated documentation prints an enumeration's base.
        "ExternalObject", "enumeration"
    };

    private static readonly HashSet<string> _names = new(StringComparer.Ordinal)
    {
        // Built-in mathematical functions (§3.7.1) and the ones with event generation (§3.7.2).
        "abs", "sign", "sqrt", "div", "mod", "rem", "ceil", "floor", "integer",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
        "sinh", "cosh", "tanh", "exp", "log", "log10",

        // Special operators (§3.7.3, §3.7.5, §8.3.x, §16, §17).
        "der", "initial", "terminal", "noEvent", "smooth", "sample", "pre", "edge", "change",
        "reinit", "delay", "cardinality", "homotopy", "semiLinear", "inStream", "actualStream",
        "spatialDistribution", "getInstanceName", "assert", "terminate",

        // Array constructors and reductions (§10.3).
        "ndims", "size", "scalar", "vector", "matrix", "identity", "diagonal",
        "zeros", "ones", "fill", "linspace", "min", "max", "sum", "product",
        "transpose", "outerProduct", "symmetric", "cross", "skew", "cat", "array",

        // Overconstrained connection graphs (§8.3.9). 'rooted' is also callable unqualified, which
        // is the form every library that predates Modelica 3.2 rev 2 still writes.
        "Connections", "rooted",

        // Synchronous language elements (§16).
        "previous", "hold", "subSample", "superSample", "shiftSample", "backSample",
        "noClock", "interval",

        // State machines (§17).
        "transition", "initialState", "activeState", "ticksInState", "timeInState",

        // The simulation time variable (§3.6.7).
        "time"
    };

    static ModelicaLanguage() => _names.UnionWith(_predefinedTypes);
}
