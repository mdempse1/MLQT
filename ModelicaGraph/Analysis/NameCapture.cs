using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Whether a class's new name would mean something else where a reference uses it - "captured" by an
/// element of that name met first on the way out. Asked before a rename rewrites the reference.
/// </summary>
/// <remarks>
/// <para>Renaming <c>Root.Src.Pkg</c> to <c>Box</c> rewrites <c>Pkg.State</c> in
/// <c>Root.Src.Other</c> as <c>Box.State</c>. If Other declares a <c>Box</c> of its own, that is
/// what <c>Box</c> now means there: the file parses, the rename reports success, and the
/// reference names a different class.</para>
/// <para><b>The lookup is walked as Modelica walks it</b> (MLS §5.3.1) - each scope's own elements,
/// then the ones it inherits, then its imports, from the reference outward - and stops at whichever
/// comes first: the place the renamed class would be found (no capture), or another element of the
/// new name (captured). Something of that name further out than the renamed class captures
/// nothing, which is why a top-level <c>Box</c> never blocks a rename.</para>
/// <para><b>It errs towards saying "captured"</b> where it cannot tell: an import of the new name in
/// a scope the renamed class is found before is reported. A refusal costs another choice of name;
/// a missed capture costs a model that silently means something else.</para>
/// </remarks>
public static class NameCapture
{
    /// <summary>
    /// What <paramref name="newName"/> would mean at <paramref name="scopeId"/> instead of the renamed
    /// class <paramref name="renamedId"/> - an id, or "component x of Y" - or null when the lookup
    /// reaches the renamed class first.
    /// </summary>
    /// <param name="interfaces">Kept for the run, as for <see cref="ClassElementResolver.Collect"/>.</param>
    public static string? CapturedBy(
        DirectedGraph graph, string scopeId, string newName, string renamedId,
        ClassElementResolver.InterfaceCache? interfaces = null)
    {
        var parent = renamedId.Contains('.') ? renamedId[..renamedId.LastIndexOf('.')] : string.Empty;
        var oldName = renamedId[(renamedId.LastIndexOf('.') + 1)..];

        var parts = scopeId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var take = parts.Length; take > 0; take--)
        {
            var scopeIdHere = string.Join('.', parts.Take(take));
            if (graph.GetNode<ModelNode>(scopeIdHere) is not { } scope)
                continue;

            // 1. The scope's elements, its own and inherited ones alike: anything already called the
            //    new name - in the renamed class's own parent, that is a clash with the class itself.
            var element = Element(graph, scope, newName, interfaces)
                          ?? graph.GetNode<ModelNode>($"{scopeIdHere}.{newName}")?.Id;
            var bases = ClassElementResolver.BaseClasses(graph, scope);
            element ??= bases.Select(b => graph.GetNode<ModelNode>($"{b.Id}.{newName}")?.Id).FirstOrDefault(id => id is not null);
            if (element is not null)
                return element;

            // 2. The renamed class: here, if this is its parent or extends its parent.
            if (scopeIdHere == parent || bases.Any(b => b.Id == parent))
                return null;

            // 3. Its imports: one that brings in the renamed class is where it is found - its own
            //    clause is renamed with it - and one that brings in the new name is a capture.
            foreach (var import in ClassImports.For(scope.Definition))
            {
                if (TypeResolver.ResolveViaImport(graph, import, oldName)?.Id == renamedId)
                    return null;
                if (TypeResolver.ResolveViaImport(graph, import, newName) is { } imported)
                    return imported.Id;
            }

            if (ClassImports.IsEncapsulated(scope.Definition))
                return null;
        }

        // The root: the renamed class, if it is a top-level one; otherwise it was never reached this
        // way, and the rename does not change what this reference finds.
        return null;
    }

    // An element of the class called `name`, declared or inherited - a component, or a nested class
    // its interface lists - described for a message, or null.
    private static string? Element(
        DirectedGraph graph, ModelNode scope, string name, ClassElementResolver.InterfaceCache? interfaces)
    {
        var element = ClassElementResolver
            .Collect(graph, scope, includeProtected: true, includeInherited: true, interfaces)
            .FirstOrDefault(e => e.Element.Kind is ClassElementKind.Component or ClassElementKind.Class
                                 && string.Equals(e.Element.Name, name, StringComparison.Ordinal));
        return element is null
            ? null
            : element.Element.Kind == ClassElementKind.Component
                ? $"component {name} of {element.OwnerId}"
                : $"{element.OwnerId}.{name}";
    }
}
