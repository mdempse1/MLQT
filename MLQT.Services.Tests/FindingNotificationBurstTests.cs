using ModelicaParser.DataTypes;
using MLQT.Services;

namespace MLQT.Services.Tests;

/// <summary>
/// B190 — a style check announces its findings a class at a time, and the UI cannot afford to
/// listen to every one.
///
/// <para><b>What the user saw.</b> Not a freeze, in the end: during style checking the window
/// stopped following the mouse and jumped to where it had been dropped. On Windows that is the
/// signature of a message pump that is not running, and the desktop host runs its pump on the same
/// thread Blazor renders on.</para>
///
/// <para><b>What was on that thread.</b> Measured over the Modelica Standard Library on five rules:
/// 5,250 findings arriving in <b>1,397 batches</b>, median size two. Every batch raised
/// <c>OnLogMessagesChanged</c>, and every raise cost a copy of the whole findings list, a sort of
/// it, a scan for misspellings and a full re-render of the page.</para>
///
/// <para>So a burst coalesces. These pin the two halves of that: far fewer announcements than
/// batches, and — the half that matters more — <b>an announcement after the last one</b>. A throttle
/// that swallows the final update trades a stutter for a table that is quietly wrong.</para>
/// </summary>
public class FindingNotificationBurstTests
{
    private static LogMessage Finding(int i) =>
        new($"Lib.Class{i:D4}", "Style warning", 1, $"finding {i}");

    /// <summary>The window the service coalesces over, plus room for the trailing run to fire.</summary>
    private static readonly TimeSpan AfterTheBurst = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public async Task ABurstOfBatchesIsNotABurstOfNotifications()
    {
        var service = new CodeReviewService();
        var notifications = 0;
        service.OnLogMessagesChanged += () => Interlocked.Increment(ref notifications);

        // The shape a real check delivers: one small batch per class with findings.
        const int Batches = 1397;
        for (var i = 0; i < Batches; i++)
            service.AddLogMessages([Finding(i * 2), Finding(i * 2 + 1)]);

        await Task.Delay(AfterTheBurst);

        Assert.Equal(Batches * 2, service.LogMessages.Count);
        Assert.True(Volatile.Read(ref notifications) < 20,
            $"{Batches} batches produced {notifications} notifications. Each one costs the UI thread "
            + "a copy of the findings list, a sort, a scan and a re-render, and the desktop host's "
            + "window message pump shares that thread (B190).");
    }

    [Fact]
    public async Task TheLastChangeIsAlwaysAnnounced()
    {
        // The half that matters more. A throttle that drops the trailing edge leaves the table
        // showing everything except the findings that arrived last, which is worse than the stutter
        // it was fixing and far harder to notice.
        var service = new CodeReviewService();
        var seen = 0;
        service.OnLogMessagesChanged += () => Interlocked.Exchange(ref seen, service.LogMessages.Count);

        for (var i = 0; i < 200; i++)
            service.AddLogMessages([Finding(i)]);

        await Task.Delay(AfterTheBurst);

        Assert.Equal(200, Volatile.Read(ref seen));
    }

    [Fact]
    public async Task TheFirstChangeIsAnnouncedAtOnce()
    {
        // The common case is one edit the user is waiting to see — resolving a finding, adding a
        // word to the dictionary. Making them wait a quarter of a second for it would be a second
        // defect introduced by the fix for the first.
        var service = new CodeReviewService();
        var notified = new TaskCompletionSource();
        service.OnLogMessagesChanged += () => notified.TrySetResult();

        service.AddLogMessage(Finding(1));

        Assert.True(await Task.WhenAny(notified.Task, Task.Delay(100)) == notified.Task,
            "the first change should be announced immediately, not after the coalescing window");
    }

    [Fact]
    public async Task ClearingIsAnnouncedImmediatelyEvenInsideABurst()
    {
        // Clearing is something the user did and is watching for, so it does not wait its turn
        // behind a burst of findings.
        var service = new CodeReviewService();
        service.AddLogMessages([Finding(1), Finding(2)]);   // takes the leading edge

        var cleared = new TaskCompletionSource();
        service.OnLogMessagesChanged += () =>
        {
            if (service.LogMessages.Count == 0)
                cleared.TrySetResult();
        };

        service.AddLogMessages([Finding(3)]);   // inside the window — would be coalesced
        service.ClearLogMessages();

        Assert.True(await Task.WhenAny(cleared.Task, Task.Delay(100)) == cleared.Task,
            "clearing should be announced at once");
    }
}
