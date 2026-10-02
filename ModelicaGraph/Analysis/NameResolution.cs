using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>How the first segment of a name was found - which says what a rename or a move may do to it.</summary>
public enum NameBinding
{
    /// <summary>A class the scope declares itself.</summary>
    Own,
    /// <summary>A class the scope inherits from a base: <c>Medium</c> under a base's replaceable one.</summary>
    Inherited,
    /// <summary>Brought in by a qualified import, <c>import A.B.C;</c> or <c>import A.B.{C, D};</c>.</summary>
    Import,
    /// <summary>Brought in under another name, <c>import X = A.B.C;</c> - the name written is the alias.</summary>
    AliasImport,
    /// <summary>Brought in by an unqualified import, <c>import A.B.*;</c>.</summary>
    WildcardImport,
    /// <summary>A top-level class, reached after every enclosing scope.</summary>
    Root,
    /// <summary>Written with a leading dot, looked up from the top only.</summary>
    Global,
}

/// <summary>
/// What a name resolved to: the class (<see cref="Node"/>), the id of the class each segment names
/// (<see cref="Path"/>, one per segment), and how the first was found (<see cref="Binding"/>).
/// </summary>
/// <remarks>
/// <see cref="Path"/> holds ids, not nodes, because a graph need not hold every package on the way: a
/// library loaded in part, or a test graph that adds <c>Modelica.Units.SI</c> without
/// <c>Modelica</c>, still resolves the name - only the class at the end has to be there.
/// </remarks>
public sealed record NameResolution(ModelNode Node, IReadOnlyList<string> Path, NameBinding Binding)
{
    /// <summary>The id of the class the first segment names.</summary>
    public string FirstId => Path[0];
}
