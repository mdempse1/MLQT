using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// <see cref="ModelScope"/> — the first piece of MainLayout's analysis pipeline to move out
/// (phase 7a-4).
///
/// <para>These three answers decide what every re-analysis pass does: which rules a class is checked
/// against, which repository's findings are cleared, and whether dependency analysis has to run
/// first. None of them could be asked without rendering the application shell until now.</para>
/// </summary>
public class ModelScopeTests
{
    private static LoadedLibrary Library(string name, string? repositoryId, params string[] modelIds) =>
        new()
        {
            Name = name,
            RepositoryId = repositoryId,
            ModelIds = [.. modelIds],
        };

    private static Repository Repo(string id, StyleCheckingSettings? settings) =>
        new() { Id = id, Name = id, StyleSettings = settings };

    // ---- ModelToRepository --------------------------------------------------------------------

    [Fact]
    public void ModelToRepository_MapsEveryClassToItsRepository()
    {
        var map = ModelScope.ModelToRepository([Library("Lib", "repo-1", "Lib.A", "Lib.B")]);

        Assert.Equal("repo-1", map["Lib.A"]);
        Assert.Equal("repo-1", map["Lib.B"]);
    }

    [Fact]
    public void ModelToRepository_LeavesOutClassesFromALibraryWithNoRepository()
    {
        // A bare directory or a single file the user opened. Mapping these to an empty id would let
        // a caller attribute findings to a repository that does not exist.
        var map = ModelScope.ModelToRepository(
        [
            Library("Repo", "repo-1", "Repo.A"),
            Library("Loose", null, "Loose.A"),
            Library("Empty", "", "Empty.A"),
        ]);

        Assert.True(map.ContainsKey("Repo.A"));
        Assert.False(map.ContainsKey("Loose.A"));
        Assert.False(map.ContainsKey("Empty.A"));
    }

    [Fact]
    public void ModelToRepository_KeepsEachLibrarysClassesSeparate()
    {
        var map = ModelScope.ModelToRepository(
        [
            Library("A", "repo-1", "A.One"),
            Library("B", "repo-2", "B.One"),
        ]);

        Assert.Equal("repo-1", map["A.One"]);
        Assert.Equal("repo-2", map["B.One"]);
    }

    [Fact]
    public void ModelToRepository_WithNoLibraries_IsEmpty()
    {
        Assert.Empty(ModelScope.ModelToRepository([]));
    }

    // ---- ModelToStyleSettings -----------------------------------------------------------------

    [Fact]
    public void ModelToStyleSettings_GivesAClassItsRepositorysSettings()
    {
        var settings = new StyleCheckingSettings();
        settings.RuleSeverities[RuleIds.OneOfEachSection] = RuleSeverity.Error;

        var map = ModelScope.ModelToStyleSettings(
            [Library("Lib", "repo-1", "Lib.A")],
            id => id == "repo-1" ? Repo("repo-1", settings) : null);

        Assert.Same(settings, map["Lib.A"]);
    }

    [Fact]
    public void ModelToStyleSettings_MapsEveryClass_EvenWithoutARepository()
    {
        // Unlike ModelToRepository this must not leave anything out. A class missing from the map
        // is checked against nothing, and an unchecked class reads as a clean one.
        var map = ModelScope.ModelToStyleSettings(
        [
            Library("Repo", "repo-1", "Repo.A"),
            Library("Loose", null, "Loose.A"),
        ],
            _ => null);

        Assert.True(map.ContainsKey("Repo.A"));
        Assert.True(map.ContainsKey("Loose.A"));
        Assert.NotNull(map["Loose.A"]);
    }

    [Fact]
    public void ModelToStyleSettings_FallsBackToDefaultsWhenARepositoryHasNoneSaved()
    {
        var map = ModelScope.ModelToStyleSettings(
            [Library("Lib", "repo-1", "Lib.A")],
            _ => Repo("repo-1", settings: null));

        Assert.NotNull(map["Lib.A"]);
    }

    [Fact]
    public void ModelToStyleSettings_GivesEveryDefaultedClassTheSameInstance()
    {
        // One shared default rather than one per library: the style checker groups its work by
        // distinct settings object, so a fresh instance each time would split a single pass into
        // as many passes as there are libraries.
        var map = ModelScope.ModelToStyleSettings(
        [
            Library("A", null, "A.One"),
            Library("B", null, "B.One"),
        ],
            _ => null);

        Assert.Same(map["A.One"], map["B.One"]);
    }

    [Fact]
    public void ModelToStyleSettings_LetsTwoRepositoriesDiffer()
    {
        var strict = new StyleCheckingSettings();
        strict.RuleSeverities[RuleIds.OneOfEachSection] = RuleSeverity.Error;
        var lenient = new StyleCheckingSettings();

        var map = ModelScope.ModelToStyleSettings(
        [
            Library("A", "repo-1", "A.One"),
            Library("B", "repo-2", "B.One"),
        ],
            id => id == "repo-1" ? Repo("repo-1", strict) : Repo("repo-2", lenient));

        Assert.Same(strict, map["A.One"]);
        Assert.Same(lenient, map["B.One"]);
    }

    // ---- RequiresDependencyAnalysis -----------------------------------------------------------

    [Fact]
    public void RequiresDependencyAnalysis_IsFalseWhenNoGraphRuleIsEnabled()
    {
        Assert.False(ModelScope.RequiresDependencyAnalysis([Repo("repo-1", new StyleCheckingSettings())]));
    }

    [Fact]
    public void RequiresDependencyAnalysis_IsTrueWhenOneRepositoryEnablesAGraphRule()
    {
        // Any one repository is enough: the dependency edges are built across the whole graph, so a
        // single repository needing them makes the pass necessary for the run.
        var needsEdges = new StyleCheckingSettings();
        needsEdges.RuleSeverities[RuleIds.UnusedClass] = RuleSeverity.Warning;

        Assert.True(ModelScope.RequiresDependencyAnalysis(
        [
            Repo("repo-1", new StyleCheckingSettings()),
            Repo("repo-2", needsEdges),
        ]));
    }

    [Fact]
    public void RequiresDependencyAnalysis_IgnoresRepositoriesWithNoSettings()
    {
        // A repository added but never configured must not throw here — this runs before every
        // style check.
        Assert.False(ModelScope.RequiresDependencyAnalysis([Repo("repo-1", settings: null)]));
    }

    [Fact]
    public void RequiresDependencyAnalysis_WithNoRepositories_IsFalse()
    {
        Assert.False(ModelScope.RequiresDependencyAnalysis([]));
    }
}
