using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks public parameters and constants have descriptions.
/// </summary>
public class PublicParametersAndConstantsHaveDescription : VisitorWithModelNameTracking
{
    private bool _isPublic = true;
    private bool _isParameter = false;
    private bool _isConstant = false;
    private readonly bool _parameterHasDescription;
    private readonly bool _constantHasDescription;

    /// <summary>
    /// Creates a new instance with the specified options and optional base package prefix.
    /// </summary>
    /// <param name="parameterHasDescription">Check that parameters have descriptions</param>
    /// <param name="constantHasDescription">Check that constants have descriptions</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public PublicParametersAndConstantsHaveDescription(bool parameterHasDescription, bool constantHasDescription, string basePackage = "")
        : base(basePackage)
    {
        _parameterHasDescription = parameterHasDescription;
        _constantHasDescription = constantHasDescription;
    }

    protected override void OnClassEntered()
    {
        _isPublic = true;
    }

    public override object? VisitStored_definition([NotNull] modelicaParser.Stored_definitionContext context)
    {
        if (!_parameterHasDescription && !_constantHasDescription)
            return null;

        return base.VisitStored_definition(context);
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
            {
                _isParameter = true;
                _isConstant = false;
            }
            else if (prefix.Contains("constant"))
            {
                _isParameter = false;
                _isConstant = true;
            }
            else
            {
                _isParameter = false;
                _isConstant = false;
            }
        }
        else
        {
            _isParameter = false;
            _isConstant = false;
        }

        return base.VisitComponent_clause(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        var logConstant = _isPublic && (_isConstant && _constantHasDescription);
        var logParameter = _isPublic && (_isParameter && _parameterHasDescription);
        var logError = logConstant || logParameter;
        if (!logError)
            return  base.VisitComponent_declaration(context);

        var declaration = context.declaration();
        var comment = context.comment();
        var variableName = declaration.IDENT().GetText();
        var variableType = _isParameter ? "parameter" : "constant";
        var lineNumber = context.Start.Line;

        if (comment != null && (_isParameter || _isConstant))
        {
            var description = comment.string_comment();
            if (description != null)
            {
                var strings = description.STRING();
                if (strings != null && strings.Length > 0)
                {
                    string descriptionString = string.Empty;
                    for (int i = 0; i < strings.Length; i++)
                    {
                        if (i > 0)
                            descriptionString += " ";
                        descriptionString += strings[i].GetText();
                    }
                    //Remove quotes and check for empty string
                    descriptionString = descriptionString.Replace("\"", "");
                    if (descriptionString.Trim().Length == 0)
                    {
                        AddViolation(lineNumber, $"Public {variableType} {variableName} has an empty string as a description");
                    }
                }
                else
                {
                    AddViolation(lineNumber, $"Public {variableType} {variableName} must have a description");
                }
            }
            //This can never be reached because description is NEVER null
            // else
            // {
            //     AddViolation(lineNumber, $"Public {variableType} {variableName} must have a description");
            // }
        }
        //This can never be reached because comment is NEVER null
        // else
        // {
        //     AddViolation(lineNumber, $"Public {variableType} {variableName} must have a description");
        // }
        return base.VisitComponent_declaration(context);
    }
}
