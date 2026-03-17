using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks extends clauses are placed at the top of class definitions.
/// </summary>
public class ExtendsClausesAtTop : VisitorWithModelNameTracking
{
    private readonly Stack<bool> _foundOtherElement = new();
    private readonly bool _extendsFirst;
    private bool _foundExtends;

    /// <summary>
    /// Creates a new instance with the specified options and optional base package prefix.
    /// </summary>
    /// <param name="extendsFirst">Whether extends clauses should be first</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public ExtendsClausesAtTop(bool extendsFirst, string basePackage = "") : base(basePackage)
    {
        _extendsFirst = extendsFirst;
    }

    protected override void OnClassEntered()
    {
        _foundOtherElement.Push(false);
        _foundExtends = false;
    }

    protected override void OnClassExited()
    {
        _foundOtherElement.Pop();
    }

    public override object? VisitElement([NotNull] modelicaParser.ElementContext context)
    {
        if (context.import_clause() != null)
        {
            if (_extendsFirst && !_foundOtherElement.Peek()) {
                _foundOtherElement.Pop();
                _foundOtherElement.Push(true);
            }
            if (_foundExtends && !_extendsFirst)
            {
                AddViolation(context.Start.Line,
                    "This class does not have its import statements before its extends clauses");
            }
        }
        else if (context.extends_clause() != null)
        {
            _foundExtends = true;
            if (_foundOtherElement.Peek())
            {
                AddViolation(context.Start.Line,
                    "This class does not have its extends clauses at the top of the class");
            }
        }
        else
        {
            if (!_foundOtherElement.Peek()) {
                _foundOtherElement.Pop();
                _foundOtherElement.Push(true);
            }
        }
        return base.VisitElement(context);
    }
}
