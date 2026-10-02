namespace ModelicaParser.DataTypes;

/// <summary>The kind of a structural element extracted from a Modelica class.</summary>
public enum ClassElementKind
{
    /// <summary>A component/variable/parameter/connector declaration.</summary>
    Component,
    /// <summary>An <c>extends</c> (inheritance) clause.</summary>
    Extends,
    /// <summary>An <c>import</c> clause.</summary>
    Import,
    /// <summary>A nested class definition (listed but not recursed into).</summary>
    Class
}

/// <summary>
/// One structural element of a Modelica class, produced by
/// <see cref="Visitors.ClassInterfaceExtractor"/>. Purely syntactic: it reports what is written in the
/// class (name, declared type text, prefixes, description) and performs no cross-class resolution
/// (e.g. it does not know whether a component's type is a connector — that needs the graph).
/// </summary>
public sealed record ClassElement
{
    /// <summary>What kind of element this is.</summary>
    public required ClassElementKind Kind { get; init; }

    /// <summary>Component name; nested class name; extends base type; or the import statement text.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Declared type text: a component's type, an extends clause's base, or the base a nested short
    /// class names (<c>type Torque = Real(unit = "N.m")</c> gives <c>Real</c>). Null for imports, and for
    /// a nested long class, enumeration or <c>der</c> class, which name no base.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// The element's array dimensions as written, in the order the language reads them, or null for a
    /// scalar: <c>Real x[3]</c> gives <c>[3]</c>, and <c>Real[2] x[3]</c> - whose type's dimensions come
    /// after the declaration's (MLS §10.1) - gives <c>[3, 2]</c>. Components, and a nested short class
    /// (<c>type Vector3 = Real[3]</c>). <see cref="Type"/> never carries them.
    /// </summary>
    public string? ArraySubscripts { get; init; }

    /// <summary>parameter | constant | discrete, or null for a plain (continuous) variable. Components only.</summary>
    public string? Variability { get; init; }

    /// <summary>input | output, or null (acausal). Components only.</summary>
    public string? Causality { get; init; }

    /// <summary>flow | stream, or null. Components only.</summary>
    public string? Connection { get; init; }

    /// <summary>True if the element is in a public section, false if in a protected section.</summary>
    public bool IsPublic { get; init; } = true;

    /// <summary>
    /// The value the component defaults to (e.g. "5"), without its <c>=</c>/<c>:=</c>, or null when it
    /// has no binding. Components only. A type modification written alongside the binding is reported
    /// separately in <see cref="TypeModification"/>, not folded in here.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// The modification applied to the type, as written (e.g. "(min = 0)" or "(k = 1, T = 2)"), or
    /// null when there is none. For a component it sets attributes on the type or configures a
    /// sub-component; it is not a value the component takes, which is why it is kept apart from
    /// <see cref="DefaultValue"/> — a declaration can carry both. For a nested short class it is what
    /// the class applies to its base: <c>(unit = "N.m")</c> in <c>type Torque = Real(unit = "N.m")</c>.
    /// </summary>
    public string? TypeModification { get; init; }

    /// <summary>
    /// The expression a conditional component is declared with — <c>heatPort if useHeatPort</c>
    /// yields <c>"useHeatPort"</c> — or null for an ordinary one. Components only.
    ///
    /// <para><b>A component whose condition is false does not exist</b>: it takes part in no
    /// connection and a tool does not draw it. The condition is almost always a boolean parameter of
    /// the same class, so what it comes to depends on the modification the instance was given, which
    /// is why this is kept as written rather than as an answer.</para>
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>Description string from the trailing comment. Components and nested classes.</summary>
    public string? Description { get; init; }

    /// <summary>Class kind (model/block/package/...) for a nested-class element.</summary>
    public string? ClassType { get; init; }

    /// <summary>Element prefixes present: replaceable, redeclare, final, inner, outer.</summary>
    public IReadOnlyList<string> Prefixes { get; init; } = Array.Empty<string>();

    /// <summary>The // and /* */ comments written immediately before this element (in source order).</summary>
    public IReadOnlyList<string> LeadingComments { get; init; } = Array.Empty<string>();

    /// <summary>1-based source line of the element within the parsed class code.</summary>
    public int Line { get; init; }

    /// <summary>
    /// The scalar modifications this element applies, or null when there are none. A nested
    /// modification is keyed by the path it reaches, as the dotted spelling of it would be:
    /// <c>flange_a(phi = 0)</c> and <c>flange_a.phi = 0</c> both give {"flange_a.phi" =&gt; "0"}.
    /// What is not a value - a modification with a class modification and no binding, or a
    /// redeclaration, which is in <see cref="Redeclarations"/> - is omitted. An attribute set inside a
    /// nested modification is a value: <c>Inertia i(J(unit = "kg.m2"))</c> gives
    /// {"J.unit" =&gt; "\"kg.m2\""}.
    ///
    /// <para>For an <see cref="ClassElementKind.Extends"/> element these are what the base class was
    /// given: <c>extends Base(k = 5)</c> yields {"k" =&gt; "5"}, and they override inherited
    /// defaults. For a <see cref="ClassElementKind.Component"/> they are what this instance was
    /// given: <c>Inertia inertia1(J = 1)</c> yields {"J" =&gt; "1"}, which is what decides what the
    /// component's own icon says and whether its conditional connectors are there at all.</para>
    ///
    /// <para>For a nested short class they are what it applies to its base, as for
    /// <see cref="ClassInterface.ShortClassModifications"/>.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? Modifications { get; init; }

    /// <summary>
    /// The redeclarations this element's modification makes, or null when it makes none, keyed by the
    /// path to the element replaced as <see cref="Modifications"/> are:
    /// <c>Pipe pipe(redeclare package Medium = Water)</c> gives {"Medium" =&gt; package Water}, and
    /// <c>Holder h(b(redeclare package Medium = Water))</c> gives {"b.Medium" =&gt; ...}. A
    /// <c>replaceable</c> written in a modification replaces the element too and is listed here.
    /// Components, extends clauses and nested short classes.
    ///
    /// <para>What an instance is given decides what its replaceable elements are: <c>Medium.T</c>
    /// inside a pipe means the redeclared medium's <c>T</c>, not the constraining one's.</para>
    /// </summary>
    public IReadOnlyDictionary<string, Redeclaration>? Redeclarations { get; init; }
}

/// <summary>
/// One redeclaration in a modification: <c>redeclare package Medium = Water</c> or
/// <c>redeclare Real x(unit = "m")</c>. Purely syntactic, as <see cref="ClassElement"/> is.
/// </summary>
public sealed record Redeclaration
{
    /// <summary>The name of the element replaced (<c>Medium</c>), without the path to it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Class kind (package/model/record/...) when a class is redeclared, or null when a component is.
    /// </summary>
    public string? ClassType { get; init; }

    /// <summary>
    /// The replacing type as written - <c>Water</c> above, <c>Real</c> for the component - or null for
    /// a redeclared enumeration, which names none.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>The modification the replacement applies to its type, as written, or null.</summary>
    public string? TypeModification { get; init; }

    /// <summary>The replacement's array dimensions as written, or null.</summary>
    public string? ArraySubscripts { get; init; }

    /// <summary>The prefixes written on the redeclaration: each, final, replaceable.</summary>
    public IReadOnlyList<string> Prefixes { get; init; } = Array.Empty<string>();

    /// <summary>1-based source line of the redeclaration within the parsed class code.</summary>
    public int Line { get; init; }
}

/// <summary>
/// The structural interface of a single Modelica class: its own description plus the flat list of its
/// outermost-level elements. Nested classes appear as <see cref="ClassElementKind.Class"/> entries but
/// are not recursed into (each nested class has its own <c>ModelNode</c> and can be queried directly).
/// </summary>
public sealed record ClassInterface
{
    /// <summary>The class's own description string (the quoted comment after its name), if any.</summary>
    public string? Description { get; init; }

    /// <summary>The class's elements in source order.</summary>
    public IReadOnlyList<ClassElement> Elements { get; init; } = Array.Empty<ClassElement>();

    /// <summary>
    /// The base a short class definition names, as written - <c>model R2 = Resistor(R = 2)</c> gives
    /// <c>Resistor</c> - or null for a long class, a <c>der</c> class or an enumeration. A short class
    /// has no <see cref="Elements"/> of its own: its members are its base's.
    /// </summary>
    public string? ShortClassBase { get; init; }

    /// <summary>
    /// The scalar modifications a short class definition applies to its base (<c>{"R" =&gt; "2"}</c>
    /// above), or null when there are none. They override the base's defaults, as an
    /// <c>extends</c> clause's do, and are keyed as <see cref="ClassElement.Modifications"/> are.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ShortClassModifications { get; init; }

    /// <summary>
    /// The redeclarations a short class definition makes in its base -
    /// <c>model WaterPipe = Pipe(redeclare package Medium = Water)</c> - keyed as
    /// <see cref="ClassElement.Redeclarations"/> are, or null when there are none.
    /// </summary>
    public IReadOnlyDictionary<string, Redeclaration>? ShortClassRedeclarations { get; init; }

    /// <summary>
    /// For a class extends - <c>redeclare record extends ThermodynamicState ... end ThermodynamicState</c>
    /// - the name of the inherited class it extends, which is its own name; null for any other class.
    /// The base is not looked up as a name is: it is the element of that name the <b>enclosing</b>
    /// class inherits (MLS §7.3.1), which the class replaces and adds to.
    /// </summary>
    public string? ClassExtendsBase { get; init; }

    /// <summary>The scalar modifications a class extends applies to that base, or null.</summary>
    public IReadOnlyDictionary<string, string>? ClassExtendsModifications { get; init; }
}
