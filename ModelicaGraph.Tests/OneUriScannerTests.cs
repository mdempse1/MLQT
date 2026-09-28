using System.Text.RegularExpressions;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That there is exactly one implementation of the <c>modelica://</c> URI scan (B209).
///
/// <para><b>Why this is a test.</b> <c>ExternalResourceExtractor</c> and <c>ModelAnalyzer</c> held
/// byte-for-byte identical copies of the scanning loop and of the "does this name a file" check. The
/// graph build uses the second, so a fix applied to the first compiled, passed its own tests, and
/// changed nothing a user could see — the Modelica Standard Library went on reporting the same two
/// phantom missing files. That cost a full round of "the fix did not work".</para>
///
/// <para>Both now call <c>ModelicaUriScanner</c>. This reads the sources rather than the behaviour,
/// because a second copy is not a behaviour difference until the two drift, which is exactly when it
/// is expensive.</para>
/// </summary>
public class OneUriScannerTests
{
    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Theory]
    [InlineData("ModelicaParser/Visitors/ExternalResourceExtractor.cs")]
    [InlineData("ModelicaGraph/ParserVisitors/ModelAnalyzer.cs")]
    public void NoVisitorScansForTheSchemeItself(string relativePath)
    {
        var root = RepositoryRoot();
        if (root is null)
            return;

        var source = File.ReadAllText(Path.Combine(root, relativePath));
        var code = Regex.Replace(source, @"//.*", string.Empty);

        Assert.DoesNotContain("IndexOf(\"modelica://\"", code, StringComparison.Ordinal);
    }
}
