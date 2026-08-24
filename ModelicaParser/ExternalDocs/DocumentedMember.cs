namespace ModelicaParser.ExternalDocs;

/// <summary>
/// One named member of a documented class — a parameter, connector, function input or
/// function output — as listed in a generated documentation table.
///
/// Only the name and description survive documentation generation; the declared type is not
/// emitted, so this cannot stand in for a parsed declaration. It is enough to detect an
/// inherited member being shadowed, and to report a unit that is present or missing.
/// </summary>
/// <param name="Name">The member name exactly as listed, which may carry array dimensions
/// (e.g. <c>a_in[:]</c>) because the generator prints the declaration's subscripts.</param>
/// <param name="Description">The member's description string, entity-decoded, without the
/// trailing unit the generator appends. Null when the table cell was empty.</param>
/// <param name="Unit">The unit the generator appended in square brackets (e.g. <c>K</c> from
/// <c>Temperature threshold [K]</c>), or null when none was shown.</param>
public sealed record DocumentedMember(string Name, string? Description, string? Unit);
