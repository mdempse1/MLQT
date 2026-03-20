using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Base class for style rule visitors that tracks the fully qualified model name
/// as it traverses nested class definitions. Provides common infrastructure for
/// rule violations, model name resolution, and hooks for subclass-specific stack management.
/// </summary>
public class VisitorWithModelNameTracking : modelicaBaseVisitor<object?>
{
    private readonly Stack<string> _parentModelNames = new();
    private readonly List<LogMessage> _ruleViolations = new();
    private string _withinPackage = string.Empty;
    private int _classDepth;

    /// <summary>
    /// Creates a new instance with an optional base package prefix.
    /// </summary>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause.</param>
    protected VisitorWithModelNameTracking(string basePackage = "")
    {
        _withinPackage = basePackage;
        if (!string.IsNullOrEmpty(basePackage))
        {
            _parentModelNames.Push(basePackage);
            OnClassEntered();
        }
    }

    /// <summary>
    /// Gets all rule violations found.
    /// </summary>
    public List<LogMessage> RuleViolations => _ruleViolations;

    /// <summary>
    /// Gets the fully qualified name of the current model being visited.
    /// </summary>
    protected string CurrentModelName => _parentModelNames.Count > 0 ? _parentModelNames.Peek() : string.Empty;

    /// <summary>
    /// Adds a style warning violation for the current model.
    /// </summary>
    protected void AddViolation(int lineNumber, string message)
    {
        _ruleViolations.Add(new LogMessage(CurrentModelName, "Style warning", lineNumber, message)
        {
            Source = "StyleChecking"
        });
    }

    /// <summary>
    /// Called after a class name has been pushed onto the model name stack.
    /// Override to push onto parallel stacks or reset per-class state.
    /// </summary>
    protected virtual void OnClassEntered() { }

    /// <summary>
    /// Called before a class name is popped from the model name stack.
    /// Override to pop from parallel stacks.
    /// </summary>
    protected virtual void OnClassExited() { }

    public override object? VisitStored_definition([NotNull] modelicaParser.Stored_definitionContext context)
    {
        var nameContexts = context.name();

        if (nameContexts != null && nameContexts.Length > 0)
        {
            var nameContext = nameContexts[0];
            var identTokens = nameContext.IDENT();
            if (identTokens != null && identTokens.Length > 0)
            {
                _withinPackage = string.Join(".", identTokens.Select(t => t.GetText()));
                _parentModelNames.Push(_withinPackage);
            }
        }

        return base.VisitStored_definition(context);
    }

    public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
    {
        _classDepth++;

        // Skip nested class definitions — each nested class has its own ModelNode
        // and will be style-checked independently. Without this guard, a parent
        // package's code (which includes nested classes) would produce duplicate
        // violations for the same model.
        if (_classDepth > 1)
        {
            _classDepth--;
            return null;
        }

        var specifier = context.class_specifier();

        if (specifier.long_class_specifier() != null)
        {
            var longSpec = specifier.long_class_specifier();
            var identTokens = longSpec.IDENT();

            if (identTokens != null && identTokens.Length > 0)
            {
                var modelName = identTokens[0].GetText();
                PushModelName(modelName);

                base.VisitClass_definition(context);

                OnClassExited();
                _parentModelNames.Pop();

                _classDepth--;
                return null;
            }
        }
        else if (specifier.short_class_specifier() != null)
        {
            var shortSpec = specifier.short_class_specifier();
            var modelName = shortSpec.IDENT().GetText();
            PushModelName(modelName);

            base.VisitClass_definition(context);

            OnClassExited();
            _parentModelNames.Pop();

            _classDepth--;
            return null;
        }
        else if (specifier.der_class_specifier() != null)
        {
            var derSpec = specifier.der_class_specifier();
            var modelName = derSpec.IDENT()[0].GetText();
            PushModelName(modelName);

            base.VisitClass_definition(context);

            OnClassExited();
            _parentModelNames.Pop();

            _classDepth--;
            return null;
        }

        _classDepth--;
        return base.VisitClass_definition(context);
    }

    private void PushModelName(string modelName)
    {
        _parentModelNames.Push(_parentModelNames.Count > 0
            ? _parentModelNames.Peek() + "." + modelName
            : _withinPackage.Length > 0
                ? _withinPackage + "." + modelName
                : modelName);
        OnClassEntered();
    }
}
