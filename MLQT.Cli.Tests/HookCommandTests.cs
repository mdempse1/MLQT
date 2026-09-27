using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Installing the pre-commit gate. The check itself is <c>mlqt check</c>; what is tested here is the
/// hook that runs it — that it lands where git looks, refuses to trample somebody else's, and blocks
/// a commit that would introduce findings.
/// </summary>
public sealed class HookCommandTests : IDisposable
{
    /// <summary>
    /// Every test here installs hooks, and the test binaries are inside MLQT's own working copy: one
    /// install that took the current directory for its library once put a hook into MLQT itself
    /// (B488). This fails the test that does it, and removes what it wrote.
    /// </summary>
    private readonly StrayHookGuard _strayHookGuard = StrayHookGuard.ForTestRun();

    public void Dispose() => _strayHookGuard.Dispose();

    /// <summary>
    /// A repository with the library in a subdirectory, as a real one usually has — in a temporary
    /// directory that is no other repository's working copy, or the constructor throws: a hook test
    /// whose repository sits inside another cannot tell an install that landed in the wrong one.
    /// </summary>
    private sealed class TempRepo : IDisposable
    {
        private readonly TempWorkspace _workspace;

        public TempRepo(bool initGit = true, string settings = ErrorOnMissingDescription)
        {
            _workspace = Build(initGit, settings);

            if (StrayHookGuard.EnclosingWorkingCopy(_workspace.Root) is { } enclosing)
            {
                _workspace.Dispose();
                throw new InvalidOperationException(
                    $"The temporary directory {_workspace.Root} is inside the working copy {enclosing}, so a " +
                    "hook test there could install into that repository. Point TEMP/TMPDIR somewhere else.");
            }
        }

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

        /// <summary>Runs git in another directory — a worktree of this repository.</summary>
        public (int Code, string Output) GitIn(string directory, string arguments) =>
            _workspace.Git($"-C \"{directory.Replace('\\', '/')}\" {arguments}");

        /// <summary>
        /// A second working tree of this repository, in its own temporary directory beside it — not
        /// inside this one's, which would make it a working copy inside another.
        /// </summary>
        public TempWorktree AddWorktree()
        {
            var path = Path.Combine(Path.GetTempPath(), $"mlqt-hook-wt-{Guid.NewGuid():N}");
            var (code, output) = Git($"worktree add -q \"{path.Replace('\\', '/')}\"");
            Assert.True(code == 0, $"git worktree add failed: {output}");
            Assert.Null(StrayHookGuard.EnclosingWorkingCopy(path));
            return new TempWorktree(this, path);
        }

        public void Dispose() => _workspace.Dispose();
    }

    private sealed class TempWorktree(TempRepo repo, string root) : IDisposable
    {
        public string Root => root;
        public string LibraryPath => Path.Combine(root, "Lib");

        /// <summary>The worktree's own git directory's hooks: where an install from here went before
        /// B490, and where git never looks.</summary>
        public string OwnGitDirHookPath =>
            Path.Combine(repo.Root, ".git", "worktrees", Path.GetFileName(root), "hooks", "pre-commit");

        public (int Code, string Output) Git(string arguments) => repo.GitIn(root, arguments);

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { /* best effort, as TempWorkspace */ }
            catch (UnauthorizedAccessException) { /* likewise */ }
        }
    }

    /// <summary>A class with no description in the library, and the commit that adds it staged.</summary>
    private static void StageAFinding(string libraryPath, Func<string, (int Code, string Output)> git)
    {
        File.WriteAllText(Path.Combine(libraryPath, "package.mo"), LibraryWithAnUndescribedClass);
        File.WriteAllText(Path.Combine(libraryPath, "package.order"), "Good\nSloppy\n");
        git("add -A");
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

    // ---- core.hooksPath, where git does not look at .git/hooks (B41) ---------------------------

    [Fact]
    public void ARepositoryWithCoreHooksPathIsRefused_RatherThanWrittenToAndIgnored()
    {
        using var repo = new TempRepo();
        repo.Git("config core.hooksPath .husky");

        var (code, _, stderr) = Cli.Run("hook", "install", repo.LibraryPath);

        // Writing .git/hooks/pre-commit here produced the one outcome a commit gate cannot have:
        // install said it worked, status said it was installed, and no commit was ever checked.
        Assert.Equal(2, code);
        Assert.False(File.Exists(repo.HookPath));
        Assert.Contains("core.hooksPath", stderr);
    }

    [Fact]
    public void TheRefusalSaysWhatToRunInstead()
    {
        using var repo = new TempRepo();
        repo.Git("config core.hooksPath .husky");

        var (_, _, stderr) = Cli.Run("hook", "install", repo.LibraryPath, "--fail-on", "warning");

        Assert.Contains("mlqt check", stderr);
        Assert.Contains("--fail-on warning", stderr);
    }

    [Fact]
    public void StatusAndUninstallStillWorkWhenHooksAreRedirected()
    {
        using var repo = new TempRepo();
        Cli.Run("hook", "install", repo.LibraryPath);       // installed before the redirect was set
        repo.Git("config core.hooksPath .husky");

        var status = Cli.Run("hook", "status", repo.LibraryPath);
        Assert.Equal(0, status.code);
        Assert.Contains("core.hooksPath", status.stderr);   // said, not hidden

        Assert.Equal(0, Cli.Run("hook", "uninstall", repo.LibraryPath).code);
        Assert.False(File.Exists(repo.HookPath));           // a stale hook can still be removed
    }

    [Fact]
    public void ARelativeHooksPathIsResolvedFromTheTopOfTheWorkingTree_NotFromTheLibrary()
    {
        // git runs hooks from the top of the working tree, so a relative core.hooksPath is relative
        // to that - here Root/.husky, where the library is Root/Lib.
        using var repo = new TempRepo();
        repo.Git("config core.hooksPath .husky");
        var managed = Path.Combine(repo.Root, ".husky", "pre-commit");
        Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
        File.WriteAllText(managed, "#!/bin/sh\nnpx lint-staged\n");

        var install = Cli.Run("hook", "install", repo.LibraryPath);
        var status = Cli.Run("hook", "status", repo.LibraryPath);

        Assert.Equal(2, install.code);
        Assert.Contains($"runs its hooks from {Path.Combine(repo.Root, ".husky")}", install.stderr);
        Assert.Contains($"A pre-commit hook exists at {managed}, but mlqt did not write it", status.stdout);
    }

    [Fact]
    public void AnAbsoluteHooksPathIsWhereStatusLooks_AndItIsNeverWrittenTo()
    {
        using var repo = new TempRepo();
        using var elsewhere = new TempWorkspace("mlqt-hook-shared");
        repo.Git($"config core.hooksPath \"{elsewhere.Root.Replace('\\', '/')}\"");
        Cli.Run("hook", "install", repo.LibraryPath, "--force");     // refused, --force or not

        var status = Cli.Run("hook", "status", repo.LibraryPath);
        var uninstall = Cli.Run("hook", "uninstall", repo.LibraryPath, "--force");

        Assert.Contains($"No pre-commit hook at {elsewhere.PathTo("pre-commit")}", status.stdout);
        Assert.Contains($"runs its hooks from {elsewhere.Root}", status.stderr);
        Assert.Equal(0, uninstall.code);
        Assert.False(File.Exists(elsewhere.PathTo("pre-commit")));
        Assert.False(File.Exists(repo.HookPath));
    }

    [Fact]
    public void StatusNamesAnMlqtHookThatARedirectHasLeftUnrun()
    {
        using var repo = new TempRepo();
        Cli.Run("hook", "install", repo.LibraryPath);
        repo.Git("config core.hooksPath .husky");

        var (_, stdout, _) = Cli.Run("hook", "status", repo.LibraryPath);

        Assert.Contains($"No pre-commit hook at {Path.Combine(repo.Root, ".husky", "pre-commit")}", stdout);
        Assert.Contains($"An mlqt pre-commit hook is also at {repo.HookPath}, where git does not run it", stdout);
    }

    // ---- worktrees, whose hooks are the repository's (B490) ------------------------------------

    [Fact]
    public void AnInstallFromAWorktreeGoesWhereGitRunsHooks()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;
        using var worktree = repo.AddWorktree();

        var (code, stdout, _) = Cli.Run("hook", "install", worktree.LibraryPath);

        // Following the worktree's .git file wrote .git/worktrees/<name>/hooks/pre-commit, which git
        // never reads: git asks the common directory.
        Assert.Equal(0, code);
        Assert.True(File.Exists(repo.HookPath));
        Assert.False(File.Exists(worktree.OwnGitDirHookPath));
        Assert.Contains($"Installed pre-commit hook: {repo.HookPath}", stdout);
        Assert.Contains("every worktree", stdout);
        Assert.Contains($"mlqt pre-commit hook installed at {repo.HookPath}",
            Cli.Run("hook", "status", worktree.LibraryPath).stdout);
    }

    [Fact]
    public void AnInstallFromAWorktreeBlocksACommitThere()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;
        using var worktree = repo.AddWorktree();
        Assert.Equal(0, Cli.Run("hook", "install", worktree.LibraryPath).code);

        StageAFinding(worktree.LibraryPath, worktree.Git);
        var (code, output) = worktree.Git("commit -m \"add a class\"");

        Assert.NotEqual(0, code);
        Assert.Contains("commit blocked", output);
    }

    [Fact]
    public void AHookInstalledFromOneWorktreeChecksTheWorktreeBeingCommitted()
    {
        // One hook serves every worktree. Baked in as an absolute path, the library checked was the
        // one it was installed from - clean here - so the finding committed in the other went through.
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;
        using var worktree = repo.AddWorktree();
        Assert.Equal(0, Cli.Run("hook", "install", repo.LibraryPath).code);

        StageAFinding(worktree.LibraryPath, worktree.Git);
        var (code, output) = worktree.Git("commit -m \"add a class\"");

        Assert.NotEqual(0, code);
        Assert.Contains("commit blocked", output);
        Assert.Contains("\"$TOP\"/\"Lib\"", File.ReadAllText(repo.HookPath));
    }

    [Fact]
    public void AHookAnOlderInstallLeftInAWorktreesOwnGitDirectoryIsReportedAndRemoved()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;
        using var worktree = repo.AddWorktree();
        Directory.CreateDirectory(Path.GetDirectoryName(worktree.OwnGitDirHookPath)!);
        File.WriteAllText(worktree.OwnGitDirHookPath, "#!/bin/sh\n# installed by `mlqt hook install` - safe to delete\n");

        var status = Cli.Run("hook", "status", worktree.LibraryPath);
        var uninstall = Cli.Run("hook", "uninstall", worktree.LibraryPath);

        Assert.Contains($"No pre-commit hook at {repo.HookPath}", status.stdout);
        Assert.Contains($"also at {worktree.OwnGitDirHookPath}, where git does not run it", status.stdout);
        Assert.Equal(0, uninstall.code);
        Assert.False(File.Exists(worktree.OwnGitDirHookPath));
    }

    [Fact]
    public void UninstallFromAWorktreeRemovesTheRepositorysHook()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;
        using var worktree = repo.AddWorktree();
        Cli.Run("hook", "install", repo.LibraryPath);

        Assert.Equal(0, Cli.Run("hook", "uninstall", worktree.LibraryPath).code);
        Assert.False(File.Exists(repo.HookPath));
    }

    // ---- without git on PATH -------------------------------------------------------------------

    [Fact]
    public void WithoutGit_AWorktreeIsFollowedToTheCommonDirectory_AndTheFallbackIsSaid()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;   // git is needed to make the worktree, not to read it
        using var worktree = repo.AddWorktree();

        var location = HookLocation.Resolve(worktree.LibraryPath, git: $"no-such-git-{Guid.NewGuid():N}");

        Assert.NotNull(location);
        Assert.Contains("git could not be run", location.FallbackReason);
        Assert.Equal(Path.GetDirectoryName(repo.HookPath), location.HooksDirectory);
        Assert.Equal(Path.GetDirectoryName(worktree.OwnGitDirHookPath), location.WorktreeHooksDirectory);
        Assert.Equal(worktree.Root, location.WorkingTreeRoot);
        Assert.False(location.IsRedirected);
    }

    [Fact]
    public void WithoutGit_APlainRepositoryIsItsDotGitDirectory()
    {
        using var repo = new TempRepo();

        var location = HookLocation.Resolve(repo.LibraryPath, git: $"no-such-git-{Guid.NewGuid():N}");

        Assert.NotNull(location);
        Assert.NotNull(location.FallbackReason);
        Assert.Equal(repo.HookPath, location.HookPath);
        Assert.Null(location.WorktreeHooksDirectory);
        Assert.Equal(repo.Root, location.WorkingTreeRoot);
    }

    [Fact]
    public void WithGit_TheLocationIsGitsAnswer_AndNothingIsSaidAboutAFallback()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;

        var location = HookLocation.Resolve(repo.LibraryPath);
        var (_, _, stderr) = Cli.Run("hook", "status", repo.LibraryPath);

        Assert.NotNull(location);
        Assert.Null(location.FallbackReason);
        Assert.Equal(repo.HookPath, location.HookPath);
        Assert.DoesNotContain("note:", stderr);
    }

    // ---- the hook script is a shell script (B48) -----------------------------------------------

    [Fact]
    public void ARefWithAShellMetacharacterIsEscaped_NotExpanded()
    {
        using var repo = new TempRepo();

        Cli.Run("hook", "install", repo.LibraryPath, "--changed-from", "origin/feature$x");

        var hook = File.ReadAllText(repo.HookPath);

        // Inside double quotes sh still expands $, so the unescaped version silently diffed against
        // "origin/feature" - a hook that checks the wrong thing rather than one that fails.
        Assert.Contains("--changed-from \"origin/feature\\$x\"", hook);
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
    public void APathWithNoLibraryInItIsRefused_AndTheRefusalNamesTheRepository()
    {
        // B488: the CLI test binaries' directory holds no library, and an install given it wrote a
        // hook into the repository enclosing it - MLQT's own - that failed every commit it saw.
        using var repo = new TempRepo();
        var notALibrary = Path.Combine(repo.Root, "bin");
        Directory.CreateDirectory(notALibrary);
        File.WriteAllText(Path.Combine(notALibrary, "mlqt.dll.config"), "not Modelica");

        var (code, _, stderr) = Cli.Run("hook", "install", notALibrary);

        Assert.Equal(2, code);
        Assert.Contains($"no Modelica library found in {notALibrary}", stderr);
        Assert.Contains($"repository at {repo.Root}", stderr);
        Assert.False(File.Exists(repo.HookPath));
    }

    [Fact]
    public void ASingleMoFileIsALibrary()
    {
        // The refusal uses check's discovery, so anything `mlqt check` accepts is accepted here.
        using var repo = new TempRepo();

        var (code, _, _) = Cli.Run("hook", "install", Path.Combine(repo.LibraryPath, "package.mo"));

        Assert.Equal(0, code);
        Assert.True(File.Exists(repo.HookPath));
    }

    [Fact]
    public void InstallSaysWhichRepositoryTheHookWentInto()
    {
        using var repo = new TempRepo();

        var (_, stdout, _) = Cli.Run("hook", "install", repo.LibraryPath);

        Assert.Contains($"Installed pre-commit hook: {repo.HookPath}", stdout);
        Assert.Contains($"in the repository at {repo.Root}.", stdout);
    }

    // ---- the guard that keeps these tests in their own repository (B488) -----------------------

    [Fact]
    public void TheTemporaryRepositoryIsInsideNoOtherWorkingCopy()
    {
        using var repo = new TempRepo();

        Assert.Null(StrayHookGuard.EnclosingWorkingCopy(repo.Root));
        Assert.Equal(repo.Root, StrayHookGuard.EnclosingWorkingCopy(repo.LibraryPath));
    }

    [Fact]
    public void AnInstallIntoAGuardedRepositoryFailsTheTest_AndIsRemoved()
    {
        // The guard stands in for MLQT's own repository here: a temp one is guarded, installed into,
        // and the guard has to both notice and clean up.
        using var repo = new TempRepo();
        var guard = new StrayHookGuard(repo.LibraryPath);
        Assert.Equal(0, Cli.Run("hook", "install", repo.LibraryPath).code);

        Assert.ThrowsAny<Exception>(guard.Dispose);
        Assert.False(File.Exists(repo.HookPath));
    }

    [Fact]
    public void AHookSomebodyElseWroteIsNeverRemovedByTheGuard()
    {
        using var repo = new TempRepo();
        var guard = new StrayHookGuard(repo.LibraryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(repo.HookPath)!);
        File.WriteAllText(repo.HookPath, "#!/bin/sh\necho mine\n");

        Assert.ThrowsAny<Exception>(guard.Dispose);           // still reported...
        Assert.True(File.Exists(repo.HookPath));              // ...but not deleted
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
