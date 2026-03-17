using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks initial equation/algorithm sections appear before or after regular ones,
/// depending on the configured mode.
/// </summary>
public class InitialEquationFirst : VisitorWithModelNameTracking
{
    private readonly Stack<bool> _foundOtherElement = new();
    private readonly Stack<bool> _foundInitialSection = new();
    private readonly bool _initialFirst;
    private readonly bool _initialLast;

    /// <summary>
    /// Creates a new instance with the specified options and optional base package prefix.
    /// </summary>
    /// <param name="initialFirst">Whether initial sections should appear before regular ones</param>
    /// <param name="initialLast">Whether initial sections should appear after regular ones</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public InitialEquationFirst(bool initialFirst, bool initialLast, string basePackage = "") : base(basePackage)
    {
        _initialFirst = initialFirst;
        _initialLast = initialLast;
    }

    protected override void OnClassEntered()
    {
        _foundOtherElement.Push(false);
        _foundInitialSection.Push(false);
    }

    protected override void OnClassExited()
    {
        _foundOtherElement.Pop();
        _foundInitialSection.Pop();
    }

    public override object? VisitEquation_section([NotNull] modelicaParser.Equation_sectionContext context)
    {
        if (context.GetChild(0)?.GetText() == "initial")
        {
            SetFoundInitial();
            if (_initialFirst && _foundOtherElement.Peek())
            {
                AddViolation(context.Start.Line,
                    "The initial equation section should appear before the equation/algorithm section");
            }
        }
        else
        {
            if (_initialLast && _foundInitialSection.Peek())
            {
                AddViolation(context.Start.Line,
                    "The initial equation section should appear after the equation/algorithm section");
            }
            SetFoundOther();
        }
        return base.VisitEquation_section(context);
    }

    public override object? VisitAlgorithm_section([NotNull] modelicaParser.Algorithm_sectionContext context)
    {
        if (context.GetChild(0)?.GetText() == "initial")
        {
            SetFoundInitial();
            if (_initialFirst && _foundOtherElement.Peek())
            {
                AddViolation(context.Start.Line,
                    "The initial algorithm section should appear before the equation/algorithm section");
            }
        }
        else
        {
            if (_initialLast && _foundInitialSection.Peek())
            {
                AddViolation(context.Start.Line,
                    "The initial algorithm section should appear after the equation/algorithm section");
            }
            SetFoundOther();
        }
        return base.VisitAlgorithm_section(context);
    }

    private void SetFoundOther()
    {
        if (!_foundOtherElement.Peek())
        {
            _foundOtherElement.Pop();
            _foundOtherElement.Push(true);
        }
    }

    private void SetFoundInitial()
    {
        if (!_foundInitialSection.Peek())
        {
            _foundInitialSection.Pop();
            _foundInitialSection.Push(true);
        }
    }
}
