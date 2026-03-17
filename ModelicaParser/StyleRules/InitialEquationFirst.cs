using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks initial equation/algorithm sections appear before regular ones.
/// </summary>
public class InitialEquationFirst : VisitorWithModelNameTracking
{
    private readonly Stack<bool> _foundOtherElement = new();
    private readonly bool _initialFirst;
    private bool _foundInitialSection = false;

    /// <summary>
    /// Creates a new instance with the specified options and optional base package prefix.
    /// </summary>
    /// <param name="initialFirst">Whether initial sections should be first</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public InitialEquationFirst(bool initialFirst, string basePackage = "") : base(basePackage)
    {
        _initialFirst = initialFirst;
    }

    protected override void OnClassEntered()
    {
        _foundOtherElement.Push(false);
    }

    protected override void OnClassExited()
    {
        _foundOtherElement.Pop();
    }

    public override object? VisitEquation_section([NotNull] modelicaParser.Equation_sectionContext context)
    {
        if (context.GetChild(0)?.GetText() == "initial")
        {
            _foundInitialSection = true;
            if (_initialFirst && _foundOtherElement.Peek())
            {
                AddViolation(context.Start.Line,
                    "The initial equation section should appear before the equation/algorithm section");
            }
        }
        else
        {
            if (_foundInitialSection && !_initialFirst)
            {
                AddViolation(context.Start.Line,
                    "The initial equation section should appear before the equation/algorithm section");
            }
            if (!_foundOtherElement.Peek()) {
                _foundOtherElement.Pop();
                _foundOtherElement.Push(true);
            }
        }
        return base.VisitEquation_section(context);
    }

    public override object? VisitAlgorithm_section([NotNull] modelicaParser.Algorithm_sectionContext context)
    {
        if (context.GetChild(0)?.GetText() == "initial")
        {
            _foundInitialSection = true;
            if (_initialFirst && _foundOtherElement.Peek())
            {
                AddViolation(context.Start.Line,
                    "The initial algorithm section should appear before the equation/algorithm section");
            }
        }
        else
        {
            if (_foundInitialSection && !_initialFirst)
            {
                AddViolation(context.Start.Line,
                    "The initial algorithm section should appear before the equation/algorithm section");
            }
            if (!_foundOtherElement.Peek()) {
                _foundOtherElement.Pop();
                _foundOtherElement.Push(true);
            }
        }
        return base.VisitAlgorithm_section(context);
    }
}
