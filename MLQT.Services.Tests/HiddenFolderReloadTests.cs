using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// A library that sits under a folder whose name starts with a dot - <c>~/.local/share</c>, or the
/// <c>&lt;export&gt;/.tmp</c> temp root of the WSL test runs where this was seen.
/// </summary>
/// <remarks>
/// The reload's guard against <c>.git</c> and <c>.svn</c> used to ask whether any folder in the whole
/// path started with a dot, so every file of such a library read as hidden: a class written there was
/// on disk, never in the graph, and the next tool call could not find it. Only folders below the
/// library may make a file hidden.
/// </remarks>
public sealed class HiddenFolderReloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-dot-folder", Guid.NewGuid().ToString("N"));

    private readonly string _lib;

    public HiddenFolderReloadTests()
    {
        _lib = Path.Combine(_dir, ".tmp", "UserLib");
        Directory.CreateDirectory(_lib);
        File.WriteAllText(Path.Combine(_lib, "package.mo"), "within;\npackage UserLib \"u\"\nend UserLib;\n");
        File.WriteAllText(Path.Combine(_lib, "package.order"), "One\n");
        File.WriteAllText(Path.Combine(_lib, "One.mo"), "within UserLib;\nmodel One \"one\"\nend One;\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<LibraryDataService> LoadAsync()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(_lib);
        Assert.NotNull(service.GetModelById("UserLib.One"));
        return service;
    }

    [Fact]
    public async Task AFileWrittenIntoTheLibrary_ReachesTheGraph()
    {
        var service = await LoadAsync();
        var mine = Path.Combine(_lib, "Mine.mo");
        File.WriteAllText(mine, "within UserLib;\nmodel Mine \"mine\"\nend Mine;\n");

        var affected = await service.ReloadFileAsync(mine);

        Assert.Contains("UserLib.Mine", affected);
        Assert.NotNull(service.GetModelById("UserLib.Mine"));
        Assert.Equal("UserLib", service.GetOwningLibrary("UserLib.Mine")?.Name);
    }

    [Fact]
    public async Task AFileRemovedFromTheLibrary_LeavesTheGraph()
    {
        var service = await LoadAsync();

        var removed = service.RemoveModelsFromFile(Path.Combine(_lib, "One.mo"));

        Assert.Equal(["UserLib.One"], removed);
        Assert.Null(service.GetModelById("UserLib.One"));
    }

    [Fact]
    public async Task AFileInAHiddenFolderOfTheLibrary_IsStillSkipped()
    {
        var service = await LoadAsync();
        var hidden = Path.Combine(_lib, ".svn", "pristine", "Ghost.mo");
        Directory.CreateDirectory(Path.GetDirectoryName(hidden)!);
        File.WriteAllText(hidden, "within UserLib;\nmodel Ghost \"ghost\"\nend Ghost;\n");

        var affected = await service.ReloadFileAsync(hidden);

        Assert.Empty(affected);
        Assert.Null(service.GetModelById("UserLib.Ghost"));
    }
}
