namespace RevisionControl.Tests;

/// <summary>
/// B265 — which URL an SVN file path means.
/// </summary>
/// <remarks>
/// <para><b>Two path spaces reach the VCS layer and they cannot be told apart by looking at them.</b>
/// <c>svn log</c> reports repository-root-relative paths — <c>trunk/Modelica/Foo.mo</c> — while
/// anything working from the checkout on disk uses working-copy-relative ones. The history diff used
/// to convert between them in the dialog, by stripping a <c>trunk/</c> prefix and testing whether the
/// result existed on disk; it tested against the repository's <c>LocalPath</c> while the lookup
/// underneath used its <c>VcsRootPath</c>, which are different directories whenever a repository is
/// registered at a library inside the checkout. The strip then never fired, the lookup ran against a
/// path that could not exist, and <b>every SVN history diff failed</b>.</para>
///
/// <para>Asking the server both ways removes the guess. These tests cover the half that needs no
/// server — the URLs — because that is where the mistakes are: a missing slash, a double slash, or a
/// library called "VeSyMA - Suspensions" that has to survive being put in a URL.</para>
///
/// <para><b>The class is called Subversion and not Svn on purpose.</b> <c>run-all-tests.ps1</c> and
/// CI run this suite with <c>FullyQualifiedName!~Svn</c>, because the SVN integration tests need a
/// working copy and a server no runner has - and that filter is a substring, so it would have
/// excluded these too. They need nothing at all. A guard written for a defect a user reported, that
/// no automated run ever executes, is not a guard. The size of the rest of that hole is B266.</para>
/// </remarks>
public class SubversionContentUrlTests
{
    private const string Root = "https://example.com/svn/ModelicaLibraries";
    private const string WorkingCopy = Root + "/trunk";

    [Fact]
    public void ALogPathIsTriedAgainstTheRepositoryRootFirst()
    {
        // The reported case: the path as `svn log` gives it, which already carries "trunk/".
        var urls = SvnRevisionControlSystem.ContentUrls(
            Root, WorkingCopy, "trunk/Modelica/Claytex/Electrical/CurrentControl.mo");

        Assert.Equal(Root + "/trunk/Modelica/Claytex/Electrical/CurrentControl.mo", urls[0]);
    }

    [Fact]
    public void AWorkingCopyPathIsStillReachable()
    {
        // The same method serves callers holding a working-copy-relative path, which against the
        // repository root would be missing its branch. That is the second candidate's whole job.
        var urls = SvnRevisionControlSystem.ContentUrls(
            Root, WorkingCopy, "Modelica/Claytex/Electrical/CurrentControl.mo");

        Assert.Equal(2, urls.Count);
        Assert.Contains(WorkingCopy + "/Modelica/Claytex/Electrical/CurrentControl.mo", urls);
    }

    [Fact]
    public void ASpaceInALibraryNameSurvives()
    {
        // "VeSyMA - Suspensions" is a real directory in the repository this was reported against,
        // and an unescaped space makes svn read the URL as two arguments.
        var urls = SvnRevisionControlSystem.ContentUrls(
            Root, WorkingCopy, "trunk/Modelica/VeSyMA - Suspensions/package.mo");

        Assert.Equal(Root + "/trunk/Modelica/VeSyMA%20-%20Suspensions/package.mo", urls[0]);
        Assert.DoesNotContain(" ", urls[0]);
    }

    [Fact]
    public void SlashesAreNotDoubledOrDropped()
    {
        var urls = SvnRevisionControlSystem.ContentUrls(
            Root + "/", WorkingCopy + "/", "/trunk//Modelica/Foo.mo");

        Assert.Equal(Root + "/trunk/Modelica/Foo.mo", urls[0]);
        Assert.DoesNotContain("//", urls[0]["https://".Length..]);
    }

    [Fact]
    public void AWindowsSeparatorIsAUrlSeparatorHere()
    {
        var urls = SvnRevisionControlSystem.ContentUrls(
            Root, WorkingCopy, @"trunk\Modelica\Foo.mo");

        Assert.Equal(Root + "/trunk/Modelica/Foo.mo", urls[0]);
    }

    [Fact]
    public void AWorkingCopyCheckedOutAtTheRootIsAskedOnce()
    {
        // Both candidates would be the same URL, and asking the server the same question twice for
        // a file that is not there doubles the wait for nothing.
        var urls = SvnRevisionControlSystem.ContentUrls(Root, Root, "trunk/Modelica/Foo.mo");

        Assert.Single(urls);
    }

    [Fact]
    public void AnEmptyPathIsNoUrlAtAll()
    {
        // Rather than the repository root itself, which is a directory and would "succeed" at
        // something nobody asked for.
        Assert.Empty(SvnRevisionControlSystem.ContentUrls(Root, WorkingCopy, "   /  "));
        Assert.Empty(SvnRevisionControlSystem.ContentUrls(Root, WorkingCopy, ""));
    }
}
