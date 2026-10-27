using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Parsing for <c>mlqt hook</c>. The options are a subset of the check's, and they are baked into a
/// script that nobody reads afterwards, so a silently misparsed one becomes a gate nobody notices.
/// </summary>
public class HookOptionsTests
{
    private static HookOptions Parse(params string[] args)
    {
        Assert.True(HookOptions.TryParse(args, out var options, out var error), error);
        return options!;
    }

    private static string Rejects(params string[] args)
    {
        Assert.False(HookOptions.TryParse(args, out _, out var error));
        return error!;
    }

    [Fact]
    public void TheLibraryDefaultsToWhereYouAreStanding()
    {
        Assert.Equal(Directory.GetCurrentDirectory(), Parse().LibraryPath);
    }

    [Fact]
    public void ErrorIsWhatBlocksACommitUnlessSaidOtherwise()
    {
        Assert.Equal(FailOnLevel.Error, Parse().FailOn);
        Assert.Equal(FailOnLevel.Warning, Parse("--fail-on", "warning").FailOn);
        Assert.Equal(FailOnLevel.Off, Parse("--fail-on", "OFF").FailOn);
    }

    [Fact]
    public void PathsResolveAsTheyDoForCheck()
    {
        // As `check` reads them: a relative --baseline against the library, a relative --dependency
        // against the current directory (B508). Both are made absolute at install, so the hook does
        // not depend on whatever directory git runs it from.
        using var workspace = new TempWorkspace("mlqt-hookopts");
        var library = workspace.PathTo("Lib");
        Directory.CreateDirectory(library);

        var options = Parse(library, "--baseline", ".mlqt/baseline.json", "--dependency", "../Other");

        Assert.Equal(Path.GetFullPath(Path.Combine(library, ".mlqt/baseline.json")), options.BaselinePath);
        Assert.Equal(Path.GetFullPath("../Other"), Assert.Single(options.DependencyPaths));
    }

    [Fact]
    public void DependenciesAccumulate()
    {
        Assert.Equal(2, Parse("--dependency", "A", "--dependency", "B").DependencyPaths.Count);
    }

    [Fact]
    public void ChangedFromIsPassedThroughUntouched()
    {
        // It is a VCS ref, not a path, so resolving it would ruin it.
        Assert.Equal("origin/main", Parse("--changed-from", "origin/main").ChangedFrom);
    }

    [Fact]
    public void ForceIsOff() => Assert.False(Parse().Force);

    [Theory]
    [InlineData(new[] { "--fail-on", "sometimes" }, "invalid --fail-on")]
    [InlineData(new[] { "--fail-on" }, "requires a value")]
    [InlineData(new[] { "--baseline" }, "requires a value")]
    [InlineData(new[] { "--elsewhere" }, "unknown option")]
    [InlineData(new[] { "one", "two" }, "unexpected argument")]
    public void WhatIsRefused(string[] args, string expected)
    {
        Assert.Contains(expected, Rejects(args));
    }
}
