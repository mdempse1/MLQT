using System.IO;
using MLQT.TestSupport;
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
        var dirs = new List<string> { TestPaths.Rooted("Projects", "MyLib", "Resources") };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(TestPaths.Rooted("Projects", "MyLib", "Resources"), result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_SameDirectory_ReturnsThatDirectory()
    {
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "MyLib", "Resources"),
            TestPaths.Rooted("Projects", "MyLib", "Resources")
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(TestPaths.Rooted("Projects", "MyLib", "Resources"), result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_SharedParent_ReturnsCommonParent()
    {
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "MyLib", "Resources", "Data"),
            TestPaths.Rooted("Projects", "MyLib", "Resources", "Images")
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(TestPaths.Rooted("Projects", "MyLib", "Resources"), result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_DeeperSharedParent_ReturnsCommonParent()
    {
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "LibA", "Resources"),
            TestPaths.Rooted("Projects", "LibB", "Resources")
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(TestPaths.Rooted("Projects"), result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_OnlyDriveInCommon_ReturnsDriveRoot()
    {
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "LibA", "Resources"),
            TestPaths.Rooted("Other", "LibB", "Resources")
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        // Should return "C:\" not just "C:" (which would be a relative path on Windows)
        Assert.Equal(TestPaths.Root, result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_NothingInCommon_ReturnsEmpty()
    {
        // The one case TestPaths cannot express, because the two platforms reach it differently:
        // on Windows nothing-in-common means two volumes, and on Linux any two absolute paths share
        // the root, so it means two paths whose first segment differs. Both must return "".
        var dirs = OperatingSystem.IsWindows()
            ? new List<string> { TestPaths.Rooted("Projects", "LibA"), "D:" + Path.DirectorySeparatorChar + "Other" }
            : new List<string> { TestPaths.Rooted("Projects", "LibA"), TestPaths.Rooted("Other", "LibB") };

        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);

        Assert.Equal("", result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_ThreeDirectories_ReturnsCommonRoot()
    {
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "LibA", "Resources"),
            TestPaths.Rooted("Projects", "LibB", "Resources"),
            TestPaths.Rooted("Projects", "LibC", "Data")
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(TestPaths.Rooted("Projects"), result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_NestedDirectories_ReturnsShallowCommon()
    {
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "MyLib", "Resources", "Data", "SubDir1"),
            TestPaths.Rooted("Projects", "MyLib", "Resources", "Images")
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal(TestPaths.Rooted("Projects", "MyLib", "Resources"), result);
    }

    [Fact]
    public void FindCommonDirectoryRoot_MixedAbsoluteAndRelative_ReturnsEmpty()
    {
        // This is the scenario that was causing the empty tree:
        // resolved paths (absolute) mixed with unresolved paths (relative)
        var dirs = new List<string>
        {
            TestPaths.Rooted("Projects", "MyLib", "Resources"),
            @"Modelica\Resources\Data"
        };
        var result = ResourceTreeHelper.FindCommonDirectoryRoot(dirs);
        Assert.Equal("", result);
    }
}
