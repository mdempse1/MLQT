namespace ModelicaGraph;

/// <summary>
/// Whether a C or C++ header named by an external function's <c>Include</c> annotation is one the
/// compiler supplies, rather than one the library ships.
///
/// <para><b>Why this is asked (B172).</b> An <c>Include</c> is resolved against the library's
/// <c>Resources/Include</c> directory, so <c>#include &lt;stdio.h&gt;</c> resolved to
/// <c>&lt;library&gt;/Resources/Include/stdio.h</c>, which is never there, and every external
/// function that uses the C standard library reported a missing file. Nothing was wrong with the
/// library.</para>
///
/// <para><b>The delimiter is the declaration, and it comes first.</b> C already distinguishes the two
/// cases: <c>"foo.h"</c> is the including project's own header, <c>&lt;foo.h&gt;</c> is looked up on
/// the compiler's search path. MLQT is not a C compiler and does not know that path, so a
/// bracketed header it cannot find locally is not evidence of anything. That rule needs no list and
/// is not platform-specific, which is why it carries the weight here.</para>
///
/// <para>The name list below is the second line only, for libraries that write
/// <c>#include "math.h"</c> — common enough to matter, and unambiguous, because no library ships its
/// own <c>math.h</c>. It is deliberately not a complete index of either standard library: a header
/// that is missing from it is reported, which is the same outcome as before this existed, whereas a
/// wrong entry silences a real missing file. Under-listing is recoverable and over-listing is
/// not.</para>
/// </summary>
public static class StandardCHeaders
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // C89/99/11
        "assert.h", "complex.h", "ctype.h", "errno.h", "fenv.h", "float.h", "inttypes.h",
        "iso646.h", "limits.h", "locale.h", "math.h", "setjmp.h", "signal.h", "stdalign.h",
        "stdarg.h", "stdatomic.h", "stdbool.h", "stddef.h", "stdint.h", "stdio.h", "stdlib.h",
        "stdnoreturn.h", "string.h", "tgmath.h", "threads.h", "time.h", "uchar.h", "wchar.h",
        "wctype.h",

        // POSIX and Windows headers an external function reaches for often enough to be worth naming.
        "dlfcn.h", "fcntl.h", "pthread.h", "unistd.h", "sys/stat.h", "sys/time.h", "sys/types.h",
        "windows.h",

        // C++ headers with a .h name. The extensionless C++ ones (<vector>, <string>) never reach
        // here: ParseIncludeDirective only yields a name, and IsStandard is asked about a file that
        // was not found beneath Resources/Include, which an extensionless header would not be looked
        // for under either.
        "cassert", "cctype", "cerrno", "cfloat", "climits", "cmath", "cstddef", "cstdint",
        "cstdio", "cstdlib", "cstring", "ctime", "cwchar"
    };

    /// <summary>
    /// Whether an include that could not be found locally should be left unreported.
    /// </summary>
    /// <param name="header">The file name from the directive, e.g. <c>stdio.h</c>.</param>
    /// <param name="isSystemInclude">
    /// Whether the directive used angle brackets. True is sufficient on its own: the program has said
    /// the header is on the compiler's search path, and MLQT does not know that path.
    /// </param>
    public static bool IsSupplied(string header, bool isSystemInclude)
    {
        if (isSystemInclude)
            return true;

        // Normalise the separator so that "sys/types.h" matches however it was written.
        var name = header.Replace('\\', '/').Trim();
        return Names.Contains(name);
    }
}
