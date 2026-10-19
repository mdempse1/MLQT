using System.Text.Json;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_session</c>, <c>mlqt_browse</c> and <c>mlqt_style_settings</c>, the consolidated set's facades
/// over the current session, tree and settings tools (B503 step 2): each mode answers what the tool it
/// replaces answers.
/// </summary>
public class SessionFacadeToolsTests
{
    private static readonly string PackageMo = """
        within;
        package L "lib"
          package Sub "sub"
            model M "m"
            end M;
          end Sub;
          annotation (uses(Modelica(version="4.1.0")));
        end L;
        """.Replace("\r\n", "\n");

    private static (SessionFacadeTools Facade, string Dir) Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageMo });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (Tools(h), dir);
    }

    private static SessionFacadeTools Tools(TestHost h) =>
        new(h.Libraries, h.Repositories, h.Resources, h.CodeReview, h.CustomDictionary, h.DictionaryManager, h.Session);

    private static SessionTools Current(TestHost h) => new(h.Libraries, h.Repositories, h.Resources, h.Session);

    private static string Json(object o) => JsonSerializer.Serialize(o, o.GetType(), McpServerJson.Options);

    [Fact]
    public async Task Status_IsTheLibrariesAndRepositories_WithWhatIsMissing()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        var status = Assert.IsType<SessionStatus>(await tools.Session());

        var library = Assert.Single(status.Libraries);
        Assert.Equal("L", library.Name);
        Assert.Contains(library.Dependencies, d => d.Name == "Modelica");
        Assert.Empty(status.Repositories);
        Assert.Equal(Json(Current(host).ListLibraries()), Json(status.Libraries));
    }

    [Fact]
    public async Task Status_OnAnEmptySession_IsEmpty()
    {
        using var host = new TestHost();

        var status = Assert.IsType<SessionStatus>(await Tools(host).Session("status"));

        Assert.Empty(status.Libraries);
    }

    [Fact]
    public async Task Unload_RemovesTheLibraryNamed_AndNeedsOne()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        var missing = Assert.IsType<ToolError>(await tools.Session("unload"));
        Assert.Contains("needs library", missing.Error);

        var unloaded = Assert.IsType<UnloadLibraryResult>(await tools.Session("unload", library: "L"));
        Assert.Equal("L", unloaded.Name);
        Assert.Empty(host.Libraries.Libraries);
    }

    [Fact]
    public async Task Reload_ReReadsAFileChangedOutsideMlqt()
    {
        using var host = new TestHost();
        var (tools, dir) = Load(host);
        var file = Path.Combine(dir, "package.mo");
        File.WriteAllText(file, PackageMo.Replace("model M \"m\"", "model M \"changed\""));

        var res = Assert.IsType<ReloadResult>(await tools.Session("reload", target: file));

        Assert.Equal("file", res.Scope);
        Assert.Contains("\"changed\"", host.Libraries.GetModelById("L.Sub.M")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task AnUnknownAction_SaysWhichThereAre()
    {
        using var host = new TestHost();

        var err = Assert.IsType<ToolError>(await Tools(host).Session("list"));

        Assert.Contains("'status', 'unload' or 'reload'", err.Error);
    }

    [Fact]
    public void Browse_IsThePackageTree()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        Assert.Equal(Json(new ClassQueryTools(host.Libraries).GetPackageTree("L", 2, 50)),
            Json(tools.Browse("L", 2, 50)));
    }

    [Fact]
    public async Task StyleSettings_WithNoSettings_ReadsThem_AndWithSettings_WritesThem()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        var style = new StyleTools(host.Libraries, host.CodeReview, host.Repositories, host.CustomDictionary,
            host.DictionaryManager, host.Session);

        Assert.Equal(Json(style.GetStyleSettings()), Json(await tools.StyleSettings()));

        // With no repository there is no settings file to write, and the write path says so.
        var write = await tools.StyleSettings(new StyleSettingsInput());
        Assert.Equal(Json(await style.SetStyleSettings(new StyleSettingsInput())), Json(write));
        Assert.IsType<ToolError>(write);
    }
}
