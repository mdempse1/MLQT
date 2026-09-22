using ModelicaGraph.DataTypes;
using ModelicaParser;

namespace ModelicaGraph;

/// <summary>
/// <b>The only answer to "which of a package's children can have their own file?"</b> — asked by
/// <c>ModelicaPackageSaver</c>, which writes them, and by <c>SingleFilePackageAnalyzer</c>, which
/// reports the ones that could and do not.
///
/// <para>Here because the two had a copy each, and the copies were the same wrong answer: both
/// compared <i>class names</i> case-insensitively and refused a pair that differed only in case.
/// <b>The filesystem is coarser than that.</b> A package is written as a directory
/// <c>Jfet</c> and everything else as a file <c>JFET.mo</c>, and on the most case-insensitive
/// filesystem there is, those two entries cannot collide. So MSL's four such pairs — MOS/Mos,
/// MOS2/Mos2, DIODE/Diode, JFET/Jfet — stayed written into their <c>package.mo</c>, and the rule
/// that would have reported them deliberately said nothing, because the fix it offered would have
/// moved nothing (B245).</para>
///
/// <para>The comparison is therefore on the <b>directory entry</b> each child would be written as,
/// which is also what makes the reserved-name question right: <c>package.mo</c> is taken, so a class
/// called <c>Package</c> may not have a file — but a <i>package</i> called <c>Package</c> becomes a
/// directory and may.</para>
/// </summary>
public static class PackageFileLayout
{
    /// <summary>
    /// The entries a package directory holds whatever its children are, and which therefore no child
    /// may be written over.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedEntries = new[] { "package.mo", "package.order" };

    /// <summary>
    /// Whether this child would be written as a directory of its own rather than as a single file.
    /// A package is, unless it is a short class definition (<c>package Foo = Bar;</c>), which has no
    /// contents to put in a directory and is written as a file like anything else.
    /// </summary>
    public static bool WrittenAsDirectory(ModelNode child, Func<ModelNode, bool>? isShortClass = null)
        => child.ClassType == "package" && !(isShortClass ?? IsShortClassDefinition)(child);

    /// <summary>The directory entry <paramref name="child"/> would be written as.</summary>
    public static string EntryFor(ModelNode child, Func<ModelNode, bool>? isShortClass = null)
        => WrittenAsDirectory(child, isShortClass)
            ? child.Definition.Name
            : child.Definition.Name + ".mo";

    /// <summary>
    /// The names among <paramref name="children"/> that may be stored as their own entry: those that
    /// the language allows to stand alone (no <c>replaceable</c>, <c>redeclare</c>, <c>inner</c> or
    /// <c>outer</c> prefix), whose entry no sibling's entry collides with, and whose entry is not one
    /// the package directory already owns.
    ///
    /// <para><paramref name="isShortClass"/> is a way in for a caller that has already worked the
    /// answer out — the saver holds every parse tree at this point and pre-computes it. Left out, it
    /// is asked of the class, and only ever for siblings whose bare names collide: two children with
    /// different names cannot produce the same entry, so nothing else needs the question put.</para>
    /// </summary>
    public static HashSet<string> StandaloneChildNames(
        IEnumerable<ModelNode> children, Func<ModelNode, bool>? isShortClass = null)
    {
        var result = new HashSet<string>();

        var byBareName = children
            .Where(c => c.CanBeStoredStandalone)
            .GroupBy(c => c.Definition.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byBareName)
        {
            // An entry is either 'Name' or 'Name.mo', so two children whose bare names differ
            // cannot collide however each is written, and a bare name that is neither 'package' nor
            // 'package.order' cannot land on a reserved entry either. Only where one of those is in
            // question does it matter which form a child takes — and only there is the class asked,
            // which is what keeps this off the parser for a library of tens of thousands.
            if (group.Count() == 1 && !MayTakeAReservedEntry(group.Key))
            {
                result.Add(group.First().Definition.Name);
                continue;
            }

            var entries = group.ToDictionary(c => c.Id, c => EntryFor(c, isShortClass), StringComparer.Ordinal);
            foreach (var child in group)
            {
                var entry = entries[child.Id];
                var collidesWithSibling = entries.Values
                    .Count(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase)) > 1;
                var collidesWithReserved = ReservedEntries.Contains(entry, StringComparer.OrdinalIgnoreCase);

                if (!collidesWithSibling && !collidesWithReserved)
                    result.Add(child.Definition.Name);
            }
        }

        return result;
    }

    /// <summary>Whether either form of a child called <paramref name="name"/> is a reserved entry.</summary>
    private static bool MayTakeAReservedEntry(string name)
        => ReservedEntries.Any(r =>
            r.Equals(name, StringComparison.OrdinalIgnoreCase)
            || r.Equals(name + ".mo", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the class is a short class definition — <c>package Foo = Bar;</c>. Borrowed rather
    /// than parsed outright, so a caller that already holds the tree does not pay for it twice and a
    /// caller that does not is not left holding one.
    /// </summary>
    public static bool IsShortClassDefinition(ModelNode model)
        => model.Definition.Borrow(tree => tree.class_definition()
            .Any(d => d.class_specifier()?.short_class_specifier() is not null));
}
