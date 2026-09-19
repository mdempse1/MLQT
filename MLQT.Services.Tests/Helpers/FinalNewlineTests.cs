using System.Text;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// B236 — how a <c>.mo</c> file ends, on every path that writes one.
///
/// <para>Reported as "Format All Files now drops the final newline from every file", and a
/// regression is what it looks like from a working copy: reformatting a library that an earlier
/// build formatted reports every file in it as modified, which buries any real change in a diff of
/// thousands of empty ones. It is not a regression. The two formatting paths have disagreed since
/// the initial public commit — <see cref="IncrementalFormatter"/> has always written
/// <c>TrimEnd() + "\n"</c> and the full library save has always written
/// <c>string.Join("\n", visitor.Code)</c>, which ends on the last line of code. Which path a user
/// meets decides what their files look like: the incremental one runs at startup and after every
/// VCS operation, so it is the one that formats most libraries most of the time, and <b>Format All
/// Files</b> then rewrites every file it has ever touched.</para>
///
/// <para>These tests assert the last character, because nothing did. The interesting one is
/// <see cref="TheTwoFormattingPaths_WriteTheSameBytes"/>: the property that was broken is not
/// "files end with a newline" but "it does not matter which way you format", and only a test that
/// runs both can see it. The single-path tests are here so a failure says which path moved.</para>
/// </summary>
public class FinalNewlineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-final-newline", Guid.NewGuid().ToString("N"));

    public FinalNewlineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A package with one standalone child, which is the smallest library that reaches both of the
    /// saver's write sites: <c>package.mo</c> for the package and <c>M.mo</c> for the child.
    /// Written the way a Modelica tool writes them, with a final newline on each.
    /// </summary>
    private string WriteLibrary(string name)
    {
        var lib = Path.Combine(_root, name, "Lib");
        Directory.CreateDirectory(lib);

        File.WriteAllText(Path.Combine(lib, "package.mo"),
            "package Lib \"A library\"\nend Lib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "M\n");
        File.WriteAllText(Path.Combine(lib, "M.mo"),
            "within Lib;\nmodel M \"A model\"\n  Real x;\nend M;\n");

        return lib;
    }

    private static async Task<(LibraryDataService Service, LoadedLibrary Library)> LoadAsync(string libraryPath)
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromDirectoryAsync(libraryPath);
        return (service, library);
    }

    /// <summary>What <b>Format All Files</b> runs: the whole library, rewritten over itself.</summary>
    private static async Task FullSaveAsync(string libraryPath)
    {
        var (service, library) = await LoadAsync(libraryPath);
        ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            service.CombinedGraph, library.ModelIds, Path.GetDirectoryName(libraryPath)!,
            showAnnotations: true, formatting: FormattingOptions.None);
    }

    /// <summary>
    /// What startup and every VCS operation run: the changed files only, formatted in place.
    /// </summary>
    private static async Task IncrementalAsync(string libraryPath, params string[] fileNames)
    {
        var (service, _) = await LoadAsync(libraryPath);
        var paths = fileNames.Select(f => Path.Combine(libraryPath, f));
        await IncrementalFormatter.FormatAndWriteAsync(
            service.CombinedGraph, paths, new StyleCheckingSettings { ApplyFormattingRules = true });
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(parts), Encoding.UTF8);

    [Fact]
    public async Task FullLibrarySave_EndsEveryFileWithANewline()
    {
        var lib = WriteLibrary("full");

        await FullSaveAsync(lib);

        // Both write sites in the saver: the package and its standalone child.
        Assert.EndsWith("\n", Read(lib, "package.mo"));
        Assert.EndsWith("\n", Read(lib, "M.mo"));
    }

    [Fact]
    public async Task IncrementalFormat_EndsTheFileWithANewline()
    {
        var lib = WriteLibrary("incremental");

        await IncrementalAsync(lib, "M.mo");

        Assert.EndsWith("\n", Read(lib, "M.mo"));
    }

    [Fact]
    public async Task TheTwoFormattingPaths_WriteTheSameBytes()
    {
        // The property the user actually depends on. Formatting is meant to be idempotent and
        // route-independent: a file formatted at startup and then caught by Format All Files must
        // not change, or every file in the library shows as modified for no reason.
        var viaFullSave = WriteLibrary("agree-full");
        var viaIncremental = WriteLibrary("agree-incremental");

        await FullSaveAsync(viaFullSave);
        await IncrementalAsync(viaIncremental, "M.mo");

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(viaFullSave, "M.mo")),
            File.ReadAllBytes(Path.Combine(viaIncremental, "M.mo")));
    }

    [Fact]
    public async Task FormattingTwiceByEitherRoute_ChangesNothingTheSecondTime()
    {
        // Idempotence stated directly, and the shape the report took: the second run is the one a
        // user sees as "every file modified".
        var lib = WriteLibrary("idempotent");

        await FullSaveAsync(lib);
        var afterFirst = File.ReadAllBytes(Path.Combine(lib, "M.mo"));

        await IncrementalAsync(lib, "M.mo");
        Assert.Equal(afterFirst, File.ReadAllBytes(Path.Combine(lib, "M.mo")));

        await FullSaveAsync(lib);
        Assert.Equal(afterFirst, File.ReadAllBytes(Path.Combine(lib, "M.mo")));
    }

    [Fact]
    public void TheEncodingFunnel_EndsWhatItWritesWithExactlyOneNewline()
    {
        // The rule lives in the one place CLAUDE.md already designates for every .mo and
        // package.order write, so a new writer cannot get it wrong by not knowing about it.
        var path = Path.Combine(_root, "funnel.mo");

        ModelicaFileEncoding.WriteAllText(path, "model M\nend M;");
        Assert.Equal("model M\nend M;\n", File.ReadAllText(path));

        // Already terminated: left alone rather than given a second one.
        ModelicaFileEncoding.WriteAllText(path, "model M\nend M;\n");
        Assert.Equal("model M\nend M;\n", File.ReadAllText(path));
    }

    [Fact]
    public void AnEmptyFile_StaysEmpty()
    {
        // package.order for a package with no children is written as empty, and a file holding one
        // newline is not empty. MCP's new-library tool writes exactly this.
        var path = Path.Combine(_root, "package.order");

        ModelicaFileEncoding.WriteAllText(path, string.Empty);

        Assert.Equal(0, new FileInfo(path).Length);
    }
}
