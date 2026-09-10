using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Every desktop host that ships to Windows carries the private svn client.
/// </summary>
/// <remarks>
/// <para><c>Documentation/getting-started.md</c> promises it: <i>"SVN repositories do not require you
/// to install any external tools... MLQT ships its own bundled copy, so everything needed is included
/// in the MLQT download."</i> <c>SvnToolLocator</c> prefers that copy and falls back to <c>PATH</c>,
/// and the payload is not committed — so a host without it builds, runs, and works perfectly on any
/// machine that happens to have svn installed.</para>
///
/// <para>Which is exactly how it went missing. <c>MLQT.Photino</c> was ported through the whole of 7b
/// without the payload; every developer machine has svn on <c>PATH</c>, the self-test's
/// <c>svn.client</c> probe answered <c>svn.exe</c> on all of them, and it took packaging the product
/// to notice that the shipped article would have had no SVN at all (7b-7).</para>
///
/// <para>A build-output check would not have caught it either, for the same reason the release
/// workflow needs its own separate verification step: the folder is empty on a developer machine, so
/// there is nothing to find. This asks the only question that is true regardless — whether the
/// project is <b>wired</b> to copy it.</para>
///
/// <para>Linux is deliberately not covered: the <c>.deb</c> declares <c>subversion</c> as a
/// dependency and the locator finds it on <c>PATH</c>, which is what a Linux user expects and avoids
/// redistributing someone else's binaries.</para>
/// </remarks>
public class BundledSvnClientTests
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

    /// <summary>The desktop hosts a user installs. The CLI and MCP server ship inside them.</summary>
    /// <remarks>
    /// One host since 7b-8, when the MAUI application was deleted. Left as a list rather than folded
    /// into one test: what is being asserted is a property of *anything that ships*, and macOS (7b-9)
    /// would add a second entry rather than a second test.
    /// </remarks>
    public static TheoryData<string> ShippingHosts() => new() { "MLQT.Photino" };

    [Theory]
    [MemberData(nameof(ShippingHosts))]
    public void AShippingHostCopiesTheBundledSvnClient(string host)
    {
        var project = Path.Combine(RepositoryRoot(), host, $"{host}.csproj");
        Assert.True(File.Exists(project), $"{host} has no project file at {project}");

        // Read as XML and find the item that carries the payload, rather than searching the file for
        // the words. A text search passed against a project whose svn item had lost its
        // CopyToOutputDirectory, because the icon items further up the same file still had one.
        var items = System.Xml.Linq.XDocument.Load(project)
            .Descendants()
            .Where(e => e.Name.LocalName == "None")
            .Where(e => (e.Attribute("Include")?.Value ?? "").Contains("svn-tools", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var payload = Assert.Single(items);

        // The payload lives at the repository root rather than inside MLQT, so deleting the MAUI
        // project at 7b-8 does not take the svn client with it. Resolved rather than matched as text:
        // "..\MLQT\svn-tools\win-x64" contains the same words and would point at a folder that is
        // about to be deleted - after which the glob matches nothing, the app falls back to PATH, and
        // nobody finds out until a user without svn installs it. Which is this defect, again.
        var resolved = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(project)!,
            payload.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));

        Assert.StartsWith(
            Path.Combine(RepositoryRoot(), "svn-tools") + Path.DirectorySeparatorChar,
            resolved,
            StringComparison.OrdinalIgnoreCase);

        // Copied to output, and renamed to the folder SvnToolLocator looks in. Both are asserted
        // because either one alone ships a payload the application cannot find.
        Assert.Equal("PreserveNewest", payload.Attribute("CopyToOutputDirectory")?.Value);
        Assert.StartsWith(@"svn\", payload.Attribute("Link")?.Value ?? "");
    }

    [Fact]
    public void TheLocatorLooksWhereTheProjectsPutIt()
    {
        // The two ends of the same contract, which live in different projects and have no compiler
        // relationship: the csproj writes to "svn/" and the locator reads from
        // AppContext.BaseDirectory + this. A rename on either side is silent.
        Assert.Equal("svn", RevisionControl.SvnToolLocator.BundledSubdirectory);
    }

    [Fact]
    public void ThePayloadFolderIsNotCommitted()
    {
        // It is third-party binaries fetched by build/fetch-svn-tools.ps1, and the release workflow
        // verifies the published output separately because this repository cannot. If the ignore rule
        // ever moved, the first anyone would know is a pull request carrying 30MB of SlikSVN.
        var ignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));

        Assert.Contains("svn-tools/win-x64/", ignore);
    }
}
