using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.Visitors;

/// <summary>
/// Visitor that extracts links to external resources from Modelica code.
/// Handles the following resource reference types:
///  1. Calls to Modelica.Utilities.Files.loadResource("path") or
///     ModelicaServices.ExternalReferences.loadResource("path")
///  2. modelica:// URI references in string literals (annotations, documentation)
///  3. External function annotations: Include, Library, IncludeDirectory, LibraryDirectory, SourceDirectory
///
/// The loadSelector annotated parameters type is handled by
/// LoadSelectorAnalyzer in ModelicaGraph, which has access to the full
/// graph for cross-model parameter modification tracking.
/// </summary>
public class ExternalResourceExtractor : modelicaBaseVisitor<object?>
{
    private static readonly Dictionary<string, ResourceReferenceType> ExternalAnnotationKeys = new(StringComparer.Ordinal)
    {
        { "Include", ResourceReferenceType.ExternalInclude },
        { "Library", ResourceReferenceType.ExternalLibrary },
        { "IncludeDirectory", ResourceReferenceType.ExternalIncludeDirectory },
        { "LibraryDirectory", ResourceReferenceType.ExternalLibraryDirectory },
        { "SourceDirectory", ResourceReferenceType.ExternalSourceDirectory }
    };

    private readonly List<ExternalResourceInfo> _resources = new();
    private bool _inLoadResourceCall = false;

    /// <summary>
    /// Gets the extracted external resource references.
    /// </summary>
    public List<ExternalResourceInfo> Resources => _resources;

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        // Check if this composition has an external clause.
        // Grammar: ('external' (language_specification)? (external_function_call)? (annotation)? ';')?
        // When 'external' is present, the external annotation (if any) is the first annotation().
        // When 'external' is absent, the first (and only) annotation() is the class-level annotation.
        bool hasExternal = false;
        for (int i = 0; i < context.ChildCount; i++)
        {
            if (context.GetChild(i) is Antlr4.Runtime.Tree.ITerminalNode terminal &&
                terminal.GetText() == "external")
            {
                hasExternal = true;
                break;
            }
        }

        if (hasExternal)
        {
            var annotations = context.annotation();
            if (annotations != null && annotations.Length > 0)
            {
                // The first annotation belongs to the external clause
                ExtractExternalAnnotationResources(annotations[0]);
            }
        }

        return base.VisitComposition(context);
    }

    /// <summary>
    /// Extracts resource references from an external clause annotation.
    /// Looks for Include, Library, IncludeDirectory, and LibraryDirectory keys.
    /// </summary>
    private void ExtractExternalAnnotationResources(modelicaParser.AnnotationContext annotation)
    {
        var classMod = annotation.class_modification();
        if (classMod == null)
            return;

        var argList = classMod.argument_list();
        if (argList == null)
            return;

        foreach (var argument in argList.argument())
        {
            var elemModOrReplaceable = argument.element_modification_or_replaceable();
            if (elemModOrReplaceable == null)
                continue;

            var elemMod = elemModOrReplaceable.element_modification();
            if (elemMod == null)
                continue;

            var nameCtx = elemMod.name();
            if (nameCtx == null)
                continue;

            var name = nameCtx.GetText();
            if (!ExternalAnnotationKeys.TryGetValue(name, out var referenceType))
                continue;

            var modification = elemMod.modification();
            if (modification == null)
                continue;

            var modExpr = modification.modification_expression();
            if (modExpr == null)
                continue;

            var expression = modExpr.expression();
            if (expression == null)
                continue;

            // Extract string values - could be a single string or an array {str1, str2, ...}
            ExtractStringValues(expression, referenceType);
        }
    }

    /// <summary>
    /// Extracts string values from an expression, handling both single strings
    /// and array literals like {"str1", "str2"}.
    /// </summary>
    private void ExtractStringValues(modelicaParser.ExpressionContext expression, ResourceReferenceType referenceType)
    {
        var primaries = new List<modelicaParser.PrimaryContext>();
        CollectPrimaries(expression, primaries);

        foreach (var primary in primaries)
        {
            if (primary.STRING() != null)
            {
                var path = StripQuotes(primary.STRING().GetText());
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _resources.Add(new ExternalResourceInfo
                    {
                        RawPath = path,
                        ReferenceType = referenceType
                    });
                }
            }
            else if (primary.array_arguments() != null)
            {
                // Array literal: {expr1, expr2, ...}
                ExtractStringsFromArrayArguments(primary.array_arguments(), referenceType);
            }
        }
    }

    /// <summary>
    /// Extracts string values from array arguments like {"lib1", "lib2"}.
    /// </summary>
    private void ExtractStringsFromArrayArguments(modelicaParser.Array_argumentsContext arrayArgs, ResourceReferenceType referenceType)
    {
        var primaries = new List<modelicaParser.PrimaryContext>();
        CollectPrimaries(arrayArgs, primaries);

        foreach (var primary in primaries)
        {
            if (primary.STRING() != null)
            {
                var path = StripQuotes(primary.STRING().GetText());
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _resources.Add(new ExternalResourceInfo
                    {
                        RawPath = path,
                        ReferenceType = referenceType
                    });
                }
            }
        }
    }

    /// <summary>
    /// Recursively collects all PrimaryContext nodes under a parse tree node.
    /// </summary>
    private static void CollectPrimaries(Antlr4.Runtime.Tree.IParseTree node, List<modelicaParser.PrimaryContext> primaries)
    {
        if (node is modelicaParser.PrimaryContext primary)
        {
            primaries.Add(primary);
            return;
        }

        for (int i = 0; i < node.ChildCount; i++)
        {
            CollectPrimaries(node.GetChild(i), primaries);
        }
    }

    public override object? VisitPrimary([NotNull] modelicaParser.PrimaryContext context)
    {
        // Check for function calls like Modelica.Utilities.Files.loadResource(...)
        // or ModelicaServices.ExternalReferences.loadResource(...)
        if (context.component_reference() != null && context.function_call_args() != null)
        {
            var identTokens = context.component_reference().IDENT();
            if (identTokens != null && identTokens.Length > 0)
            {
                var function = string.Join(".", identTokens.Select(t => t.GetText()));
                if (function == "Modelica.Utilities.Files.loadResource" ||
                    function == "ModelicaServices.ExternalReferences.loadResource")
                {
                    _inLoadResourceCall = true;
                    Visit(context.function_call_args());
                    _inLoadResourceCall = false;
                    return null;
                }
            }
        }

        // Check for string literals containing modelica:// URIs
        if (context.STRING() != null)
        {
            var text = context.STRING().GetText();
            if (_inLoadResourceCall)
            {
                // Inside loadResource call - capture the argument as a LoadResource reference
                var path = StripQuotes(text);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _resources.Add(new ExternalResourceInfo
                    {
                        RawPath = path,
                        ReferenceType = ResourceReferenceType.LoadResource
                    });
                }
            }
            else
            {
                // Check for modelica:// URI references in any string literal
                ExtractModelicaUris(text);
            }
            return null;
        }

        return base.VisitPrimary(context);
    }

    /// <summary>
    /// Extracts modelica:// URIs from a string literal that reference files
    /// (as opposed to model references which don't have file extensions).
    /// </summary>
    private void ExtractModelicaUris(string stringLiteral)
    {
        var text = StripQuotes(stringLiteral);
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Find all modelica:// URIs in the text
        // These can appear in HTML documentation strings like:
        //   <img src="modelica://Modelica/Resources/Images/foo.png">
        var searchText = text;
        int startIndex = 0;

        while (startIndex < searchText.Length)
        {
            var uriStart = searchText.IndexOf("modelica://", startIndex, StringComparison.OrdinalIgnoreCase);
            if (uriStart < 0)
                break;

            // Extract the URI up to the next whitespace, quote, or angle bracket
            var uriEnd = uriStart + "modelica://".Length;
            while (uriEnd < searchText.Length &&
                   !char.IsWhiteSpace(searchText[uriEnd]) &&
                   searchText[uriEnd] != '"' &&
                   searchText[uriEnd] != '\'' &&
                   searchText[uriEnd] != '>' &&
                   searchText[uriEnd] != '<' &&
                   searchText[uriEnd] != ')' &&
                   searchText[uriEnd] != '\\')
            {
                uriEnd++;
            }

            var uri = searchText.Substring(uriStart, uriEnd - uriStart);

            // Only include URIs that reference files (have a file extension)
            // Skip model references like modelica://Modelica.Blocks.Continuous
            if (HasFileExtension(uri))
            {
                _resources.Add(new ExternalResourceInfo
                {
                    RawPath = uri,
                    ReferenceType = ResourceReferenceType.UriReference
                });
            }

            startIndex = uriEnd;
        }
    }

    /// <summary>
    /// Checks if a modelica:// URI references a file (has an extension like .mat, .png, .txt).
    /// URIs without a '/' path separator after the library name are model references, not files.
    /// </summary>
    private static bool HasFileExtension(string uri)
    {
        // Strip the modelica:// prefix
        var pathPart = uri.Substring("modelica://".Length);

        // Must have a '/' to be a file reference (model references use dots only)
        var lastSlash = pathPart.LastIndexOf('/');
        if (lastSlash < 0)
            return false;

        var lastSegment = pathPart.Substring(lastSlash + 1);
        var dotIndex = lastSegment.LastIndexOf('.');
        return dotIndex > 0 && dotIndex < lastSegment.Length - 1;
    }

    /// <summary>
    /// Removes surrounding quotes from a string literal.
    /// </summary>
    private static string StripQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2);
        return text;
    }
}
