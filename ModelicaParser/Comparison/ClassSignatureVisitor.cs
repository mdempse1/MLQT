using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;

namespace ModelicaParser.Comparison;

/// <summary>
/// Walks a parsed file and reduces every class in it to a <see cref="ClassSignature"/>.
/// </summary>
/// <remarks>
/// <para><b>Two walks, not one.</b> The visitor recurses the way <c>ModelExtractorVisitor</c> does,
/// so every class — nested or not — gets a signature and a full name. The signature itself is built
/// by <see cref="Emit"/>, which stops at the first class definition below the one it started on.
/// Each node of the file is therefore visited once by each walk, whatever the nesting depth.</para>
///
/// <para><b>Offsets index the preprocessed source.</b> <see cref="ClassSignatures"/> hands this the
/// exact string it parsed, because <c>Surface</c> is sliced by character offset and ANTLR's offsets
/// are into the text it was given. Parsing one string and slicing another is how a CRLF file's
/// slices come out shifted by the number of stripped carriage returns.</para>
///
/// <para><b>A well-formed tree is assumed, and that is deliberate.</b>
/// <see cref="ClassSignatures.Of"/> refuses a file the parser reported any error on, so nothing
/// here ever sees the half-built subtrees ANTLR's error recovery leaves — which is why there are no
/// null guards against them. Should one arrive anyway, the exception is caught there and the file
/// becomes <see cref="ClassChangeKind.Unknown"/>: an answer, in the one direction that cannot
/// mislead. Guarding here instead would mean signing half a class, and a signature missing an
/// equation reports a real change as no change at all.</para>
/// </remarks>
internal sealed class ClassSignatureVisitor : modelicaBaseVisitor<object?>
{
    /// <summary>
    /// Separates emitted tokens. A control character rather than a space, because a space is a
    /// legitimate part of a string literal and <c>"a b"</c> must not compare equal to <c>"ab"</c>.
    /// </summary>
    private const char Separator = '';

    private readonly string _source;
    private readonly List<ClassSignature> _signatures = new();
    private readonly Stack<string> _parents = new();

    /// <summary>Somewhere for <see cref="Emit"/> to put nested spans it has no use for.</summary>
    private readonly List<NestedClass> _discarded = new();

    private string _within = string.Empty;

    internal ClassSignatureVisitor(string preprocessedSource) => _source = preprocessedSource;

    internal IReadOnlyList<ClassSignature> Signatures => _signatures;

    private readonly record struct NestedClass(int Start, int Stop, string Name);

    public override object? VisitStored_definition([NotNull] modelicaParser.Stored_definitionContext context)
    {
        var identifiers = context.name()?.IDENT();
        if (identifiers is { Length: > 0 })
            _within = string.Join(".", identifiers.Select(t => t.GetText()));

        return base.VisitStored_definition(context);
    }

    public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
    {
        var name = NameOf(context.class_specifier());
        var parent = _parents.Count > 0 ? _parents.Peek() : _within;
        var fullName = parent.Length > 0 ? parent + "." + name : name;

        var semantic = new StringBuilder();
        var nested = new List<NestedClass>();
        Emit(context, semantic, nested, isRoot: true);

        _signatures.Add(new ClassSignature(fullName, semantic.ToString(), Surface(context, nested)));

        _parents.Push(fullName);
        base.VisitClass_definition(context);
        _parents.Pop();
        return null;
    }

    /// <summary>The declared name of a class, whichever of the three specifier shapes it uses.</summary>
    /// <remarks>
    /// A long specifier has two IDENTs — the name and the one repeated after <c>end</c> — and the
    /// first is the name. A <c>der</c> specifier has the name and then the variables differentiated
    /// with respect to, and again the first is the name. A short specifier has only the name.
    /// </remarks>
    private static string NameOf(modelicaParser.Class_specifierContext specifier)
    {
        if (specifier.long_class_specifier() is { } longSpec)
            return longSpec.IDENT()[0].GetText();

        if (specifier.short_class_specifier() is { } shortSpec)
            return shortSpec.IDENT().GetText();

        return specifier.der_class_specifier().IDENT()[0].GetText();
    }

    /// <summary>
    /// Appends the meaning-carrying tokens of <paramref name="node"/>, skipping what a translator
    /// does not read and recording the classes nested directly inside the root.
    /// </summary>
    private void Emit(IParseTree node, StringBuilder builder, List<NestedClass> nested, bool isRoot)
    {
        switch (node)
        {
            // Neither reaches a translator. Comments are on the default channel in this grammar
            // rather than hidden, so they are in the tree and have to be skipped explicitly.
            case modelicaParser.C_commentContext:
            case modelicaParser.String_commentContext:
                return;

            case modelicaParser.AnnotationContext annotation:
                EmitAnnotation(annotation, builder);
                return;

            // A class body is the one place an annotation is a statement of its own rather than
            // part of something else, so it is the one place its terminating semicolon has to go
            // with it. See EmitComposition.
            case modelicaParser.CompositionContext composition:
                EmitComposition(composition, builder, nested);
                return;

            // A nested class carries its own signature, so only its name is part of this one:
            // renaming, adding or removing one changes the parent, editing one does not.
            case modelicaParser.Class_definitionContext definition when !isRoot:
                var name = NameOf(definition.class_specifier());
                builder.Append("class ").Append(name).Append(Separator);
                nested.Add(new NestedClass(definition.Start.StartIndex, definition.Stop.StopIndex, name));
                return;

            case ITerminalNode terminal:
                if (terminal.Symbol.Type != TokenConstants.EOF)
                    builder.Append(terminal.GetText()).Append(Separator);
                return;

            default:
                for (var i = 0; i < node.ChildCount; i++)
                    Emit(node.GetChild(i), builder, nested, isRoot: false);
                return;
        }
    }

    /// <summary>
    /// Emits a class body, dropping the semicolon after an annotation that contributed nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this case is special.</b> Everywhere else an annotation is attached to something
    /// that ends in a semicolon of its own — an element, an equation, a statement — so both versions
    /// of a class emit that semicolon whether or not the annotation survives. A class body is the
    /// exception: <c>annotation (...) ';'</c> is a statement in its own right, so a class that
    /// gained nothing but a graphical annotation would otherwise differ from the one before it by a
    /// stray <c>;</c> and read as a change to what is simulated.</para>
    ///
    /// <para><b>The external clause's semicolon is not that semicolon.</b> It terminates
    /// <c>external "C" f() annotation (Library="m")</c>, which is a declaration, and it is emitted
    /// even when the annotation on it drops out — so an external function whose only annotation is
    /// display-only still compares equal to one with no annotation at all.</para>
    /// </remarks>
    private void EmitComposition(
        modelicaParser.CompositionContext composition, StringBuilder builder, List<NestedClass> nested)
    {
        var children = composition.children;
        var inExternalClause = false;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (child is ITerminalNode keyword && keyword.GetText() == "external")
                inExternalClause = true;

            if (inExternalClause)
            {
                // Everything up to and including the clause's own semicolon is emitted as written.
                if (child is ITerminalNode terminator && terminator.GetText() == ";")
                    inExternalClause = false;

                Emit(child, builder, nested, isRoot: false);
                continue;
            }

            if (child is modelicaParser.AnnotationContext annotation)
            {
                var lengthBefore = builder.Length;
                EmitAnnotation(annotation, builder);

                if (builder.Length == lengthBefore
                    && i + 1 < children.Count
                    && children[i + 1] is ITerminalNode next
                    && next.GetText() == ";")
                {
                    i++;
                }

                continue;
            }

            Emit(child, builder, nested, isRoot: false);
        }
    }

    /// <summary>
    /// Appends only those annotation elements a translator acts on, or nothing when none of them do.
    /// </summary>
    /// <remarks>
    /// The kept elements are sorted, because an annotation's elements are a set: a tool that
    /// rewrites <c>annotation(Evaluate=true, Inline=true)</c> the other way round has changed
    /// nothing, and that is exactly the sort of rewrite a save from another tool produces.
    /// </remarks>
    private void EmitAnnotation(modelicaParser.AnnotationContext annotation, StringBuilder builder)
    {
        var arguments = annotation.class_modification()?.argument_list()?.argument();
        if (arguments is null || arguments.Length == 0)
            return;

        List<string>? kept = null;
        foreach (var argument in arguments)
        {
            // Only a recognised name is dropped. Anything whose shape this cannot read — a
            // redeclaration, or a tree ANTLR recovered — is kept, on the same principle as an
            // unrecognised name: it gets looked at rather than hidden.
            var name = argument.element_modification_or_replaceable()?.element_modification()?.name()?.GetText();
            if (name is not null && !SimulationAnnotations.AffectsSimulation(name))
                continue;

            var element = new StringBuilder();
            Emit(argument, element, _discarded, isRoot: false);
            (kept ??= new List<string>()).Add(element.ToString());
        }

        if (kept is null)
            return;

        kept.Sort(StringComparer.Ordinal);
        builder.Append("annotation(").Append(Separator);
        foreach (var element in kept)
            builder.Append(element);
        builder.Append(')').Append(Separator);
    }

    /// <summary>
    /// The class's own source, with each directly nested class replaced by its name.
    /// </summary>
    /// <remarks>
    /// The nested spans arrive in document order and cannot overlap, because <see cref="Emit"/>
    /// records each one as it reaches it and stops there rather than descending.
    /// </remarks>
    private string Surface(modelicaParser.Class_definitionContext context, List<NestedClass> nested)
    {
        var start = context.Start.StartIndex;
        var stop = context.Stop.StopIndex;

        if (nested.Count == 0)
            return _source.Substring(start, stop - start + 1);

        var builder = new StringBuilder();
        var cursor = start;
        foreach (var child in nested)
        {
            builder.Append(_source, cursor, child.Start - cursor);
            builder.Append(Separator).Append(child.Name).Append(Separator);
            cursor = child.Stop + 1;
        }

        builder.Append(_source, cursor, stop - cursor + 1);
        return builder.ToString();
    }
}
