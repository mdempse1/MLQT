using ModelicaGraph;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class GuidanceToolsTests
{
    [Fact]
    public void Overview_WhenNoTopic()
    {
        var tool = new GuidanceTools();
        var result = tool.GetGuidance();
        Assert.IsNotType<ToolError>(result);
        Assert.Equal("overview", Prop(result, "topic"));
        Assert.Contains("Load first", (string)Prop(result, "guidance")!);
    }

    [Theory]
    [InlineData("workflows")]
    [InlineData("dependencies")]
    [InlineData("style")]
    [InlineData("spelling")]
    [InlineData("formatting")]
    [InlineData("vcs")]
    [InlineData("resources")]
    public void KnownTopics_ReturnGuidance(string topic)
    {
        var result = new GuidanceTools().GetGuidance(topic);
        Assert.IsNotType<ToolError>(result);
        Assert.Equal(topic, Prop(result, "topic"));
    }

    [Fact]
    public void UnknownTopic_Errors()
    {
        Assert.IsType<ToolError>(new GuidanceTools().GetGuidance("nonsense"));
    }

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);
}

public class HeadlessSettingsServiceTests
{
    [Fact]
    public async Task RoundTrips_AndPersists()
    {
        var file = Path.Combine(Path.GetTempPath(), "mlqt-mcp-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            var svc = new HeadlessSettingsService(file);
            await svc.SetAsync("num", 42);
            await svc.SetAsync("obj", new StyleCheckingSettings { ClassHasDescription = true });

            Assert.Equal(42, await svc.GetAsync("num", 0));
            Assert.Equal("fallback", await svc.GetAsync("missing", "fallback"));

            // A new instance reads the persisted file.
            var reloaded = new HeadlessSettingsService(file);
            Assert.True((await reloaded.GetAsync("obj", new StyleCheckingSettings())).ClassHasDescription);

            await svc.RemoveAsync("num");
            Assert.Equal(-1, await svc.GetAsync("num", -1));

            await svc.ClearAsync();
            Assert.Equal("x", await svc.GetAsync("obj", "x"));
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public async Task CorruptFile_FallsBackToEmpty()
    {
        var file = Path.Combine(Path.GetTempPath(), "mlqt-mcp-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ this is not valid json");
        try
        {
            var svc = new HeadlessSettingsService(file);
            Assert.Equal("d", await svc.GetAsync("anything", "d"));
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }
}

public class StyleSettingsInputTests
{
    [Fact]
    public void ToSettings_And_From_RoundTrip()
    {
        var input = new StyleSettingsInput
        {
            ClassHasDescription = true,
            SpellCheckDescription = true,
            OneOfEachSection = true,
        };

        var settings = input.ToSettings();
        Assert.True(settings.ClassHasDescription);
        Assert.True(settings.SpellCheckDescription);
        Assert.True(settings.OneOfEachSection);
        Assert.False(settings.ValidateModelReferences);
        // SpellCheckLanguages defaults are preserved for the pipeline.
        Assert.Contains("en_US", settings.SpellCheckLanguages);

        var back = StyleSettingsInput.From(settings);
        Assert.True(back.ClassHasDescription);
        Assert.True(back.SpellCheckDescription);
        Assert.True(back.OneOfEachSection);
        Assert.False(back.ValidateModelReferences);
    }
}

public class ToolDiagnosticsTests
{
    [Fact]
    public void ClassQuery_NoLibrary_GuidesToLoad()
    {
        using var host = new TestHost();
        var q = new ClassQueryTools(host.Libraries);
        var err = ToolAssert.Error(q.GetClassInfo("Modelica.Blocks.Continuous.Integrator"));
        Assert.Contains("load_", err.Error);
        Assert.Contains("No library is loaded", err.Error);
    }

    [Fact]
    public void ClassQuery_LibraryLoadedBadId_GuidesToSearch()
    {
        using var host = new TestHost();
        host.Libraries.AddLibraryFromFileAsync(
            host.WriteMoFile("X.mo", "model X\n Real a;\nequation\n a=1;\nend X;")).GetAwaiter().GetResult();
        var q = new ClassQueryTools(host.Libraries);
        var err = ToolAssert.Error(q.GetClassInfo("Nope"));
        Assert.Contains("search_classes", err.Error);
    }

    [Fact]
    public void ListClasses_NoLibrary_Guides()
    {
        using var host = new TestHost();
        var q = new ClassQueryTools(host.Libraries);
        var err = ToolAssert.Error(q.ListClasses());
        Assert.Contains("load_", err.Error);
    }
}

public class SessionStateAndServerInfoTests
{
    [Fact]
    public void SessionState_Defaults()
    {
        var s = new SessionState();
        Assert.False(s.DependenciesAnalyzed);
        Assert.False(s.ResourcesAnalyzed);
        s.DependenciesAnalyzed = true;
        Assert.True(s.DependenciesAnalyzed);
    }

    [Fact]
    public void ServerInfo_ReflectsLoadedLibraries()
    {
        using var host = new TestHost();
        var info = new ServerInfoTools(host.Libraries);

        var before = info.GetServerInfo();
        Assert.Equal(0, Prop(before, "librariesLoaded"));

        host.Libraries.AddLibraryFromFileAsync(
            host.WriteMoFile("X.mo", "model X\n Real a;\nequation\n a=1;\nend X;")).GetAwaiter().GetResult();

        var after = info.GetServerInfo();
        Assert.Equal(1, Prop(after, "librariesLoaded"));
    }

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);
}
