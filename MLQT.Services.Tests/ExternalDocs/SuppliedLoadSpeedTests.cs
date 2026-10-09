using System.Diagnostics;
using MLQT.Services;
using ModelicaGraph;
using Xunit;

namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// How long a real library takes to load as a supplied one: every <c>.mo</c> and <c>package.order</c>
/// of it read into memory first, then timed through <see cref="LibraryDataService.AddLibraryFromSourceAsync"/>
/// alone - which is what a host holding a library in memory pays when an agent session starts.
///
/// <para><b>Opt-in</b>, as the fidelity and resolution corpora are: a library that size is not in the
/// repository. Point <c>MLQT_SUPPLIED_CORPUS</c> at one library's own directory (the one holding its
/// top-level <c>package.mo</c>). With the variable unset the test returns at once. It reports the time
/// rather than asserting one - a stopwatch in a test is a flaky test - and asserts only that every
/// class arrived read-only.</para>
/// </summary>
public class SuppliedLoadSpeedTests(ITestOutputHelper output)
{
    private const string Variable = "MLQT_SUPPLIED_CORPUS";

    private sealed class DirectorySource(string root, IReadOnlyList<SuppliedText> texts) : IReadOnlyClassSource
    {
        public string LibraryName => Path.GetFileName(root);
        public string? LibraryVersion => null;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "Load-speed corpus.";
        public ReadOnlySourceContent Read(CancellationToken cancellationToken) => new() { Texts = texts };
    }

    [Fact]
    public async Task ARealLibrary_LoadsAsASuppliedOne()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } root)
            return;

        var parent = Path.GetDirectoryName(Path.GetFullPath(root))
            ?? throw new InvalidOperationException($"{Variable} names a root directory, not a library: {root}");
        var texts = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(f).Equals("package.order", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.StartsWith('.') || segment == "Resources"))
            .Select(f => new SuppliedText(Path.GetRelativePath(parent, f), File.ReadAllText(f)))
            .ToList();

        var service = new LibraryDataService();
        var clock = Stopwatch.StartNew();
        var library = await service.AddLibraryFromSourceAsync(new DirectorySource(root, texts));
        clock.Stop();

        output.WriteLine(
            $"{library.Name}: {texts.Count} files, {library.ModelIds.Count} classes in {clock.Elapsed.TotalSeconds:F2} s " +
            $"({library.ModelIds.Count / Math.Max(clock.Elapsed.TotalSeconds, 0.001):F0} classes/s)");
        Assert.NotEmpty(library.ModelIds);
        Assert.All(library.ModelIds, id => Assert.True(service.GetModelById(id)?.IsExternalStub == true, id));
    }
}
