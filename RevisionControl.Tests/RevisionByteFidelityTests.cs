using System.Text;
using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// B264 — what comes back out of history is what went in.
/// </summary>
/// <remarks>
/// <para>Reading a revision used to decode as UTF-8: a default <c>StreamReader</c> over the git blob,
/// and <c>StandardOutputEncoding</c> on <c>svn cat</c>. Modelica files declare no encoding and the
/// population is mixed — older libraries are single-byte Windows-1252 — so an accented library
/// showed replacement characters on <b>both</b> sides of a diff of itself, and the bytes were gone
/// before anything could say otherwise.</para>
///
/// <para>The git half runs anywhere. The svn half needs a client and lives with the other
/// svn-dependent tests.</para>
/// </remarks>
public class RevisionByteFidelityTests : IDisposable
{
    private readonly GitRevisionControlSystem _git = new();
    private readonly List<string> _tempPaths = new();

    /// <summary>A line of Modelica a French or German library really would contain.</summary>
    private const string Accented = "model Café \"Température de l'eau — Übersicht\"\nend Café;\n";

    static RevisionByteFidelityTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static byte[] Windows1252(string text) =>
        CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(text);

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, recursive: true);
            }
            catch { }
        }
    }

    private (Repository Repo, string Path) RepoWith(string fileName, byte[] contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Fidelity_{Guid.NewGuid():N}");
        _tempPaths.Add(path);
        Repository.Init(path);

        var repo = new Repository(path);
        File.WriteAllBytes(Path.Combine(path, fileName), contents);
        Commands.Stage(repo, fileName);
        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit("add " + fileName, sig, sig);
        return (repo, path);
    }

    [Fact]
    public void Git_AWindows1252FileComesBackByteForByte()
    {
        var stored = Windows1252(Accented);
        var (repo, path) = RepoWith("Café.mo", stored);

        using (repo)
        {
            var bytes = _git.GetFileBytesAtRevision(path, "Café.mo", "HEAD");

            Assert.NotNull(bytes);
            Assert.Equal(stored, bytes);
        }
    }

    /// <summary>
    /// Stated separately because byte equality alone would still pass if the file were UTF-8: this
    /// is the case that used to lose information, and it says what was lost.
    /// </summary>
    [Fact]
    public void Git_TheAccentedCharactersAreNotReplacementCharacters()
    {
        var (repo, path) = RepoWith("Café.mo", Windows1252(Accented));

        using (repo)
        {
            var bytes = _git.GetFileBytesAtRevision(path, "Café.mo", "HEAD")!;

            // What the old code did, for comparison: decoding these bytes as UTF-8 cannot represent
            // them, so every accented character becomes U+FFFD and no caller can tell.
            Assert.Contains('�', Encoding.UTF8.GetString(bytes));

            // What the bytes actually say, read with the encoding they were written in.
            var text = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
            Assert.Equal(Accented, text);
            Assert.DoesNotContain('�', text);
        }
    }

    [Fact]
    public void Git_AUtf8FileIsUnaffected()
    {
        var stored = Encoding.UTF8.GetBytes(Accented);
        var (repo, path) = RepoWith("Utf8.mo", stored);

        using (repo)
        {
            Assert.Equal(stored, _git.GetFileBytesAtRevision(path, "Utf8.mo", "HEAD"));
        }
    }

    [Fact]
    public void Git_ABinaryFileIsNotMangled()
    {
        // A library's Resources folder holds images and compiled libraries, and a round trip through
        // any text decoding would corrupt them. Nothing in MLQT reads one this way today; the point
        // is that the primitive no longer makes that impossible.
        var stored = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF, 0xFE };
        var (repo, path) = RepoWith("icon.png", stored);

        using (repo)
        {
            Assert.Equal(stored, _git.GetFileBytesAtRevision(path, "icon.png", "HEAD"));
        }
    }

    [Fact]
    public void Git_AMissingFileIsStillNull()
    {
        var (repo, path) = RepoWith("Café.mo", Windows1252(Accented));

        using (repo)
        {
            Assert.Null(_git.GetFileBytesAtRevision(path, "NotHere.mo", "HEAD"));
        }
    }
}
