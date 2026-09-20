using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.Visitors;

/// <summary>
/// B233 — how much of what a user asked to hide actually goes.
///
/// <para><b>This is a measurement, not a guard.</b> The backlog item asks for a number before any
/// code is written, and names which number: not "what fraction of annotations are left visible" but
/// <b>what fraction of equation lines</b>, because a <c>connect(...)</c> almost always carries its
/// <c>annotation(Line(...))</c> on the same line, and the equation section is where the noise the
/// user complained about lives.</para>
///
/// <para>Opt-in through <c>MLQT_FIDELITY_CORPUS</c>, the same variable the round-trip test uses, so
/// pointing at a real library measures both. With it unset this returns at once and asserts
/// nothing — it is here to be run deliberately and its output read, not to pass.</para>
/// </summary>
public class AnnotationElisionCoverageTests
{
    /// <summary>A few examples of a splice that broke the text, for reading.</summary>
    private static readonly List<string> Damage = [];

    /// <summary>One library's worth of counting.</summary>
    private sealed class Tally
    {
        public int Files;
        public int Annotations;
        public int AnnotationsElided;
        public int EquationLines;
        public int EquationLinesWithAnnotation;
        public int EquationLinesWithAnnotationElided;

        /// <summary>
        /// Files that parsed before the splice and not after. Hiding everything is easy if the text
        /// may be damaged doing it, so this is the number that makes the one above mean something.
        /// </summary>
        public int SplicedNoLongerParses;

        public void Add(Tally other)
        {
            Files += other.Files;
            Annotations += other.Annotations;
            AnnotationsElided += other.AnnotationsElided;
            EquationLines += other.EquationLines;
            EquationLinesWithAnnotation += other.EquationLinesWithAnnotation;
            EquationLinesWithAnnotationElided += other.EquationLinesWithAnnotationElided;
            SplicedNoLongerParses += other.SplicedNoLongerParses;
        }
    }

    [Fact]
    public void HowMuchOfAnEquationSectionHidingAnnotationsActuallyHides()
    {
        var roots = Environment.GetEnvironmentVariable("MLQT_FIDELITY_CORPUS");
        if (string.IsNullOrWhiteSpace(roots))
            return;

        var report = new List<string>();
        var overall = new Tally();

        foreach (var root in roots.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var tally = new Tally();
            foreach (var file in Directory.EnumerateFiles(root.Trim(), "*.mo", SearchOption.AllDirectories))
                Measure(file, tally);

            overall.Add(tally);
            report.Add(Describe(Path.GetFileName(root.Trim().TrimEnd('\\', '/')), tally));
        }

        report.Add(Describe("ALL", overall));
        if (Damage.Count > 0)
            report.Add(Environment.NewLine + "=== damage ===" + Environment.NewLine
                + string.Join(Environment.NewLine, Damage));

        // Written out, not just printed: Microsoft.Testing.Platform swallows stdout from a passing
        // test, so a measurement whose whole purpose is to be read has to leave something behind.
        var text = string.Join(Environment.NewLine, report);
        var path = Path.Combine(Path.GetTempPath(), "mlqt-annotation-elision.txt");
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        Console.WriteLine($"(written to {path})");

        Assert.True(overall.Files > 0, $"MLQT_FIDELITY_CORPUS matched no .mo files: {roots}");
    }

    private static void Measure(string file, Tally tally)
    {
        string source;
        try
        {
            source = ModelicaFileEncoding.ReadAllTextOnly(file);
        }
        catch (IOException)
        {
            return;
        }

        var (tree, _) = ModelicaParserHelper.ParseWithTokens(source);
        if (tree is null)
            return;

        tally.Files++;

        var lines = ModelicaParserHelper.NormalizeLineEndings(source).Split('\n');

        // What the viewer does: splice the inline ones out of the text and drop the rest by line.
        // A line counts as "hidden" when it no longer carries an annotation, whichever half did it.
        var (spliced, elided) = ElisionFinder.WithoutAnnotations(tree, source);
        var splicedLines = spliced.Split('\n');

        // The splice has to leave Modelica behind, or the viewer loses its parse-tree colouring —
        // and "100% hidden" would only mean the text had been destroyed thoroughly. Counted against
        // files that parsed cleanly to begin with, so a library's own syntax errors are not blamed
        // on this.
        var (_, _, before) = ModelicaParserHelper.ParseWithTokensAndErrors(source);
        if (before.Count == 0)
        {
            var (_, _, after) = ModelicaParserHelper.ParseWithTokensAndErrors(spliced);
            if (after.Count > 0)
            {
                tally.SplicedNoLongerParses++;
                if (Damage.Count < 8)
                {
                    var line = after[0].Line;
                    Damage.Add($"{Path.GetFileName(file)}:{line} {after[0].Message}"
                        + $"{Environment.NewLine}    was: "
                        + (line <= lines.Length ? lines[line - 1].Trim() : "?")
                        + $"{Environment.NewLine}    now: "
                        + (line <= splicedLines.Length ? splicedLines[line - 1].Trim() : "?"));
                }
            }
        }

        // The lines the viewer would actually drop, as a set, so "was this annotation hidden" is a
        // lookup rather than a second walk with its own idea of the answer.
        var hidden = new HashSet<int>();
        foreach (var range in elided.Ranges)
            for (var line = range.FirstLine; line <= range.LastLine; line++)
                hidden.Add(line);

        foreach (var annotation in Descendants<modelicaParser.AnnotationContext>(tree))
        {
            tally.Annotations++;
            if (annotation.Start is not { } start)
                continue;

            if (hidden.Contains(start.Line)
                || start.Line > splicedLines.Length
                || !splicedLines[start.Line - 1].Contains("annotation"))
            {
                tally.AnnotationsElided++;
            }
        }

        // Equation sections, by the lines they span. A class may have several, and a package holds
        // classes that each have their own.
        foreach (var section in Descendants<modelicaParser.Equation_sectionContext>(tree))
        {
            var first = section.Start?.Line ?? 0;
            var last = section.Stop?.Line ?? 0;
            if (first <= 0 || last < first)
                continue;

            for (var line = first; line <= last && line <= lines.Length; line++)
            {
                if (lines[line - 1].Trim().Length == 0)
                    continue;

                tally.EquationLines++;
                if (!lines[line - 1].Contains("annotation"))
                    continue;

                tally.EquationLinesWithAnnotation++;

                // Gone if the line went, or if what is left of it no longer mentions one.
                if (hidden.Contains(line)
                    || line > splicedLines.Length
                    || !splicedLines[line - 1].Contains("annotation"))
                {
                    tally.EquationLinesWithAnnotationElided++;
                }
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(IParseTree node) where T : ParserRuleContext
    {
        if (node is T match)
        {
            yield return match;
            yield break;   // an annotation inside an annotation is covered by the outer one
        }

        for (var i = 0; i < node.ChildCount; i++)
            foreach (var found in Descendants<T>(node.GetChild(i)))
                yield return found;
    }

    private static string Describe(string name, Tally t)
    {
        static string Pct(int part, int whole) =>
            whole == 0 ? "n/a" : $"{100.0 * part / whole:F1}%";

        return $"""

            === {name} ===
              files parsed                        {t.Files}
              annotations                         {t.Annotations}
                hidden today                      {t.AnnotationsElided} ({Pct(t.AnnotationsElided, t.Annotations)})
                left visible                      {t.Annotations - t.AnnotationsElided} ({Pct(t.Annotations - t.AnnotationsElided, t.Annotations)})
              files damaged by the splice         {t.SplicedNoLongerParses}
              equation-section lines (non-blank)   {t.EquationLines}
                carrying an annotation            {t.EquationLinesWithAnnotation} ({Pct(t.EquationLinesWithAnnotation, t.EquationLines)})
                  of those, hidden today          {t.EquationLinesWithAnnotationElided} ({Pct(t.EquationLinesWithAnnotationElided, t.EquationLinesWithAnnotation)})
                  of those, left visible          {t.EquationLinesWithAnnotation - t.EquationLinesWithAnnotationElided} ({Pct(t.EquationLinesWithAnnotation - t.EquationLinesWithAnnotationElided, t.EquationLinesWithAnnotation)})
            """;
    }
}
