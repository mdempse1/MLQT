using ModelicaParser.Icons;
using Xunit;

namespace ModelicaParser.Tests.Icons;

/// <summary>
/// What an icon's text says once its <c>%</c> references are resolved (B278).
///
/// <para>Substituting <c>%name</c> and nothing else gives a diagram whose every parameter reads
/// <c>J=%J</c>: legible, and silent about the model in front of you.</para>
/// </summary>
public class IconTextTests
{
    private static Func<string, string?> Values(params (string Name, string Value)[] values)
        => name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void TheComponentsNameAndItsClass()
    {
        Assert.Equal("inertia1", IconText.Resolve("%name", "inertia1"));
        Assert.Equal("Inertia", IconText.Resolve("%class", "inertia1", "Inertia"));
    }

    [Fact]
    public void AParameterIsShownAsTheValueItWasGiven()
    {
        Assert.Equal("J=1", IconText.Resolve("J=%J", "inertia1", null, Values(("J", "1"))));
        Assert.Equal(
            "c=1e4\nd=100",
            IconText.Resolve("c=%c\nd=%d", "spring", null, Values(("c", "1e4"), ("d", "100"))));
    }

    [Fact]
    public void AnUnknownNameIsLeftAsItWasWritten()
    {
        // Blanking it would say the parameter has no value, which a reader cannot tell from a tool
        // that failed to find one. The literal at least says which parameter is meant.
        Assert.Equal("J=%J", IconText.Resolve("J=%J", "inertia1", null, Values()));
        Assert.Equal("%class", IconText.Resolve("%class", "inertia1"));
    }

    [Fact]
    public void AQualifiedNameIsShownByItsLastSegment()
    {
        // MSL's PID example sets controllerType=Modelica.Blocks.Types.SimpleController.PI and the
        // block is labelled PI, not with forty characters of package path across the diagram.
        Assert.Equal(
            "PI",
            IconText.Resolve("%controllerType", "PI", null,
                Values(("controllerType", "Modelica.Blocks.Types.SimpleController.PI"))));
    }

    [Theory]
    [InlineData("{driveAngle}")]     // an array
    [InlineData("1e4")]              // a literal
    [InlineData("2*n + 1")]          // an expression
    [InlineData("1.5")]              // ...and a decimal, which is dotted and is not a name
    public void AnythingThatIsNotAQualifiedNameIsShownAsWritten(string value)
        => Assert.Equal(value, IconText.Resolve("%v", "c", null, Values(("v", value))));

    [Fact]
    public void ADoubledPerCentIsOne()
        => Assert.Equal("50% of %name", IconText.Resolve("50%% of %%name", "x"));

    [Fact]
    public void ALonePerCentIsLeftAlone()
        => Assert.Equal("100% ", IconText.Resolve("100% ", "x"));

    [Fact]
    public void TextWithNoSubstitutionsComesBackUnchanged()
    {
        const string plain = "rad/s";
        Assert.Same(plain, IconText.Resolve(plain, "speedSensor"));
    }

    [Fact]
    public void NullsAreRefusedRatherThanTreatedAsEmpty()
        => Assert.Throws<ArgumentNullException>(() => IconText.Resolve(null!, "x"));
}
