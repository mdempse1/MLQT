using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The code-behind policy, held to the source that is supposed to follow it.
///
/// <para><b>Why these exist.</b> CODING_GUIDELINES.md says component logic lives in a
/// <c>.razor.cs</c> partial class and an <c>@code { }</c> block is only for a component with no logic
/// worth testing. A rule stated in a document and enforced nowhere is the defect shape this backlog
/// has named more than once (B68, B72, B97, B101–B103): the document stays right and the code drifts
/// away from it, and nobody finds out until somebody reads both. Phase 7a-1 converted 31 components
/// in one sweep; without these, the thirty-second arrives in the old shape and nothing says so.</para>
///
/// <para><b>Why a size limit rather than a test for "logic".</b> The real rule is a judgement — does
/// this component carry anything worth a test — and no regex decides that. Length is a proxy, chosen
/// because it is unambiguous and because logic reaches it quickly: the largest block left after the
/// sweep is 25 lines of parameters and a colour switch, and the smallest thing converted was 36 lines
/// with a settings read and a null bug in it. A component that genuinely needs a longer block goes in
/// the ledger with a reason, which is where the judgement is recorded.</para>
/// </summary>
public class CodeBehindPolicyTests
{
    /// <summary>
    /// The repository root, found by walking up from the test binary. Null when the tests run from
    /// somewhere the sources are not.
    /// </summary>
    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.Shared", "_Imports.razor")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string SharedDirectory() =>
        Path.Combine(RepositoryRoot() ?? throw new InvalidOperationException("MLQT.Shared not found"),
                     "MLQT.Shared");

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(SharedDirectory(), pattern, SearchOption.AllDirectories)
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                          && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>Markup files, excluding _Imports.razor, which is directives and no component.</summary>
    private static IEnumerable<string> Markup() =>
        Files("*.razor").Where(p => Path.GetFileName(p) != "_Imports.razor");

    private static IEnumerable<string> CodeBehinds() => Files("*.razor.cs");

    private sealed record Ledger(int MaximumCodeBlockLines, Dictionary<string, LedgerEntry> Components);

    private sealed record LedgerEntry(string? Reason);

    private static Ledger ReadLedger()
    {
        var path = Path.Combine(RepositoryRoot()!, "build", "code-behind-exemptions.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var components = new Dictionary<string, LedgerEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.GetProperty("components").EnumerateObject())
        {
            components[entry.Name] = new LedgerEntry(
                entry.Value.TryGetProperty("reason", out var r) ? r.GetString() : null);
        }
        return new Ledger(root.GetProperty("maximumCodeBlockLines").GetInt32(), components);
    }

    /// <summary>
    /// The number of lines inside a component's <c>@code { }</c> block, or zero when it has none.
    /// </summary>
    private static int CodeBlockLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("@code"));
        if (start < 0)
            return 0;

        var end = Array.FindLastIndex(lines, l => l.Trim() == "}");
        return end > start ? end - start - 1 : 0;
    }

    [Fact]
    public void TheseChecksCanSeeBothHalvesOfTheSource()
    {
        // The guard that phase 7a-1 taught this repository to write. SharedUiConventionTests had one
        // of these and it still missed the sweep silently defeating all four of its checks, because
        // it counted *files* and *.razor still returned 39 of them after every line of C# had moved
        // out. Count the material each check below actually consumes: markup files, code-behind
        // files, and the ledger.
        Assert.NotNull(RepositoryRoot());
        Assert.True(Markup().Count() > 30, "Expected the markup half of MLQT.Shared.");
        Assert.True(CodeBehinds().Count() > 25,
            "Expected MLQT.Shared to be mostly code-behind files. If it is not, either the sweep was "
            + "reverted or these checks are looking in the wrong place.");
        Assert.True(ReadLedger().MaximumCodeBlockLines > 0, "Expected a readable exemptions ledger.");
    }

    [Fact]
    public void NoComponentKeepsALargeCodeBlock()
    {
        var ledger = ReadLedger();
        var offenders = new List<string>();

        foreach (var path in Markup())
        {
            var name = Path.GetFileName(path);
            var lines = CodeBlockLines(File.ReadAllText(path));
            if (lines <= ledger.MaximumCodeBlockLines || ledger.Components.ContainsKey(name))
                continue;
            offenders.Add($"{name} ({lines} lines)");
        }

        Assert.True(offenders.Count == 0,
            $"These components keep an @code block over {ledger.MaximumCodeBlockLines} lines. Component "
            + "logic belongs in a .razor.cs partial class, where a test can reach it without a renderer "
            + "and the coverage gate can see it — see CODING_GUIDELINES.md, Blazor Patterns. If the "
            + "component really is a display surface, add it to build/code-behind-exemptions.json with a "
            + "reason: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryExemptionCarriesAReason()
    {
        // The same rule the coverage ratchet applies to its own ledger, for the same reason: a debt
        // list whose entries do not say why is a list nobody can review.
        var unexplained = ReadLedger().Components
            .Where(e => string.IsNullOrWhiteSpace(e.Value.Reason))
            .Select(e => e.Key)
            .ToList();

        Assert.True(unexplained.Count == 0,
            "These exemptions from the code-behind policy carry no reason: "
            + string.Join(", ", unexplained));
    }

    [Fact]
    public void NoComponentWithACodeBehindStillUsesTheInjectDirective()
    {
        // @inject generates the property into the .razor.g.cs half, which a test cannot assign. A
        // component that has been converted but kept its directives is the shape that quietly undoes
        // the sweep: it looks converted and is not testable.
        var offenders = new List<string>();

        foreach (var codeBehind in CodeBehinds())
        {
            var markup = codeBehind[..^".cs".Length];
            if (!File.Exists(markup))
                continue;
            if (Regex.IsMatch(File.ReadAllText(markup), @"^@inject\b", RegexOptions.Multiline))
                offenders.Add(Path.GetFileName(markup));
        }

        Assert.True(offenders.Count == 0,
            "These components have a code-behind but still inject services with the @inject directive, "
            + "which puts the property in the generated half where a test cannot set it. Use "
            + "[Inject] properties in the .razor.cs instead: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoCodeBehindIsOrphaned()
    {
        // The failure the compiler cannot see. Rename or delete a .razor and leave its .razor.cs and
        // everything still builds: the partial class is now a whole class, in a namespace that looks
        // right, that no component is ever paired with. It reads as live component code and is dead.
        var orphans = CodeBehinds()
            .Where(p => !File.Exists(p[..^".cs".Length]))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(orphans.Count == 0,
            "These code-behind files have no .razor beside them, so the Razor compiler never pairs "
            + "them with a component and nothing they contain runs: " + string.Join(", ", orphans));
    }

    [Fact]
    public void EveryCodeBehindDeclaresTheRightPartialClassAndNamespace()
    {
        // Unlike the check above, a mismatch here is a build error rather than a silent one, so this
        // fires only when the tests run against sources the build has not seen. It is kept because
        // the compiler's message names neither the convention nor where it is written down, and
        // because it is what makes the convention checkable by reading rather than by guessing.
        var wrong = new List<string>();

        foreach (var path in CodeBehinds())
        {
            var text = File.ReadAllText(path);
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            var folder = Path.GetFileName(Path.GetDirectoryName(path)!);
            var expected = folder == "MLQT.Shared" ? "MLQT.Shared" : $"MLQT.Shared.{folder}";

            if (!Regex.IsMatch(text, $@"\bpartial class {Regex.Escape(name)}\b"))
                wrong.Add($"{Path.GetFileName(path)}: no 'partial class {name}'");
            if (!text.Contains($"namespace {expected};"))
                wrong.Add($"{Path.GetFileName(path)}: namespace is not {expected}");
        }

        Assert.True(wrong.Count == 0,
            "A code-behind must declare a partial class named after its file, in the namespace matching "
            + "its folder — that is the other half of the class the Razor compiler generates: "
            + string.Join("; ", wrong));
    }
}
