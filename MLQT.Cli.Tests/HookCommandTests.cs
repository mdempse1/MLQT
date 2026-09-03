using System.Diagnostics;
using MLQT.Cli;

namespace MLQT.Cli.Tests;

/// <summary>
/// Installing the pre-commit gate. The check itself is <c>mlqt check</c>; what is tested here is the
/// hook that runs it — that it lands where git looks, refuses to trample somebody else's, and blocks
/// a commit that would introduce findings.
/// </summary>
public class HookCommandTests
{
    private sealed class TempRepo : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mlqt-hook-" + Guid.NewGuid().ToString("N"));

        /// <summary>A repository with the library in a subdirectory, as a real one usually has.</summary>
        public TempRepo(bool initGit = true, string settings = ErrorOnMissingDescription)
        {
            Directory.CreateDirectory(LibraryPath);
            File.WriteAllText(System.IO.Path.Combine(LibraryPath, "package.mo"), GoodLibrary);
            File.WriteAllText(System.IO.Path.Combine(LibraryPath, "package.order"), "Good\n");
            Directory.CreateDirectory(System.IO.Path.Combine(Root, ".mlqt"));
            File.WriteAllText(System.IO.Path.Combine(Root, ".mlqt", "settings.json"), settings);

            if (initGit)
            {
                Git("init -q");
                Git("config user.email test@example.com");
                Git("config user.name Test");
                Git("add -A");
                Git("commit -qm initial");
            }
        }

        public string LibraryPath => System.IO.Path.Combine(Root, "Lib");
        public string HookPath => System.IO.Path.Combine(Root, ".git", "hooks", "pre-commit");

        public (int Code, string Output) Git(string arguments)
        {
            var info = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // The hook falls back to `mlqt` on PATH, because the process installing it here is a test
            // host rather than the tool. The build output holds mlqt.exe, so that is the PATH to give.
            info.Environment["PATH"] =
                AppContext.BaseDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        public void Dispose()
        {
            try
            {
                // git marks objects read-only, which stops a plain recursive delete.
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, recursive: true);
            }
            catch { /* best effort */ }
        }
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

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }

    // ---- installing ----------------------------------------------------------------------------

    [Fact]
    public void InstallWritesTheHookWhereGitLooksForIt()
    {
        using var repo = new TempRepo();

        var (code, stdout, _) = Run("hook", "install", repo.LibraryPath);

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

        Run("hook", "install", repo.LibraryPath);

        Assert.True(File.Exists(repo.HookPath));
    }

    [Fact]
    public void TheChosenOptionsAreBakedIntoTheHook()
    {
        using var repo = new TempRepo();

        Run("hook", "install", repo.LibraryPath, "--fail-on", "warning");

        var hook = File.ReadAllText(repo.HookPath);
        Assert.Contains("--fail-on warning", hook);   // as documented, not as the enum prints
    }

    [Fact]
    public void StatusSaysWhetherOneIsInstalled()
    {
        using var repo = new TempRepo();

        Assert.Contains("No pre-commit hook", Run("hook", "status", repo.LibraryPath).stdout);
        Run("hook", "install", repo.LibraryPath);
        Assert.Contains("mlqt pre-commit hook installed", Run("hook", "status", repo.LibraryPath).stdout);
    }

    // ---- not trampling anything ----------------------------------------------------------------

    [Fact]
    public void AHookSomebodyElseWroteIsLeftAlone()
    {
        using var repo = new TempRepo();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(repo.HookPath)!);
        File.WriteAllText(repo.HookPath, "#!/bin/sh\necho mine\n");

        var (installCode, _, installError) = Run("hook", "install", repo.LibraryPath);
        var (uninstallCode, _, uninstallError) = Run("hook", "uninstall", repo.LibraryPath);

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

        Assert.Equal(0, Run("hook", "install", repo.LibraryPath, "--force").code);
        Assert.Contains("\"$MLQT\" check", File.ReadAllText(repo.HookPath));
    }

    [Fact]
    public void UninstallRemovesOurs()
    {
        using var repo = new TempRepo();
        Run("hook", "install", repo.LibraryPath);

        Assert.Equal(0, Run("hook", "uninstall", repo.LibraryPath).code);
        Assert.False(File.Exists(repo.HookPath));
    }

    // ---- what it refuses -----------------------------------------------------------------------

    [Fact]
    public void OutsideAGitWorkingCopy_ItSaysSoRatherThanWritingNowhere()
    {
        using var repo = new TempRepo(initGit: false);

        var (code, _, stderr) = Run("hook", "install", repo.LibraryPath);

        Assert.Equal(2, code);
        Assert.Contains("not inside a git working copy", stderr);
        Assert.Contains("SVN runs its hooks on the server", stderr);
    }

    [Fact]
    public void AMissingLibraryIsRefused()
    {
        var (code, _, stderr) = Run("hook", "install", System.IO.Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid()));

        Assert.Equal(2, code);
        Assert.Contains("library not found", stderr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sideways")]
    public void AnUnknownActionIsRefused(string action)
    {
        var args = action.Length == 0 ? new[] { "hook" } : ["hook", action];

        var (code, _, stderr) = Run(args);

        Assert.Equal(2, code);
        Assert.Contains("install|uninstall|status", stderr);
    }

    // ---- and does it actually gate a commit -----------------------------------------------------

    [Fact]
    public void TheHookBlocksACommitThatWouldIntroduceAFinding()
    {
        using var repo = new TempRepo();
        if (!GitIsAvailable(repo)) return;   // no git, nothing to gate — see GitIsAvailable

        Run("hook", "install", repo.LibraryPath);

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

        Run("hook", "install", repo.LibraryPath);

        File.WriteAllText(System.IO.Path.Combine(repo.Root, "README.md"), "notes");
        repo.Git("add -A");
        var (code, output) = repo.Git("commit -m docs");

        Assert.Equal(0, code);
        Assert.DoesNotContain("mlqt: checking", output);
    }
}
