using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.Visitors;

/// <summary>
/// Extracts the <c>Documentation(info=..., revisions=...)</c> annotation strings of a class from its
/// parse tree. Only the outermost class's class-level annotation is read. The returned values are the
/// raw (typically HTML) string contents with the surrounding quotes removed and any string
/// concatenation joined.
/// </summary>
public static class DocumentationExtractor
{
    /// <summary>Extract (info, revisions) for the first (outermost) class in a stored_definition.</summary>
    public static (string? Info, string? Revisions) Extract(modelicaParser.Stored_definitionContext? stored)
    {
        var cls = stored?.class_definition()?.FirstOrDefault();
        var composition = cls?.class_specifier()?.long_class_specifier()?.composition();
        if (composition is null)
            return (null, null);

        string? info = null;
        string? revisions = null;
        foreach (var annotation in composition.annotation())
            ReadAnnotation(annotation, ref info, ref revisions);
        return (info, revisions);
    }

    /// <summary>Parse <paramref name="modelicaCode"/> and extract its Documentation strings.</summary>
    public static (string? Info, string? Revisions) ExtractFromCode(string modelicaCode)
        => Extract(ModelicaParserHelper.Parse(modelicaCode));

    private static void ReadAnnotation(
        modelicaParser.AnnotationContext annotation, ref string? info, ref string? revisions)
    {
        var argList = annotation.class_modification()?.argument_list();
        if (argList is null)
            return;

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod?.name()?.GetText() != "Documentation")
                continue;

            var docArgList = elemMod.modification()?.class_modification()?.argument_list();
            if (docArgList is null)
                continue;

            foreach (var docArg in docArgList.argument())
            {
                var docElemMod = docArg.element_modification_or_replaceable()?.element_modification();
                var paramName = docElemMod?.name()?.GetText();
                if (paramName == "info")
                    info = ReadStringValue(docElemMod!.modification());
                else if (paramName == "revisions")
                    revisions = ReadStringValue(docElemMod!.modification());
            }
        }
    }

    /// <summary>Concatenate all STRING literals under a modification (handles "a" + "b" splits).</summary>
    private static string? ReadStringValue(modelicaParser.ModificationContext? modification)
    {
        if (modification is null)
            return null;

        var parts = new List<string>();
        CollectStrings(modification, parts);
        return parts.Count == 0 ? null : string.Concat(parts.Select(TextExtractor.StripQuotes));
    }

    private static void CollectStrings(IParseTree node, List<string> into)
    {
        if (node is ITerminalNode terminal)
        {
            if (terminal.Symbol.Type == modelicaParser.STRING)
                into.Add(terminal.GetText());
            return;
        }

        for (var i = 0; i < node.ChildCount; i++)
            CollectStrings(node.GetChild(i), into);
    }
}
