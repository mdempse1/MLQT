using MLQT.Services;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// That the order repositories are shown in is the user's, and that it survives a restart (B188).
/// </summary>
/// <remarks>
/// <para>There is no sort key anywhere: the list order <i>is</i> the order, because the library
/// browser renders <c>Repositories</c> straight through and the saved settings are written from the
/// same list. So these tests assert on the two things that order passes through — the live list and
/// the settings entries — rather than on a property that would have to be kept in step with them.</para>
/// </remarks>
public class RepositoryOrderTests : IDisposable
{
    private readonly string _root;
    private readonly InMemorySettingsService _settings = new();
    private readonly RepositoryService _service;

    public RepositoryOrderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b188-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _service = new RepositoryService(new LibraryDataService(), _settings, new FileMonitoringService());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A minimal one-library repository on disk, named so the order is readable.</summary>
    private async Task<string> AddRepositoryAsync(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "package.mo"), $$"""
            within ;
            package {{name}} "test library"

              model Class1 "test class"
                Real x;
              end Class1;

            end {{name}};
            """);
        File.WriteAllText(Path.Combine(path, "package.order"), "Class1\r\n");

        var result = await _service.AddRepositoryAsync(path, name: name, startMonitoring: false);
        Assert.True(result.Success, result.ErrorMessage);
        return result.Repository!.Id;
    }

    private string[] Names() => _service.Repositories.Select(r => r.Name).ToArray();

    private async Task AddThreeAsync() =>
        _ = new[] { await AddRepositoryAsync("Alpha"), await AddRepositoryAsync("Beta"), await AddRepositoryAsync("Gamma") };

    [Fact]
    public async Task RepositoriesStartInTheOrderTheyWereAdded()
    {
        await AddThreeAsync();

        Assert.Equal(["Alpha", "Beta", "Gamma"], Names());
    }

    [Fact]
    public async Task MovingUpSwapsWithThePrecedingRepository()
    {
        await AddThreeAsync();
        var gamma = _service.Repositories[2].Id;

        Assert.True(_service.MoveRepository(gamma, -1));

        Assert.Equal(["Alpha", "Gamma", "Beta"], Names());
    }

    [Fact]
    public async Task MovingDownSwapsWithTheFollowingRepository()
    {
        await AddThreeAsync();
        var alpha = _service.Repositories[0].Id;

        Assert.True(_service.MoveRepository(alpha, 1));

        Assert.Equal(["Beta", "Alpha", "Gamma"], Names());
    }

    /// <summary>
    /// A delta larger than one place moves that far, rather than one place — the buttons only ever
    /// ask for one, but nothing in the contract says they are the only caller.
    /// </summary>
    [Fact]
    public async Task ARepositoryCanMoveMoreThanOnePlace()
    {
        await AddThreeAsync();
        var alpha = _service.Repositories[0].Id;

        Assert.True(_service.MoveRepository(alpha, 2));

        Assert.Equal(["Beta", "Gamma", "Alpha"], Names());
    }

    /// <summary>
    /// Off either end is refused rather than clamped: the caller's buttons are disabled there, so a
    /// call that arrives is a mistake, and "moved" and "did nothing" have to be distinguishable.
    /// </summary>
    [Theory]
    [InlineData(0, -1)]
    [InlineData(2, 1)]
    [InlineData(0, -5)]
    [InlineData(2, 5)]
    public async Task MovingPastTheEndIsRefusedAndChangesNothing(int index, int delta)
    {
        await AddThreeAsync();

        Assert.False(_service.MoveRepository(_service.Repositories[index].Id, delta));

        Assert.Equal(["Alpha", "Beta", "Gamma"], Names());
    }

    [Fact]
    public async Task MovingNowhereIsRefused()
    {
        await AddThreeAsync();

        Assert.False(_service.MoveRepository(_service.Repositories[0].Id, 0));
    }

    [Fact]
    public async Task AnUnknownRepositoryIsRefused()
    {
        await AddThreeAsync();

        Assert.False(_service.MoveRepository("not-a-repository", -1));

        Assert.Equal(["Alpha", "Beta", "Gamma"], Names());
    }

    [Fact]
    public async Task MovingARepositoryRaisesOnRepositoriesChanged()
    {
        await AddThreeAsync();
        var raised = 0;
        _service.OnRepositoriesChanged += () => raised++;

        _service.MoveRepository(_service.Repositories[0].Id, 1);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ARefusedMoveRaisesNothing()
    {
        await AddThreeAsync();
        var raised = 0;
        _service.OnRepositoriesChanged += () => raised++;

        _service.MoveRepository(_service.Repositories[0].Id, -1);

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// The point of the feature: the order the user chose is what the next session starts with.
    /// Asserted on the saved settings because that is the only thing that crosses a restart.
    /// </summary>
    [Fact]
    public async Task TheChosenOrderIsWhatGetsSaved()
    {
        await AddThreeAsync();
        _service.MoveRepository(_service.Repositories[2].Id, -2);

        await _service.SaveRepositorySettingsAsync();

        var saved = await _settings.GetAsync("Repositories", new RepositorySettingsCollection());
        var project = Assert.Single(saved.Projects);
        Assert.Equal(["Gamma", "Alpha", "Beta"], project.Repositories.Select(r => r.Name).ToArray());
    }
}
