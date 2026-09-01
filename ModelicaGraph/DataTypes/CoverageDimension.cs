namespace ModelicaGraph.DataTypes;

/// <summary>
/// The measurable coverage dimensions. A flags enum because a class records, in one value each, what
/// was measured for it and what it failed — cheaper than a set per class when a library has tens of
/// thousands of them.
///
/// <para>Which dimensions a report actually shows is decided per repository from its rule settings
/// (see <c>CoverageDimensions.TrackedFor</c>): a rule switched off is a decision that its gap is not
/// worth tracking, and a gap the formatter closes on every save is not debt to report.</para>
/// </summary>
[Flags]
public enum CoverageDimension
{
    None = 0,

    // Documentation and interface quality: measured from the class's own structure.
    ClassDescription = 1 << 0,
    DocumentationInfo = 1 << 1,
    DocumentationRevisions = 1 << 2,
    Icon = 1 << 3,
    ParameterDescription = 1 << 4,
    ConstantDescription = 1 << 5,
    Unit = 1 << 6,

    // Layout: each is pass/fail for the class as a whole, measured by running the rule's own visitor
    // so a waived or baselined finding cannot hide the gap.
    ImportsFirst = 1 << 7,
    ExtendsAtTop = 1 << 8,
    OneOfEachSection = 1 << 9,
    InitialSectionsFirst = 1 << 10,
    InitialSectionsLast = 1 << 11,
    EquationAlgorithmNotMixed = 1 << 12,
    ConnectionsNotMixed = 1 << 13,

    /// <summary>The layout dimensions, which the formatter can rewrite and which are therefore
    /// skipped for a class excluded from formatting — the style checker skips them there too.</summary>
    Layout = ImportsFirst | ExtendsAtTop | OneOfEachSection | InitialSectionsFirst
             | InitialSectionsLast | EquationAlgorithmNotMixed | ConnectionsNotMixed,

    All = ClassDescription | DocumentationInfo | DocumentationRevisions | Icon | ParameterDescription
          | ConstantDescription | Unit | Layout
}
