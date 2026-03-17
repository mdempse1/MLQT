using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// Shared fixture that creates a local test repository for all tests.
/// Creates a repository with Modelica files and commits that mirror the expected structure.
/// </summary>
public class GitTestRepositoryFixture : IDisposable
{
    public string ClonePath { get; }
    public bool RepositoryAvailable { get; }
    public string? CloneError { get; }

    public GitTestRepositoryFixture()
    {
        ClonePath = Path.Combine(Path.GetTempPath(), "GitAdvancedTest_Shared_" + Guid.NewGuid().ToString());

        // Create a local test repository
        try
        {
            CreateTestRepository();
            RepositoryAvailable = true;
        }
        catch (Exception ex)
        {
            CloneError = ex.Message;
            RepositoryAvailable = false;
        }
    }

    private void CreateTestRepository()
    {
        Directory.CreateDirectory(ClonePath);
        Repository.Init(ClonePath);

        using var repo = new Repository(ClonePath);
        var signature = new Signature("Test Author", "test@test.com", DateTimeOffset.Now);

        // Create initial structure (v1.0.0)
        Directory.CreateDirectory(Path.Combine(ClonePath, "Models"));

        // Create package.mo
        File.WriteAllText(Path.Combine(ClonePath, "package.mo"),
            "within;\npackage ModelicaEditorTests\n  extends Modelica.Icons.Package;\nend ModelicaEditorTests;\n");

        // Create README.md
        File.WriteAllText(Path.Combine(ClonePath, "README.md"),
            "# ModelicaEditorTests\n\nTest repository for ModelicaEditor tests.\n");

        // Create Models/package.mo
        File.WriteAllText(Path.Combine(ClonePath, "Models", "package.mo"),
            "within ModelicaEditorTests;\npackage Models\nend Models;\n");

        // Create Models/SimpleModel.mo (v1 - without z variable)
        File.WriteAllText(Path.Combine(ClonePath, "Models", "SimpleModel.mo"),
            "within ModelicaEditorTests.Models;\n\nmodel SimpleModel\n  Real x(start=0);\n  Real y(start=1);\nequation\n  der(x) = y;\n  der(y) = -x;\nend SimpleModel;\n");

        // Stage and commit all files
        Commands.Stage(repo, "*");
        var commit1 = repo.Commit("Initial commit", signature, signature);

        // Rename the default branch to "main" if it's not already
        var currentBranch = repo.Head;
        if (currentBranch.FriendlyName != "main")
        {
            var mainBranch = repo.Branches.Rename(currentBranch, "main");
        }

        // Create v1.0.0 tag
        repo.ApplyTag("v1.0.0");

        // Create v2.0.0 changes (add z variable)
        File.WriteAllText(Path.Combine(ClonePath, "Models", "SimpleModel.mo"),
            "within ModelicaEditorTests.Models;\n\nmodel SimpleModel \"New variable added\"\n  Real x(start=0);\n  Real y(start=1);\n  Real z(start=0) \"New variable\";\nequation\n  der(x) = y;\n  der(y) = -x;\n  der(z) = x + y;\nend SimpleModel;\n");

        Commands.Stage(repo, "*");
        var commit2 = repo.Commit("Add z variable to SimpleModel", signature, signature);

        // Create v2.0.0 tag
        repo.ApplyTag("v2.0.0");

        // Create feature-test branch at first commit
        repo.CreateBranch("feature-test", commit1);

        // Add more commits on main
        File.WriteAllText(Path.Combine(ClonePath, "README.md"),
            "# ModelicaEditorTests\n\nTest repository for ModelicaEditor tests.\n\n## Updated\nAdditional content.\n");

        Commands.Stage(repo, "*");
        repo.Commit("Update README", signature, signature);
    }

    public void Dispose()
    {
        ForceDeleteDirectory(ClonePath);
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                    // Continue even if we can't change attributes
                }
            }

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
