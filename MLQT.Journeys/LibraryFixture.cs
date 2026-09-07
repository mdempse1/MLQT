using LibGit2Sharp;

namespace MLQT.Journeys;

/// <summary>
/// A small Modelica library in a Git working copy, on disk, for a journey to open.
/// </summary>
/// <remarks>
/// <para>Journeys need a real repository rather than mocks: they exercise the loader, the graph, the
/// style checker and the VCS status together, and every one of those reads the file system. The
/// library is deliberately small and deliberately imperfect — four classes, one of which breaks a
/// rule that is on by default, so "run a check and see a finding" has something to find.</para>
///
/// <para>There is no SVN fixture, for the reason <c>RevisionControl.Tests</c> already documents: SVN
/// integration needs a live working copy and a server no runner has. The Git side covers the same
/// pipeline.</para>
/// </remarks>
public sealed class LibraryFixture : IDisposable
{
    /// <summary>The working copy root, which is also the repository root.</summary>
    public string RepositoryPath { get; }

    /// <summary>The library directory inside it — the path a user would open.</summary>
    public string LibraryPath => Path.Combine(RepositoryPath, "Lib");

    /// <summary>A file that is modified relative to HEAD, so VCS status has something to report.</summary>
    public string ModifiedFile => Path.Combine(LibraryPath, "Modified.mo");

    public LibraryFixture()
    {
        RepositoryPath = Path.Combine(Path.GetTempPath(), "mlqt-journey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(LibraryPath);

        WriteLibrary();
        CommitEverything();

        // One uncommitted edit, so the working copy is not clean - the baseline classification has
        // nothing to classify against a pristine checkout, and neither has the "changed files only"
        // filter in Code Review. Deliberately badly laid out as well as changed, so a journey that
        // formats it has something to put right; a file that is already canonical would pass a
        // "formatting ran" assertion by doing nothing.
        Write("Modified.mo", """
            within Lib;
                model Modified    "The model the journey edits"
              Real y   "Another state";
                    equation
                der(y) =    -y;
            annotation(Documentation(info="<html><p>Edited by the fixture.</p></html>"));
                end Modified;
            """);
    }

    private void WriteLibrary()
    {
        // A package with three classes. Documented and named properly except where noted.
        Write("package.mo", """
            within;
            package Lib "A small library for the journey tests"
              annotation(Documentation(info="<html><p>The library the journeys open.</p></html>"));
            end Lib;
            """);

        Write("package.order", "Documented\nModified\nBadlyNamed\n");

        Write("Documented.mo", """
            within Lib;
            model Documented "A model with everything a rule could ask for"
              Real x "The state";
            equation
              der(x) = -x;
              annotation(Documentation(info="<html><p>Nothing to report here.</p></html>"));
            end Documented;
            """);

        Write("Modified.mo", """
            within Lib;
            model Modified "The model the journey edits"
              Real y "Another state";
            equation
              der(y) = -y;
              annotation(Documentation(info="<html><p>Edited by the fixture.</p></html>"));
            end Modified;
            """);

        // Lower-case class name: a naming-convention finding, on purpose, so a journey that runs a
        // check has something to see.
        Write("BadlyNamed.mo", """
            within Lib;
            model badly_named
              Real z;
            equation
              der(z) = -z;
            end badly_named;
            """);
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(LibraryPath, name), content.ReplaceLineEndings("\n"));

    private void CommitEverything()
    {
        Repository.Init(RepositoryPath);
        using var repo = new Repository(RepositoryPath);
        Commands.Stage(repo, "*");
        var who = new Signature("MLQT journeys", "journeys@mlqt.invalid", DateTimeOffset.Now);
        repo.Commit("The library as it stands before the journey edits it", who, who);
    }

    public void Dispose()
    {
        if (!Directory.Exists(RepositoryPath))
            return;

        // A git working copy has read-only files under .git that a plain recursive delete refuses.
        foreach (var file in Directory.EnumerateFiles(RepositoryPath, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        try { Directory.Delete(RepositoryPath, recursive: true); }
        catch (IOException) { /* a watcher still has a handle; the temp directory will be swept */ }
    }
}
