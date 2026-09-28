using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Every <c>ISettingsService</c> in the repository is held to <c>SettingsServiceContract</c>.
/// </summary>
/// <remarks>
/// <para>B205 made one settings double, in <c>TestSupport/</c>, and put it and the real services
/// under one contract. <c>MLQT.TestHost</c> kept a copy of its own that no contract test reached, so
/// every journey, every documentation screenshot and the test host's <c>/selftest</c> ran on a double
/// nothing checked (B365). It had the same name as the checked one, which is why a guard asking only
/// "does each implementation have a contract test?" would have been satisfied by the other copy's —
/// so implementations are also held to having distinct names.</para>
/// </remarks>
public class SettingsDoublePolicyTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static readonly Regex Implementation = new(
        @"\bclass\s+(?<name>\w+)\s*(?:\([^)]*\))?\s*:[^{]*\bISettingsService\b",
        RegexOptions.Compiled);

    private static readonly string[] SkippedSegments = ["bin", "obj", ".claude", ".git"];

    private static List<(string Name, string File, string Text)> SourceFiles()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => SkippedSegments.Contains(s, StringComparer.OrdinalIgnoreCase)))
            .Select(f => (Path.GetFileName(f), Path.GetRelativePath(root, f), File.ReadAllText(f)))
            .ToList();
    }

    private static List<(string Class, string File)> Implementations(
        List<(string Name, string File, string Text)> files) =>
        files.SelectMany(f => Implementation.Matches(f.Text)
                .Select(m => (m.Groups["name"].Value, f.File)))
            .ToList();

    [Fact]
    public void EveryImplementationIsOneClassWithOneName()
    {
        var implementations = Implementations(SourceFiles());

        Assert.True(implementations.Count >= 3,
            $"found only {implementations.Count} ISettingsService implementations - the search is broken");

        var duplicated = implementations.GroupBy(i => i.Class)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(i => i.File))}")
            .ToList();

        Assert.True(duplicated.Count == 0,
            "more than one ISettingsService with the same name - link the TestSupport double rather "
            + "than copying it:\n" + string.Join("\n", duplicated));
    }

    [Fact]
    public void EveryImplementationIsHeldToTheContract()
    {
        var files = SourceFiles();
        var contractTests = files.Where(f => f.Text.Contains(": SettingsServiceContract")).ToList();

        var unchecked_ = Implementations(files)
            .Where(i => !contractTests.Any(t => t.Text.Contains($"new {i.Class}(")))
            .Select(i => $"{i.Class} ({i.File})")
            .ToList();

        Assert.True(unchecked_.Count == 0,
            "ISettingsService implementations no SettingsServiceContract subclass constructs:\n"
            + string.Join("\n", unchecked_));
    }
}
