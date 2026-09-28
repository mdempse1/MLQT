using System.Reflection;

namespace DymolaInterface.Tests;

/// <summary>
/// Every test class that needs a live Dymola says so, because that is how CI tells them apart.
/// </summary>
/// <remarks>
/// <para>Backlog B399. Most of this suite needs no Dymola at all - wire format against a fake
/// handler, socket stubs, spawn environment, detection - but CI ran none of it, because the suite as
/// a whole drives a live install no runner has. So the tool-free half was under no gate, and new
/// tests for this code went into <c>MLQT.Services.Tests</c> instead (B331, B337).</para>
///
/// <para>CI now runs this suite with <c>--filter "Requires!=Dymola"</c>: the classes needing Dymola
/// carry <c>[Trait("Requires", "Dymola")]</c> and everything else runs. Classified by what a class
/// needs, never by what it is called - B266 is what classifying by name cost the svn tests. The
/// filter string is held to this trait by <c>LiveToolTestFilterTests</c> in MLQT.Shared.Tests.</para>
///
/// <para>This is the half of that which can be checked mechanically: a class that takes the shared
/// Dymola fixture, or joins its collection, needs Dymola. A class that starts one some other way is
/// not visible here and has to carry the trait by hand; CI would say so, by failing.</para>
/// </remarks>
public class ToolTraitTests
{
    private const string CollectionName = "Dymola Collection";

    private static bool UsesTheFixture(Type type) =>
        type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(DymolaFixture)))
        || type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(CollectionAttribute)
            && a.ConstructorArguments.Any(arg => Equals(arg.Value, CollectionName)));

    private static bool RequiresDymola(Type type) =>
        type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(TraitAttribute)
            && a.ConstructorArguments.Count == 2
            && Equals(a.ConstructorArguments[0].Value, "Requires")
            && Equals(a.ConstructorArguments[1].Value, "Dymola"));

    private static List<Type> TestClasses() =>
        [.. typeof(ToolTraitTests).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Any(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any()))];

    [Fact]
    public void EveryClassUsingTheDymolaFixture_IsMarkedAsRequiringDymola()
    {
        var users = TestClasses().Where(UsesTheFixture).ToList();

        // Not allowed to come back empty: a check that stopped seeing the fixture would pass trivially,
        // and the tool-dependent classes would then run in CI and fail there.
        Assert.True(users.Count >= 4, $"found only {users.Count} classes using the Dymola fixture");

        var unmarked = users.Where(t => !RequiresDymola(t)).Select(t => t.Name).ToList();
        Assert.True(unmarked.Count == 0,
            "these use the Dymola fixture and do not carry [Trait(\"Requires\", \"Dymola\")], so CI's "
            + "tool-free run would start them: " + string.Join(", ", unmarked));
    }

    [Fact]
    public void MostOfTheSuiteNeedsNoDymola()
    {
        // The reason the filter exists. If this ever stops being true the suite has nothing for CI to
        // run and the step is ceremony - worth knowing rather than finding out from a trx.
        var classes = TestClasses();
        var toolFree = classes.Count(t => !RequiresDymola(t));

        Assert.True(toolFree > classes.Count / 2, $"only {toolFree} of {classes.Count} classes run without Dymola");
        Assert.False(RequiresDymola(typeof(ToolTraitTests)), "this guard has to run where the others do not");
    }
}
