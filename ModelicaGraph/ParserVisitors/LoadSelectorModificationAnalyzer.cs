using Antlr4.Runtime.Misc;
using ModelicaParser;
using ModelicaParser.DataTypes;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Pass 2 analyzer for loadSelector/loadResource parameter modifications.
/// Runs after ModelAnalyzer has discovered all loadSelector and loadResource parameters
/// across all models. Finds modifications of those parameters in component instances.
///
/// Example of a modification this analyzer detects:
///   SomeComponent comp(fileName="modelica://MyLib/Resources/data.mat")
/// where "fileName" is a known loadSelector/loadResource parameter on SomeComponent's type.
/// </summary>
public class LoadSelectorModificationAnalyzer : modelicaBaseVisitor<object?>
{
    private readonly string _modelId;
    private readonly DirectedGraph _graph;
    private readonly List<ExternalResourceInfo> _resources = new();

    // Known loadSelector and loadResource parameters from all models (populated by ModelAnalyzer Pass 1)
    private readonly HashSet<string> _loadSelectorParameters;
    private readonly HashSet<string> _loadResourceParameters;

    // State tracking during traversal
    private bool _isParameterDeclaration;
    private string? _currentTypeName;

    /// <summary>
    /// Gets the extracted external resource references from parameter modifications.
    /// </summary>
    public List<ExternalResourceInfo> Resources => _resources;

    /// <summary>
    /// The two parameter sets this analyzer matches against, built once for a whole pass.
    ///
    /// <para>They describe the <b>graph</b>, not the model being visited, so building them per model
    /// made the pass cost models × graph size. That went unnoticed while the graph held only the
    /// code under check; loading a tool's library folder for reference resolution adds tens of
    /// thousands of classes and turns the same loop into hundreds of millions of visits.</para>
    /// </summary>
    public sealed record ParameterIndex(
        HashSet<string> LoadSelectorParameters, HashSet<string> LoadResourceParameters)
    {
        /// <summary>Indexes every loadSelector/loadResource parameter discovered in pass 1.</summary>
        public static ParameterIndex Build(DirectedGraph graph)
        {
            var loadSelector = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loadResource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var model in graph.ModelNodes)
            {
                foreach (var paramName in model.LoadSelectorParameters)
                    loadSelector.Add($"{model.Id}.{paramName}");

                foreach (var paramName in model.LoadResourceParameters)
                    loadResource.Add($"{model.Id}.{paramName}");
            }

            return new ParameterIndex(loadSelector, loadResource);
        }
    }

    /// <summary>
    /// Creates an analyzer sharing a prebuilt <see cref="ParameterIndex"/>. Prefer this: the index
    /// is the same for every model in a pass.
    /// </summary>
    public LoadSelectorModificationAnalyzer(string modelId, DirectedGraph graph, ParameterIndex parameters)
    {
        _modelId = modelId;
        _graph = graph;
        _loadSelectorParameters = parameters.LoadSelectorParameters;
        _loadResourceParameters = parameters.LoadResourceParameters;
    }

    /// <summary>
    /// Creates an analyzer that builds its own index. Convenient for a one-off analysis of a single
    /// model; do not use it in a loop, which is what made this pass quadratic.
    /// </summary>
    public LoadSelectorModificationAnalyzer(string modelId, DirectedGraph graph)
        : this(modelId, graph, ParameterIndex.Build(graph))
    {
    }

    /// <summary>
    /// Visit component_clause to track type name and parameter status.
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
    /// Visit element_modification to detect modifications of known loadSelector or loadResource parameters.
    /// </summary>
    public override object? VisitElement_modification([NotNull] modelicaParser.Element_modificationContext context)
    {
        var name = context.name()?.GetText();
        if (name != null && context.modification() != null && _currentTypeName != null)
        {
            var fullyQualifiedParam = ResolveParameterName(_currentTypeName, name);
            if (fullyQualifiedParam != null)
            {
                var isLoadSelectorParam = _loadSelectorParameters.Contains(fullyQualifiedParam);
                var isLoadResourceParam = _loadResourceParameters.Contains(fullyQualifiedParam);

                if (isLoadSelectorParam || isLoadResourceParam)
                {
                    var mod = context.modification();
                    if (mod.modification_expression() != null)
                    {
                        var value = ExtractModificationValue(mod.modification_expression());
                        if (!string.IsNullOrWhiteSpace(value) && value != "NoName" && value != "")
                        {
                            _resources.Add(new ExternalResourceInfo
                            {
                                RawPath = value,
                                ReferenceType = isLoadSelectorParam
                                    ? ResourceReferenceType.LoadSelector
                                    : ResourceReferenceType.LoadResourceParameter,
                                ParameterName = name
                            });
                        }
                    }
                }
            }
        }

        return base.VisitElement_modification(context);
    }

    private static string? ExtractModificationValue(modelicaParser.Modification_expressionContext modExpr)
    {
        var expression = modExpr.expression();
        if (expression == null)
            return StripQuotes(modExpr.GetText());

        if (TryExtractLoadResourcePath(expression) != null)
            return null;

        return StripQuotes(modExpr.GetText());
    }

    private static string? TryExtractLoadResourcePath(Antlr4.Runtime.Tree.IParseTree tree)
    {
        if (tree is modelicaParser.PrimaryContext primary)
        {
            var path = TryExtractLoadResourcePathFromPrimary(primary);
            if (path != null) return path;
        }

        for (int i = 0; i < tree.ChildCount; i++)
        {
            var result = TryExtractLoadResourcePath(tree.GetChild(i));
            if (result != null) return result;
        }

        return null;
    }

    private static string? TryExtractLoadResourcePathFromPrimary(modelicaParser.PrimaryContext primary)
    {
        if (primary.component_reference() == null || primary.function_call_args() == null)
            return null;

        var identTokens = primary.component_reference().IDENT();
        if (identTokens == null || identTokens.Length == 0)
            return null;

        var functionName = string.Join(".", identTokens.Select(t => t.GetText()));
        if (functionName != "Modelica.Utilities.Files.loadResource" &&
            functionName != "ModelicaServices.ExternalReferences.loadResource" &&
            functionName != "loadResource")
        {
            return null;
        }

        var funcArgs = primary.function_call_args()?.function_arguments();
        if (funcArgs == null) return null;

        var firstArgExpression = funcArgs.expression();
        if (firstArgExpression != null)
            return ExtractStringFromTree(firstArgExpression);

        var namedArgs = funcArgs.named_arguments();
        if (namedArgs != null)
        {
            var namedArg = namedArgs.named_argument()?.FirstOrDefault();
            if (namedArg?.function_argument()?.expression() != null)
                return ExtractStringFromTree(namedArg.function_argument().expression());
        }

        return null;
    }

    private static string? ExtractStringFromTree(Antlr4.Runtime.Tree.IParseTree tree)
    {
        if (tree is modelicaParser.PrimaryContext primary && primary.STRING() != null)
            return StripQuotes(primary.STRING().GetText());

        for (int i = 0; i < tree.ChildCount; i++)
        {
            var result = ExtractStringFromTree(tree.GetChild(i));
            if (result != null) return result;
        }

        return null;
    }

    private string? ResolveParameterName(string typeName, string paramName)
    {
        var directKey = $"{typeName}.{paramName}";
        if (_loadSelectorParameters.Contains(directKey) || _loadResourceParameters.Contains(directKey))
            return directKey;

        var resolvedType = ResolveTypeName(typeName);
        if (resolvedType != null)
            return $"{resolvedType}.{paramName}";

        return null;
    }

    private string? ResolveTypeName(string typeName)
    {
        if (_graph.GetNode<ModelNode>(typeName) != null)
            return typeName;

        if (_modelId.Contains('.'))
        {
            var parts = _modelId.Split('.');
            for (int i = parts.Length - 1; i >= 1; i--)
            {
                var packagePath = string.Join(".", parts.Take(i));
                var candidate = $"{packagePath}.{typeName}";
                if (_graph.GetNode<ModelNode>(candidate) != null)
                    return candidate;
            }
        }

        return null;
    }

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2);
        return text;
    }
}
