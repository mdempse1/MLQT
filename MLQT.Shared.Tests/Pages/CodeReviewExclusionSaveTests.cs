using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Pages;
using ModelicaGraph;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B380: re-including a class from Code Review is the user's own change to the repository's
/// settings, so it is saved as an explicit Apply - which records the default-on rules (B244) -
/// rather than as the plain save a load or reorder makes (B310). And only when the class was in
/// the name list at all: otherwise nothing changed, and recording the defaults would leave a
/// modified settings file nobody asked for.
/// </summary>
public class CodeReviewExclusionSaveTests
{
    private static Repository Repo(params string[] excluded) => new()
    {
        Id = "repo",
        Name = "repo",
        StyleSettings = new StyleCheckingSettings { FormattingExcludedModels = [.. excluded] },
    };

    [Fact]
    public async Task RemovingAListedClassSavesAsAnExplicitApply()
    {
        var repository = Repo("P.M", "P.N");
        var service = new Mock<IRepositoryService>();

        await CodeReview.RemoveExcludedModelEntryAsync(service.Object, repository, "P.M");

        Assert.Equal(["P.N"], repository.StyleSettings!.FormattingExcludedModels);
        service.Verify(s => s.ApplyRepositorySettingsAsync("repo"), Times.Once);
        service.Verify(s => s.SaveRepositorySettingsAsync(), Times.Never);
    }

    [Fact]
    public async Task AClassNotInTheListWritesNothing()
    {
        // Excluded by the annotation alone, which is what the button writes since B175.
        var repository = Repo("P.N");
        var service = new Mock<IRepositoryService>();

        await CodeReview.RemoveExcludedModelEntryAsync(service.Object, repository, "P.M");

        Assert.Equal(["P.N"], repository.StyleSettings!.FormattingExcludedModels);
        service.Verify(s => s.ApplyRepositorySettingsAsync(It.IsAny<string>()), Times.Never);
        service.Verify(s => s.SaveRepositorySettingsAsync(), Times.Never);
    }
}
