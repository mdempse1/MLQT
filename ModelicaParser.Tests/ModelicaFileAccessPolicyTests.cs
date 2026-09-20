using System.Text.RegularExpressions;

namespace ModelicaParser.Tests;

/// <summary>
/// The rule that every <c>.mo</c> and <c>package.order</c> read and write goes through
/// <see cref="ModelicaParser.Helpers.ModelicaFileEncoding"/>, held to the source that is supposed to
/// follow it (B239).
///
/// <para><b>Why this exists.</b> CLAUDE.md has stated that rule for a long time and nothing enforced
/// it. It held because people remembered, which is the defect shape this repository keeps producing:
/// a promise written down, no test behind it, and the code free to drift. B236 is what it looks like
/// when it goes wrong — two write paths disagreed about how a file ends for the whole life of the
/// project, and the thing that eventually noticed was a user reformatting a library.</para>
///
/// <para><b>Why a ledger rather than a clever regex.</b> No scan can tell whether the <c>path</c>
/// variable at a call site holds a <c>.mo</c> file or a settings JSON. So this does not try: it
/// finds every raw file-text API in production code and requires each one to be accounted for, with
/// a reason a human wrote once. A new raw read or write fails the test until somebody says which it
/// is. That puts the judgement where it belongs and keeps it visible, which is the same trade
/// <c>CodeBehindPolicyTests</c> makes with its size limit.</para>
///
/// <para>The count is part of the entry so that a second raw call appearing in a file that already
/// has one is not covered by the first one's reason.</para>
/// </summary>
public class ModelicaFileAccessPolicyTests
{
    /// <summary>
    /// The raw file-text APIs. <c>File.Exists</c>, <c>File.Create</c> used as a writability probe and
    /// the <c>Directory</c> family are not here: they do not read or write text, so they cannot get
    /// an encoding or a line ending wrong.
    /// </summary>
    private static readonly Regex RawFileAccess = new(
        @"\bFile\.(ReadAllText|ReadAllLines|WriteAllText|WriteAllLines|AppendAllText|AppendAllLines|WriteAllBytes|ReadAllBytes|OpenText|AppendText)(Async)?\b"
        + @"|\bnew\s+Stream(Reader|Writer)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Every accounted-for raw access, by repository-relative path, with how many the file has and
    /// why they are not Modelica source.
    ///
    /// <para><b>Adding to this is a decision, not a formality.</b> If the file being read or written
    /// is Modelica, the entry is wrong and the call should go through the encoding funnel instead.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Accounted = new(StringComparer.Ordinal)
    {
        // Reports and machine-readable output. Written for CI and for humans, never re-read as Modelica.
        ["MLQT.Cli/CheckRunner.cs"] = (1, "writes a report (console/JSON/SARIF/JUnit/…) to --out"),
        ["MLQT.Cli/CompareCommand.cs"] = (1, "writes the compare report to --out"),
        ["MLQT.Shared/Pages/CodeReview.razor.cs"] = (1, "exports the finding list as JSON"),
        ["MLQT.Shared/Pages/SelfTest.razor.cs"] = (2, "writes the self-test result as JSON; the StreamReader wraps a stream, not a path"),

        // The git hook is a shell script MLQT generates, with its own explicit encoding.
        ["MLQT.Cli/HookCommand.cs"] = (3, "reads and writes the git pre-commit hook script"),

        // Settings, baselines and metrics: JSON, all of it, and UTF-8 by definition.
        ["MLQT.Cli/SettingsResolver.cs"] = (1, "reads .mlqt/settings.json"),
        ["MLQT.McpServer/Services/HeadlessSettingsService.cs"] = (2, "reads and writes the MCP server's settings JSON"),
        ["MLQT.Services/Checking/Baseline.cs"] = (2, "reads and writes the accepted-debt baseline JSON"),
        ["MLQT.Services/Helpers/MetricsHistoryStore.cs"] = (2, "reads and writes the metrics history JSON"),
        ["MLQT.Services/JsonSettingsService.cs"] = (2, "reads and writes the application settings JSON"),
        ["MLQT.Services/MauiPreferencesFile.cs"] = (1, "reads the retired MAUI host's preferences JSON, once, on first run"),
        ["MLQT.Services/RepositoryService.cs"] = (2, "reads and writes a repository's .mlqt/settings.json"),
        ["MLQT.Services/StaticWebAssetManifest.cs"] = (1, "reads the RCL static web asset manifest JSON"),

        // Logs and word lists.
        ["MLQT.McpServer/Services/ToolUsageLogger.cs"] = (1, "appends a line to the tool usage log"),
        ["MLQT.Shared/Components/SettingsRepositoryDictionary.razor.cs"] = (1, "writes the staged custom dictionary word list"),

        // Not Modelica source, though both are Modelica-adjacent.
        ["MLQT.Services/EncryptedLibraryDetector.cs"] = (1, "reads libraryinfo.mos, a Modelica *script*, for a name and version"),
        ["ModelicaParser/ExternalDocs/DymolaHelpDocument.cs"] = (2, "reads a vendor's generated help HTML — see "
            + "skill-encrypted-libraries.md — and hashes the same file's bytes to detect a changed help set"),

        // Bytes, not text: there is no encoding to get wrong.
        ["MLQT.Services/LibraryDataService.cs"] = (1, "reads an image file's bytes for a data: URI"),

        // The picker reads a user-chosen file. PickModelicaFileAsync goes through the funnel (B239);
        // this is the other entry point, which takes a dictionary word list.
        ["MLQT.Photino/Services/PhotinoFilePickerService.cs"] = (1, "reads a picked .txt/.aff word list; "
            + "the Modelica entry point beside it uses ModelicaFileEncoding"),

        // Streams rather than paths: there is no file here to have an encoding.
        ["ModelicaParser/SpellChecking/SpellChecker.cs"] = (1, "wraps an embedded dictionary resource stream"),
        ["RevisionControl/GitRevisionControlSystem.cs"] = (1, "wraps a git blob stream"),

        // SVN plumbing.
        ["RevisionControl/SvnRevisionControlSystem.cs"] = (3, "writes the svn targets file (a list of paths); "
            + "the two reads take a conflicted file's .mine/.rN sidecars, which ARE Modelica source — "
            + "RevisionControl deliberately has no project references, so it cannot reach the funnel. "
            + "Read-only and shown in the conflict diff, so the cost is mojibake on a Windows-1252 "
            + "library rather than a corrupted file. Backlog B240"),
    };

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

    /// <summary>
    /// Production source only. Test projects, the journey harness and the test host build fixtures on
    /// purpose — a fixture that wrote through the funnel could not produce the malformed input a test
    /// needs.
    /// </summary>
    private static bool IsProduction(string relative) =>
        !relative.Contains(".Tests/", StringComparison.Ordinal)
        && !relative.StartsWith("MLQT.Journeys/", StringComparison.Ordinal)
        && !relative.StartsWith("MLQT.TestHost/", StringComparison.Ordinal)
        && !relative.Contains("/bin/", StringComparison.Ordinal)
        && !relative.Contains("/obj/", StringComparison.Ordinal)
        // The funnel itself, which is the one place allowed to call the OS.
        && relative != "ModelicaParser/Helpers/ModelicaFileEncoding.cs";

    /// <summary>
    /// The source with comment-only lines removed, so prose naming an API does not count as a call
    /// to it — this file's own rules are explained in comments that mention <c>File.ReadAllText</c>,
    /// and so is the picker that was changed to stop using it.
    ///
    /// <para><b>Whole lines only, deliberately.</b> Stripping a trailing comment would mean finding
    /// where a <c>//</c> stops being inside a string literal, and getting that wrong deletes real
    /// code from the scan — a false negative, which is the one kind of mistake a guard must not
    /// make. A trailing comment that names an API is left to produce a false positive, and the fix
    /// for one is to reword the comment.</para>
    /// </summary>
    private static string WithoutCommentLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                    && !trimmed.StartsWith("*", StringComparison.Ordinal)
                    && !trimmed.StartsWith("/*", StringComparison.Ordinal);
            }));

    /// <summary>Every production file with raw file-text access, and how many calls it makes.</summary>
    private static Dictionary<string, int> RawAccessByFile()
    {
        var root = RepositoryRoot();
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!IsProduction(relative))
                continue;

            var count = RawFileAccess.Matches(WithoutCommentLines(File.ReadAllText(path))).Count;
            if (count > 0)
                found[relative] = count;
        }

        return found;
    }

    [Fact]
    public void EveryRawFileAccessInProductionCodeIsAccountedFor()
    {
        var found = RawAccessByFile();

        var unaccounted = found.Keys.Where(f => !Accounted.ContainsKey(f))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(unaccounted.Count == 0,
            "These files read or write a file without going through ModelicaFileEncoding, and nothing "
            + "says what they are reading or writing. If it is Modelica source (.mo or package.order), "
            + "use the funnel — it preserves the file's encoding and ends it with a newline. If it is "
            + "not, add it to the ledger in this test with the reason:\n  "
            + string.Join("\n  ", unaccounted));
    }

    [Fact]
    public void NoFileHasGainedARawAccessItsEntryDoesNotCover()
    {
        var found = RawAccessByFile();

        var changed = found
            .Where(f => Accounted.TryGetValue(f.Key, out var entry) && entry.Count != f.Value)
            .Select(f => $"{f.Key}: ledger says {Accounted[f.Key].Count}, source has {f.Value}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(changed.Count == 0,
            "A file in the ledger has a different number of raw file accesses than it was recorded "
            + "with. A new one is not covered by the existing reason — check what it reads or writes, "
            + "then update the count:\n  " + string.Join("\n  ", changed));
    }

    [Fact]
    public void TheLedgerHasNoStaleEntries()
    {
        // A reason for a call that no longer exists is a reason nobody will question, and it makes
        // the ledger look more considered than it is.
        var found = RawAccessByFile();

        var stale = Accounted.Keys.Where(f => !found.ContainsKey(f))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0,
            "The ledger accounts for raw file access in files that no longer have any. Remove these "
            + "entries:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void EveryLedgerEntryGivesAReason()
    {
        var empty = Accounted.Where(e => string.IsNullOrWhiteSpace(e.Value.Reason)
                                      || e.Value.Reason.Contains("TODO", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Key).ToList();

        Assert.True(empty.Count == 0,
            "A ledger entry with no reason is an exception nobody has to justify: " + string.Join(", ", empty));
    }

    [Fact]
    public void TheScanFindsSomething()
    {
        // The guard against the guard: a regex that matched nothing, or a root that resolved
        // somewhere without sources, would make every test above pass while checking nothing.
        var found = RawAccessByFile();

        Assert.NotEmpty(found);
        Assert.Contains("MLQT.Services/JsonSettingsService.cs", found.Keys);
    }
}
