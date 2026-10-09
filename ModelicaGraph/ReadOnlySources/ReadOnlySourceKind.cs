namespace ModelicaGraph;

/// <summary>
/// What a read-only library's classes are. The order is their precedence: for the same library,
/// readable source beats <see cref="Supplied"/>, which beats <see cref="RecoveredFromDocumentation"/>
/// (<see cref="ReadOnlySources.Precedence"/>).
/// </summary>
public enum ReadOnlySourceKind
{
    /// <summary>
    /// Rebuilt from the vendor's documentation: names, descriptions, base classes, whether there is
    /// an icon, and member lists with no types. Each class keeps the record it was rebuilt from in
    /// <see cref="DataTypes.ModelNode.RecoveredFromDocumentation"/>.
    /// </summary>
    RecoveredFromDocumentation,

    /// <summary>
    /// Modelica text a host supplies — declarations, connectors, graphics and whatever equations the
    /// vendor makes visible — parsed like any file but held only in memory.
    /// </summary>
    Supplied
}
