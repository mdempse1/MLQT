using System.Text;
using System.Text.Json;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>
/// A pull request review, as GitHub's "create a review" endpoint wants it: a summary body and a list
/// of inline comments. Written to a file and posted with
/// <c>gh api --method POST /repos/O/R/pulls/N/reviews --input review.json</c>, so MLQT holds no token,
/// speaks no HTTP, and has nothing to keep working when the API moves.
///
/// <para>The hard constraint the shape of this output comes from: <b>GitHub accepts a review comment
/// only on a line that appears in the pull request's diff</b>, and one comment it will not take
/// fails the entire review with a 422 — the other forty are lost with it. So a finding is commented
/// inline only when its line is one this change actually added or rewrote
/// (<see cref="CheckReport.Diff"/>), and everything else is listed in the summary body instead,
/// where it is still read and cannot cause a rejection.</para>
///
/// <para>The review is always posted as a comment, never as <c>REQUEST_CHANGES</c>. The gate is the
/// exit code, which is what CI acts on; a tool that also blocks a human's merge button is one that
/// gets its permissions taken away.</para>
/// </summary>
internal sealed class ReviewFindingFormatter : IFindingFormatter
{
    /// <summary>
    /// How many inline comments one review may carry. A change that adds a large file can produce
    /// hundreds of findings, and a review of hundreds of comments is not read by anyone — it is also
    /// where GitHub starts refusing the request outright. The rest are not dropped: they go into the
    /// summary body with everything else that could not be placed.
    /// </summary>
    private const int MaxInlineComments = 50;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Format(CheckReport report)
    {
        // Accepted debt is agreed history. It is not what this pull request did, and a comment on it
        // asks the wrong person to fix it.
        var actionable = report.Findings
            .Where(c => c.Status != FindingStatus.AcceptedDebt)
            .ToList();

        var placeable = new List<(string Path, int Line, ClassifiedFinding Finding)>();
        var unplaceable = new List<ClassifiedFinding>();

        foreach (var c in actionable)
        {
            // A location's file path follows the library path it was given, so it is relative when
            // that was; the diff is keyed absolutely. Resolving here is what makes the two comparable.
            var file = report.FileFor(c.Finding) is { } f ? Path.GetFullPath(f) : null;
            var line = report.LineFor(c.Finding);
            var path = file is null ? null : report.Diff?.RepositoryRelativePath(file);

            if (file is not null && path is not null && report.Diff!.Covers(file, line))
                placeable.Add((path, line, c));
            else
                unplaceable.Add(c);
        }

        // One comment per line, however many findings are on it: two comments at the same position
        // read as a doubled remark, and each one spends part of the budget.
        var grouped = placeable
            .GroupBy(p => (p.Path, p.Line))
            .OrderBy(g => g.Key.Path, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Line)
            .ToList();

        var inline = grouped.Take(MaxInlineComments).ToList();
        var overflow = grouped.Skip(MaxInlineComments).SelectMany(g => g.Select(p => p.Finding)).ToList();

        var comments = inline.Select(g => new
        {
            path = g.Key.Path,
            line = g.Key.Line,
            side = "RIGHT",
            body = CommentBody(g.Select(p => p.Finding))
        }).ToList();

        var review = new
        {
            body = Summary(report, inline.Count, unplaceable, overflow),
            @event = "COMMENT",
            comments
        };

        return JsonSerializer.Serialize(review, Options);
    }

    /// <summary>What one line's findings say. Kept short: it is rendered in a diff, not on a page.</summary>
    private static string CommentBody(IEnumerable<ClassifiedFinding> findings)
    {
        var sb = new StringBuilder();
        var list = findings.ToList();

        foreach (var c in list)
        {
            var f = c.Finding;
            var status = c.Status == FindingStatus.TouchedDebt ? " _(pre-existing)_" : "";
            if (list.Count > 1)
                sb.Append("- ");
            sb.AppendLine($"**{Severity(f.Severity)}** `{f.RuleId}`{status} — {f.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Summary(
        CheckReport report,
        int inlineCount,
        IReadOnlyList<ClassifiedFinding> unplaceable,
        IReadOnlyList<ClassifiedFinding> overflow)
    {
        var sb = new StringBuilder();

        var newCount = report.CountOfStatus(FindingStatus.New);
        var touched = report.CountOfStatus(FindingStatus.TouchedDebt);
        var accepted = report.CountOfStatus(FindingStatus.AcceptedDebt);
        var gate = report.GatePassed ? "passed" : "failed";

        sb.AppendLine(
            $"## MLQT check — {newCount} new, {touched} touched, {accepted} accepted " +
            $"(gate: {gate})");
        sb.AppendLine();

        if (newCount + touched == 0)
        {
            sb.AppendLine("No new findings.");
        }
        else
        {
            sb.AppendLine(
                inlineCount == 1
                    ? "1 finding is commented on the diff below."
                    : $"{inlineCount} findings are commented on the diff below.");
        }

        // Everything the review could not point at. Said plainly, because a reader who sees only the
        // inline comments would otherwise take them for the whole answer.
        var elsewhere = unplaceable.Concat(overflow).ToList();
        if (elsewhere.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"<details><summary>{elsewhere.Count} finding(s) not on a changed line</summary>");
            sb.AppendLine();
            sb.AppendLine("They are not on a line this change added or rewrote, so a review comment");
            sb.AppendLine("cannot be attached to them.");
            sb.AppendLine();
            sb.AppendLine("| Severity | Status | Rule | Model | Line | Message |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var c in elsewhere)
            {
                var f = c.Finding;
                sb.AppendLine(
                    $"| {Severity(f.Severity)} | {c.Status} | {f.RuleId} | {Cell(f.ModelId)} | " +
                    $"{report.LineFor(f)} | {Cell(f.Message)} |");
            }
            sb.AppendLine();
            sb.AppendLine("</details>");
        }

        if (report.FixedEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Fixed in changed models ({report.FixedEntries.Count}):**");
            sb.AppendLine();
            foreach (var e in report.FixedEntries
                         .OrderBy(e => e.Model, StringComparer.Ordinal)
                         .ThenBy(e => e.RuleId, StringComparer.Ordinal))
                sb.AppendLine($"- {e.RuleId} — {Cell(e.Model)}: {Cell(e.Message)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Severity(RuleSeverity severity) => severity.ToString().ToLowerInvariant();

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
}
