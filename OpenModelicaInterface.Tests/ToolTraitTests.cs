using System.Reflection;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// Every test class that needs a live omc says so, because that is how CI tells them apart.
/// </summary>
/// <remarks>
/// <para>Backlog B399. Part of this suite needs no omc at all - the command builder, the factory's
/// state handling, detection - but CI ran none of it, because the suite as a whole drives a live
/// install no runner has.</para>
///
/// <para>CI now runs this suite with <c>--filter "Requires!=OpenModelica"</c>: the classes needing
/// omc carry <c>[Trait("Requires", "OpenModelica")]</c> and everything else runs. Classified by what
/// a class needs, never by what it is called (B266). The filter string is held to this trait by
/// <c>LiveToolTestFilterTests</c> in MLQT.Shared.Tests.</para>
///
/// <para>This is the half of that which can be checked mechanically: a class that takes the shared
/// omc fixture, or joins its collection, needs omc. <c>TimeLimitTests</c> starts an omc of its own
/// and carries the trait by hand, which is what any class doing that has to do.</para>
/// </remarks>
public class ToolTraitTests
{
    private const string CollectionName = "OpenModelica Collection";

    private static bool UsesTheFixture(Type type) =>
        type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(OpenModelicaFixture)))
        || type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(CollectionAttribute)
            && a.ConstructorArguments.Any(arg => Equals(arg.Value, CollectionName)));

    private static bool RequiresOmc(Type type) =>
        type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(TraitAttribute)
            && a.ConstructorArguments.Count == 2
            && Equals(a.ConstructorArguments[0].Value, "Requires")
            && Equals(a.ConstructorArguments[1].Value, "OpenModelica"));

    private static List<Type> TestClasses() =>
        [.. typeof(ToolTraitTests).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Any(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any()))];

    [Fact]
    public void EveryClassUsingTheOmcFixture_IsMarkedAsRequiringOpenModelica()
    {
        var users = TestClasses().Where(UsesTheFixture).ToList();

        // Not allowed to come back empty: a check that stopped seeing the fixture would pass trivially,
        // and the tool-dependent classes would then run in CI and fail there.
        Assert.True(users.Count >= 4, $"found only {users.Count} classes using the omc fixture");

        var unmarked = users.Where(t => !RequiresOmc(t)).Select(t => t.Name).ToList();
        Assert.True(unmarked.Count == 0,
            "these use the omc fixture and do not carry [Trait(\"Requires\", \"OpenModelica\")], so CI's "
            + "tool-free run would start them: " + string.Join(", ", unmarked));
    }

    [Fact]
    public void SomethingInTheSuiteNeedsNoOmc()
    {
        // The reason the filter exists, and this guard is part of it: it has to run where the
        // tool-dependent classes do not.
        Assert.False(RequiresOmc(typeof(ToolTraitTests)), "this guard has to run where the others do not");
        Assert.True(TestClasses().Count(t => !RequiresOmc(t)) >= 3, "nothing in the suite runs without omc");
    }
}
