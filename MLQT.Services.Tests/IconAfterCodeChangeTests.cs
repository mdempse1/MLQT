namespace MLQT.Services.Tests;

/// <summary>
/// B300 — after a class's code changes, the tree's next look at it draws the icon the new code has.
/// </summary>
/// <remarks>
/// <para>Since B300 a code change no longer blanks the icon it had, so the library browser goes on
/// showing it rather than a plain folder until the tree is refreshed. That is only safe if the
/// refresh then draws the right one - including none at all, for a class whose icon was taken out:
/// nothing blanks it in advance any more, so a stale icon would otherwise stay.</para>
/// </remarks>
public class IconAfterCodeChangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlqt-icon-change", Guid.NewGuid().ToString("N"));

    public IconAfterCodeChangeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<(LibraryDataService Service, ModelicaGraph.DataTypes.ModelNode Lib)> LoadWithIcon()
    {
        var file = Path.Combine(_dir, "Lib.mo");
        var code = """
            package Lib "a package with an icon"
              annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
            end Lib;
            """.Replace("\r\n", "\n");
        await File.WriteAllTextAsync(file, code);

        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync(file, code);
        var lib = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Lib");
        Assert.Contains("<rect", lib.IconSvg);
        return (service, lib);
    }

    [Fact]
    public async Task ANewIcon_IsDrawnAtTheNextRefresh()
    {
        var (service, lib) = await LoadWithIcon();

        lib.Definition.ModelicaCode = """
            package Lib "now an ellipse"
              annotation (Icon(graphics={Ellipse(extent={{-100,-100},{100,100}})}));
            end Lib;
            """.Replace("\r\n", "\n");

        var refreshed = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Lib");

        Assert.Contains("<ellipse", refreshed.IconSvg);
        Assert.DoesNotContain("<rect", refreshed.IconSvg);
    }

    [Fact]
    public async Task AnIconTakenOut_IsGoneAtTheNextRefresh()
    {
        var (service, lib) = await LoadWithIcon();

        lib.Definition.ModelicaCode = "package Lib \"no icon now\"\nend Lib;";

        var refreshed = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Lib");

        Assert.True(string.IsNullOrEmpty(refreshed.IconSvg),
            "the old icon outlived the code that drew it - nothing blanks it in advance any more, so the "
            + "refresh has to");
    }
}
