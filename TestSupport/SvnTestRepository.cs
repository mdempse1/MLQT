using System.ComponentModel;
using System.Diagnostics;

namespace MLQT.TestSupport;

/// <summary>
/// A Subversion repository of the test run's own, built with <c>svnadmin create</c> in a temporary
/// folder and deleted again on disposal - the SVN counterpart of <c>RevisionControl.Tests</c>'
/// <c>GitTestRepositoryFixture</c>.
/// </summary>
/// <remarks>
/// <para><b>Why every run has its own (B426).</b> The SVN integration tests used to share one
/// repository on the developer's machine, and a working copy of it, both at fixed paths. The tests
/// commit - to branches, but a commit anywhere moves HEAD - so two runs at the same time (two worktrees, two agents) moved it
/// under each other: <c>UpdateToLatest_AlreadyUpToDate_ReturnsNoChanges</c> read the other run's
/// commit as an update and failed, and passed again on its own. The shared repository had also
/// collected 22,000 revisions and 12,000 branches of earlier runs' debris, and on a machine without
/// it the tests returned before asserting anything and passed.</para>
///
/// <para>The layout is the one those tests were written against: <c>trunk</c> holding a small
/// Modelica library (<c>package.mo</c>, <c>README.txt</c>, <c>Models/package.mo</c>,
/// <c>Models/SimpleModel.mo</c>, <c>Models/TestModel.mo</c>) built up over several commits so there
/// is history to read, the tags <c>v1.0</c> and <c>v2.0</c> taken from it, and one branch,
/// <c>branches/feature-test</c>.</para>
///
/// <para>Needs <c>svnadmin</c> and <c>svn</c> on PATH. The classes filtered out where there is no
/// client take it as a class fixture and fail without one; the classes that run everywhere use
/// <see cref="SvnWorkingCopyFixture"/>, which asks <see cref="ToolsAvailable"/> first.</para>
///
/// <para><b>Linked into each suite that needs it</b> (<c>RevisionControl.Tests</c> and
/// <c>MLQT.Services.Tests</c>), as <c>InMemorySettingsService</c> is, rather than copied: the
/// repository a suite tests against is one decision, and <c>RepositoryServiceSvnIntegrationTests</c>
/// was the last user of the shared working copy when it had not been made (B471).</para>
/// </remarks>
public sealed class SvnTestRepository : IDisposable
{
    private static readonly Lazy<bool> Tools = new(() => CanRun("svnadmin") && CanRun("svn"));

    private readonly string _root;
    private int _checkouts;

    /// <summary>Whether <c>svnadmin</c> and <c>svn</c> can both be started from PATH.</summary>
    public static bool ToolsAvailable => Tools.Value;

    /// <summary>The repository's root URL, <c>file:///...</c>, with no trailing slash.</summary>
    public string RootUrl { get; }

    /// <summary><see cref="RootUrl"/> + <c>/trunk</c>.</summary>
    public string TrunkUrl => RootUrl + "/trunk";

    /// <summary>
    /// The last revision that changed trunk - "Add TestModel", which added
    /// <c>trunk/Models/TestModel.mo</c> - and so what a trunk working copy reports as its current
    /// revision. <see cref="Populate"/> decides it: the layout is r1, the six trunk commits r2-r7.
    /// </summary>
    public const long TrunkLastChangedRevision = 7;

    /// <summary>The repository's HEAD once built: the two tags (r8, r9) and the branch (r10).</summary>
    public const long HeadRevision = 10;

    /// <summary>The number of revisions in trunk's history: the layout and the six trunk commits.</summary>
    public const int TrunkHistoryLength = 7;

    public SvnTestRepository()
    {
        if (!ToolsAvailable)
            throw new InvalidOperationException(
                "These tests build their own Subversion repository and need svnadmin and svn on PATH.");

        _root = Path.Combine(Path.GetTempPath(), "MlqtSvnTest_" + Guid.NewGuid().ToString("N")[..12]);
        var repositoryPath = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_root);

        try
        {
            Run("svnadmin", "create", repositoryPath);
            RootUrl = new Uri(repositoryPath).AbsoluteUri.TrimEnd('/');
            Populate(Path.Combine(_root, "staging"));
        }
        catch
        {
            ForceDeleteDirectory(_root);
            throw;
        }
    }

    /// <summary>
    /// Checks <paramref name="relativeUrl"/> out into a new folder that is deleted with the
    /// repository, and returns the folder.
    /// </summary>
    public string CheckOut(string relativeUrl = "trunk")
    {
        var path = Path.Combine(_root, "wc" + Interlocked.Increment(ref _checkouts));
        Svn("checkout", RootUrl + "/" + relativeUrl, path);
        return path;
    }

    public void Dispose() => ForceDeleteDirectory(_root);

    private void Populate(string staging)
    {
        Svn("mkdir", TrunkUrl, RootUrl + "/branches", RootUrl + "/tags", "-m", "Create standard layout");
        Svn("checkout", TrunkUrl, staging);

        void Commit(string message, params (string Path, string Content)[] files)
        {
            foreach (var (relative, content) in files)
            {
                var full = Path.Combine(staging, relative);
                var isNew = !File.Exists(full);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
                if (isNew)
                    Svn("add", "--parents", full);
            }
            Svn("commit", staging, "-m", message);
        }

        Commit("Initial commit: Add README", ("README.txt", "Test SVN repository for MLQT.\n"));
        Commit("Add root package",
            ("package.mo", "within;\npackage ModelicaEditorTest\nend ModelicaEditorTest;\n"));
        Commit("Add Models package",
            (Path.Combine("Models", "package.mo"), "within ModelicaEditorTest;\npackage Models\nend Models;\n"));
        Commit("Add SimpleModel v1",
            (Path.Combine("Models", "SimpleModel.mo"),
             "within ModelicaEditorTest.Models;\nmodel SimpleModel\n  Real x;\nend SimpleModel;\n"));
        Commit("Update SimpleModel to v2",
            (Path.Combine("Models", "SimpleModel.mo"),
             "within ModelicaEditorTest.Models;\nmodel SimpleModel\n  Real x;\n  Real y \"Added variable\";\nend SimpleModel;\n"));
        Commit("Add TestModel",
            (Path.Combine("Models", "TestModel.mo"),
             "within ModelicaEditorTest.Models;\nmodel TestModel\n  Real value;\nend TestModel;\n"));

        Svn("copy", TrunkUrl, RootUrl + "/tags/v1.0", "-m", "Tag version 1.0");
        Svn("copy", TrunkUrl, RootUrl + "/tags/v2.0", "-m", "Tag version 2.0");
        Svn("copy", TrunkUrl, RootUrl + "/branches/feature-test", "-m", "Create feature branch");
    }

    private static void Svn(params string[] arguments) =>
        Run("svn", ["--non-interactive", .. arguments]);

    private static void Run(string tool, params string[] arguments)
    {
        var psi = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{tool} {string.Join(' ', arguments)} did not finish in 60s");
        }
        _ = stdout.Result;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{tool} {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}");
    }

    private static bool CanRun(string tool)
    {
        try
        {
            Run(tool, "--version", "--quiet");
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* delete what can be deleted */ }
            }
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A temporary folder left behind is not a test failure.
        }
    }
}

/// <summary>
/// A working copy of a run's own <see cref="SvnTestRepository"/>, for the classes that run
/// everywhere - including CI, which has no svn client. <see cref="WorkingCopy"/> is null when the
/// tools are absent, and the tests that need it call <see cref="RequireWorkingCopy"/>, which reports
/// them as skipped there. With the tools present it is always there, so on a developer's machine
/// these tests always assert.
/// </summary>
public sealed class SvnWorkingCopyFixture : IDisposable
{
    private readonly SvnTestRepository? _repository;

    /// <summary>A trunk working copy, or null when svn is not installed.</summary>
    public string? WorkingCopy { get; }

    public SvnWorkingCopyFixture()
    {
        if (!SvnTestRepository.ToolsAvailable)
            return;

        _repository = new SvnTestRepository();
        WorkingCopy = _repository.CheckOut();
    }

    /// <summary>
    /// The working copy, or the test is skipped where svn is not installed - reported as skipped,
    /// not passed. The classes on this fixture run on CI, which has no svn client, and used to
    /// <c>return</c> there: a test that had asserted nothing read as a pass (B481, B486). One helper
    /// for all of them, so every class says the same thing about the same absence.
    /// </summary>
    public string RequireWorkingCopy()
    {
        Assert.SkipUnless(WorkingCopy is not null, "svn is not installed, so there is no working copy to test against");
        return WorkingCopy!;
    }

    public void Dispose() => _repository?.Dispose();
}
