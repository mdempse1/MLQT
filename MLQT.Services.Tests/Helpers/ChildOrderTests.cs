using MLQT.Services.Helpers;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="ChildOrder"/>, lifted out of <c>LibraryDataService</c> so the library browser and the MCP
/// server's package tree order a package's children by one rule.
/// </summary>
public class ChildOrderTests
{
    private static ModelNode Node(string name) => new($"P.{name}", name);

    private static readonly ModelNode[] Children = [Node("A"), Node("B"), Node("C")];

    [Fact]
    public void PackageOrder_Wins_AndChildrenItDoesNotName_ComeAfter()
    {
        var parent = new ModelNode("P", "P") { PackageOrder = ["C", "A", "Gone"], NestedChildrenOrder = ["A", "B", "C"] };

        Assert.Equal(["C", "A", "B"], ChildOrder.Sort(Children, parent).Select(c => c.Name));
    }

    [Fact]
    public void WithoutAPackageOrder_TheSourceOrderIsUsed()
    {
        var parent = new ModelNode("P", "P") { NestedChildrenOrder = ["B", "C", "A"] };

        Assert.Equal(["B", "C", "A"], ChildOrder.Sort(Children, parent).Select(c => c.Name));
    }

    [Fact]
    public void WithNeitherOrder_TheChildrenAreReturnedAsGiven()
    {
        Assert.Equal(["A", "B", "C"], ChildOrder.Sort(Children, new ModelNode("P", "P")).Select(c => c.Name));
    }
}
