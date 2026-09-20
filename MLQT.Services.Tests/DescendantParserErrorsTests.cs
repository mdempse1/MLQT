using MLQT.Services;

namespace MLQT.Services.Tests;

/// <summary>
/// B258 — which packages contain a class that would not parse, worked out once for the project.
///
/// <para><b>Why it moved here.</b> Every library browser computed it, and each walked every model in
/// the project to do so — 69,141 of them, on the dispatcher, once per repository per tree refresh.
/// The answer never depended on which repository was asking. Measured at <b>872ms</b> for one of
/// those walks while a load still held the service's lock.</para>
/// </summary>
public class DescendantParserErrorsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-descendant-errors", Guid.NewGuid().ToString("N"));

    public DescendantParserErrorsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<LibraryDataService> WithABrokenClassAsync()
    {
        var service = new LibraryDataService();
        var file = Path.Combine(_dir, "Lib.mo");

        // Deep enough that the ancestors are worth reporting: the warning has to be visible from the
        // root without the user expanding anything.
        await File.WriteAllTextAsync(file, """
            package Lib "a library"
              package Inner "a package"
                model Broken "will not parse"
                  Real x = ;
                end Broken;
              end Inner;
            end Lib;
            """.Replace("\r\n", "\n"));

        await service.AddLibraryFromFileAsync(file, await File.ReadAllTextAsync(file));
        return service;
    }

    [Fact]
    public async Task EveryPackageAboveABrokenClassIsNamed()
    {
        var service = await WithABrokenClassAsync();

        var packages = service.ModelsWithDescendantParserErrors();

        Assert.Contains("Lib", packages);
        Assert.Contains("Lib.Inner", packages);

        // The broken class itself is not a *descendant* of itself — the tree marks it directly.
        Assert.DoesNotContain("Lib.Inner.Broken", packages);
    }

    [Fact]
    public async Task AskingTwiceGivesTheSameInstance()
    {
        // The whole point: four repositories asking during one refresh must not mean four walks of
        // every model in the project.
        var service = await WithABrokenClassAsync();

        Assert.Same(service.ModelsWithDescendantParserErrors(), service.ModelsWithDescendantParserErrors());
    }

    [Fact]
    public async Task LoadingAnotherLibraryRebuildsIt()
    {
        // The invalidation, which is the half worth testing. A set built before a library arrived
        // would leave that library's broken classes unmarked, and nothing would ever ask again.
        var service = await WithABrokenClassAsync();
        var before = service.ModelsWithDescendantParserErrors();

        var second = Path.Combine(_dir, "Other.mo");
        await File.WriteAllTextAsync(second, """
            package Other "another"
              model AlsoBroken "will not parse"
                Real y = ;
              end AlsoBroken;
            end Other;
            """.Replace("\r\n", "\n"));
        await service.AddLibraryFromFileAsync(second, await File.ReadAllTextAsync(second));

        var after = service.ModelsWithDescendantParserErrors();

        Assert.NotSame(before, after);
        Assert.Contains("Lib.Inner", after);
        Assert.Contains("Other", after);
    }

    [Fact]
    public async Task ALibraryThatParsesNamesNothing()
    {
        var service = new LibraryDataService();
        var file = Path.Combine(_dir, "Fine.mo");
        await File.WriteAllTextAsync(file, "package Fine \"fine\"\n  model M \"m\"\n  end M;\nend Fine;\n");
        await service.AddLibraryFromFileAsync(file, await File.ReadAllTextAsync(file));

        Assert.Empty(service.ModelsWithDescendantParserErrors());
    }

    [Fact]
    public async Task PreparingTheSameLibraryFromSeveralThreadsNeverShowsAHalfRenderedIcon()
    {
        // Concurrent preparation is possible now that this runs off the dispatcher, and this checks
        // the outcome: every class comes back both marked as rendered and carrying its icon.
        //
        // **It is not a race detector, and was tried as one.** Setting the flag before rendering —
        // which would let a second thread see "already rendered" with no SVG — passes this every
        // time, because the window between the two writes is far too narrow to hit on demand. The
        // ordering in `PrepareModelForDisplay` is reasoned rather than pinned; what is pinned is
        // that eight simultaneous callers all get a usable answer.
        var service = new LibraryDataService();
        var file = Path.Combine(_dir, "Icons.mo");
        await File.WriteAllTextAsync(file, """
            package Icons "with icons"
              model A "a"
                annotation (Icon(graphics={Rectangle(extent={{-10,-10},{10,10}})}));
              end A;
              model B "b"
                annotation (Icon(graphics={Ellipse(extent={{-10,-10},{10,10}})}));
              end B;
            end Icons;
            """.Replace("\r\n", "\n"));
        await service.AddLibraryFromFileAsync(file, await File.ReadAllTextAsync(file));

        // Eight concurrent refreshes, as several open trees would do.
        var root = (await service.GetTopLevelModelsAsync())[0];
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.GetChildModelsAsync(root)));

        foreach (var batch in results)
        {
            Assert.Equal(2, batch.Count);
            foreach (var model in batch)
            {
                Assert.True(model.Definition.IconRendered);
                Assert.False(string.IsNullOrEmpty(model.IconSvg),
                    $"{model.Id} was marked rendered with no icon, which is the race the flag "
                    + "ordering exists to prevent");
            }
        }
    }
}
