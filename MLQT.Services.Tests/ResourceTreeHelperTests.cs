using MLQT.Services.Helpers;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the ResourceTreeHelper class.
/// </summary>
public class ResourceTreeHelperTests
{
    [Fact]
    public void FindCommonDirectoryRoot_EmptyList_ReturnsEmpty()
    {
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(new List<string>());
        Assert.Equal("", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_SingleDirectory_ReturnsThatDirectory()
    {
        var dirs = new List<string> { @"C:\Projects\MyLib\Resources" };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(@"C:\Projects\MyLib\Resources", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_SameDirectory_ReturnsThatDirectory()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\MyLib\Resources",
            @"C:\Projects\MyLib\Resources"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(@"C:\Projects\MyLib\Resources", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_SharedParent_ReturnsCommonParent()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\MyLib\Resources\Data",
            @"C:\Projects\MyLib\Resources\Images"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(@"C:\Projects\MyLib\Resources", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_DeeperSharedParent_ReturnsCommonParent()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\LibA\Resources",
            @"C:\Projects\LibB\Resources"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(@"C:\Projects", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_OnlyDriveInCommon_ReturnsDriveRoot()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\LibA\Resources",
            @"C:\Other\LibB\Resources"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        // Should return "C:\" not just "C:" (which would be a relative path on Windows)
        Assert.Equal(@"C:\", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_NothingInCommon_ReturnsEmpty()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\LibA",
            @"D:\Other\LibB"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal("", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_ThreeDirectories_ReturnsCommonRoot()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\LibA\Resources",
            @"C:\Projects\LibB\Resources",
            @"C:\Projects\LibC\Data"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(@"C:\Projects", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_NestedDirectories_ReturnsShallowCommon()
    {
        var dirs = new List<string>
        {
            @"C:\Projects\MyLib\Resources\Data\SubDir1",
            @"C:\Projects\MyLib\Resources\Images"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(@"C:\Projects\MyLib\Resources", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_MixedAbsoluteAndRelative_ReturnsEmpty()
    {
        // This is the scenario that was causing the empty tree:
        // resolved paths (absolute) mixed with unresolved paths (relative)
        var dirs = new List<string>
        {
            @"C:\Projects\MyLib\Resources",
            @"Modelica\Resources\Data"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal("", result);
    }
}
