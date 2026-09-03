using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Installing the pre-commit gate. The check itself is <c>mlqt check</c>; what is tested here is the
/// hook that runs it — that it lands where git looks, refuses to trample somebody else's, and blocks
/// a commit that would introduce findings.
/// </summary>
public class HookCommandTests
{
    /// <summary>A repository with the library in a subdirectory, as a real one usually has.</summary>
    private sealed class TempRepo(bool initGit = true, string settings = ErrorOnMissingDescription)
        : IDisposable
    {
        private readonly TempWorkspace _workspace = Build(initGit, settings);

        private static TempWorkspace Build(bool initGit, string settings)
        {
            var workspace = new TempWorkspace("mlqt-hook")
                .Write(Path.Combine("Lib", "package.mo"), GoodLibrary)
                .Write(Path.Combine("Lib", "package.order"), "Good\n")
                .WithSettings(settings);

            return initGit ? workspace.InitGit() : workspace;
        }

        public string Root => _workspace.Root;
        public string LibraryPath => _workspace.PathTo("Lib");
        public string HookPath => _workspace.PathTo(".git", "hooks", "pre-commit");

        public (int Code, string Output) Git(string arguments) => _workspace.Git(arguments);

        public void Dispose() => _workspace.Dispose();
    }

    private const string GoodLibrary = """
        within ;
        package Lib "A library"
          model Good "Described"
          end Good;
        end Lib;
        """;

    private const string LibraryWithAnUndescribedClass = """
        within ;
        package Lib "A library"
          model Good "Described"
          end Good;
          model Sloppy
          end Sloppy;
        end Lib;
        """;

    private const string ErrorOnMissingDescription =
        """{ "RuleSeverities": { "MLQT.Doc.ClassDescription": "Error" } }""";

    /// <summary>
    /// Whether git can be run here. These two tests drive a real commit, which is the only way to
    /// prove the hook actually gates one; without git there is nothing to prove and nothing to fail.
    /// Every runner that checks this repository out has git, so this is a guard rather than a hole.
    /// </summary>
    private static bool GitIsAvailable(TempRepo repo)
    {
        try
        {
            return repo.Git("--version").Code == 0;
        }
        catch
        {
            return false;
        }
    }

    // ---- installing ----------------------------------------------------------------------------

    [Fact]
    public void InstallWritesTheHookWhereGitLooksForIt()
    {
        using var repo = new TempRepo();

        var (code, stdout, _) = Cli.Run("hook", "install", repo.LibraryPath);

        Assert.Equal(0, code);
        Assert.True(File.Exists(repo.HookPath));
        Assert.Contains("\"$MLQT\" check", File.ReadAllText(repo.HookPath));
        Assert.Contains("--no-verify", stdout);      // the way out is part of the install message
    }

    [Fact]
    public void TheRepositoryIsFoundFromALibraryInASubdirectory()
    {
        // The library is Root/Lib; the hook belongs to the repository above it.
        using var repo = new TempRepo();

        Cli.Run("hook", "install", repo.LibraryPath);

        Assert.True(File.Exists(repo.HookPath));
    }

    [Fact]
    public void TheChosenOptionsAreBakedIntoTheHook()
    {
        using var repo = new TempRepo();

        Cli.Run("hook", "install", repo.LibraryPath, "--fail-on", "warning");

        var hook = File.ReadAllText(repo.HookPath);
        Assert.Contains("--fail-on warning", hook);   // as documented, not as the enum prints
    }

    [Fact]
    public void StatusSaysWhetherOneIsInstalled()
    {
        using var repo = new TempRepo();

        Assert.Contains("No pre-commit hook", Cli.Run("hook", "status", repo.LibraryPath).stdout);
        Cli.Run("hook", "install", repo.LibraryPath);
        Assert.Contains("mlqt pre-commit hook installed", Cli.Run("hook", "status", repo.LibraryPath).stdout);
    }

    // ---- not trampling anything ----------------------------------------------------------------

    [Fact]
    public void AHookSomebodyElseWroteIsLeftAlone()
    {
        using var repo = new TempRepo();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(repo.HookPath)!);
        File.WriteAllText(repo.HookPath, "#!/bin/sh\necho mine\n");

        var (installCode, _, installError) = Cli.Run("hook", "install", repo.LibraryPath);
        var (uninstallCode, _, uninstallError) = Cli.Run("hook", "uninstall", repo.LibraryPath);

        Assert.Equal(2, installCode);
        Assert.Equal(2, uninstallCode);
        Assert.Contains("not written by mlqt", installError);
        Assert.Contains("not written by mlqt", uninstallError);
        Assert.Equal("#!/bin/sh\necho mine\n", File.ReadAllText(repo.HookPath));
    }

    [Fact]
    public void ForceReplacesIt()
    {
        using var repo = new TempRepo();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(repo.HookPath)!);
        File.WriteAllText(repo.HookPath, "#!/bin/sh\necho mine\n");

        Assert.Equal(0, Cli.Run("hook", "install", repo.LibraryPath, "--force").code);
        Assert.Contains("\"$MLQT\" check", File.ReadAllText(repo.HookPath));
    }

    [Fact]
    public void UninstallRemovesOurs()
    {
        using var repo = new TempRepo();
        Cli.Run("hook", "install", repo.LibraryPath);

        Assert.Equal(0, Cli.Run("hook", "uninstall", repo.LibraryPath).code);
        Assert.False(File.Exists(repo.HookPath));
    }

    // ---- what it refuses -----------------------------------------------------------------------

    [Fact]
    public void OutsideAGitWorkingCopy_ItSaysSoRatherThanWritingNowhere()
    {
        using var repo = new TempRepo(initGit: false);

        var (code, _, stderr) = Cli.Run("hook", "install", repo.LibraryPath);

        Assert.Equal(2, code);
        Assert.Contains("not inside a git working copy", stderr);
        Assert.Contains("SVN runs its hooks on the server", stderr);
    }

    [Fact]
    public void AMissingLibraryIsRefused()
    {
        var (code, _, stderr) = Cli.Run("hook", "install", System.IO.Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid()));

        Assert.Equal(2, code);
        Assert.Contains("library not found", stderr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sideways")]
    public void AnUnknownActionIsRefused(string action)
    {
        var args = action.Length == 0 ? new[] { "hook" } : ["hook", action];

        var (code, _, stderr) = Cli.Run(args);

        Assert.Equal(2, code);
        Assert.Contains("install|uninstall|status", stderr);
    }

    // ---- and does it actually gate a commit -----------------------------------------------------

    [Fact]
    public void TheHookBlocksACommitThatWouldIntroduceAFinding()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;   // no git, nothing to gate — see GitIsAvailable

        Cli.Run("hook", "install", repo.LibraryPath);

        File.WriteAllText(System.IO.Path.Combine(repo.LibraryPath, "package.mo"), LibraryWithAnUndescribedClass);
        File.WriteAllText(System.IO.Path.Combine(repo.LibraryPath, "package.order"), "Good\nSloppy\n");
        repo.Git("add -A");
        var (code, output) = repo.Git("commit -m \"add a class\"");

        Assert.NotEqual(0, code);
        Assert.Contains("commit blocked", output);
        Assert.Single(repo.Git("log --oneline").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ACommitWithNoModelicaInItIsNotChecked()
    {
        // The hook has to be free on the commits it has nothing to say about, or it gets uninstalled.
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;   // no git, nothing to gate — see GitIsAvailable

        Cli.Run("hook", "install", repo.LibraryPath);

        File.WriteAllText(System.IO.Path.Combine(repo.Root, "README.md"), "notes");
        repo.Git("add -A");
        var (code, output) = repo.Git("commit -m docs");

        Assert.Equal(0, code);
        Assert.DoesNotContain("mlqt: checking", output);
    }
}
