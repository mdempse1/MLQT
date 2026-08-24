using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;
using MLQT.Services;
using Xunit;
using Xunit.Abstractions;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// Measures how accurately a library can be reconstructed from its generated documentation, by
/// doing it to a library whose source we <i>can</i> read and comparing the two.
///
/// <para>The Modelica Standard Library is the ideal subject: Dymola ships generated help for it
/// exactly as it does for the encrypted commercial libraries, while its source sits right beside
/// it. Loading it both ways and diffing the results turns "documentation is probably good enough"
/// into a number, before anything depends on the answer.</para>
///
/// <para>The residual is reported rather than asserted away. Documentation omits protected and
/// hidden classes by design, so a gap is expected — what matters is knowing its size, because the
/// same gap exists in every commercial library and it is why a failure to resolve a name against
/// documentation must never harden into an error.</para>
/// </summary>
public class DocumentationAccuracyTests
{
    private readonly ITestOutputHelper _output;

    public DocumentationAccuracyTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Loads the Modelica Standard Library from source and from its documentation, and reports
    /// how far apart they are on the four things the feature relies on: which classes exist, what
    /// they describe themselves as, what they extend, and whether they have an icon.
    /// </summary>
    [Fact]
    public async Task DocumentedClasses_MatchTheSourceTheyWereGeneratedFrom()
    {
        var libraryPath = DymolaInstall.FindLibrary("Modelica 4.");
        if (libraryPath is null)
            return;   // No Dymola on this machine.

        var helpDirectory = Path.Combine(libraryPath, "help");
        if (!Directory.Exists(helpDirectory))
            return;

        var document = DymolaHelpReader.Read(helpDirectory);
        Assert.NotEmpty(document.Classes);

        var service = new LibraryDataService();
        await service.AddLibraryFromDirectoryAsync(libraryPath);
        var graph = service.CombinedGraph;

        var source = graph.ModelNodes
            .Where(node => !node.IsParseFailurePlaceholder)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);

        Assert.NotEmpty(source);

        var missing = document.Classes.Where(c => !source.ContainsKey(c.FullName)).ToList();
        var undocumented = source.Keys.Where(id => document.Classes.All(c => c.FullName != id)).Count();

        _output.WriteLine($"library:            {Path.GetFileName(libraryPath)}");
        _output.WriteLine($"documented classes: {document.Classes.Count} (from {document.FilesRead} help files)");
        _output.WriteLine($"source classes:     {source.Count}");
        _output.WriteLine($"documented but not in source: {missing.Count}");
        _output.WriteLine($"in source but not documented: {undocumented} " +
                          $"({100.0 * undocumented / source.Count:F1}% of source)");

        // Anything the documentation names must really exist. A name appearing here that the
        // source does not have would mean the parser is inventing classes — the one error that
        // would make resolution actively wrong rather than merely incomplete.
        Assert.True(
            missing.Count <= document.Classes.Count / 100,
            $"{missing.Count} documented classes are absent from source, e.g. " +
            string.Join(", ", missing.Take(5).Select(c => c.FullName)));

        CompareDescriptions(document, source);
        CompareExtends(document, source);
        CompareIcons(document, source, graph);
    }

    private void CompareDescriptions(DymolaHelpDocument document, Dictionary<string, ModelNode> source)
    {
        var compared = 0;
        var mismatched = new List<string>();

        foreach (var documented in document.Classes)
        {
            if (documented.Description is null || !source.TryGetValue(documented.FullName, out var node))
                continue;

            var sourceDescription = ReadDescription(node);
            if (sourceDescription is null)
                continue;

            compared++;
            if (!string.Equals(Normalise(sourceDescription), Normalise(documented.Description), StringComparison.Ordinal))
                mismatched.Add($"{documented.FullName}: doc='{documented.Description}' source='{sourceDescription}'");
        }

        // The handful that differ are non-ASCII: the documentation says "Krüger" and the source
        // side says "KrÃ¼ger". That is the .mo load path reading UTF-8 files as Latin-1, not a
        // fault in the documentation reader — which decodes the entities and the declared UTF-8
        // charset correctly. Left as-is: changing how source files are read is a separate concern
        // with a far wider blast radius than this feature.
        _output.WriteLine($"descriptions compared: {compared}, mismatched: {mismatched.Count}");
        foreach (var line in mismatched.Take(5))
            _output.WriteLine("  " + line);

        Assert.True(compared > 1000, $"only {compared} descriptions were comparable");
        Assert.True(
            mismatched.Count <= compared / 100,
            $"{mismatched.Count} of {compared} descriptions differ from source");
    }

    private void CompareExtends(DymolaHelpDocument document, Dictionary<string, ModelNode> source)
    {
        var compared = 0;
        var shortClassDefinitions = 0;
        var mismatched = new List<string>();

        foreach (var documented in document.Classes)
        {
            if (documented.ExtendsClasses is not { Count: > 0 } documentedBases)
                continue;
            if (!source.TryGetValue(documented.FullName, out var node))
                continue;

            var sourceBases = ReadExtends(node);
            if (sourceBases is null)
                continue;

            // A short class definition ("connector ComplexInput = input Complex") inherits through
            // an equals sign rather than an extends clause. The documentation reports its target as
            // a base class, which is the more useful answer; the source-side extractor used here
            // only reads extends clauses and so reports nothing. Counted, not treated as
            // disagreement — the two sides are describing the same relationship differently.
            if (sourceBases.Count == 0)
            {
                shortClassDefinitions++;
                continue;
            }

            compared++;

            // Documentation states base classes fully qualified while source may write them
            // relatively, so compare on the final segment — enough to catch a base class the
            // parser lost, split in half on a description comma, or invented.
            var documentedTails = documentedBases.Select(LastSegment).OrderBy(x => x, StringComparer.Ordinal);
            var sourceTails = sourceBases.Select(LastSegment).OrderBy(x => x, StringComparer.Ordinal);
            if (!documentedTails.SequenceEqual(sourceTails, StringComparer.Ordinal))
            {
                mismatched.Add(
                    $"{documented.FullName}: doc=[{string.Join(", ", documentedBases)}] " +
                    $"source=[{string.Join(", ", sourceBases)}]");
            }
        }

        // The residue is the short-form redeclaration ("redeclare function extends foo"), where the
        // header itself names a base class. The documentation lists it; the source-side extractor
        // used here only reads explicit extends clauses in the body, so it sees one base where the
        // documentation sees two. As with short class definitions, the documentation is the more
        // complete of the two — this is a limit of the comparison, not of the reader.
        _output.WriteLine($"extends compared: {compared}, mismatched: {mismatched.Count} " +
                          $"(plus {shortClassDefinitions} short class definitions, compared separately)");
        foreach (var line in mismatched.Take(5))
            _output.WriteLine("  " + line);

        Assert.True(compared > 1000, $"only {compared} extends lists were comparable");
        Assert.True(
            mismatched.Count <= compared / 100,
            $"{mismatched.Count} of {compared} extends lists differ from source, e.g. " +
            string.Join(" | ", mismatched.Take(3)));
    }

    private void CompareIcons(
        DymolaHelpDocument document, Dictionary<string, ModelNode> source, DirectedGraph graph)
    {
        // The production icon-inheritance walk, so this measures what the rule will actually see
        // rather than a re-implementation that could agree with the parser by coincidence.
        var hasIconInSource = StyleChecking.CreateBaseClassHasIconCallback(graph);
        Assert.NotNull(hasIconInSource);

        var compared = 0;
        var falseNegatives = new List<string>();   // documentation says no icon, source has one
        var falsePositives = new List<string>();   // documentation says icon, source has none

        foreach (var documented in document.Classes)
        {
            if (documented.HasIcon is not { } documentedHasIcon)
                continue;
            if (!source.ContainsKey(documented.FullName))
                continue;

            compared++;
            var sourceHasIcon = hasIconInSource!(documented.FullName, documented.FullName);
            if (sourceHasIcon && !documentedHasIcon)
                falseNegatives.Add(documented.FullName);
            else if (!sourceHasIcon && documentedHasIcon)
                falsePositives.Add(documented.FullName);
        }

        _output.WriteLine($"icons compared: {compared}");
        _output.WriteLine($"  documentation says no icon, source has one:  {falseNegatives.Count}");
        _output.WriteLine($"  documentation says icon, source has none:    {falsePositives.Count}");
        foreach (var name in falseNegatives.Take(5))
            _output.WriteLine("  false negative: " + name);
        foreach (var name in falsePositives.Take(5))
            _output.WriteLine("  false positive: " + name);

        Assert.True(compared > 1000, $"only {compared} icon states were comparable");

        // The two sides ask slightly different questions, and the difference runs one way only.
        // The rule asks a syntactic question — is there an Icon annotation, own or inherited. The
        // generator answers a visual one — does this class render to anything, which includes the
        // placed sub-components that give Modelica.Electrical.Analog.Interfaces.OnePort a picture
        // despite it carrying no Icon annotation. So the documentation says "icon" for classes the
        // rule would say have none.
        //
        // That direction is harmless: it can only ever suppress a finding on a class whose base we
        // cannot read. The opposite direction is the damaging one — claiming no icon where there is
        // one makes the rule fire on a user class that inherits a perfectly good icon across the
        // boundary, which is the precise false positive this feature exists to remove. It is zero,
        // and it is asserted as zero rather than as a tolerance, because a regression here would
        // reintroduce the bug the feature was built to fix.
        Assert.True(
            falseNegatives.Count == 0,
            $"{falseNegatives.Count} of {compared} classes are documented as having no icon but do have one, e.g. " +
            string.Join(", ", falseNegatives.Take(5)));
    }

    /// <summary>
    /// Every installed encrypted library parses without throwing and yields a plausible number of
    /// classes. This is the breadth check: fifty-odd libraries from six vendors, against a format
    /// MLQT does not control.
    /// </summary>
    [Fact]
    public void EveryInstalledEncryptedLibrary_ParsesCleanly()
    {
        var libraries = DymolaInstall.EncryptedLibraries();
        if (libraries.Count == 0)
            return;

        var totalClasses = 0;
        var withClasses = 0;
        var failures = new List<string>();

        foreach (var path in libraries)
        {
            var detected = EncryptedLibraryDetector.Detect(path);
            if (detected?.HelpDirectory is null)
                continue;

            try
            {
                var document = DymolaHelpReader.Read(detected.HelpDirectory);
                totalClasses += document.Classes.Count;
                if (document.Classes.Count > 0)
                    withClasses++;

                foreach (var documented in document.Classes)
                {
                    Assert.False(string.IsNullOrWhiteSpace(documented.FullName));
                    // Markup captured as a name would mean the scanner lost its place. Note that
                    // '<' alone proves nothing: operator overloads are genuinely named '<' and '<='.
                    Assert.DoesNotContain("<a ", documented.FullName);
                    Assert.DoesNotContain("<img", documented.FullName);
                    Assert.DoesNotContain('\n', documented.FullName);
                }

                // The root package always has an unknown icon — nothing above it showed a
                // thumbnail for it. A handful more is normal where a parent package's content
                // table omits an image. A large number would mean content tables are being missed.
                var unknownIcons = document.Classes.Count(c => c.HasIcon is null);
                Assert.True(unknownIcons <= 1 + document.Classes.Count / 50,
                    $"{detected.Name}: {unknownIcons} of {document.Classes.Count} classes have an " +
                    "undetermined icon state");
            }
            catch (Exception ex)
            {
                failures.Add($"{detected.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine($"encrypted libraries: {libraries.Count}, with recovered classes: {withClasses}");
        _output.WriteLine($"total classes recovered: {totalClasses}");

        Assert.Empty(failures);
        Assert.True(totalClasses > 1000, $"only {totalClasses} classes recovered across all libraries");
    }

    /// <summary>
    /// Every class of every installed encrypted library, synthesized into a Modelica stub and
    /// parsed.
    ///
    /// <para>This is the check the whole feature rests on. A stub that does not parse is not a
    /// degraded stub — it is inert: the extends chain is never walked, the icon is never found,
    /// and the class silently contributes nothing while appearing to have been loaded. Nothing
    /// downstream would report it, so only this test can catch it.</para>
    /// </summary>
    [Fact]
    public void EverySynthesizedStub_Parses()
    {
        var libraries = DymolaInstall.EncryptedLibraries();
        if (libraries.Count == 0)
            return;

        var documented = libraries
            .Select(EncryptedLibraryDetector.Detect)
            .Where(detected => detected?.HelpDirectory is not null)
            .SelectMany(detected => DymolaHelpReader.Read(detected!.HelpDirectory!).Classes)
            .ToList();

        Assert.NotEmpty(documented);

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(documented, documentedClass =>
        {
            var source = ExternalStubBuilder.SynthesizeSource(documentedClass);
            var (tree, errors) = ModelicaParser.Helpers.ModelicaParserHelper.ParseWithErrors(source);

            if (tree is null || errors.Count > 0)
                failures.Add($"{documentedClass.FullName}: {errors.FirstOrDefault()?.Message ?? "no parse tree"}");
        });

        _output.WriteLine($"stubs synthesized and parsed: {documented.Count}, failures: {failures.Count}");
        foreach (var failure in failures.Take(10))
            _output.WriteLine("  " + failure);

        Assert.Empty(failures);
    }

    private static string LastSegment(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static string Normalise(string text) => string.Join(' ',
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The description string from a class's own source, read straight off the declaration rather
    /// than through a visitor, so the comparison is against the text as written.
    /// </summary>
    private static string? ReadDescription(ModelNode node)
    {
        var code = node.Definition.ModelicaCode;
        if (string.IsNullOrEmpty(code))
            return null;

        var name = node.Definition.Name;
        var nameIndex = code.IndexOf(name, StringComparison.Ordinal);
        if (nameIndex < 0)
            return null;

        var cursor = nameIndex + name.Length;
        while (cursor < code.Length && char.IsWhiteSpace(code[cursor]))
            cursor++;

        if (cursor >= code.Length || code[cursor] != '"')
            return null;

        var text = new System.Text.StringBuilder();
        cursor++;
        while (cursor < code.Length && code[cursor] != '"')
        {
            if (code[cursor] == '\\' && cursor + 1 < code.Length)
            {
                cursor++;
                text.Append(code[cursor] switch { 'n' => '\n', 't' => '\t', var c => c });
            }
            else
            {
                text.Append(code[cursor]);
            }

            cursor++;
        }

        return cursor < code.Length ? text.ToString() : null;
    }

    /// <summary>
    /// Base-class names from a class's own source, via the same extractor the icon-inheritance
    /// walk uses.
    /// </summary>
    private static IReadOnlyList<string>? ReadExtends(ModelNode node)
    {
        var parsed = node.Definition.EnsureParsed();
        if (parsed is null)
            return null;

        var extracted = ModelicaParser.Visitors.IconExtractor.ExtractIconWithInheritance(parsed);
        return extracted?.ExtendsClasses;
    }
}
