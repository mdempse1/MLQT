using Antlr4.Runtime.Misc;
using ModelicaParser;
using ModelicaParser.DataTypes;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Combined visitor that performs dependency analysis, external resource extraction,
/// and loadSelector parameter discovery in a single pass over the parse tree.
///
/// Replaces separate DependencyAnalyzer, ExternalResourceExtractor, and LoadSelectorAnalyzer
/// (Pass 1) visitors to avoid walking the same parse tree multiple times.
///
/// Cross-model loadSelector modification tracking (Pass 2) remains in
/// LoadSelectorModificationAnalyzer as it requires knowledge of all discovered parameters.
/// </summary>
public class ModelAnalyzer : modelicaBaseVisitor<object?>
{
    private readonly string _modelId;
    private readonly DirectedGraph _graph;

    // --- Dependency analysis state (from DependencyAnalyzer) ---
    private readonly HashSet<string> _referencedModels = new();
    private readonly List<ImportInfo> _imports = new();

    // Depth of class_definition nesting within the node currently being analysed. The node's own
    // (outermost) class is depth 1; any nested class definition is depth >= 2. Dependencies are only
    // recorded at depth 1 — see ResolveAndAddDependency.
    private int _classDepth;

    // --- External resource state (from ExternalResourceExtractor) ---
    private static readonly Dictionary<string, ResourceReferenceType> ExternalAnnotationKeys = new(StringComparer.Ordinal)
    {
        { "Include", ResourceReferenceType.ExternalInclude },
        { "Library", ResourceReferenceType.ExternalLibrary },
        { "IncludeDirectory", ResourceReferenceType.ExternalIncludeDirectory },
        { "LibraryDirectory", ResourceReferenceType.ExternalLibraryDirectory },
        { "SourceDirectory", ResourceReferenceType.ExternalSourceDirectory }
    };

    private readonly List<ExternalResourceInfo> _resources = new();
    private bool _inLoadResourceCall;

    // --- LoadSelector Pass 1 state (from LoadSelectorAnalyzer) ---
    private bool _isParameterDeclaration;
    private string? _currentTypeName;
    private string? _currentParameterName;
    private string? _currentDefaultValue;
    private bool _hasLoadSelector;
    private bool _hasLoadResourceDefault;

    /// <summary>
    /// Gets the discovered model dependencies.
    /// </summary>
    public HashSet<string> ReferencedModels => _referencedModels;

    /// <summary>
    /// Gets the extracted external resource references.
    /// </summary>
    public List<ExternalResourceInfo> Resources => _resources;

    public ModelAnalyzer(string modelId, DirectedGraph graph)
    {
        _modelId = modelId;
        _graph = graph;
    }

    #region Dependency Analysis (from DependencyAnalyzer)

    /// <summary>
    /// Track class-definition nesting depth. A package/class stored in a single file carries the full
    /// source of its nested classes, so this node's parse tree contains those nested class definitions.
    /// Each nested class has its own <see cref="ModelNode"/> and is analysed independently, so we must
    /// NOT attribute their references to this (enclosing) node — otherwise a package appears to "use"
    /// everything its members do (e.g. an Examples package would depend on every class its examples
    /// instantiate). We still descend into nested classes here so external-resource and loadSelector
    /// extraction continue to run; only dependency recording is gated on depth (see ResolveAndAddDependency).
    /// </summary>
    public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
    {
        _classDepth++;
        try
        {
            return base.VisitClass_definition(context);
        }
        finally
        {
            _classDepth--;
        }
    }

    /// <summary>
    /// Visit import clauses to build the import list for dependency resolution.
    /// </summary>
    public override object? VisitImport_clause([NotNull] modelicaParser.Import_clauseContext context)
    {
        var importText = context.GetText();
        var name = context.name();
        if (name != null)
        {
            var qualifiedName = GetQualifiedName(name);
            var ident = context.IDENT();
            if (ident != null)
            {
                _imports.Add(new ImportInfo
                {
                    Alias = ident.GetText(),
                    QualifiedName = qualifiedName,
                    IsWildcard = false
                });
            }
            else if (importText.Contains(".*"))
            {
                _imports.Add(new ImportInfo
                {
                    QualifiedName = qualifiedName,
                    IsWildcard = true
                });
            }
            else
            {
                _imports.Add(new ImportInfo
                {
                    QualifiedName = qualifiedName,
                    IsWildcard = false
                });
            }
        }

        return base.VisitImport_clause(context);
    }

    /// <summary>
    /// Visit name contexts to extract dependencies from function calls and references.
    /// </summary>
    public override object? VisitName([NotNull] modelicaParser.NameContext context)
    {
        var reference = GetQualifiedName(context);
        if (!string.IsNullOrWhiteSpace(reference))
        {
            ResolveAndAddDependency(reference);
        }
        return base.VisitName(context);
    }

    /// <summary>
    /// Visit component references to extract dependencies.
    /// </summary>
    public override object? VisitComponent_reference([NotNull] modelicaParser.Component_referenceContext context)
    {
        var reference = GetComponentReferenceName(context);
        if (!string.IsNullOrWhiteSpace(reference))
        {
            ResolveAndAddDependency(reference);
        }
        return base.VisitComponent_reference(context);
    }

    /// <summary>
    /// Visit type specifiers to extract dependencies from variable declarations.
    /// </summary>
    public override object? VisitType_specifier([NotNull] modelicaParser.Type_specifierContext context)
    {
        var name = context.name();
        if (name != null)
        {
            var typeName = GetQualifiedName(name);
            ResolveAndAddDependency(typeName);
        }
        return base.VisitType_specifier(context);
    }

    /// <summary>
    /// Visit extends clauses to extract inheritance dependencies.
    /// </summary>
    public override object? VisitExtends_clause([NotNull] modelicaParser.Extends_clauseContext context)
    {
        var typeSpecifier = context.type_specifier();
        if (typeSpecifier != null)
        {
            var baseName = GetQualifiedName(typeSpecifier.name());
            ResolveAndAddDependency(baseName);
        }
        return base.VisitExtends_clause(context);
    }

    #endregion

    #region External Resource Extraction (from ExternalResourceExtractor)

    /// <summary>
    /// Visit composition to extract external clause annotation resources
    /// (Include, Library, IncludeDirectory, LibraryDirectory, SourceDirectory).
    /// </summary>
    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
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
                ExtractExternalAnnotationResources(annotations[0]);
            }
        }

        return base.VisitComposition(context);
    }

    /// <summary>
    /// Visit primary to handle both external resource extraction and loadSelector parameter detection.
    /// Merges VisitPrimary from ExternalResourceExtractor and LoadSelectorAnalyzer.
    /// </summary>
    public override object? VisitPrimary([NotNull] modelicaParser.PrimaryContext context)
    {
        // Check for function calls like loadResource(...)
        if (context.component_reference() != null && context.function_call_args() != null)
        {
            var identTokens = context.component_reference().IDENT();
            if (identTokens != null && identTokens.Length > 0)
            {
                var function = string.Join(".", identTokens.Select(t => t.GetText()));

                if (function == "Modelica.Utilities.Files.loadResource" ||
                    function == "ModelicaServices.ExternalReferences.loadResource")
                {
                    // External resource: capture the resource path argument
                    _inLoadResourceCall = true;
                    Visit(context.function_call_args());
                    _inLoadResourceCall = false;

                    // LoadSelector Pass 1: flag loadResource default for parameter tracking
                    if (_isParameterDeclaration)
                        _hasLoadResourceDefault = true;

                    return null;
                }

                // LoadSelector: also check short form "loadResource" for parameter default detection
                if (_isParameterDeclaration && function == "loadResource")
                    _hasLoadResourceDefault = true;
            }
        }

        // Check for string literals
        if (context.STRING() != null)
        {
            var text = context.STRING().GetText();
            if (_inLoadResourceCall)
            {
                // Inside loadResource() call — capture the argument as a LoadResource reference
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

    #endregion

    #region LoadSelector Pass 1 - Parameter Discovery (from LoadSelectorAnalyzer)

    /// <summary>
    /// Visit component_clause to detect parameter declarations and track component type.
    /// </summary>
    public override object? VisitComponent_clause([NotNull] modelicaParser.Component_clauseContext context)
    {
        var typePrefix = context.type_prefix();
        _isParameterDeclaration = typePrefix != null &&
            typePrefix.GetText().Contains("parameter");

        _currentTypeName = context.type_specifier()?.GetText();

        var result = base.VisitComponent_clause(context);

        _isParameterDeclaration = false;
        _currentTypeName = null;

        return result;
    }

    /// <summary>
    /// Visit component_declaration to extract parameter name, default value, and annotation.
    /// After visiting children, checks if a loadSelector or loadResource parameter was found.
    /// </summary>
    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        _currentParameterName = null;
        _currentDefaultValue = null;
        _hasLoadSelector = false;
        _hasLoadResourceDefault = false;

        // Default traversal visits declaration (sets _currentParameterName, _currentDefaultValue,
        // _hasLoadResourceDefault) and comment/annotation (sets _hasLoadSelector)
        var result = base.VisitComponent_declaration(context);

        // After visiting children, process loadSelector/loadResource results
        if (_isParameterDeclaration && _hasLoadSelector && _currentParameterName != null)
        {
            StoreLoadSelectorParameter(_currentParameterName);

            if (!_hasLoadResourceDefault && !string.IsNullOrWhiteSpace(_currentDefaultValue))
            {
                var path = StripQuotes(_currentDefaultValue);
                if (!string.IsNullOrWhiteSpace(path) && path != "NoName" && path != "")
                {
                    _resources.Add(new ExternalResourceInfo
                    {
                        RawPath = path,
                        ReferenceType = ResourceReferenceType.LoadSelector,
                        ParameterName = _currentParameterName
                    });
                }
            }
        }

        if (_isParameterDeclaration && _hasLoadResourceDefault && _currentParameterName != null)
        {
            StoreLoadResourceParameter(_currentParameterName);
        }

        return result;
    }

    /// <summary>
    /// Visit declaration to extract parameter name and default value.
    /// </summary>
    public override object? VisitDeclaration([NotNull] modelicaParser.DeclarationContext context)
    {
        if (context.IDENT() != null)
        {
            _currentParameterName = context.IDENT().GetText();
        }

        if (context.modification()?.modification_expression() != null)
        {
            _currentDefaultValue = context.modification().modification_expression().GetText();
        }

        return base.VisitDeclaration(context);
    }

    /// <summary>
    /// Visit annotation to check for Dialog(loadSelector(...)) pattern in parameter annotations.
    /// </summary>
    public override object? VisitAnnotation([NotNull] modelicaParser.AnnotationContext context)
    {
        if (_isParameterDeclaration && context.class_modification() != null)
        {
            CheckForLoadSelector(context.class_modification());
        }

        return base.VisitAnnotation(context);
    }

    #endregion

    #region Dependency Resolution

    private void ResolveAndAddDependency(string reference)
    {
        // Only record references that appear in this node's own (outermost) class body. References
        // inside nested class definitions belong to those classes' own ModelNodes, which are analysed
        // separately — attributing them here would pollute the enclosing node's dependency edges.
        if (_classDepth > 1)
            return;

        var resolvedId = ReferenceResolver.Resolve(_graph, _modelId, _imports, reference);
        if (resolvedId != null && resolvedId != _modelId)
        {
            var referencedModel = _graph.GetNode<ModelNode>(resolvedId);
            if (referencedModel != null)
            {
                _referencedModels.Add(resolvedId);
            }
        }
    }

    private string GetQualifiedName(modelicaParser.NameContext context)
        => ReferenceResolver.GetQualifiedName(context);

    private string GetComponentReferenceName(modelicaParser.Component_referenceContext context)
        => ReferenceResolver.GetComponentReferenceName(context);

    #endregion

    #region External Resource Helpers

    private void ExtractExternalAnnotationResources(modelicaParser.AnnotationContext annotation)
    {
        var classMod = annotation.class_modification();
        if (classMod == null) return;

        var argList = classMod.argument_list();
        if (argList == null) return;

        foreach (var argument in argList.argument())
        {
            var elemMod = argument.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null) continue;

            var nameCtx = elemMod.name();
            if (nameCtx == null) continue;

            var name = nameCtx.GetText();
            if (!ExternalAnnotationKeys.TryGetValue(name, out var referenceType))
                continue;

            var modification = elemMod.modification();
            var modExpr = modification?.modification_expression();
            var expression = modExpr?.expression();
            if (expression == null) continue;

            ExtractStringValues(expression, referenceType);
        }
    }

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
                ExtractStringsFromArrayArguments(primary.array_arguments(), referenceType);
            }
        }
    }

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

    private void ExtractModelicaUris(string stringLiteral)
    {
        var text = StripQuotes(stringLiteral);
        if (string.IsNullOrWhiteSpace(text)) return;

        var searchText = text;
        int startIndex = 0;

        while (startIndex < searchText.Length)
        {
            var uriStart = searchText.IndexOf("modelica://", startIndex, StringComparison.OrdinalIgnoreCase);
            if (uriStart < 0) break;

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

    private static bool HasFileExtension(string uri)
    {
        var pathPart = uri.Substring("modelica://".Length);
        var lastSlash = pathPart.LastIndexOf('/');
        if (lastSlash < 0) return false;

        var lastSegment = pathPart.Substring(lastSlash + 1);
        var dotIndex = lastSegment.LastIndexOf('.');
        return dotIndex > 0 && dotIndex < lastSegment.Length - 1;
    }

    #endregion

    #region LoadSelector Helpers

    private void CheckForLoadSelector(modelicaParser.Class_modificationContext context)
    {
        var argList = context.argument_list();
        if (argList == null) return;

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null) continue;

            var name = elemMod.name()?.GetText();
            if (string.Equals(name, "Dialog", StringComparison.Ordinal))
            {
                var mod = elemMod.modification();
                if (mod?.class_modification() != null)
                {
                    CheckForLoadSelectorInDialog(mod.class_modification());
                }
            }
        }
    }

    private void CheckForLoadSelectorInDialog(modelicaParser.Class_modificationContext context)
    {
        var argList = context.argument_list();
        if (argList == null) return;

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null) continue;

            var name = elemMod.name()?.GetText();
            if (string.Equals(name, "loadSelector", StringComparison.Ordinal))
            {
                _hasLoadSelector = true;
                return;
            }
        }
    }

    private void StoreLoadSelectorParameter(string parameterName)
    {
        var modelNode = _graph.GetNode<ModelNode>(_modelId);
        if (modelNode == null) return;

        if (!modelNode.LoadSelectorParameters.Contains(parameterName))
        {
            modelNode.LoadSelectorParameters.Add(parameterName);
        }
    }

    private void StoreLoadResourceParameter(string parameterName)
    {
        var modelNode = _graph.GetNode<ModelNode>(_modelId);
        if (modelNode == null) return;

        if (!modelNode.LoadResourceParameters.Contains(parameterName))
        {
            modelNode.LoadResourceParameters.Add(parameterName);
        }
    }

    #endregion

    #region Common Helpers

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2);
        return text;
    }

    #endregion
}
