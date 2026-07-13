namespace ModelicaParser.DataTypes;

/// <summary>A component declared in a class body, with the spans needed to modify or remove it.</summary>
public sealed record ClassBodyComponent(
    string Name,
    string TypeText,
    int DeclStart,     // component_declaration span (name + subscripts + modification)
    int DeclStop,
    int ClauseStart,   // enclosing component_clause span (prefix + type + all declarations)
    int ClauseStop,
    bool SoleInClause,      // true when the clause declares only this component
    int? ModStart,          // modification span, or null if the component has none
    int? ModStop,
    int BindingInsertOffset); // where to insert a modifier when the component has none (after name/subscripts)

/// <summary>A connect(a, b) equation in a class body.</summary>
public sealed record ClassBodyConnection(
    string PortA,
    string PortB,
    int Start,
    int Stop);

/// <summary>
/// The structural layout of a class body (from <see cref="Visitors.ClassBodyLocator"/>): the character
/// offsets a surgical edit needs — where to append a public element, an equation or a statement, where
/// the class closes — plus its components and connections. All offsets are into the exact class source
/// that was analysed.
/// </summary>
public sealed record ClassBodyLayout(
    bool Found,
    int PublicAppendOffset,       // insert a new element (component/import/extends) here
    int? FirstPublicElementOffset,// start of the first public element (for top inserts like extends/import)
    int? ProtectedAppendOffset,   // append to the protected section here, or null if there is none
    int? EquationAppendOffset,    // append an equation here, or null if there is no equation section
    int? AlgorithmAppendOffset,   // append a statement here, or null if there is no algorithm section
    int BodyEndOffset,            // offset of the class's closing 'end' (create new sections before it)
    string Indent,                // the body's detected indentation, for inserted lines
    IReadOnlyList<ClassBodyComponent> Components,
    IReadOnlyList<ClassBodyConnection> Connections)
{
    public static ClassBodyLayout NotFound { get; } =
        new(false, 0, null, null, null, null, 0, "  ", Array.Empty<ClassBodyComponent>(), Array.Empty<ClassBodyConnection>());
}
