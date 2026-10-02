using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The two facts the unit-consistency design (<c>Design/unit-consistency.md</c>) stands on, asserted over
/// real libraries: <b>every component type resolves</b>, and <b>an instance's package redeclaration
/// changes no unit</b> - so the check can resolve against the constraining type. Both were measured on
/// 2026-10-02 over MSL 4.1.0 (29,463 component types, 316 redeclarations) and Buildings 13 (70,230 and
/// 5,920), with no exception, by a probe that did not live in the repository. This is that probe, kept,
/// so a change to the resolvers that breaks either fact is caught rather than built on.
///
/// <para><b>Opt-in</b>, as <c>MLQT_FIDELITY_CORPUS</c> is: libraries that size are not in the repository.
/// Point <c>MLQT_RESOLUTION_CORPUS</c> at them, separated by <c>;</c>. They are loaded into <b>one
/// graph</b>, because a library's types resolve only with its dependencies beside it - so name each
/// library's own directory (or a single-file library's <c>.mo</c>), not a repository root holding test
/// libraries written against another version, whose names would rightly not resolve. With the variable
/// unset both tests return at once, so an ordinary run says nothing about either fact.</para>
/// </summary>
public class ResolutionCorpusTests
{
    private const string Variable = "MLQT_RESOLUTION_CORPUS";
    private readonly ITestOutputHelper _output;

    public ResolutionCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryComponentType_Resolves()
    {
        if (Corpus() is not { } graph)
            return;

        var ancestors = new TypeResolver.AncestorCache();
        var interfaces = new ClassElementResolver.InterfaceCache();
        var types = 0;
        var unresolved = new List<string>();

        foreach (var node in Checked(graph))
            foreach (var m in ClassElementResolver.Collect(graph, node, includeProtected: true, includeInherited: true, interfaces))
            {
                if (m.Element.Kind != ClassElementKind.Component
                    || string.IsNullOrWhiteSpace(m.Element.Type)
                    || TypeResolver.IsPredefined(m.Element.Type.TrimStart('.')))
                    continue;

                types++;
                if (TypeResolver.ResolveWithInheritance(graph, m.OwnerId, m.Element.Type, m.OwnerImports, ancestors) is null)
                    unresolved.Add($"{node.Id}: {m.Element.Name} : {m.Element.Type} (declared in {m.OwnerId})");
            }

        _output.WriteLine($"{types} component types, {unresolved.Count} unresolved");
        Assert.True(types > 0, $"{Variable} gave no component types to resolve");
        Assert.True(unresolved.Count == 0,
            $"{unresolved.Count} of {types} component types did not resolve:{Environment.NewLine}"
            + string.Join(Environment.NewLine, unresolved.Take(20)));
    }

    [Fact]
    public void APackageRedeclaration_ChangesNoUnit()
    {
        if (Corpus() is not { } graph)
            return;

        var ancestors = new TypeResolver.AncestorCache();
        var interfaces = new ClassElementResolver.InterfaceCache();
        var cache = new Dictionary<string, UnitAttributes>(StringComparer.Ordinal);
        int redeclarations = 0, members = 0;
        var differ = new List<string>();

        foreach (var node in Checked(graph))
            foreach (var instance in ClassElementResolver.Collect(graph, node, includeProtected: true, includeInherited: false, interfaces))
            {
                if (instance.Element.Kind is not (ClassElementKind.Component or ClassElementKind.Extends)
                    || instance.Element.Redeclarations is not { } redeclared)
                    continue;

                foreach (var (path, redeclaration) in redeclared)
                {
                    // A class replaced directly on this instance: `Pipe p(redeclare package Medium = W)`.
                    if (redeclaration.ClassType is null || path.Contains('.'))
                        continue;

                    var target = TypeResolver.ResolveWithInheritance(graph, instance.OwnerId, instance.Element.Type, instance.OwnerImports, ancestors);
                    if (target is null)
                        continue;   // a component type: the other test's business
                    var replacement = TypeResolver.ResolveWithInheritance(graph, instance.OwnerId, redeclaration.Type, instance.OwnerImports, ancestors);
                    if (replacement is null)
                    {
                        // Not a component type, so nothing else would say so - and skipping it would
                        // leave a redeclaration this test cannot judge reading as one that agreed.
                        differ.Add($"{node.Id}: {instance.Element.Name}({path} = {redeclaration.Type}): the replacement does not resolve");
                        continue;
                    }
                    redeclarations++;

                    // Each member the instantiated class types through the replaced name - `Medium.T` -
                    // asked of the constraining class, as resolution does, and of the replacement.
                    var prefix = path + ".";
                    foreach (var m in ClassElementResolver.Collect(graph, target, includeProtected: true, includeInherited: true, interfaces))
                    {
                        if (m.Element.Kind != ClassElementKind.Component
                            || m.Element.Type is not { } type
                            || !type.StartsWith(prefix, StringComparison.Ordinal))
                            continue;

                        members++;
                        var constraining = UnitResolver.ResolveAttributes(graph, m.OwnerId, type, m.OwnerImports, cache, ancestors);
                        // `Medium.T` is a member of the replacement - its own or one it inherits - and
                        // nothing outside it, so it is asked by full name. A bare `T` looked up from
                        // inside the replacement would go on to its enclosing packages when the member
                        // is missing, and could agree with the constraining type by finding another `T`.
                        var redeclaredUnit = UnitResolver.ResolveAttributes(
                            graph, replacement.Id, "." + replacement.Id + type[path.Length..], imports: null, cache, ancestors);
                        if (constraining.Unit != redeclaredUnit.Unit || constraining.IsRealDerived != redeclaredUnit.IsRealDerived)
                            differ.Add($"{node.Id}: {instance.Element.Name}({path} = {replacement.Id}): {m.Element.Name} : {type} "
                                       + $"is '{constraining.Unit}' constrained and '{redeclaredUnit.Unit}' redeclared");
                    }
                }
            }

        _output.WriteLine($"{redeclarations} package redeclarations, {members} members, {differ.Count} differing");
        Assert.True(differ.Count == 0,
            $"{differ.Count} problem(s) over {redeclarations} redeclarations and {members} members: a member whose unit "
            + "differs from the constraining type's means the unit check must resolve through redeclarations "
            + $"(Design/unit-consistency.md):{Environment.NewLine}"
            + string.Join(Environment.NewLine, differ.Take(20)));
    }

    // The classes a user's library is made of; a stub stands in for an encrypted one and has no source.
    private static IEnumerable<ModelNode> Checked(DirectedGraph graph)
        => graph.ModelNodes.Where(n => !n.IsExternalStub).OrderBy(n => n.Id, StringComparer.Ordinal);

    private DirectedGraph? Corpus()
    {
        var roots = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(roots))
            return null;

        var files = new List<string>();
        foreach (var entry in roots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(entry))
                files.Add(entry);
            else if (Directory.Exists(entry))
                files.AddRange(Directory.EnumerateFiles(entry, "*.mo", SearchOption.AllDirectories));
            else
                Assert.Fail($"{Variable} names '{entry}', which is neither a file nor a directory");
        }

        Assert.True(files.Count > 0, $"{Variable} matched no .mo files: {roots}");
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFiles(graph, files.ToArray());
        _output.WriteLine($"{files.Count} files, {graph.ModelNodes.Count()} classes");
        return graph;
    }
}
