using System.Collections.Concurrent;
using MLQT.Services;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// The thread-safety promise in the remarks on <c>ILibraryDataService</c>: once the libraries are
/// loaded, and nothing loads, reloads or edits, lookups, parsing and icon rendering may all run at
/// once on the same classes, with no lock of the host's own.
/// </summary>
/// <remarks>
/// A host that could not rely on this put every use of the service behind one lock, so selecting a
/// class waited behind the class tree's icon rendering. The tree renders icons outside the
/// service's own lock on purpose, which only helps if hosts can do the same.
/// </remarks>
public class LibraryDataServiceConcurrencyTests
{
    private const int Children = 40;

    private static string Library()
    {
        var models = string.Join("\n", Enumerable.Range(0, Children).Select(i => $"""
              model M{i} "inherits its icon"
                extends Base;
                Real x{i};
              equation
                x{i} = {i};
              end M{i};
            """));

        return $$$"""
            package Lib "a package of classes that inherit an icon"
              model Base "draws something"
                annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
              end Base;
            {{{models}}}
            end Lib;
            """.Replace("\r\n", "\n");
    }

    private static async Task<(LibraryDataService Service, ModelNode Root)> LoadAsync()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("Lib.mo", Library());
        var root = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "Lib");
        return (service, root);
    }

    [Fact]
    public async Task LookupsParsingAndIconRendering_RunAtOnce_GiveWhatOneThreadGives()
    {
        // What one thread sees, on a service of its own.
        var (reference, referenceRoot) = await LoadAsync();
        var expected = (await reference.GetChildModelsAsync(referenceRoot))
            .ToDictionary(m => m.Id, m => (m.IconSvg, m.Definition.ModelicaCode));
        Assert.Equal(Children + 1, expected.Count);
        Assert.All(expected.Values.Where(e => e.ModelicaCode.Contains("extends Base")),
            e => Assert.Contains("<rect", e.IconSvg));

        var (service, root) = await LoadAsync();
        var ids = expected.Keys.ToArray();
        var failures = new ConcurrentQueue<string>();

        // Tree expansions render icons while other threads look the same classes up, read their
        // source and borrow their trees. No lock is taken here, as the promise says none is needed.
        var expansions = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            foreach (var child in await service.GetChildModelsAsync(root))
            {
                if (child.IconSvg != expected[child.Id].IconSvg)
                    failures.Enqueue($"{child.Id}: icon differs from the one-thread render");
            }
        }));

        var readers = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var round = 0; round < 20; round++)
            {
                foreach (var id in ids)
                {
                    var node = service.GetModelById(id);
                    if (node is null)
                    {
                        failures.Enqueue($"{id}: GetModelById returned null");
                        continue;
                    }

                    if (node.Definition.ModelicaCode != expected[id].ModelicaCode)
                        failures.Enqueue($"{id}: source differs");

                    if (!node.Definition.Borrow(tree => tree.ChildCount > 0, ifUnparseable: false))
                        failures.Enqueue($"{id}: a borrower got no tree");

                    if ((t + round) % 4 == 0 && node.Definition.EnsureParsed() is null)
                        failures.Enqueue($"{id}: EnsureParsed returned null");

                    _ = service.ModelHasChildren(id);
                }

                if (!service.ModelHasChildren("Lib"))
                    failures.Enqueue("Lib: ModelHasChildren said no");
            }
        }));

        await Task.WhenAll(expansions.Concat(readers));

        Assert.Empty(failures);

        // Every icon a reader now sees as rendered is the one-thread answer.
        foreach (var id in ids)
        {
            var definition = service.GetModelById(id)!.Definition;
            Assert.True(definition.IconRendered, $"{id} was shown but not marked rendered");
            Assert.Equal(expected[id].IconSvg, definition.IconSvg);
        }
    }
}
