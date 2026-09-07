using System.Text;
using ModelicaGraph.DataTypes;
using ModelicaParser.ExternalDocs;

namespace ModelicaGraph;

/// <summary>
/// Turns classes recovered from a vendor's generated documentation into graph nodes, by
/// synthesizing a minimal Modelica declaration for each and loading it like any other class.
///
/// <para><b>Why synthesize source rather than carry metadata.</b> Everything downstream already
/// works through the parse tree: icon inheritance parses the class and walks its extends clauses,
/// the type and element resolvers walk them too, dependency analysis builds its edges from the
/// parse, and reference validation only needs the name present in the graph. A stub that parses is
/// therefore resolved by all of them with no rule changes at all. The alternative — a second,
/// metadata-shaped path through every consumer — would have to be kept in step with the first
/// forever, which is precisely what the shared check pipeline exists to avoid.</para>
///
/// <para>The synthesized code is a declaration, never an implementation: no equations, no
/// algorithms, no real graphics. It exists to be read, and <see cref="ModelNode.IsExternalStub"/>
/// marks it so it is never written back.</para>
/// </summary>
public static class ExternalStubBuilder
{
    /// <summary>The extension of an encrypted Modelica package — the whole library in one unreadable file.</summary>
    public const string EncryptedPackageExtension = ".moe";

    /// <summary>
    /// Whether a path names an encrypted package: a file MLQT can neither read nor, above all,
    /// <b>write</b>. A stub's file node points at it, honestly, because that is where the class came
    /// from — so every write path has to ask this before taking a class's file path at face value.
    ///
    /// <para>Decided by the extension rather than by what the file contains, so the answer does not
    /// depend on how much of the graph has been built yet, and so it is still the right answer for a
    /// path that has not been loaded at all.</para>
    /// </summary>
    public static bool IsEncryptedPackageFile(string? path) =>
        path is not null && path.EndsWith(EncryptedPackageExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a stub node for every documented class to <paramref name="graph"/>, plus one file node
    /// standing for the encrypted package the classes came from.
    /// </summary>
    /// <param name="graph">Graph to populate.</param>
    /// <param name="documented">Classes recovered from documentation.</param>
    /// <param name="encryptedPackagePath">Path of the encrypted <c>package.moe</c>, used as the
    /// file node's path so "where does this class live" has an honest answer.</param>
    /// <param name="libraryVersion">Version of the library, from the versioned directory name or
    /// its <c>libraryinfo.mos</c>. Stamped on the root package so the dependency-version check can
    /// see which copy is loaded.</param>
    /// <returns>The ids of the model nodes added.</returns>
    public static List<string> AddDocumentedClasses(
        DirectedGraph graph,
        IReadOnlyList<DocumentedClass> documented,
        string encryptedPackagePath,
        string? libraryVersion = null)
        => AddDocumentedClasses(graph, documented, encryptedPackagePath, out _, libraryVersion);

    /// <param name="supersededBySource">How many documented classes were left alone because their
    /// real source is already loaded. Reported because it is the difference between a library that
    /// shipped nothing readable and one whose every class we already have properly — both of which
    /// add no nodes, and only one of which is a problem.</param>
    /// <inheritdoc cref="AddDocumentedClasses(DirectedGraph, IReadOnlyList{DocumentedClass}, string, string?)"/>
    public static List<string> AddDocumentedClasses(
        DirectedGraph graph,
        IReadOnlyList<DocumentedClass> documented,
        string encryptedPackagePath,
        out int supersededBySource,
        string? libraryVersion = null)
    {
        supersededBySource = 0;
        var modelIds = new List<string>(documented.Count);
        if (documented.Count == 0)
            return modelIds;

        var fileId = GraphBuilder.GenerateFileId(encryptedPackagePath);
        graph.AddNode(new FileNode(fileId, encryptedPackagePath));

        var documentedNames = new HashSet<string>(documented.Select(d => d.FullName), StringComparer.Ordinal);

        foreach (var documentedClass in documented)
        {
            var node = BuildNode(documentedClass, documentedNames, libraryVersion);

            // A class we already have the source of is left entirely alone. AddNode knows to keep the
            // real node over a stub, but registering the containment afterwards would still point that
            // real node at the encrypted package — and everything that asks a class where it lives
            // would then be told package.moe. That is how correcting a spelling in a class whose
            // source is checked out came to read, and try to parse, a vendor's encrypted blob.
            var existing = graph.GetNode<ModelNode>(node.Id);
            if (existing is not null && !existing.IsExternalStub)
            {
                supersededBySource++;
                continue;
            }

            graph.AddNode(node);
            graph.AddFileContainsModel(fileId, node.Id);
            modelIds.Add(node.Id);
        }

        return modelIds;
    }

    private static ModelNode BuildNode(
        DocumentedClass documented, HashSet<string> documentedNames, string? libraryVersion)
    {
        var source = SynthesizeSource(documented);
        var node = new ModelNode(documented.FullName, documented.SimpleName, source)
        {
            IsExternalStub = true,
            ClassType = documented.Kind,
            ParentModelName = documented.ParentName,
            // The documentation lists a package's children in declaration order, which is what
            // package.order would have said had it been readable.
            PackageOrder = documented.Children.Count > 0
                ? documented.Children.Select(DocumentedClass.SimpleNameOf).ToArray()
                : null,
            // A stub stands for a whole class; there is no enclosing file text it was sliced from.
            StartLine = 1,
            StopLine = 1,
            StartIndex = -1,
            StopIndex = -1,
            // Only classes the documentation actually lists exist as nodes, so a parent being
            // present is the same question as the name being documented.
            IsNested = documented.ParentName is not null && documentedNames.Contains(documented.ParentName),
            // Documentation omits protected classes entirely, so anything visible here is public.
            IsPublic = true,
            // Never offer a stub as something that could be written to its own file.
            CanBeStoredStandalone = false
        };

        if (documented.ParentName is null)
            node.Version = libraryVersion;

        return node;
    }

    /// <summary>
    /// <summary>
    /// Header written above every stub.
    ///
    /// <para>Without it the synthesized declaration reads as ordinary Modelica that happens to be
    /// nearly empty, which is a worse impression to give than "MLQT cannot read this library": a
    /// user could reasonably conclude the vendor's class has no parameters, or that MLQT had lost
    /// them. It travels with the text, so it is still there if the code is copied out of the viewer
    /// or reaches somewhere the surrounding UI does not.</para>
    /// </summary>
    private const string StubHeader =
        "// Reconstructed by MLQT from this library's documentation — this is NOT the vendor's source.\n" +
        "// The library ships encrypted, so only what its documentation states is known here: the name,\n" +
        "// the description, the base classes and whether there is an icon. Read-only.\n";

    /// <summary>Builds the Modelica declaration for one documented class.</summary>
    public static string SynthesizeSource(DocumentedClass documented)
    {
        var source = new StringBuilder();
        source.Append(StubHeader);

        if (documented.ParentName is { Length: > 0 } parent)
            source.Append("within ").Append(parent).Append(";\n");

        source.Append(documented.Kind).Append(' ').Append(documented.SimpleName);

        if (!string.IsNullOrEmpty(documented.Description))
            source.Append(' ').Append(QuoteModelicaString(documented.Description));

        source.Append('\n');

        // Absent extends information means "the source did not say", which is not the same as
        // "extends nothing" — but either way there is nothing to emit. What matters is that the
        // icon decision below does not then read the silence as a definite no.
        foreach (var baseClass in documented.ExtendsClasses ?? [])
            source.Append("  extends ").Append(baseClass).Append(";\n");

        // An icon is emitted whenever the documentation did not positively say there is none.
        //
        // The asymmetry is deliberate. "No image on the heading" is a definite no and is honoured.
        // But for the one class per library whose icon cannot be determined — the root package,
        // which has no parent to have shown a thumbnail for it — guessing "no icon" would make
        // every user class extending it fail the icon rule, inventing a finding out of missing
        // input. Guessing "has icon" can only ever suppress one, which is the safe direction for
        // a library whose source we cannot read.
        if (documented.HasIcon != false)
            source.Append("  annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));\n");

        source.Append("end ").Append(documented.SimpleName).Append(";\n");
        return source.ToString();
    }

    /// <summary>
    /// Wraps a description as a Modelica string literal, escaping what the language requires.
    /// Vendor descriptions routinely contain quotes and backslashes.
    /// </summary>
    private static string QuoteModelicaString(string text)
    {
        var quoted = new StringBuilder(text.Length + 2);
        quoted.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        quoted.Append('"');
        return quoted.ToString();
    }
}
