using System.Text.RegularExpressions;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// Nothing in the assemblies others ship obfuscated may ask an enum about itself (B502).
/// </summary>
/// <remarks>
/// <para>An obfuscator does not only rename an enum's members: under internalization, or with public
/// names renamed, it can delete them from metadata. The values still work - they compiled to numbers -
/// but <c>Enum.GetValues</c> returns nothing, <c>GetNames</c> nothing, <c>IsDefined</c> false,
/// <c>Parse</c> fails and <c>ToString()</c> gives a number. A consumer measured exactly this on a
/// five-member enum: 5 values plain, 0 protected. An array sized from <c>GetValues</c> then had length
/// zero, and a static initializer threw from inside a parallel loop. A reflection-based string-enum
/// converter builds its names the same way, so it writes numbers into files.</para>
/// <para>So in these seven assemblies an enum's members are listed where they are needed, and its
/// names spelled where they are written (<c>RuleSeverityText</c>, <c>WireNames</c>). Every test runs
/// on the plain build, where the metadata is all there, so only a rule over the source catches a new
/// call - which is what this is. A test may use them freely: it never runs protected.</para>
/// </remarks>
public class EnumMetadataPolicyTests
{
    private static readonly string[] Shipped =
    [
        "ModelicaParser", "ModelicaGraph", "RevisionControl", "DymolaInterface",
        "OpenModelicaInterface", "MLQT.Services", "MLQT.McpServer",
    ];

    private static readonly Regex EnumMetadata = new(
        @"\bEnum\s*\.\s*(GetValues|GetNames|GetName|IsDefined|Parse|TryParse|GetValuesAsUnderlyingType|Format|GetUnderlyingType)\b" +
        @"|\.GetEnum(Values|Names|Name|UnderlyingType)\s*\(" +
        @"|\bJsonStringEnumConverter\b|\bJsonStringEnumMemberName\b",
        RegexOptions.Compiled);

    [Fact]
    public void NoShippedCodeReadsAnEnumsMetadata()
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;
        var found = Shipped
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{separator}obj{separator}") && !f.Contains($"{separator}bin{separator}"))
            .SelectMany(f => File.ReadLines(f)
                .Select((line, i) => (File: Path.GetRelativePath(root, f), Line: i + 1, Text: line.Trim()))
                .Where(l => !l.Text.StartsWith("//", StringComparison.Ordinal) && EnumMetadata.IsMatch(l.Text)))
            .Select(l => $"{l.File}:{l.Line}: {l.Text}")
            .ToList();

        Assert.Empty(found);
    }

    [Theory]
    [InlineData("var all = Enum.GetValues<DeclarationKind>();")]
    [InlineData("var n = new int[Enum.GetValues(typeof(Kind)).Length];")]
    [InlineData("if (Enum.IsDefined(value)) return;")]
    [InlineData("Enum.TryParse<RuleSeverity>(text, out var s)")]
    [InlineData("typeof(Kind).GetEnumNames()")]
    [InlineData("[JsonConverter(typeof(JsonStringEnumConverter<Kind>))]")]
    public void ThePatternFindsEachShape(string line)
    {
        // Not hollow: the scan above passes on an empty match as well as on clean code.
        Assert.Matches(EnumMetadata, line);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not find the repository root (MLQT.slnx)");
    }
}
