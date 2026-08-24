using System.Text;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// Tests for <see cref="ModelicaFileEncoding"/> — reading Modelica source in whatever encoding it
/// happens to be in, and writing it back unchanged.
/// </summary>
public class ModelicaFileEncodingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-encoding-tests", Guid.NewGuid().ToString("N"));

    public ModelicaFileEncodingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // "Krüger" — the non-ASCII character that distinguishes the encodings.
    private const string Text = "package P \"Krüger\"\nend P;\n";

    private static byte[] Utf8NoBom(string text) => new UTF8Encoding(false).GetBytes(text);
    private static byte[] Utf8WithBom(string text) => new UTF8Encoding(true).GetPreamble()
        .Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    #region Detection

    [Fact]
    public void ReadAllText_Utf8WithoutBom_DecodesCorrectly()
    {
        // The case that was silently mangled: BOM-less UTF-8 is the norm for Modelica source, and
        // reading it as Latin-1 turns "Krüger" into "KrÃ¼ger".
        var path = Write("utf8.mo", Utf8NoBom(Text));

        var (text, _) = ModelicaFileEncoding.ReadAllText(path);

        Assert.Equal(Text, text);
        Assert.Contains("Krüger", text);
    }

    [Fact]
    public void ReadAllText_Utf8WithBom_DecodesCorrectlyAndDropsTheMark()
    {
        // A byte-order mark left in the text sits immediately before `package` and fails the parse.
        var path = Write("bom.mo", Utf8WithBom(Text));

        var (text, _) = ModelicaFileEncoding.ReadAllText(path);

        Assert.Equal(Text, text);
        Assert.False(text.StartsWith('﻿'));
    }

    [Fact]
    public void ReadAllText_SingleByteEncoding_DecodesWithoutReplacementCharacters()
    {
        // The case Latin-1 was originally chosen for: older libraries store curly quotes and
        // accented characters as single high bytes, which are not valid UTF-8.
        var path = Write("latin1.mo", Latin1(Text));

        var (text, encoding) = ModelicaFileEncoding.ReadAllText(path);

        Assert.Equal(Text, text);
        Assert.DoesNotContain('�', text);
        Assert.Equal(Encoding.Latin1, encoding);
    }

    [Fact]
    public void ReadAllText_Windows1252CurlyQuote_DoesNotFail()
    {
        // 0x92 is a right single quote in Windows-1252 and invalid on its own in UTF-8. It must
        // decode to *something* rather than throwing or producing a replacement character, because
        // the alternative is a spurious syntax error on a valid file.
        var path = Write("cp1252.mo", [.. "package P \"it"u8.ToArray(), 0x92, .. "s\"\nend P;\n"u8.ToArray()]);

        var (text, encoding) = ModelicaFileEncoding.ReadAllText(path);

        Assert.DoesNotContain('�', text);
        Assert.Equal(Encoding.Latin1, encoding);
    }

    [Fact]
    public void ReadAllText_PureAscii_IsReadAsUtf8()
    {
        // Identical under either encoding; recording UTF-8 means a later edit that introduces a
        // non-ASCII character is stored the way the rest of the world expects.
        var path = Write("ascii.mo", Utf8NoBom("package P \"plain\"\nend P;\n"));

        var (_, encoding) = ModelicaFileEncoding.ReadAllText(path);

        Assert.Equal(ModelicaFileEncoding.Default.WebName, encoding.WebName);
    }

    [Fact]
    public void ReadAllText_Empty_IsHandled()
    {
        var path = Write("empty.mo", []);

        var (text, _) = ModelicaFileEncoding.ReadAllText(path);

        Assert.Equal(string.Empty, text);
    }

    #endregion

    #region Round trips — the corruption this prevents

    [Theory]
    [InlineData("utf8")]
    [InlineData("bom")]
    [InlineData("latin1")]
    public void ReadThenWrite_LeavesTheFileByteIdentical(string kind)
    {
        // The whole point. Reading in one encoding and writing in another re-encodes the decoded
        // characters: "ü" read as Latin-1 (2 chars) and written as UTF-8 becomes 4 bytes, and every
        // later save doubles it again.
        var original = kind switch
        {
            "utf8" => Utf8NoBom(Text),
            "bom" => Utf8WithBom(Text),
            _ => Latin1(Text)
        };
        var path = Write($"roundtrip-{kind}.mo", original);

        var (text, encoding) = ModelicaFileEncoding.ReadAllText(path);
        ModelicaFileEncoding.WriteAllText(path, text, encoding);

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllText_WithoutAnEncoding_PreservesWhatTheFileAlreadyUsed()
    {
        // Callers that never read the file (a renderer producing new content for an existing path)
        // must still not change its encoding underneath its authors.
        var original = Latin1(Text);
        var path = Write("preserve.mo", original);

        ModelicaFileEncoding.WriteAllText(path, Text);

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteAllText_NewFile_UsesUtf8WithoutABom()
    {
        var path = Path.Combine(_root, "new.mo");

        ModelicaFileEncoding.WriteAllText(path, Text);

        Assert.Equal(Utf8NoBom(Text), File.ReadAllBytes(path));
    }

    [Fact]
    public void RepeatedWrites_DoNotAccumulateDamage()
    {
        // The failure mode was progressive: 2 bytes became 4, then 8. Saving repeatedly is the
        // normal way a formatted repository is used, so it has to be a fixed point.
        var path = Write("repeat.mo", Utf8NoBom(Text));

        for (var i = 0; i < 5; i++)
        {
            var (text, encoding) = ModelicaFileEncoding.ReadAllText(path);
            ModelicaFileEncoding.WriteAllText(path, text, encoding);
        }

        Assert.Equal(Utf8NoBom(Text), File.ReadAllBytes(path));
        Assert.Contains("Krüger", ModelicaFileEncoding.ReadAllTextOnly(path));
    }

    #endregion

    #region Lines

    [Fact]
    public void ReadAllLines_SplitsOnEitherLineEndingAndDropsTheTrailingBlank()
    {
        var path = Write("order", Utf8NoBom("Alpha\r\nBeta\nGamma\n"));

        var (lines, _) = ModelicaFileEncoding.ReadAllLines(path);

        Assert.Equal(["Alpha", "Beta", "Gamma"], lines);
    }

    [Fact]
    public void ReadAllLines_NoTrailingNewline_KeepsTheLastLine()
    {
        var path = Write("order-no-eol", Utf8NoBom("Alpha\nBeta"));

        Assert.Equal(["Alpha", "Beta"], ModelicaFileEncoding.ReadAllLinesOnly(path));
    }

    [Fact]
    public void WriteAllLines_PreservesTheExistingEncoding()
    {
        var path = Write("order-latin1", Latin1("Über\n"));

        ModelicaFileEncoding.WriteAllLines(path, ["Über"]);

        Assert.Equal(Encoding.Latin1, ModelicaFileEncoding.DetectExisting(path));
        Assert.Equal("Über", ModelicaFileEncoding.ReadAllLinesOnly(path).Single());
    }

    #endregion
}
