using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks class names and element names against configurable naming conventions.
/// Tracks public/protected visibility and parameter/constant/variable categorization
/// using the same patterns as <see cref="PublicParametersAndConstantsHaveDescription"/>.
/// </summary>
public class FollowNamingConvention : VisitorWithModelNameTracking
{
    private readonly NamingConventionConfig _config;
    private readonly Stack<string> _classTypeStack = new();
    private bool _isPublic = true;
    private ElementCategory _currentElementCategory = ElementCategory.Variable;

    // Pending class name check — stored before base call, executed in OnClassEntered
    // after the model name has been pushed onto the stack
    private string? _pendingClassName;
    private string? _pendingClassType;
    private int _pendingClassLine;

    private enum ElementCategory { Variable, Parameter, Constant }

    public FollowNamingConvention(NamingConventionConfig config, string basePackage = "")
        : base(basePackage)
    {
        _config = config;
    }

    protected override void OnClassEntered()
    {
        _isPublic = true;

        // Check the class name now that CurrentModelName points to this class
        if (_pendingClassName != null && _pendingClassType != null)
        {
            CheckClassName(_pendingClassName, _pendingClassType, _pendingClassLine);
            _pendingClassName = null;
            _pendingClassType = null;
        }
    }

    protected override void OnClassExited()
    {
        if (_classTypeStack.Count > 0)
            _classTypeStack.Pop();
    }

    public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
    {
        var classType = ExtractClassType(context.class_prefixes());
        _classTypeStack.Push(classType);

        // Store pending check — will be executed in OnClassEntered after the model name is pushed
        _pendingClassName = ExtractClassName(context.class_specifier());
        _pendingClassType = classType;
        _pendingClassLine = context.Start.Line;

        return base.VisitClass_definition(context);
    }

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        var elementList = context.element_list();
        var elementCounter = 0;

        if (context.children != null)
        {
            for (int i = 0; i < context.children.Count; i++)
            {
                var child = context.children[i];
                var text = child.GetText();

                if (text == "public")
                {
                    _isPublic = true;
                    Visit(elementList[elementCounter]);
                    elementCounter++;
                    i++;
                }
                else if (text == "protected")
                {
                    _isPublic = false;
                    Visit(elementList[elementCounter]);
                    elementCounter++;
                    i++;
                }
                else if (child is modelicaParser.Element_listContext)
                {
                    Visit(elementList[elementCounter]);
                    elementCounter++;
                }
            }
        }
        return null;
    }

    public override object? VisitComponent_clause([NotNull] modelicaParser.Component_clauseContext context)
    {
        if (context.type_prefix() != null)
        {
            var prefix = context.type_prefix().GetText();
            if (prefix.Contains("parameter"))
                _currentElementCategory = ElementCategory.Parameter;
            else if (prefix.Contains("constant"))
                _currentElementCategory = ElementCategory.Constant;
            else
                _currentElementCategory = ElementCategory.Variable;
        }
        else
        {
            _currentElementCategory = ElementCategory.Variable;
        }

        return base.VisitComponent_clause(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        var declaration = context.declaration();
        var name = declaration.IDENT().GetText();

        // Strip surrounding single quotes from quoted Modelica identifiers (e.g., 'r_0' → r_0)
        name = StripQuotes(name);

        // Strip array subscripts if present (e.g., "variableName[3]" → "variableName")
        var bracketIndex = name.IndexOf('[');
        if (bracketIndex > 0)
            name = name[..bracketIndex];

        var lineNumber = context.Start.Line;

        if (_config.ExceptionNames.Contains(name))
            return base.VisitComponent_declaration(context);

        var style = GetElementNamingStyle();
        if (style != NamingStyle.Any && !NamingValidator.IsValid(name, style,
                _config.AllowUnderscoreSuffixes))
        {
            var visibility = _isPublic ? "public" : "protected";
            var category = _currentElementCategory switch
            {
                ElementCategory.Parameter => "parameter",
                ElementCategory.Constant => "constant",
                _ => "variable"
            };
            AddViolation(lineNumber,
                $"{char.ToUpper(category[0])}{category[1..]} name '{name}' should be {FormatStyleName(style)} ({visibility} {category})");
        }

        return base.VisitComponent_declaration(context);
    }

    private void CheckClassName(string className, string classType, int lineNumber)
    {
        if (_config.ExceptionNames.Contains(className))
            return;

        if (!_config.ClassNamingRules.TryGetValue(classType, out var style))
            return;

        if (style == NamingStyle.Any)
            return;

        if (!NamingValidator.IsValid(className, style, _config.AllowUnderscoreSuffixes))
        {
            AddViolation(lineNumber,
                $"Class name '{className}' should be {FormatStyleName(style)} ({classType})");
        }
    }

    private NamingStyle GetElementNamingStyle()
    {
        return (_isPublic, _currentElementCategory) switch
        {
            (true, ElementCategory.Parameter) => _config.PublicParameterNaming,
            (true, ElementCategory.Constant) => _config.PublicConstantNaming,
            (true, ElementCategory.Variable) => _config.PublicVariableNaming,
            (false, ElementCategory.Parameter) => _config.ProtectedParameterNaming,
            (false, ElementCategory.Constant) => _config.ProtectedConstantNaming,
            (false, ElementCategory.Variable) => _config.ProtectedVariableNaming,
            _ => NamingStyle.Any
        };
    }

    private static string ExtractClassType(modelicaParser.Class_prefixesContext prefixes)
    {
        var text = prefixes.GetText();
        // Check most specific first
        if (text.Contains("function")) return "function";
        if (text.Contains("connector")) return "connector";
        if (text.Contains("record")) return "record";
        if (text.Contains("model")) return "model";
        if (text.Contains("block")) return "block";
        if (text.Contains("type")) return "type";
        if (text.Contains("package")) return "package";
        if (text.Contains("operator")) return "operator";
        return "class";
    }

    private static string? ExtractClassName(modelicaParser.Class_specifierContext specifier)
    {
        string? name = null;
        if (specifier.long_class_specifier() != null)
        {
            var idents = specifier.long_class_specifier().IDENT();
            name = idents?.Length > 0 ? idents[0].GetText() : null;
        }
        else if (specifier.short_class_specifier() != null)
            name = specifier.short_class_specifier().IDENT().GetText();
        else if (specifier.der_class_specifier() != null)
            name = specifier.der_class_specifier().IDENT()[0].GetText();

        return name != null ? StripQuotes(name) : null;
    }

    /// <summary>
    /// Strips surrounding single quotes from quoted Modelica identifiers (e.g., 'r_0' → r_0).
    /// </summary>
    private static string StripQuotes(string name)
    {
        if (name.Length >= 2 && name[0] == '\'' && name[^1] == '\'')
            return name[1..^1];
        return name;
    }

    private static string FormatStyleName(NamingStyle style) => style switch
    {
        NamingStyle.CamelCase => "camelCase",
        NamingStyle.PascalCase => "PascalCase",
        NamingStyle.SnakeCase => "snake_case",
        NamingStyle.UpperCase => "UPPER_CASE",
        _ => style.ToString()
    };
}
