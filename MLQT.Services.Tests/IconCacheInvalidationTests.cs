using MLQT.Services;

namespace MLQT.Services.Tests;

/// <summary>
/// B349 — a class's cached icon is thrown away when what it was drawn from changes, not only when
/// its own code does.
/// </summary>
/// <remarks>
/// <para>B258 keeps each class's rendered icon on its definition, cleared only by the class's own
/// <c>ModelicaCode</c> setter. But the render resolves base classes - often in another library - so
/// a class extending a base that was not loaded yet was "no icon" for good, and editing a base
/// class's icon never reached the classes that inherit it until a restart. Before B258 each tree
/// refresh healed both.</para>
/// </remarks>
public class IconCacheInvalidationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-icon-cache", Guid.NewGuid().ToString("N"));

    public IconCacheInvalidationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Heir = """
        package Heirs "classes whose icon is inherited"
          model M "inherits its icon"
            extends Bases.WithIcon;
          end M;
        end Heirs;
        """;

    private static string Bases(string graphic) => $$"""
        package Bases "the icons"
          model WithIcon "draws something"
            annotation (Icon(graphics={{{graphic}}}));
          end WithIcon;
        end Bases;
        """;

    private async Task<string> WriteAndLoadAsync(LibraryDataService service, string name, string code)
    {
        var file = Path.Combine(_dir, name);
        await File.WriteAllTextAsync(file, code.Replace("\r\n", "\n"));
        await service.AddLibraryFromFileAsync(file, await File.ReadAllTextAsync(file));
        return file;
    }

    private static async Task<string?> IconOfMAsync(LibraryDataService service)
    {
        var root = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Heirs");
        var children = await service.GetChildModelsAsync(root);
        return children.Single(m => m.Id == "Heirs.M").IconSvg;
    }

    [Fact]
    public async Task AnHeirRenderedBeforeItsBaseLoaded_GetsItsIconOnceTheBaseArrives()
    {
        var service = new LibraryDataService();
        await WriteAndLoadAsync(service, "Heirs.mo", Heir);

        // Asked while the base is not there - a repository added mid-session, say.
        Assert.True(string.IsNullOrEmpty(await IconOfMAsync(service)));

        await WriteAndLoadAsync(service, "Bases.mo", Bases("Rectangle(extent={{-10,-10},{10,10}})"));

        Assert.False(string.IsNullOrEmpty(await IconOfMAsync(service)),
            "the heir kept the 'no icon' it was given before its base class was loaded");
    }

    [Fact]
    public async Task EditingABaseClassesIcon_ReachesItsHeirs()
    {
        var service = new LibraryDataService();
        var bases = await WriteAndLoadAsync(service, "Bases.mo", Bases("Rectangle(extent={{-10,-10},{10,10}})"));
        await WriteAndLoadAsync(service, "Heirs.mo", Heir);

        var before = await IconOfMAsync(service);
        Assert.Contains("rect", before);

        await File.WriteAllTextAsync(bases, Bases("Ellipse(extent={{-10,-10},{10,10}})").Replace("\r\n", "\n"));
        await service.ReloadFileAsync(bases);

        var after = await IconOfMAsync(service);
        Assert.Contains("ellipse", after);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task AskingAgainWithNothingChanged_KeepsTheRenderedIcon()
    {
        // B258's point, which this must not undo: nothing changed, so nothing is rendered again.
        var service = new LibraryDataService();
        await WriteAndLoadAsync(service, "Bases.mo", Bases("Rectangle(extent={{-10,-10},{10,10}})"));
        await WriteAndLoadAsync(service, "Heirs.mo", Heir);

        var first = await IconOfMAsync(service);
        var second = await IconOfMAsync(service);

        Assert.Same(first, second);
    }
}
