using MLQT.Services.Interfaces;
using MLQT.TestHost.Services;
using MLQT.TestSupport;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// The test host's gated settings double behaves as every settings service must (B365).
/// </summary>
/// <remarks>
/// Every journey runs on it, so it is held to <see cref="SettingsServiceContract"/> like the double it
/// wraps - <c>SettingsDoublePolicyTests</c> refuses an implementation no contract subclass constructs.
/// </remarks>
public sealed class GatedSettingsServiceContractTests : SettingsServiceContract
{
    protected override ISettingsService CreateStore() => new GatedSettingsService(new InMemorySettingsService());
}

/// <summary>
/// The gate itself: it holds the one write it was asked to hold, and nothing else.
/// </summary>
public class GatedSettingsServiceGateTests
{
    [Fact]
    public async Task AHeldWrite_WaitsForTheRelease_AndOtherWritesDoNot()
    {
        var store = new GatedSettingsService(new InMemorySettingsService());
        using var gate = store.HoldWrite((key, value) => key == "held");

        // Another key goes straight through while the gate is armed.
        await store.SetAsync("other", 1);
        Assert.Equal(1, await store.GetAsync("other", 0));
        Assert.False(gate.Arrived.IsCompleted);

        var held = store.SetAsync("held", 2);
        await gate.WaitForArrivalAsync(TimeSpan.FromSeconds(10));
        Assert.False(held.IsCompleted);
        Assert.Equal(0, await store.GetAsync("held", 0));

        gate.Release();
        await held;
        Assert.Equal(2, await store.GetAsync("held", 0));

        // One write, once: the next goes straight through.
        await store.SetAsync("held", 3);
        Assert.Equal(3, await store.GetAsync("held", 0));
    }
}
