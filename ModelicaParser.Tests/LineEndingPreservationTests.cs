using System.Text;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

/// <summary>
/// B251 — a file is written back with the line endings it already had.
///
/// <para>Reported from real use: <b>Format All Files</b> made every file in a library show as
/// modified while <c>git diff</c> reported no differences at all. That combination is the signature
/// of a line-ending-only change on Windows. With <c>core.autocrlf=true</c> — the default on a
/// Windows checkout — git stores LF and checks out CRLF, so a file MLQT rewrites as LF cleans back
/// to LF and the command-line diff is empty; LibGit2Sharp, which is what MLQT's own status reads,
/// compares the bytes and calls every one of them modified. Thousands of files, nothing to review in
/// any of them.</para>
///
/// <para><b>The cause is structural, not accidental.</b> <c>ModelicaRenderer</c> builds its output
/// with <c>string.Join("\n", …)</c> and nothing has ever converted it back, so every save path has
/// always written LF whatever the file was. It is the same mistake the encoding had before
/// <see cref="ModelicaFileEncoding"/> existed — MLQT deciding how a user's file should look instead
/// of writing it back the way it found it — and it belongs in the same place for the same
/// reason.</para>
/// </summary>
public class LineEndingPreservationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-line-endings", Guid.NewGuid().ToString("N"));

    public LineEndingPreservationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Existing(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static (int Crlf, int LoneLf) Count(string path)
    {
        var text = File.ReadAllText(path);
        var crlf = 0;
        var lone = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                if (i > 0 && text[i - 1] == '\r') crlf++;
                else lone++;
            }
        return (crlf, lone);
    }

    /// <summary>What the renderer produces: LF, whatever the file on disk uses.</summary>
    private const string RenderedWithLf = "model M \"a\"\n  Real x;\nend M;\n";

    [Fact]
    public void ACrlfFileStaysCrlf()
    {
        // The case behind the report. Every file in a Windows checkout of a Modelica library looks
        // like this, and rewriting one as LF is a change git will not show you and MLQT will.
        var path = Existing("Crlf.mo", "model M \"a\"\r\n  Real x;\r\nend M;\r\n");

        ModelicaFileEncoding.WriteAllText(path, RenderedWithLf);

        var (crlf, lone) = Count(path);
        Assert.Equal(3, crlf);
        Assert.Equal(0, lone);
    }

    [Fact]
    public void AnLfFileStaysLf()
    {
        // The other direction matters just as much: a library kept with LF endings — every Linux
        // checkout, and a Windows one with autocrlf off — must not acquire CRLF.
        var path = Existing("Lf.mo", "model M \"a\"\n  Real x;\nend M;\n");

        ModelicaFileEncoding.WriteAllText(path, "model M \"a\"\r\n  Real y;\r\nend M;\r\n");

        var (crlf, lone) = Count(path);
        Assert.Equal(0, crlf);
        Assert.Equal(3, lone);
    }

    [Fact]
    public void TheFinalNewlineMatchesToo()
    {
        // B236 gave every file a final newline. On a CRLF file that newline is a CRLF, or the file
        // ends in a style of its own and the last line reads as changed.
        var path = Existing("End.mo", "model M \"a\"\r\nend M;\r\n");

        ModelicaFileEncoding.WriteAllText(path, "model M \"a\"\nend M;");

        Assert.EndsWith("\r\n", File.ReadAllText(path));
        Assert.Equal(0, Count(path).LoneLf);
    }

    [Fact]
    public void AMixedFileIsWrittenWithWhicheverItHasMost()
    {
        // Mixed files exist — a merge, or an editor that appended with the wrong style. There is no
        // right answer, so take the majority and make the file consistent rather than preserving the
        // mixture.
        var path = Existing("Mixed.mo", "a\r\nb\r\nc\r\nd\ne\r\n");

        ModelicaFileEncoding.WriteAllText(path, "a\nb\nc\n");

        Assert.Equal(0, Count(path).LoneLf);
    }

    [Fact]
    public void ANewFileKeepsWhatItWasGiven()
    {
        // Nothing to preserve, so nothing is invented. The renderer emits LF and that is what a new
        // file gets — the same on every platform, which matters because a repository is shared.
        var path = Path.Combine(_dir, "New.mo");

        ModelicaFileEncoding.WriteAllText(path, RenderedWithLf);

        var (crlf, lone) = Count(path);
        Assert.Equal(0, crlf);
        Assert.Equal(3, lone);
    }

    [Fact]
    public void RewritingAFileWithItsOwnContentChangesNothing()
    {
        // The property the report is really about: formatting a library that is already formatted
        // must leave every byte alone. Read the file, write it straight back, compare.
        var original = "within Lib;\r\nmodel M \"a\"\r\n  Real x;\r\nend M;\r\n";
        var path = Existing("Stable.mo", original);

        var read = ModelicaFileEncoding.ReadAllTextOnly(path);
        ModelicaFileEncoding.WriteAllText(path, read);

        Assert.Equal(Encoding.UTF8.GetBytes(original), File.ReadAllBytes(path));
    }

    [Fact]
    public void PackageOrderKeepsItsEndingsToo()
    {
        // package.order is written line by line, which used Environment.NewLine — so the same file
        // came out CRLF on Windows and LF on Linux, and a repository shared between them churned.
        var path = Existing("package.order", "A\r\nB\r\n");

        ModelicaFileEncoding.WriteAllLines(path, ["A", "B", "C"]);

        var (crlf, lone) = Count(path);
        Assert.Equal(3, crlf);
        Assert.Equal(0, lone);
    }

    [Fact]
    public void AnLfPackageOrderStaysLf()
    {
        var path = Existing("package.order", "A\nB\n");

        ModelicaFileEncoding.WriteAllLines(path, ["A", "B", "C"]);

        Assert.Equal(0, Count(path).Crlf);
    }

    [Fact]
    public async Task TheAsyncWriteAgrees()
    {
        // Two methods, one answer: the MCP server writes through the async one and must not produce
        // a different file from the desktop app.
        var path = Existing("Async.mo", "model M \"a\"\r\nend M;\r\n");

        await ModelicaFileEncoding.WriteAllTextAsync(path, "model M \"b\"\nend M;\n");

        Assert.Equal(0, Count(path).LoneLf);
    }

    [Fact]
    public void TheEncodingIsStillPreserved()
    {
        // Line endings are a second thing this method owns, not a replacement for the first.
        var latin1 = Encoding.Latin1;
        var path = Path.Combine(_dir, "Latin1.mo");
        File.WriteAllBytes(path, latin1.GetBytes("model M \"Krüger\"\r\nend M;\r\n"));

        ModelicaFileEncoding.WriteAllText(path, "model M \"Krüger\"\nend M;\n");

        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain((byte)0xC3, bytes);   // not re-encoded as UTF-8
        Assert.Equal(0, Count(path).LoneLf);
    }
}
