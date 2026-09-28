using MLQT.Services.Interfaces;
using Xunit;

namespace MLQT.TestSupport;

/// <summary>
/// The behaviour every <see cref="ISettingsService"/> has to have, real or test double.
/// </summary>
/// <remarks>
/// <para><b>Why a contract rather than tests per implementation (B205).</b> A double is only useful
/// while it behaves like the thing it stands in for, and nothing was checking that. One of the three
/// in this repository stored objects by reference where the real service round-trips through JSON,
/// so a test could mutate "stored" settings without saving them and see the change come back — while
/// the app would have lost it. That is not a difference anyone spots by reading two files; it is a
/// difference you find when a user reports something the suite said was fine.</para>
///
/// <para>Each suite derives a class from this and supplies its implementation, so the same
/// assertions run against the real <c>JsonSettingsService</c>, the MCP server's
/// <c>HeadlessSettingsService</c>, and the in-memory double.</para>
///
/// <para>These are deliberately about <b>storage semantics</b> and nothing else — no file layout, no
/// paths, no defaults beyond the one the caller passes. Anything narrower than that belongs in the
/// suite that owns the implementation.</para>
/// </remarks>
public abstract class SettingsServiceContract
{
    /// <summary>A fresh, empty store. Called once per test.</summary>
    protected abstract ISettingsService CreateStore();

    /// <summary>A type with enough shape to notice a round-trip that did not happen.</summary>
    public sealed class Settings
    {
        public string Name { get; set; } = "";
        public List<string> Items { get; set; } = [];
    }

    [Fact]
    public async Task AValueThatWasNeverSetComesBackAsTheDefault()
    {
        var store = CreateStore();

        var value = await store.GetAsync("absent", "fallback");

        Assert.Equal("fallback", value);
    }

    [Fact]
    public async Task AValueSurvivesBeingStoredAndReadBack()
    {
        var store = CreateStore();
        await store.SetAsync("k", new Settings { Name = "one", Items = ["a", "b"] });

        var read = await store.GetAsync("k", new Settings());

        Assert.Equal("one", read.Name);
        Assert.Equal(["a", "b"], read.Items);
    }

    [Fact]
    public async Task ChangingWhatWasStoredAfterwardsDoesNotChangeTheStore()
    {
        // The defect this contract exists for. A store that keeps the caller's object sees every
        // later edit to it, so a test can "save" by mutating and the app cannot.
        var store = CreateStore();
        var settings = new Settings { Name = "one", Items = ["a"] };
        await store.SetAsync("k", settings);

        settings.Name = "changed after saving";
        settings.Items.Add("b");

        var read = await store.GetAsync("k", new Settings());

        Assert.Equal("one", read.Name);
        Assert.Equal(["a"], read.Items);
    }

    [Fact]
    public async Task ChangingWhatWasReadDoesNotChangeTheStore()
    {
        // The other direction, and the one that hid a real defect:
        // RepositoryService.LoadRepositorySettingsAsync adds a "Default" project to the object it
        // just read and never saves it. Under an aliasing store that persists; in the app it does
        // not.
        var store = CreateStore();
        await store.SetAsync("k", new Settings { Name = "one", Items = ["a"] });

        var first = await store.GetAsync("k", new Settings());
        first.Name = "changed after reading";
        first.Items.Add("b");

        var second = await store.GetAsync("k", new Settings());

        Assert.Equal("one", second.Name);
        Assert.Equal(["a"], second.Items);
    }

    [Fact]
    public async Task TwoReadsGiveTwoObjects()
    {
        // Follows from the above, and is worth its own assertion because it is the cheapest way to
        // see that a store is aliasing.
        var store = CreateStore();
        await store.SetAsync("k", new Settings { Name = "one" });

        var first = await store.GetAsync("k", new Settings());
        var second = await store.GetAsync("k", new Settings());

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task StoringAgainReplacesWhatWasThere()
    {
        var store = CreateStore();
        await store.SetAsync("k", new Settings { Name = "one", Items = ["a", "b"] });

        await store.SetAsync("k", new Settings { Name = "two", Items = ["c"] });

        var read = await store.GetAsync("k", new Settings());
        Assert.Equal("two", read.Name);
        Assert.Equal(["c"], read.Items);
    }

    [Fact]
    public async Task KeysDoNotCollide()
    {
        var store = CreateStore();

        await store.SetAsync("a", "first");
        await store.SetAsync("b", "second");

        Assert.Equal("first", await store.GetAsync("a", ""));
        Assert.Equal("second", await store.GetAsync("b", ""));
    }

    [Fact]
    public async Task RemovingLeavesTheDefault()
    {
        var store = CreateStore();
        await store.SetAsync("k", "stored");

        await store.RemoveAsync("k");

        Assert.Equal("fallback", await store.GetAsync("k", "fallback"));
    }

    [Fact]
    public async Task RemovingSomethingAbsentIsNotAnError()
    {
        var store = CreateStore();

        await store.RemoveAsync("never-set");

        Assert.Equal("fallback", await store.GetAsync("never-set", "fallback"));
    }

    [Fact]
    public async Task ClearingRemovesEverything()
    {
        var store = CreateStore();
        await store.SetAsync("a", "first");
        await store.SetAsync("b", "second");

        await store.ClearAsync();

        Assert.Equal("", await store.GetAsync("a", ""));
        Assert.Equal("", await store.GetAsync("b", ""));
    }

    [Fact]
    public void ABackingStoreIsNamed()
    {
        // The /selftest probe reports it, so an implementation that returned null or empty would
        // make a store that does not persist indistinguishable from one that does.
        Assert.False(string.IsNullOrWhiteSpace(CreateStore().BackingStore));
    }
}
