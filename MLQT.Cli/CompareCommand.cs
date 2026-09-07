using System.Text;
using System.Text.Json;

namespace MLQT.Cli;

/// <summary>
/// A class present in one library and not the other, with the names in the other library that could
/// be where it went.
/// </summary>
/// <param name="Candidates">Classes on the other side that are new there and share this one's simple
/// name. Restricted to the new ones on purpose: in a library the size of MSL a dozen packages hold a
/// class called <c>Interfaces</c>, and matching against everything would suggest a move for every
/// name in the list. A class that is new on the other side and carries the same simple name is a
/// genuinely useful lead — most often the same class re-rooted because its <c>within</c> clause
/// changed.</param>
internal sealed record ClassDifference(ClassEntry Entry, IReadOnlyList<string> Candidates);

/// <summary>The result of comparing two libraries' class inventories.</summary>
internal sealed record CompareReport(
    ClassInventory Left,
    ClassInventory Right,
    IReadOnlyList<ClassDifference> Missing,
    IReadOnlyList<ClassDifference> Added)
{
    /// <summary>
    /// Builds the report. Comparison is by full Modelica name and nothing else, so moving a class out
    /// of a package file and into a file of its own is invisible here — which is the point.
    /// </summary>
    public static CompareReport Build(ClassInventory left, ClassInventory right)
    {
        var missingNames = left.Classes.Keys.Where(name => !right.Classes.ContainsKey(name)).ToList();
        var addedNames = right.Classes.Keys.Where(name => !left.Classes.ContainsKey(name)).ToList();

        var addedBySimpleName = Index(addedNames, right);
        var missingBySimpleName = Index(missingNames, left);

        return new CompareReport(
            left, right,
            Describe(missingNames, left, addedBySimpleName),
            Describe(addedNames, right, missingBySimpleName));
    }

    private static ILookup<string, string> Index(IEnumerable<string> names, ClassInventory side) =>
        names.ToLookup(name => side.Classes[name].SimpleName, StringComparer.Ordinal);

    private static List<ClassDifference> Describe(
        IEnumerable<string> names, ClassInventory side, ILookup<string, string> candidatesBySimpleName) =>
        names
            .Select(name => side.Classes[name])
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .Select(entry => new ClassDifference(
                entry,
                [.. candidatesBySimpleName[entry.SimpleName].OrderBy(name => name, StringComparer.Ordinal)]))
            .ToList();
}

/// <summary>
/// The `compare` command: load two copies of a library and say which classes the second one no longer
/// has.
///
/// <para>Written for the case where a bulk edit — a reformat, a restructure, a merge — is suspected of
/// having lost classes. A class count tells you that something went; only a name-by-name comparison
/// tells you what, and the file layout is free to have changed completely in between.</para>
/// </summary>
internal static class CompareCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!CompareOptions.TryParse(args, out var opts, out var error))
        {
            stderr.WriteLine($"error: {error}");
            stderr.WriteLine(CliEntry.Usage);
            return ExitCodes.Error;
        }

        // Both paths are checked before either is loaded: the libraries this is aimed at take minutes
        // to read, and finding out then that the second path was mistyped wastes all of it.
        if (!ClassInventory.ValidatePath(opts!.LeftPath, stderr) ||
            !ClassInventory.ValidatePath(opts.RightPath, stderr))
            return ExitCodes.Error;

        stderr.WriteLine($"note: loading {opts.LeftPath}");
        var left = await ClassInventory.LoadAsync(opts.LeftPath, stderr);
        if (left is null)
            return ExitCodes.Error;

        stderr.WriteLine($"note: loading {opts.RightPath}");
        var right = await ClassInventory.LoadAsync(opts.RightPath, stderr);
        if (right is null)
            return ExitCodes.Error;

        var report = CompareReport.Build(left, right);
        var output = opts.Format == OutputFormat.Json
            ? FormatJson(report, opts)
            : FormatConsole(report, opts);

        if (opts.OutPath is not null)
        {
            try
            {
                await File.WriteAllTextAsync(opts.OutPath, output);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: failed to write '{opts.OutPath}': {ex.Message}");
                return ExitCodes.Error;
            }
        }
        else
        {
            await stdout.WriteAsync(output);
            if (!output.EndsWith('\n'))
                await stdout.WriteLineAsync();
        }

        // A class going missing is the thing this command exists to catch, so it fails the same way a
        // quality gate does: exit 1, distinct from 2 for a bad invocation. Classes added on the right
        // are reported but never fail — gaining a class is not a loss.
        return report.Missing.Count == 0 ? ExitCodes.Ok : ExitCodes.GateFailed;
    }

    private static string FormatConsole(CompareReport report, CompareOptions opts)
    {
        var text = new StringBuilder();
        text.AppendLine("Comparing class inventories");
        AppendSide(text, "A", report.Left);
        AppendSide(text, "B", report.Right);

        WarnAboutUnparseableFiles(text, "A", report.Left);
        WarnAboutUnparseableFiles(text, "B", report.Right);

        text.AppendLine();
        if (report.Missing.Count == 0)
        {
            text.AppendLine("No classes are missing from B.");
        }
        else
        {
            text.AppendLine($"{Classes(report.Missing.Count)} missing from B:");
            text.AppendLine();
            AppendList(text, report.Missing, "B has a new class of this name");
        }

        if (opts.ShowAdded && report.Added.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"{Classes(report.Added.Count)} only in B:");
            text.AppendLine();
            AppendList(text, report.Added, "A has a class of this name that B is missing");
        }

        text.AppendLine();
        text.AppendLine(
            $"{report.Left.Count} classes in A, {report.Right.Count} in B - " +
            $"{report.Missing.Count} missing, {report.Added.Count} added");
        return text.ToString();
    }

    private static void AppendList(
        StringBuilder text, IReadOnlyList<ClassDifference> differences, string candidateLabel)
    {
        // Padded to the longest name so the class type and location line up and can be read down the
        // page: this list is routinely thousands of lines long.
        var width = Math.Min(differences.Max(difference => difference.Entry.FullName.Length), 70);

        foreach (var difference in differences)
        {
            var entry = difference.Entry;
            var location = entry.FilePath.Length > 0 ? $"  {entry.FilePath}:{entry.StartLine}" : "";
            text.AppendLine($"  {entry.FullName.PadRight(width)}  {entry.ClassType,-10}{location}");

            if (entry.IsParseFailure)
                text.AppendLine("      (stands for a file that could not be parsed, not a real class)");

            foreach (var candidate in difference.Candidates)
                text.AppendLine($"      -> {candidateLabel}: {candidate}");
        }
    }

    private static void WarnAboutUnparseableFiles(StringBuilder text, string label, ClassInventory side)
    {
        if (side.UnparseableFiles.Count == 0)
            return;

        text.AppendLine();
        text.AppendLine(
            $"warning: {side.UnparseableFiles.Count} file(s) in {label} could not be parsed, so every " +
            "class they hold is counted as absent:");
        foreach (var file in side.UnparseableFiles)
            text.AppendLine($"           {file}");
    }

    private static void AppendSide(StringBuilder text, string label, ClassInventory inventory)
    {
        text.AppendLine($"  {label}  {inventory.Path}");
        text.AppendLine($"     {inventory.Count} classes in {Describe(inventory.LibraryNames)}");
    }

    /// <summary>
    /// Names the libraries that were loaded, capped — a path holding a tool's whole library folder
    /// produces a list nobody reads, and the counts underneath it are the part that matters.
    /// </summary>
    private static string Describe(IReadOnlyList<string> libraryNames)
    {
        const int Shown = 6;
        if (libraryNames.Count == 0)
            return "nothing (no library loaded)";
        if (libraryNames.Count <= Shown)
            return string.Join(", ", libraryNames);

        return $"{string.Join(", ", libraryNames.Take(Shown))} and {libraryNames.Count - Shown} more";
    }

    private static string Classes(int count) => count == 1 ? "1 class is" : $"{count} classes are";

    private static string FormatJson(CompareReport report, CompareOptions opts)
    {
        var payload = new
        {
            tool = "mlqt",
            command = "compare",
            left = Side(report.Left),
            right = Side(report.Right),
            summary = new
            {
                leftClassCount = report.Left.Count,
                rightClassCount = report.Right.Count,
                missing = report.Missing.Count,
                added = report.Added.Count
            },
            missing = report.Missing.Select(Difference).ToList(),
            added = opts.ShowAdded ? report.Added.Select(Difference).ToList() : []
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object Side(ClassInventory inventory) => new
    {
        path = inventory.Path,
        libraries = inventory.LibraryNames,
        classCount = inventory.Count,
        unparseableFiles = inventory.UnparseableFiles
    };

    private static object Difference(ClassDifference difference) => new
    {
        name = difference.Entry.FullName,
        classType = difference.Entry.ClassType,
        file = difference.Entry.FilePath,
        line = difference.Entry.StartLine,
        unparseable = difference.Entry.IsParseFailure,
        candidates = difference.Candidates
    };
}
