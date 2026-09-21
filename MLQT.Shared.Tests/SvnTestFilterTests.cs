using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The one filter that decides which SVN tests run, held to being one filter (B266).
/// </summary>
/// <remarks>
/// <para><c>RevisionControl.Tests</c> is run with a filter in four places — both CI jobs in
/// <c>build-and-test.yml</c>, the release workflow, and <c>check-coverage.ps1</c> — because three
/// of its nine SVN classes need an svn client. <c>run-all-tests.ps1</c> applies the same filter only
/// when the machine has no svn, so a developer's run covers the integration tests as well.</para>
///
/// <para><b>It was a substring, and it hid 281 of the suite's 672 tests.</b> Six classes that need
/// no svn at all were excluded by their names, including guards written for defects users had
/// reported — and one of the tests it hid had been failing. Four copies of a rule with nothing
/// holding them together is the shape this backlog has named more often than any other, and this is
/// the guard that was missing when it was copied around.</para>
/// </remarks>
public class SvnTestFilterTests
{
    /// <summary>
    /// The filter every caller must use. The two classes needing the fixed working copy skip
    /// themselves when it is absent; <c>SvnIntegrationTests</c> builds its own repository and needs
    /// only the client.
    /// </summary>
    internal const string Expected =
        "FullyQualifiedName!~SvnIntegration&FullyQualifiedName!~SvnMergeCommit";

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

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)).Replace("\r\n", "\n");

    public static TheoryData<string> FilesThatFilterTheSuite() => new()
    {
        ".github/workflows/build-and-test.yml",
        ".github/workflows/release.yml",
        "build/check-coverage.ps1",
        "build/run-all-tests.ps1",
    };

    [Theory]
    [MemberData(nameof(FilesThatFilterTheSuite))]
    public void EveryCopyOfTheFilterIsTheSameFilter(string relativePath)
    {
        var text = Read(relativePath);

        var filters = Regex.Matches(text, @"FullyQualifiedName!~[A-Za-z0-9_&!~.]+")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(filters);
        Assert.All(filters, f => Assert.Equal(Expected, f));
    }

    /// <summary>
    /// The substring that caused this. Named so that reintroducing it fails here rather than in six
    /// months, when somebody notices a class has never run.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilesThatFilterTheSuite))]
    public void NobodyExcludesEverythingCalledSvnAgain(string relativePath)
    {
        Assert.DoesNotContain("FullyQualifiedName!~Svn\"", Read(relativePath));
        Assert.DoesNotContain("FullyQualifiedName!~Svn'", Read(relativePath));
    }

    /// <summary>
    /// A developer's run is not the CI run: on a machine with svn, all three integration classes
    /// run, because that is the only place they can.
    /// </summary>
    [Fact]
    public void TheLocalScriptRunsTheIntegrationTestsWhenItCan()
    {
        var script = Read("build/run-all-tests.ps1");

        Assert.Contains("Get-Command svn", script);
        Assert.Matches(@"if \(\$svnAvailable\) \{ \$null \}", script);
    }
}
