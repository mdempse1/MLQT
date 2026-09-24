using System.Diagnostics;
using Xunit;

namespace OpenModelicaInterface.Tests;

/// <summary>
/// omc honouring its own time limits (B263), against a real omc.
/// </summary>
/// <remarks>
/// <para><c>OpenModelicaSettings.CommandTimeoutMs</c> and <c>StartupTimeoutMs</c> existed from the
/// start and nothing read either: a command's reply was a bare blocking receive, so a long check hung
/// its caller for as long as omc took, and start-up slept a fixed two seconds whatever the setting
/// said. These start an omc of their own on their own port, so the fixture's session is not the one
/// they close.</para>
///
/// <para><c>loadModel(Modelica)</c> is the long command: loading the whole standard library takes
/// omc seconds, far longer than the limits set here.</para>
/// </remarks>
public class TimeLimitTests
{
    private const string OmcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
    private static CancellationToken Test => TestContext.Current.CancellationToken;

    private static async Task<OpenModelicaInterface> StartedAsync(int port)
    {
        var omc = new OpenModelicaInterface(OmcPath, port) { StartupTimeout = TimeSpan.FromSeconds(30) };
        await omc.StartAsync();
        return omc;
    }

    [Fact]
    public async Task StartingNeedsNoFixedWait()
    {
        // The two-second sleep is gone; start-up takes as long as omc does, and a session that has
        // started answers at once.
        var clock = Stopwatch.StartNew();
        using var omc = await StartedAsync(13131);
        clock.Stop();

        Assert.True(omc.IsConnected);
        Assert.False(string.IsNullOrEmpty(await omc.GetVersionAsync()));
    }

    [Fact]
    public async Task ACommandThatRunsOutOfTime_ClosesTheSession()
    {
        using var omc = await StartedAsync(13132);
        omc.CommandTimeout = TimeSpan.FromMilliseconds(200);

        var clock = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<TimeoutException>(() => omc.SendCommandAsync("loadModel(Modelica)"));
        clock.Stop();

        // Closed, not merely given up on: the REQ socket cannot send again until it has received, and
        // omc is still working on the command behind it.
        Assert.False(omc.IsConnected);
        Assert.Contains("loadModel", failure.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"the limit was not kept: {clock.Elapsed}");
    }

    [Fact]
    public async Task CancellingACommandInFlight_ClosesTheSession()
    {
        using var omc = await StartedAsync(13133);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Test);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => omc.SendCommandAsync("loadModel(Modelica)", cancel.Token));

        Assert.False(omc.IsConnected);
    }

    [Fact]
    public async Task AStartThatRunsOutOfTime_IsReportedAndLeavesNothingRunning()
    {
        using var omc = new OpenModelicaInterface(OmcPath, 13134) { StartupTimeout = TimeSpan.FromMilliseconds(1) };

        await Assert.ThrowsAsync<TimeoutException>(omc.StartAsync);

        Assert.False(omc.IsConnected);
    }

    [Fact]
    public async Task AfterATimeout_TheFactoryStartsAFreshSession()
    {
        var factory = new OpenModelicaInterfaceFactory();
        factory.UpdateSettings(new OpenModelicaSettings(OmcPath)
        {
            PortNumber = 13135,
            CommandTimeoutMs = 200,
            StartupTimeoutMs = 30_000,
        });
        try
        {
            var first = (OpenModelicaInterface)await factory.GetOrCreateAsync();
            await Assert.ThrowsAsync<TimeoutException>(() => first.SendCommandAsync("loadModel(Modelica)"));

            var second = (OpenModelicaInterface)await factory.GetOrCreateAsync();

            Assert.NotSame(first, second);
            Assert.True(second.IsConnected);
            Assert.False(string.IsNullOrEmpty(await second.GetVersionAsync()));
        }
        finally
        {
            factory.Dispose();
        }
    }

    [Fact]
    public async Task TheFactoryAppliesBothLimitsFromTheSettings()
    {
        var factory = new OpenModelicaInterfaceFactory();
        factory.UpdateSettings(new OpenModelicaSettings(OmcPath)
        {
            PortNumber = 13136,
            CommandTimeoutMs = 45_000,
            StartupTimeoutMs = 20_000,
        });
        try
        {
            var omc = (OpenModelicaInterface)await factory.GetOrCreateAsync();

            Assert.Equal(TimeSpan.FromSeconds(45), omc.CommandTimeout);
            Assert.Equal(TimeSpan.FromSeconds(20), omc.StartupTimeout);
        }
        finally
        {
            factory.Dispose();
        }
    }
}
