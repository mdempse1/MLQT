using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks import statements appear before other class elements.
/// </summary>
public class ImportStatementsFirst : VisitorWithModelNameTracking
{
    private readonly Stack<bool> _foundOtherElement = new();
    private readonly bool _importsFirst;
    private bool _foundImports;

    /// <summary>
    /// Creates a new instance with the specified options and optional base package prefix.
    /// </summary>
    /// <param name="importsFirst">Whether imports should be first</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public ImportStatementsFirst(bool importsFirst, string basePackage = "") : base(basePackage)
    {
        _importsFirst = importsFirst;
    }

    protected override void OnClassEntered()
    {
        _foundOtherElement.Push(false);
        _foundImports = false;
    }

    protected override void OnClassExited()
    {
        _foundOtherElement.Pop();
    }

    public override object? VisitElement([NotNull] modelicaParser.ElementContext context)
    {
        if (context.import_clause() != null)
        {
            _foundImports = true;
            if (_foundOtherElement.Peek())
            {
                AddViolation(context.Start.Line,
                    "This class does not have its import statements before the rest of the class definition");
            }
        }
        else if (context.extends_clause() != null)
        {
            if (_importsFirst && !_foundOtherElement.Peek()) {
                _foundOtherElement.Pop();
                _foundOtherElement.Push(true);
            }
            if (_foundImports && !_importsFirst)
            {
                AddViolation(context.Start.Line,
                    "This class does not have its extends clauses before the import statements");
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
