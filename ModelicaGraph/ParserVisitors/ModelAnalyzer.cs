using Antlr4.Runtime.Misc;
using ModelicaParser;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Combined visitor that performs dependency analysis, external resource extraction,
/// and loadSelector parameter discovery in a single pass over the parse tree.
///
/// Replaces separate DependencyAnalyzer and LoadSelectorAnalyzer (Pass 1) visitors to avoid walking
/// the same parse tree multiple times.
///
/// <para><b>The resource rules are inherited, not copied (B511).</b> loadResource calls,
/// <c>modelica://</c> URIs and the external clause's annotations are found by
/// <see cref="ExternalResourceExtractor"/>, which this class derives from. It used to hold its own
/// copy of those rules, and B209 was fixed in the extractor - which only tests constructed - while
/// the graph build went on using the copy. What is here is what the extractor cannot know: the
/// dependencies, and the loadSelector parameters that need the graph.</para>
///
/// Cross-model loadSelector modification tracking (Pass 2) remains in
/// LoadSelectorModificationAnalyzer as it requires knowledge of all discovered parameters.
/// </summary>
public class ModelAnalyzer : ExternalResourceExtractor
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

    // Depth of annotation nesting, and of the code-bearing annotation arguments inside it. An
    // annotation is metadata, not code, so its content is not collected as references — see
    // ResolveAndAddDependency and CodeBearingAnnotationKeys.
    private int _annotationDepth;
    private int _annotationCodeDepth;

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

    /// <param name="ancestors">Each class's bases, kept for the run: name lookup asks every scope
    /// what it inherits. Share one across the analyzers of one pass.</param>
    /// <param name="interfaces">The interfaces of the classes a component reference reaches through
    /// (<c>Modelica.Constants</c>), kept for the run. Share one across the analyzers of one pass.</param>
    public ModelAnalyzer(
        string modelId, DirectedGraph graph, Analysis.TypeResolver.AncestorCache? ancestors = null,
        Analysis.ClassElementResolver.InterfaceCache? interfaces = null)
    {
        _modelId = modelId;
        _graph = graph;
        _ancestors = ancestors;
        _interfaces = interfaces;
    }

    private readonly Analysis.TypeResolver.AncestorCache? _ancestors;
    private readonly Analysis.ClassElementResolver.InterfaceCache? _interfaces;

    // Made the first time a reference is not a class by its whole name, which is most classes with an
    // equation in them, and kept for this class only.
    private Analysis.ComponentReferences? _references;

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
            // The class's own imports, read before anything in it is visited. An import is in scope
            // for the whole class wherever it is written, and a nested class's imports are that
            // class's scope, not this one's. Collected as the visit reached them, a reference written
            // above its import missed it and a nested class's import leaked out to every reference
            // after it (B348).
            if (_classDepth == 1)
            {
                _imports.Clear();
                _imports.AddRange(ReferenceResolver.CollectClassImports(context));
            }

            return base.VisitClass_definition(context);
        }
        finally
        {
            _classDepth--;
        }
    }

    /// <summary>
    /// Visit name contexts to extract dependencies from function calls and references.
    /// </summary>
    public override object? VisitName([NotNull] modelicaParser.NameContext context)
    {
        var reference = ReferenceResolver.GetReferenceName(context);
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

    #region LoadSelector Pass 1 - loadResource defaults

    /// <summary>
    /// Flags a parameter whose default comes from <c>loadResource</c>, then leaves the resource itself
    /// to <see cref="ExternalResourceExtractor.VisitPrimary"/> - the one place the loadResource,
    /// <c>modelica://</c> and external-annotation rules are written.
    /// </summary>
    public override object? VisitPrimary([NotNull] modelicaParser.PrimaryContext context)
    {
        // The short form - loadResource(...) under an import - marks the parameter too. It is not
        // captured as a resource: which function it names is not known without the imports.
        if (_isParameterDeclaration && (IsLoadResourceCall(context) || IsShortFormLoadResourceCall(context)))
            _hasLoadResourceDefault = true;

        return base.VisitPrimary(context);
    }

    private static bool IsShortFormLoadResourceCall(modelicaParser.PrimaryContext context)
        => context.function_call_args() != null
           && context.component_reference()?.IDENT() is [{ } only]
           && only.GetText() == "loadResource";

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
                    Resources.Add(new ExternalResourceInfo
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

        // Everything below here is annotation content, and dependency recording is off for it (see
        // ResolveAndAddDependency). The subtree is still walked: external resources, modelica:// URIs
        // and loadSelector parameters all live inside annotations and are what this visitor is for.
        _annotationDepth++;
        try
        {
            return base.VisitAnnotation(context);
        }
        finally
        {
            _annotationDepth--;
        }
    }

    /// <summary>
    /// The annotation arguments whose contents name classes the translator really calls, rather than
    /// describing how to draw something. Inside one of these, dependency recording is on again.
    ///
    /// <para><c>derivative</c> and <c>inverse</c> name functions (§12.7, §12.8) and <c>choices</c>
    /// names the classes a redeclaration may be given (§7.3.4). Without them the blanket exclusion
    /// would drop real edges and report every <c>_der</c> function as an unused class.</para>
    /// </summary>
    private static readonly HashSet<string> CodeBearingAnnotationKeys = new(StringComparer.Ordinal)
    {
        "derivative", "inverse", "choices", "choice"
    };

    /// <summary>
    /// An annotation argument: <c>name (modification)? string_comment</c>. The name is the key —
    /// <c>Icon</c>, <c>Dialog</c>, <c>Placement</c> — and is never a class reference, so it is not
    /// visited as one. Only the modification is, and only under a code-bearing key does it record.
    /// </summary>
    public override object? VisitElement_modification([NotNull] modelicaParser.Element_modificationContext context)
    {
        if (_annotationDepth == 0)
            return base.VisitElement_modification(context);

        var key = context.name()?.GetText();
        var isCodeBearing = key != null && CodeBearingAnnotationKeys.Contains(key);
        if (isCodeBearing)
            _annotationCodeDepth++;
        try
        {
            // The key itself is skipped; its value is not.
            if (context.modification() is { } modification)
                Visit(modification);
            return null;
        }
        finally
        {
            if (isCodeBearing)
                _annotationCodeDepth--;
        }
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

        // An annotation describes the class; it does not use anything. Its contents are annotation
        // grammar — Line, Rectangle, Dialog, Placement — and name resolution cannot tell that from a
        // class, so a library holding a class of the same name collected an edge from every model
        // with an icon. The uses(...) annotation is the sharpest case of both directions: it names
        // the libraries, so counting it as a reference made every declared library look used and
        // every graphics keyword look like a library (B246). CodeBearingAnnotationKeys is the
        // exception — derivative, inverse and choices do name classes.
        if (_annotationDepth > 0 && _annotationCodeDepth == 0)
            return;

        var resolvedId = ReferenceResolver.Resolve(_graph, _modelId, _imports, reference, _ancestors)
                         ?? ClassAReferenceStartsIn(reference);
        if (resolvedId != null && resolvedId != _modelId)
        {
            var referencedModel = _graph.GetNode<ModelNode>(resolvedId);
            if (referencedModel != null)
            {
                _referencedModels.Add(resolvedId);
            }
        }
    }

    // The class a component reference goes through: Modelica.Constants in `Modelica.Constants.pi`,
    // the base's Medium in `Medium.p`, the package that lends a constant used by its bare name, the
    // enumeration in `Types.Init.SteadyState`. Whole-name resolution answers only for classes, so
    // these uses linked to nothing, and the classes they use read as unused.
    private string? ClassAReferenceStartsIn(string reference)
    {
        if (_references is null)
        {
            if (_graph.GetNode<ModelNode>(_modelId) is not { } node)
                return null;
            _references = Analysis.ClassElementResolver.ReferencesIn(_graph, node, _interfaces, _ancestors);
        }

        return _references.Start(reference)?.Scope.Id;
    }

    private string GetQualifiedName(modelicaParser.NameContext context)
        => ReferenceResolver.GetQualifiedName(context);

    private string GetComponentReferenceName(modelicaParser.Component_referenceContext context)
        => ReferenceResolver.GetComponentReferenceName(context);

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
