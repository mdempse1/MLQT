using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using ModelicaGraph;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A server that outlives a conversation, or is shared by tasks running at once (B505). MCP gives a server
/// no conversation id, so it cannot tell them apart; what it can do is say how old what it holds is, warn
/// before one task moves another's edits onto its own copy of a library, and offer a way back to empty.
/// </summary>
public class SharedServerStateTests
{
    private static string Package(string name) => $"within;\npackage {name} \"{name}\"\n  model M \"m\"\n  end M;\nend {name};\n";

    // A library's folder is named after it, as a real one's is, unless a test says otherwise.
    private static string Library(TestHost h, string parent, string name, string? folder = null)
    {
        var dir = Path.Combine(h.NewTempDir(), parent, folder ?? name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.mo"), Package(name));
        return dir;
    }

    private static LoadTools Load(TestHost h) => new(h.Libraries, h.Repositories, h.Resources, h.Session)
    {
        DesktopSettingsDirectory = null,
        Environment_ = _ => null,
    };

    // ---- How old the state is -------------------------------------------------------------------

    [Fact]
    public void ALibraryIsTimedByTheFirstCallThatSawIt_AndForgottenWhenItGoes()
    {
        var session = new SessionState();
        var now = new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero);
        session.Clock = () => now;

        session.NoteLibraries(["a"]);
        now = now.AddMinutes(34);
        session.NoteLibraries(["a", "b"]);

        Assert.Equal("34 minutes ago", session.LoadedAgo("a"));
        Assert.Equal("less than a minute ago", session.LoadedAgo("b"));

        session.NoteLibraries(["b"]);
        Assert.Equal("less than a minute ago", session.LoadedAgo("a"));   // gone, so a later "a" is new
    }

    [Theory]
    [InlineData(1, "1 minute ago")]
    [InlineData(119, "119 minutes ago")]
    [InlineData(180, "3 hours ago")]
    public void TheAgeReadsAsASentence(int minutes, string expected)
    {
        var session = new SessionState();
        var start = DateTimeOffset.UnixEpoch;
        session.Clock = () => start;
        session.NoteLibraries(["a"]);
        session.Clock = () => start.AddMinutes(minutes);

        Assert.Equal(expected, session.LoadedAgo("a"));
    }

    [Fact]
    public async Task EveryCallTimesTheLibraries_AndTheStatusSaysWhen()
    {
        await using var server = await InProcessServer.StartAsync(new MlqtMcpServerOptions { Toolset = McpToolset.Consolidated });
        await server.CallAsync("mlqt_create_library", new() { ["name"] = "Lib", ["directory"] = server.TempDirectory() });

        var status = MlqtMcpServerExtensionsTests.Text(await server.CallAsync("mlqt_session", []));

        Assert.Matches("\"loadedAt\":\"\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ\"", status);
    }

    // ---- Another copy of a library ---------------------------------------------------------------

    [Fact]
    public async Task AnotherCopy_IsRefused_SayingWhoseItMayBe_AndWhatReplacingItDoes()
    {
        using var host = new TestHost();
        var mine = Library(host, "A", "Lib");
        var other = Library(host, "B", "Lib");
        ToolAssert.Ok<LoadResult>(await Load(host).Load([mine]));

        var res = ToolAssert.Ok<LoadResult>(await Load(host).Load([other]));

        var refusal = Assert.Single(res.Errors);
        Assert.Contains("loaded less than a minute ago", refusal);
        Assert.Contains("another task's using this MLQT server", refusal);
        Assert.Contains("every later MLQT edit to Lib's classes", refusal);
        Assert.Contains("ask the user", refusal);
        Assert.Equal(mine, host.Libraries.Libraries.Single(l => l.Name == "Lib").SourcePath);   // nothing moved
    }

    // A clone in a folder named otherwise was loaded beside the first copy, with no refusal at all, and
    // every class id still answered with the first: the second task edited the first task's files.
    [Fact]
    public async Task AnotherCopy_InAFolderNamedOtherwise_IsRefusedToo()
    {
        using var host = new TestHost();
        var mine = Library(host, "A", "Lib");
        ToolAssert.Ok<LoadResult>(await Load(host).Load([mine]));

        var res = ToolAssert.Ok<LoadResult>(await Load(host).Load([Library(host, "B", "Lib", folder: "Lib-copy")]));

        Assert.Contains("another task's using this MLQT server", Assert.Single(res.Errors));
        Assert.Equal(mine, Assert.Single(host.Libraries.Libraries, l => l.Name == "Lib").SourcePath);
    }

    [Fact]
    public async Task AnotherCopy_InAFolderNamedOtherwise_ReplacesTheFirst_WhenAskedTo()
    {
        using var host = new TestHost();
        ToolAssert.Ok<LoadResult>(await Load(host).Load([Library(host, "A", "Lib")]));
        var copy = Library(host, "B", "Lib", folder: "Lib-copy");

        var res = ToolAssert.Ok<LoadResult>(await Load(host).Load([copy], replace: true));

        Assert.Equal(("Lib", copy), (Assert.Single(res.Replaced).Name, Assert.Single(res.Replaced).NewPath));
        Assert.Equal(copy, Assert.Single(host.Libraries.Libraries, l => l.Name == "Lib").SourcePath);
    }

    // ---- Back to empty ---------------------------------------------------------------------------

    private sealed class SuppliedSource : IReadOnlyClassSource
    {
        public string LibraryName => "Vendor";
        public string? LibraryVersion => null;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "Supplied by a test host.";

        public ReadOnlySourceContent Read(CancellationToken cancellationToken) =>
            new() { Texts = [new SuppliedText("Vendor/package.mo", "within;\npackage Vendor \"v\"\n  model S\n  end S;\nend Vendor;")] };
    }

    [Fact]
    public async Task UnloadAll_EmptiesTheSession_ButKeepsWhatAHostSupplied()
    {
        using var host = new TestHost();
        await host.Libraries.AddLibraryFromSourceAsync(new SuppliedSource());
        ToolAssert.Ok<LoadResult>(await Load(host).Load([Library(host, "A", "Lib")]));
        host.Session.DependenciesAnalyzed = true;
        var tools = new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session, host.CodeReview);

        var res = Assert.IsType<SessionResetResult>(tools.UnloadLibrary(all: true));

        Assert.Equal(["Lib"], res.Unloaded);
        Assert.Equal(["Vendor"], res.Kept);
        Assert.Equal(["Vendor"], host.Libraries.Libraries.Select(l => l.Name));
        Assert.False(host.Session.DependenciesAnalyzed);
        Assert.Empty(host.Session.LibraryOrigins);
        // Empty means the same copy can be loaded again, and another copy too, with nothing refused.
        Assert.Empty(ToolAssert.Ok<LoadResult>(await Load(host).Load([Library(host, "B", "Lib")])).Errors);
    }

    [Fact]
    public void Unload_WithNeitherALibraryNorAll_SaysWhichToGive()
    {
        using var host = new TestHost();

        var err = Assert.IsType<ToolError>(new SessionTools(host.Libraries, host.Repositories, host.Resources, host.Session).UnloadLibrary());

        Assert.Contains("all=true", err.Error);
    }

    [Fact]
    public async Task TheConsolidatedSessionTool_Resets_ThroughTheSameCode()
    {
        using var host = new TestHost();
        ToolAssert.Ok<LoadResult>(await Load(host).Load([Library(host, "A", "Lib")]));
        var facade = new SessionFacadeTools(host.Libraries, host.Repositories, host.Resources, host.CodeReview,
            host.CustomDictionary, host.DictionaryManager, host.Session);

        var res = Assert.IsType<SessionResetResult>(await facade.Session("reset"));

        Assert.Equal(["Lib"], res.Unloaded);
        Assert.Empty(host.Libraries.Libraries);
    }
}
