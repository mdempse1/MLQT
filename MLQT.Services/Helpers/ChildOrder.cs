using ModelicaGraph.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// The order a package's children are shown in: its <c>package.order</c>, else the order they are written in
/// its source, with any child neither names after them. One rule, so the library browser and the MCP
/// server's package tree list a package the same way.
/// </summary>
public static class ChildOrder
{
    /// <summary>
    /// <paramref name="children"/> in <paramref name="parent"/>'s order. With neither a <c>package.order</c>
    /// nor a source order, they are returned as given.
    /// </summary>
    public static List<ModelNode> Sort(IEnumerable<ModelNode> children, ModelNode parent)
    {
        var childList = children.ToList();
        var order = parent.PackageOrder ?? parent.NestedChildrenOrder;
        if (order == null)
            return childList;

        var byName = new Dictionary<string, ModelNode>();
        foreach (var child in childList)
            byName.TryAdd(child.Name, child);

        var sorted = new List<ModelNode>(childList.Count);
        foreach (var name in order)
        {
            if (byName.Remove(name, out var child))
                sorted.Add(child);
        }

        // Any child the order does not name, after those it does.
        sorted.AddRange(byName.Values);
        return sorted;
    }
}
