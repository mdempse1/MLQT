using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace MLQT.TestSupport;

/// <summary>
/// Holds a compiled JSON contract (a <c>JsonSerializerContext</c>) to the one reflection produces, for
/// a type and every type it reaches.
/// </summary>
/// <remarks>
/// The contracts exist so that an obfuscated build, whose member names are no longer the ones in the
/// source, still reads and writes the files and results every other build does (B502). Two things
/// can go wrong when one is introduced or a type changes under it, and this catches both: a type
/// reached from the root that the context does not cover (which fails at run time, and only on the
/// path that reaches it), and a name or an order the generator settles differently from reflection
/// (which changes a committed file's bytes for no reason anyone asked for).
/// </remarks>
internal static class JsonContractAssert
{
    /// <summary>
    /// Asserts that <paramref name="compiled"/> covers <paramref name="root"/> and everything reachable
    /// from it, with the same property names in the same order as reflection, under the settings of
    /// <paramref name="like"/> (its naming policy, say); its own resolver is ignored.
    /// </summary>
    public static void MatchesReflection(Type root, IJsonTypeInfoResolver compiled, JsonSerializerOptions? like = null)
    {
        var reflected = With(like, new DefaultJsonTypeInfoResolver());
        var generated = With(like, compiled);
        var seen = new HashSet<Type>();
        var objects = 0;

        Walk(root);
        Assert.True(objects > 0, $"{root} reaches no object type; the walk checked nothing");

        void Walk(Type type)
        {
            if (!seen.Add(type))
                return;

            JsonTypeInfo mine;
            try
            {
                mine = generated.GetTypeInfo(type);
            }
            catch (NotSupportedException)
            {
                Assert.Fail($"{type} is reached from {root} and is not in the compiled contracts");
                return;
            }

            var theirs = reflected.GetTypeInfo(type);
            Assert.Equal(theirs.Kind, mine.Kind);

            switch (mine.Kind)
            {
                case JsonTypeInfoKind.Object:
                    objects++;
                    Assert.Equal(
                        theirs.Properties.Where(Written).Select(p => $"{p.Name}: {p.PropertyType}"),
                        mine.Properties.Where(Written).Select(p => $"{p.Name}: {p.PropertyType}"));
                    foreach (var property in mine.Properties.Where(Written))
                        Walk(property.PropertyType);
                    break;
                case JsonTypeInfoKind.Enumerable:
                    Walk(mine.ElementType!);
                    break;
                case JsonTypeInfoKind.Dictionary:
                    Walk(mine.KeyType!);
                    Walk(mine.ElementType!);
                    break;
            }
        }
    }

    // A property marked [JsonIgnore] for good is in both lists but never read or written, and the
    // generator types it as object since it builds nothing for it - no getter, no setter, no
    // attributes; reflection keeps the attribute. Either way it is not part of the contract.
    private static bool Written(JsonPropertyInfo property) =>
        (property.Get is not null || property.Set is not null) &&
        property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
            .OfType<JsonIgnoreAttribute>()
            .Any(a => a.Condition == JsonIgnoreCondition.Always) != true;

    private static JsonSerializerOptions With(JsonSerializerOptions? like, IJsonTypeInfoResolver resolver)
    {
        var options = like is null ? new JsonSerializerOptions() : new JsonSerializerOptions(like);
        options.TypeInfoResolver = resolver;
        options.MakeReadOnly();
        return options;
    }
}
