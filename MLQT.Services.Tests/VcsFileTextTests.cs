using System.Text;
using MLQT.Services.Helpers;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// B240 — a conflicted Windows-1252 file's accented characters survive the trip.
/// </summary>
/// <remarks>
/// <para>Both version control systems used to decode for their caller and both assumed UTF-8 —
/// Git through <c>Blob.GetContentText()</c>, SVN through <c>File.ReadAllText</c> on the
/// <c>.mine</c>/<c>.rN</c> sidecars — so a library written in Windows-1252 showed replacement
/// characters in its conflict diff exactly where the characters a reader cares about were.</para>
///
/// <para><b>The fix could not be to call the encoding funnel</b>: <c>RevisionControl</c> has no
/// project references and that is deliberate. It now returns the bytes it was given and MLQT decodes
/// them here, which says the same thing without giving that property up. These tests are that
/// decision stated twice — once as the behaviour, and once (the last one) as the property that made
/// it necessary.</para>
/// </remarks>
public class VcsFileTextTests
{
    /// <summary>The reported case: a single-byte encoding that is not UTF-8.</summary>
    private static byte[] Windows1252(string text) =>
        CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(text);

    static VcsFileTextTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void AWindows1252FileKeepsItsAccentedCharacters()
    {
        const string source = "model Café \"Température de l'eau\"\nend Café;";

        var decoded = VcsFileText.Decode(Windows1252(source));

        Assert.Equal(source, decoded);
        Assert.DoesNotContain('�', decoded!);   // the replacement character, which is what was shown
    }

    [Fact]
    public void AUtf8FileIsUnaffected()
    {
        // The common case, and the one a fallback must not break: most Modelica files are BOM-less
        // UTF-8, and mis-detecting one as Latin-1 would mangle every multi-byte character in it.
        const string source = "model M \"Übersicht — 温度\"\nend M;";

        Assert.Equal(source, VcsFileText.Decode(Encoding.UTF8.GetBytes(source)));
    }

    [Fact]
    public void AByteOrderMarkDoesNotSurviveAsACharacter()
    {
        // It decodes to a zero-width no-break space, which is invisible in the viewer and counts as
        // a difference in the diff - so one version having a BOM would read as a change to line 1.
        const string source = "model M\nend M;";
        var withBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(source)).ToArray();

        Assert.Equal(source, VcsFileText.Decode(withBom));
    }

    [Fact]
    public void NothingAndEmptyAreDifferentAnswers()
    {
        // "there is no such version" against "that version is empty": the conflict dialog shows a
        // pane for one and not for the other.
        Assert.Null(VcsFileText.Decode(null));
        Assert.Equal(string.Empty, VcsFileText.Decode([]));
    }

    /// <summary>
    /// The property that forced the shape of this fix: <c>RevisionControl</c> references nothing, so
    /// it cannot reach the encoding funnel and must not be taught to.
    /// </summary>
    /// <remarks>
    /// Asserted rather than left in a comment because the tempting fix is the one that breaks it —
    /// adding a reference to <c>ModelicaParser</c> makes the sidecar read a one-line change, and
    /// costs the one assembly in MLQT that knows nothing about Modelica.
    /// </remarks>
    [Fact]
    public void RevisionControlStillDependsOnNoOtherProjectOfOurs()
    {
        var referenced = typeof(SvnRevisionControlSystem).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .ToList();

        Assert.DoesNotContain("ModelicaParser", referenced);
        Assert.DoesNotContain("ModelicaGraph", referenced);
        Assert.DoesNotContain("MLQT.Services", referenced);
    }
}
