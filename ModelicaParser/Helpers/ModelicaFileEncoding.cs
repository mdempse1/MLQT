using System.Text;

namespace ModelicaParser.Helpers;

/// <summary>
/// Reads and writes Modelica source files with the encoding each file actually uses, and — the part
/// that matters — writes a file back in the same encoding it was read in.
///
/// <para><b>Why this exists.</b> There is no encoding declaration in a <c>.mo</c> file, and the
/// Modelica specification does not mandate one, so the population in the wild is mixed. Reading
/// everything as UTF-8 fails on the older libraries that use a single-byte Windows-1252 encoding for
/// curly quotes and accented characters: the decode produces replacement characters and the lexer
/// then reports syntax errors on a file that is perfectly valid. That is why the loader was changed
/// to <see cref="Encoding.Latin1"/>, which maps all 256 byte values and therefore cannot fail.</para>
///
/// <para>But Latin-1 is only safe for files that really are single-byte. A UTF-8 file <i>without</i>
/// a byte-order mark decodes to mojibake under it — "Krüger" becomes "KrÃ¼ger" — and that is not a
/// display problem, because MLQT writes files back. Reading two bytes as two characters and then
/// re-encoding those characters as UTF-8 produces four bytes, so a single formatting pass corrupts
/// the file and every later pass doubles the damage again. A BOM was no protection: it is the files
/// without one that are affected, and BOM-less UTF-8 is the norm for Modelica source.</para>
///
/// <para>So neither encoding is right for every file, and the choice cannot be made once for all of
/// them. It is made <b>per file, from its bytes</b>: a byte-order mark is believed; otherwise a
/// strict UTF-8 decode is attempted, and Latin-1 is the fallback when that fails. This keeps the
/// property that made Latin-1 attractive — no input can make it throw or produce replacement
/// characters — while decoding the BOM-less UTF-8 files correctly. Pure ASCII, which is the vast
/// majority of Modelica source, is identical under both and needs no thought.</para>
///
/// <para><b>Always write through <see cref="WriteAllText"/> or with an encoding this class
/// returned.</b> A read here paired with a plain <c>File.WriteAllText</c> re-encodes as UTF-8
/// whatever was decoded, which is precisely the corruption described above.</para>
/// </summary>
public static class ModelicaFileEncoding
{
    /// <summary>
    /// What a new file is written as. UTF-8 without a byte-order mark: the encoding Modelica
    /// tooling produces, and the one a BOM-less reader is guaranteed to handle.
    /// </summary>
    public static Encoding Default { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// A UTF-8 decoder that throws rather than substituting replacement characters, so that
    /// "is this valid UTF-8" can be asked as a question instead of guessed at from the output.
    /// </summary>
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads a file, returning its text and the encoding it turned out to be in. Pass that encoding
    /// back to <see cref="WriteAllText"/> to write the file without changing its bytes needlessly.
    /// </summary>
    public static (string Text, Encoding Encoding) ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectFromBytes(bytes);
        return (Decode(bytes, encoding), encoding);
    }

    /// <summary>
    /// Reads a file's text, discarding the encoding. For callers that only display or parse the
    /// content and will never write it back.
    /// </summary>
    public static string ReadAllTextOnly(string path) => ReadAllText(path).Text;

    /// <summary>
    /// Reads a file's lines and the encoding it was in. Line endings are not normalised — the
    /// split accepts CRLF, LF and CR alike, and no terminator is invented for the final line.
    /// </summary>
    public static (string[] Lines, Encoding Encoding) ReadAllLines(string path)
    {
        var (text, encoding) = ReadAllText(path);
        return (SplitLines(text), encoding);
    }

    /// <summary>Reads a file's lines, discarding the encoding.</summary>
    public static string[] ReadAllLinesOnly(string path) => ReadAllLines(path).Lines;

    /// <summary>
    /// Async counterparts, so call sites that already await their file access keep doing so rather
    /// than acquiring a <c>Task.Run</c> wrapper just to reach this class.
    /// </summary>
    public static async Task<(string Text, Encoding Encoding)> ReadAllTextAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var encoding = DetectFromBytes(bytes);
        return (Decode(bytes, encoding), encoding);
    }

    /// <inheritdoc cref="ReadAllTextAsync"/>
    public static async Task<string> ReadAllTextOnlyAsync(string path) =>
        (await ReadAllTextAsync(path)).Text;

    /// <inheritdoc cref="WriteAllText"/>
    public static async Task WriteAllTextAsync(string path, string text, Encoding? encoding = null)
    {
        await File.WriteAllTextAsync(path, text, encoding ?? EncodingToWrite(path, text));
    }

    /// <summary>
    /// Writes text to a file.
    ///
    /// <para>When <paramref name="encoding"/> is null the existing file's encoding is preserved, so
    /// a round trip through MLQT does not silently rewrite a library in a different encoding from
    /// the one its authors chose. A file that does not exist yet is written as
    /// <see cref="Default"/>.</para>
    /// </summary>
    public static void WriteAllText(string path, string text, Encoding? encoding = null)
    {
        File.WriteAllText(path, text, encoding ?? EncodingToWrite(path, text));
    }

    /// <summary>
    /// The encoding to write <paramref name="text"/> to <paramref name="path"/> with, preserving
    /// whatever the file already used.
    ///
    /// <para>Deciding that normally means reading the file back to test whether it is valid UTF-8.
    /// When the text being written is pure ASCII — which nearly all Modelica source is — that work
    /// is unnecessary: ASCII encodes to the same bytes under both UTF-8 and Latin-1, so only a
    /// byte-order mark can distinguish the outcomes, and the first few bytes settle it. This keeps
    /// a full-library reformat from re-reading every file it is about to overwrite.</para>
    /// </summary>
    private static Encoding EncodingToWrite(string path, string text)
    {
        if (!IsAscii(text))
            return DetectExisting(path);

        try
        {
            if (!File.Exists(path))
                return Default;

            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[3];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return DetectFromBytes(head[..read]);
        }
        catch (IOException)
        {
            return Default;
        }
        catch (UnauthorizedAccessException)
        {
            return Default;
        }
    }

    private static bool IsAscii(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Writes lines to a file, joined with the platform's newline, preserving the existing file's
    /// encoding unless one is given. Mirrors <c>File.WriteAllLines</c>, which appends a trailing
    /// newline.
    /// </summary>
    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding? encoding = null)
    {
        File.WriteAllLines(path, lines, encoding ?? DetectExisting(path));
    }

    /// <summary>
    /// The encoding of an existing file, or <see cref="Default"/> when it does not exist or cannot
    /// be read. Reading the file to answer this is deliberate: the alternative — assuming — is what
    /// corrupts it.
    /// </summary>
    public static Encoding DetectExisting(string path)
    {
        try
        {
            return File.Exists(path) ? DetectFromBytes(File.ReadAllBytes(path)) : Default;
        }
        catch (IOException)
        {
            return Default;
        }
        catch (UnauthorizedAccessException)
        {
            return Default;
        }
    }

    /// <summary>
    /// Works out how a file's bytes are encoded.
    ///
    /// <para>A byte-order mark is conclusive and is believed. Without one, a strict UTF-8 decode
    /// decides: multi-byte UTF-8 sequences are structurally distinctive, so a file that decodes
    /// cleanly as UTF-8 is UTF-8 in all but vanishingly rare cases, while a single-byte encoding
    /// with any high-bit character will fail the decode almost immediately. Latin-1 is the fallback
    /// precisely because it cannot fail, which makes the whole chain total: every possible input
    /// yields text, and none yields replacement characters.</para>
    /// </summary>
    public static Encoding DetectFromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Utf8WithBom;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        try
        {
            StrictUtf8.GetString(bytes);
            return Default;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    /// <summary>
    /// Decodes bytes with the given encoding, dropping the byte-order mark so it does not appear as
    /// a stray character at the start of the text — which in a Modelica file lands immediately
    /// before the <c>within</c> keyword and fails the parse.
    /// </summary>
    private static string Decode(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        return preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)
            ? encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length)
            : encoding.GetString(bytes);
    }

    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
            return [];

        var lines = text.Split('\n');

        // A trailing newline ends the last line rather than starting an empty one, matching
        // File.ReadAllLines.
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var result = new string[count];
        for (var i = 0; i < count; i++)
            result[i] = lines[i].TrimEnd('\r');

        return result;
    }
}
