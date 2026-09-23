using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

/// <summary>
/// Whether a conditional component is there (B277).
///
/// <para>A component declared <c>heatPort if useHeatPort</c> does not exist when that parameter is
/// false, and a tool does not draw it. MSL's Integrator, Torque and SpringDamper each carry one that
/// is off by default, which put three ports on a diagram that Dymola leaves bare.</para>
///
/// <para><b>The interesting answer is the third one.</b> Modelica allows any boolean expression here
/// and this understands a small part of it; everything else answers null, and a caller must read
/// null as "draw it" — showing a port that is switched off is a smaller lie than hiding one that is
/// switched on, and a connection into it is real either way.</para>
/// </summary>
public class ModelicaConditionTests
{
    private static Func<string, string?> Values(params (string Name, string Value)[] values)
        => name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void NoConditionMeansTheComponentIsAlwaysThere()
    {
        Assert.True(ModelicaCondition.Evaluate(null, Values()));
        Assert.True(ModelicaCondition.Evaluate("   ", Values()));
    }

    [Theory]
    [InlineData("useHeatPort", "false", false)]
    [InlineData("useHeatPort", "true", true)]
    [InlineData("not useHeatPort", "false", true)]
    [InlineData("not useHeatPort", "true", false)]
    public void AParameterIsLookedUp(string condition, string value, bool expected)
        => Assert.Equal(expected, ModelicaCondition.Evaluate(condition, Values(("useHeatPort", value))));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ALiteralNeedsNoLookup(string condition, bool expected)
        => Assert.Equal(expected, ModelicaCondition.Evaluate(condition, Values()));

    [Theory]
    [InlineData("a and b", "true", "true", true)]
    [InlineData("a and b", "true", "false", false)]
    [InlineData("a and b", "false", "false", false)]
    [InlineData("a or b", "false", "false", false)]
    [InlineData("a or b", "false", "true", true)]
    public void AndAndOrAreUnderstood(string condition, string a, string b, bool expected)
        => Assert.Equal(expected, ModelicaCondition.Evaluate(condition, Values(("a", a), ("b", b))));

    [Fact]
    public void ADecisiveOperandDecidesEvenWhenTheOtherIsUnknown()
    {
        // MSL's Integrator declares `set if use_reset and use_set`, and use_reset alone settles it.
        Assert.False(ModelicaCondition.Evaluate("use_reset and use_set", Values(("use_reset", "false"))));
        Assert.True(ModelicaCondition.Evaluate("a or b", Values(("a", "true"))));
    }

    [Theory]
    [InlineData("useHeatPort")]                 // nothing known about it
    [InlineData("n > 0")]                       // a comparison
    [InlineData("size(ports, 1) > 1")]          // a call
    [InlineData("(a and b) or c")]              // parenthesised
    [InlineData("a and unknown")]               // one side unknown, the other not decisive
    public void AnythingItCannotWorkOutSaysSo(string condition)
        => Assert.Null(ModelicaCondition.Evaluate(condition, Values(("a", "true"), ("c", "false"))));

    [Fact]
    public void AParameterBoundToAnExpressionIsNotAValue()
    {
        // "not known" rather than "false": the value is a thing this does not evaluate.
        Assert.Null(ModelicaCondition.Evaluate("useHeatPort", Values(("useHeatPort", "n > 0"))));
    }

    [Fact]
    public void NullsAreRefusedRatherThanTreatedAsEmpty()
        => Assert.Throws<ArgumentNullException>(() => ModelicaCondition.Evaluate("a", null!));
}
