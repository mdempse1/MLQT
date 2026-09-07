using MLQT.Services.Helpers;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="ModelicaPackageSaver.ResolveSaveDirectory"/>, lifted out of <c>MainLayout</c> in
/// phase 7a-4.
///
/// <para>It decides where a full save writes a user's library. The three shapes differ by one
/// directory level, and getting that level wrong does not fail — it writes a second copy of the
/// library one folder in, or scatters a package's files across its parent.</para>
/// </summary>
public class ResolveSaveDirectoryTests
{
    private static string? Resolve(string? sourcePath, string[] files, string[] directories) =>
        ModelicaPackageSaver.ResolveSaveDirectory(
            sourcePath,
            p => files.Contains(p, StringComparer.OrdinalIgnoreCase),
            p => directories.Contains(p, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void ASingleFileLibrary_IsWrittenBesideItself()
    {
        var resolved = Resolve(@"C:\libs\Thing.mo",
            files: [@"C:\libs\Thing.mo"], directories: [@"C:\libs"]);

        Assert.Equal(@"C:\libs", resolved);
    }

    [Fact]
    public void APackageDirectory_IsWrittenToItsParent()
    {
        // The saver creates the library folder itself. Handing it the package directory nests a
        // second copy of the library inside the first.
        var resolved = Resolve(@"C:\libs\Lib",
            files: [@"C:\libs\Lib\package.mo"], directories: [@"C:\libs\Lib", @"C:\libs"]);

        Assert.Equal(@"C:\libs", resolved);
    }

    [Fact]
    public void ADirectoryOfLooseClasses_IsWrittenToItself()
    {
        // No package.mo means no library folder to create, so writing to the parent would scatter
        // the classes beside the directory rather than into it.
        var resolved = Resolve(@"C:\libs\Loose",
            files: [], directories: [@"C:\libs\Loose", @"C:\libs"]);

        Assert.Equal(@"C:\libs\Loose", resolved);
    }

    [Fact]
    public void ASourcePathThatIsGone_ResolvesToNothing()
    {
        // Rather than guessing at a location to write a user's library to.
        Assert.Null(Resolve(@"C:\libs\Vanished", files: [], directories: []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoSourcePathAtAll_ResolvesToNothing(string? sourcePath)
    {
        Assert.Null(Resolve(sourcePath, files: [], directories: []));
    }

    [Fact]
    public void APackageWhoseParentIsGone_ResolvesToNothing()
    {
        // The parent is where it would be written, so if that is not there the answer is no answer.
        var resolved = Resolve(@"C:\libs\Lib",
            files: [@"C:\libs\Lib\package.mo"], directories: [@"C:\libs\Lib"]);

        Assert.Null(resolved);
    }

    [Fact]
    public void AFileIsCheckedBeforeADirectoryOfTheSameName()
    {
        // A path that is both cannot happen on a real file system, but the order states the intent:
        // a single-file library is the more specific reading.
        var resolved = Resolve(@"C:\libs\Thing.mo",
            files: [@"C:\libs\Thing.mo"], directories: [@"C:\libs\Thing.mo", @"C:\libs"]);

        Assert.Equal(@"C:\libs", resolved);
    }
}
