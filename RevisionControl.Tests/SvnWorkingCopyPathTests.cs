namespace RevisionControl.Tests;

/// <summary>
/// The shape of the paths <c>SvnRevisionControlSystem.GetWorkingCopyChanges</c> hands out: relative
/// to the working copy and forward-slashed, which is what Git reports on both platforms (B472).
///
/// <para>SVN built them with <c>Path.GetRelativePath</c>, so on Windows they carried backslashes and
/// every consumer was handed <c>Models\SimpleModel.mo</c> by one system and
/// <c>Models/SimpleModel.mo</c> by the other. The SVN merge dialog showed it; anything comparing the
/// path against one it built for itself would have missed.</para>
///
/// <para>A class fixture of its own, so its working copy is not one another class's tests edit.
/// Returns without asserting where svn is not installed, as the other classes on
/// <see cref="SvnWorkingCopyFixture"/> do. On Linux the old code already passed: this is a Windows
/// test in effect, which is where the defect was.</para>
/// </summary>
public class SvnWorkingCopyPathTests(SvnWorkingCopyFixture fixture) : IClassFixture<SvnWorkingCopyFixture>
{
    private readonly SvnRevisionControlSystem _svn = new();
    private readonly string _workingCopy = fixture.WorkingCopy ?? "";

    [Fact]
    public void ChangedFiles_AreRelativeAndForwardSlashed_IncludingThoseInsideAnUnversionedDirectory()
    {
        if (_workingCopy.Length == 0)
            return;

        // A versioned file in a subdirectory, reported by svn status itself...
        File.AppendAllText(Path.Combine(_workingCopy, "Models", "SimpleModel.mo"), "// edited\n");
        // ...and a file in an unversioned directory, which svn reports only as the directory and
        // GetWorkingCopyChanges expands by walking the disk - the second place a path is made.
        var newPackage = Path.Combine(_workingCopy, "NewPackage", "Inner");
        Directory.CreateDirectory(newPackage);
        File.WriteAllText(Path.Combine(newPackage, "Thing.mo"), "model Thing\nend Thing;\n");

        var paths = _svn.GetWorkingCopyChanges(_workingCopy).Select(c => c.Path).ToList();

        Assert.Contains("Models/SimpleModel.mo", paths);
        Assert.Contains("NewPackage/Inner/Thing.mo", paths);
        Assert.DoesNotContain(paths, p => p.Contains('\\'));
    }
}
