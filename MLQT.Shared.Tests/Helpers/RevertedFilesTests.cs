using MLQT.Shared.Helpers;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// <see cref="RevertedFiles.ToReload"/>: which reverted files the library browser hands the graph in
/// its one batch reload.
/// </summary>
public class RevertedFilesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-revert", "wc");

    [Fact]
    public void ModelicaFilesAndPackageOrders_AreReloaded_AsFullPaths()
    {
        var reload = RevertedFiles.ToReload(["Lib/A.mo", "Lib/package.order", "Lib/Resources/data.txt"], Root);

        Assert.Equal(
            [Path.GetFullPath(Path.Combine(Root, "Lib/A.mo")), Path.GetFullPath(Path.Combine(Root, "Lib/package.order"))],
            reload);
    }

    [Fact]
    public void AFileListedTwice_IsReloadedOnce() =>
        Assert.Single(RevertedFiles.ToReload(["Lib/A.mo", "Lib/A.mo", "./Lib/A.mo"], Root));

    [Fact]
    public void NothingTheGraphHolds_NothingToReload() =>
        Assert.Empty(RevertedFiles.ToReload(["Lib/Resources/data.txt", "README.md"], Root));
}
