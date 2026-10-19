using System.Text.Json;
using MLQT.McpServer.Helpers;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The startup-script reader against <c>TestFixtures/StartupScripts</c>: real users' scripts, anonymised,
/// and the libraries each must open (<c>expected/&lt;script&gt;.json</c>). The fixture names its absolute
/// paths as <c>{root}</c>; each test reads a copy with <c>{root}</c> replaced, so no test depends on where
/// the repository is checked out.
/// </summary>
public class StartupScriptReaderTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestHost _host = new();
        public string Root { get; }

        public Fixture()
        {
            Root = _host.NewTempDir();
            var source = Path.Combine(ToolCountTests.RepositoryRoot(), "TestFixtures", "StartupScripts");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Path.GetExtension(file) is ".mos" or ".mo")
                    File.WriteAllText(target, File.ReadAllText(file).Replace("{root}", Root));
                else
                    File.Copy(file, target);
            }
        }

        public string Relative(string path) =>
            Path.GetRelativePath(Root, DependencyLoader.LibraryRoot(path) ?? path).Replace('\\', '/');

        public ScriptReading Read(string script, IReadOnlyDictionary<string, string> environment) =>
            new StartupScriptReader(k => environment.GetValueOrDefault(k), []).Read(Path.Combine(Root, script));

        public void Dispose() => _host.Dispose();
    }

    private static JsonElement Expected(string script) => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        ToolCountTests.RepositoryRoot(), "TestFixtures", "StartupScripts", "expected", Path.ChangeExtension(script, ".json")))).RootElement;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static TheoryData<string> Scripts => ["startup1.mos", "startup3.mos", "startup4.mos"];

    [Theory]
    [MemberData(nameof(Scripts))]
    public void ReadingAScript_GivesWhatItsExpectedFileSays(string script)
    {
        using var f = new Fixture();
        var expected = Expected(script);
        var environment = expected.GetProperty("environment").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);

        var reading = f.Read(script, environment);

        var wantLoads = expected.GetProperty("loads").EnumerateArray()
            .Select(l => (Path: Path.GetRelativePath(".", Str(l, "path")!).Replace('\\', '/'), Line: l.GetProperty("line").GetInt32()))
            .Select(l => (Path: f.Relative(Path.Combine(f.Root, l.Path)), l.Line)).ToList();
        Assert.Equal(wantLoads, reading.Loads.Select(l => (f.Relative(l.Path), l.Line)).ToList());

        var wantVia = expected.GetProperty("loads").EnumerateArray().Select(l => Str(l, "via")!).ToList();
        for (var i = 0; i < wantVia.Count; i++)
            Assert.StartsWith(wantVia[i].Split(" > ")[0], reading.Loads[i].Via);

        foreach (var notLoaded in expected.GetProperty("notLoaded").EnumerateArray())
            Assert.DoesNotContain(f.Relative(Path.Combine(f.Root, Str(notLoaded, "path")!)), reading.Loads.Select(l => f.Relative(l.Path)));

        Assert.Equal(expected.GetProperty("ignored").EnumerateArray().Select(i => i.GetProperty("line").GetInt32()).ToList(),
            reading.Ignored.Select(i => i.Line).ToList());
        Assert.Empty(reading.Unresolved);

        Assert.Equal(Str(expected, "currentDirectory"), f.Relative(reading.CurrentDirectory));
        Assert.Equal(Str(expected, "modelicaPathSet"), reading.ModelicaPathSet is { } set ? f.Relative(set) : null);
        var added = expected.TryGetProperty("modelicaPathAdded", out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        Assert.Equal(added, reading.ModelicaPathAdded.Select(f.Relative).ToList());
    }

    [Fact]
    public void AScriptThatCallsAFunctionOfALibraryItOpened_ReportsTheCall_ForNow()
    {
        // startup2's libraries are opened by MyTools.OpenTrunk(); following such a call is the next step.
        using var f = new Fixture();
        var reading = f.Read("startup2.mos", new Dictionary<string, string>());

        Assert.Equal("Libraries/MyTools", f.Relative(Assert.Single(reading.Loads).Path));
        var call = Assert.Single(reading.Unresolved);
        Assert.Equal(5, call.Line);
        Assert.Contains("MyTools.OpenTrunk", call.Reason);
    }

    [Fact]
    public void ABackslashInAString_IsAPathSeparator_NotAnEscape()
    {
        // In Modelica "\n" is a newline, but in a Windows path it is a folder that starts with n.
        using var host = new TestHost();
        var dir = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "nested", "tools"));
        File.WriteAllText(Path.Combine(dir, "nested", "tools", "package.mo"), "within;\npackage tools\nend tools;\n");
        var script = Path.Combine(dir, "s.mos");
        File.WriteAllText(script, "openModel(\"" + dir + "\\nested\\tools\\package.mo\");\n");

        var reading = new StartupScriptReader(_ => null, []).Read(script);

        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "nested", "tools", "package.mo")), Assert.Single(reading.Loads).Path);
    }

    [Fact]
    public void WhatCannotBeWorkedOut_IsReported_NotGuessed()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        var script = Path.Combine(dir, "s.mos");
        File.WriteAllText(script, string.Join("\n",
            "p = Modelica.Utilities.Files.fullPathName(\"x\");",
            "openModel(p + \"/package.mo\");",
            "simulateModel(\"A.B\");",
            "x = 3;",
            "RunScript(\"missing.mos\");",
            "openModelFile(\"Nowhere\");",
            "import Absent;",
            ""));

        var reading = new StartupScriptReader(_ => null, []).Read(script);

        Assert.Empty(reading.Loads);
        Assert.Equal([1, 2, 3, 5, 6, 7], reading.Unresolved.Select(u => u.Line).ToArray());
        Assert.Contains(reading.Unresolved, u => u.Line == 3 && u.Reason.Contains("not a command the reader knows"));
        Assert.Equal([4], reading.Ignored.Select(i => i.Line).ToArray());
    }

    [Fact]
    public void Statements_SpanLines_AndDropComments()
    {
        var statements = StartupScriptReader.Statements(string.Join("\n",
            "// a comment",
            "a = \"x;y\";  /* block",
            "comment */ openModel(a +",
            "  \"/b\",",
            "  changeDirectory=false)",
            "cd C:/Work",
            "")).ToList();

        Assert.Equal([("a = \"x;y\"", 2), ("openModel(a +   \"/b\",   changeDirectory=false)", 3), ("cd C:/Work", 6)],
            statements.Select(s => (s.Statement, s.Line)).ToArray());
    }

    [Fact]
    public void ACommentAfterAJoin_DoesNotEndTheStatement()
    {
        var statements = StartupScriptReader.Statements("p = \"a\" + // the rest follows\n  \"b\";\n").ToList();
        Assert.Equal("p = \"a\" +    \"b\"", Assert.Single(statements).Statement);
    }

    [Fact]
    public void ACommandWithoutParentheses_IsRead()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, "work"));
        var script = Path.Combine(dir, "s.mos");
        File.WriteAllText(script, $"cd {Path.Combine(dir, "work")}\n");

        Assert.Equal(Path.Combine(dir, "work"), new StartupScriptReader(_ => null, []).Read(script).CurrentDirectory);
    }

    [Fact]
    public async Task MlqtLoad_LoadsWhatAScriptOpens_AsExplicitLibraries()
    {
        using var f = new Fixture();
        using var host = new TestHost();
        var tools = new MLQT.McpServer.Tools.LoadTools(host.Libraries, host.Repositories, host.Session)
        {
            DesktopSettingsDirectory = null,
            Environment_ = k => k == "USERNAME" ? "testuser" : null,
        };

        var res = ToolAssert.Ok<MLQT.McpServer.Dtos.LoadResult>(await tools.Load(script: Path.Combine(f.Root, "startup3.mos")));

        Assert.NotNull(res.Script);
        Assert.Equal(15, res.Script!.Loads.Count);
        var loaded = res.Loaded.Select(l => l.Name).ToHashSet();
        foreach (var name in new[] { "Modelica", "VehicleInterfaces", "ExternData", "OpenDynLib", "TyreTests", "RaceCarA", "RaceCarATests", "RaceCarB", "CarModel" })
            Assert.Contains(name, loaded);
        Assert.All(res.Loaded, l => Assert.Equal("explicit", l.Origin));
        Assert.DoesNotContain("RaceCarBTests", loaded); // its line is commented out
        // The encrypted ones are placeholders with no documentation, and two are single files.
        Assert.Contains(res.Warnings, w => w.Contains("DynLib.moe") && w.Contains("single file"));
        Assert.Contains(res.Warnings, w => w.Contains("ships no usable documentation"));
    }

    [Fact]
    public async Task MlqtLoad_SearchesTheScriptsOwnModelicaPath_ForDependencies()
    {
        using var f = new Fixture();
        using var host = new TestHost();
        var tools = new MLQT.McpServer.Tools.LoadTools(host.Libraries, host.Repositories, host.Session)
        {
            DesktopSettingsDirectory = null,
            Environment_ = _ => null,
        };

        var res = ToolAssert.Ok<MLQT.McpServer.Dtos.LoadResult>(await tools.Load(script: Path.Combine(f.Root, "startup4.mos")));

        Assert.Equal(["Common", "Plotting", "Rig", "RigTests", "Units"], res.Loaded.Select(l => l.Name).Order().ToArray());
        Assert.Equal("1.0", res.Loaded.Single(l => l.Name == "Common").Version);
        Assert.Empty(res.NotFound);
    }

    [Fact]
    public void AScriptThatRunsItself_IsReadOnce()
    {
        using var host = new TestHost();
        var dir = host.NewTempDir();
        var script = Path.Combine(dir, "loop.mos");
        File.WriteAllText(script, "RunScript(\"loop.mos\");\n");

        var reading = new StartupScriptReader(_ => null, []).Read(script);

        Assert.Contains(reading.Ignored, i => i.Reason.Contains("read once"));
    }
}
