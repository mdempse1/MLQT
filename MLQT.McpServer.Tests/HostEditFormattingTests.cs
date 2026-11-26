using Microsoft.Extensions.DependencyInjection;
using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A class written through a server built on <see cref="MlqtMcpServerExtensions.AddMlqtMcpServer"/> is
/// formatted with its repository's rules whoever registered the <see cref="SessionState"/>.
/// </summary>
/// <remarks>
/// The edit tools find the repositories through the session, and the session was given them only where
/// MLQT made it: every service is TryAdded, so a host that registered a SessionState of its own got one
/// with none, and every class it wrote was written as composed - imports after extends and the rest -
/// while mlqt_format, which asks the repository service itself, formatted the same class. The README
/// said so, which is a trap documented rather than one removed.
/// </remarks>
public class HostEditFormattingTests
{
    private const string Written = "model M \"m\"\n  extends Base;\n  import Modelica.Units.SI;\n  parameter SI.Time t = 1;\nend M;";

    private static string Repository(InProcessServer server)
    {
        var root = server.TempDirectory();
        var lib = Path.Combine(root, "P");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "within;\npackage P \"p\"\nend P;\n");
        File.WriteAllText(Path.Combine(lib, "Base.mo"), "within P;\npartial model Base \"b\"\nend Base;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Base\n");
        Directory.CreateDirectory(Path.Combine(root, ".mlqt"));
        File.WriteAllText(Path.Combine(root, ".mlqt", "settings.json"),
            "{\n  \"ApplyFormattingRules\": true,\n  \"OneOfEachSection\": true,\n  \"ImportStatementsFirst\": true,\n" +
            "  \"ComponentsBeforeClasses\": true\n}\n");
        return root;
    }

    private static async Task<string> WriteThroughTheServer(InProcessServer server)
    {
        var root = Repository(server);
        var load = await server.CallAsync("mlqt_load", new() { ["paths"] = new[] { root }, ["withDependencies"] = false });
        Assert.NotEqual(true, load.IsError);
        var write = await server.CallAsync("mlqt_write_class", new()
        {
            ["classes"] = new[] { new Dictionary<string, object?> { ["parentId"] = "P", ["source"] = Written } },
        });
        Assert.NotEqual(true, write.IsError);
        return File.ReadAllText(Path.Combine(root, "P", "M.mo")).Replace("\r\n", "\n");
    }

    private static void AssertImportFirst(string file)
    {
        Assert.True(file.IndexOf("import Modelica.Units.SI;", StringComparison.Ordinal)
                    < file.IndexOf("extends Base;", StringComparison.Ordinal),
            "the import was written after the extends clause:\n" + file);
    }

    [Fact]
    public async Task AClassWritten_IsFormatted_WithTheSessionMlqtRegisters()
    {
        await using var server = await InProcessServer.StartAsync();
        AssertImportFirst(await WriteThroughTheServer(server));
    }

    [Fact]
    public async Task AClassWritten_IsFormatted_WithASessionTheHostRegistered()
    {
        await using var server = await InProcessServer.StartAsync(before: s => s.AddSingleton(new SessionState()));
        AssertImportFirst(await WriteThroughTheServer(server));
    }
}
