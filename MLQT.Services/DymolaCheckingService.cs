using DymolaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using DymolaInterface;
using MLQT.Services.Helpers;

namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using Dymola. The run, its progress and cancellation, and
/// the shape of a check are <see cref="ModelCheckingServiceBase{TSession}"/>'s; this is what Dymola
/// does differently.
/// </summary>
/// <remarks>
/// Dymola answers <c>false</c> or <c>null</c> whatever went wrong, so <see cref="IDymolaInterface.LastOutcome"/>
/// is asked why - and it cannot be interrupted, so a command that ran out of time leaves it busy, and
/// its log must not be read then (B263).
/// </remarks>
public class DymolaCheckingService : ModelCheckingServiceBase<IDymolaInterface>
{
    private readonly IDymolaInterfaceFactory _dymolaFactory;

    public override string ToolName => "Dymola";

    /// <summary>
    /// What a timed-out command leaves Dymola doing — the part of the message that differs from omc.
    /// </summary>
    private const string StillBusy =
        "Dymola cannot be interrupted, so it is still working on it and will not answer anything else until it has finished.";

    /// <summary>The time limit the factory is applying, kept here to say it in a message.</summary>
    private TimeSpan _commandTimeout = TimeSpan.FromMinutes(5);

    public DymolaCheckingService(IDymolaInterfaceFactory dymolaFactory)
    {
        _dymolaFactory = dymolaFactory;
    }

    public void UpdateSettings(DymolaSettings settings)
    {
        _commandTimeout = settings.CommandTimeout;
        _dymolaFactory.UpdateSettings(settings);
    }

    private protected override Task<IDymolaInterface> GetSessionAsync(CancellationToken token) =>
        _dymolaFactory.GetOrCreateAsync(token);

    private protected override Task ResetSessionAsync() => _dymolaFactory.ResetAsync();

    private protected override async Task<LibraryLoad> LoadLibraryAsync(string filePath, CancellationToken token)
    {
        try
        {
            Session = await _dymolaFactory.GetOrCreateAsync(token);

            var isOpen = await Session.OpenModelAsync(filePath, false, false, cancellationToken: token);
            if (!isOpen && Interrupted(filePath) is { } interrupted)
                return interrupted;

            if (!isOpen)
            {
                if (File.Exists(filePath))
                {
                    // File exists so maybe Dymola already had a version open. The log is cleared too, so
                    // what Dymola says about the retry is about the retry.
                    await Session.ClearAsync();
                    await ClearLogAsync();
                    isOpen = await Session.OpenModelAsync(filePath, false, false, cancellationToken: token);
                    if (!isOpen && Interrupted(filePath) is { } interruptedAgain)
                        return interruptedAgain;

                    if (!isOpen && await IsGoneAsync())
                        return LibraryLoad.Gone(
                            $"Dymola stopped answering while opening {Path.GetFileName(filePath)} - its window " +
                            "was closed, or its process has exited. The next check starts a new session.");

                    if (!isOpen)
                    {
                        return LibraryLoad.Failed(LibraryLoad.WouldNotOpen(ToolName, await ReadLogOrNullAsync()));
                    }
                }
                else
                {
                    return LibraryLoad.Failed($"File not found: {filePath}");
                }
            }

            return LibraryLoad.Loaded;
        }
        catch (Exception ex)
        {
            Error(LogSource, "Error connecting to Dymola", ex);
            return LibraryLoad.Failed($"Error connecting to Dymola: {ex.Message}");
        }
    }

    /// <summary>
    /// Why an open that did not succeed was not a refusal, or null when it was one: Dymola never
    /// answered, or the user cancelled. Asked before retrying, because a retry after a timeout waits
    /// out the whole limit again against a Dymola still busy with the first attempt.
    /// </summary>
    private LibraryLoad? Interrupted(string filePath) => Session?.LastOutcome switch
    {
        CommandOutcome.Cancelled => LibraryLoad.Failed("Cancelled"),
        CommandOutcome.TimedOut => LibraryLoad.RanOutOfTime(
            ToolTimeLimit.LoadTimedOut(ToolName, filePath, _commandTimeout, StillBusy)),
        _ => null,
    };

    /// <summary>
    /// Empties Dymola's log, which otherwise accumulates, so the next read belongs to the command
    /// that follows it. Never throws: failing to clear is not a reason to fail the check.
    /// </summary>
    private protected override async Task ClearLogAsync()
    {
        try
        {
            if (Session is not null)
                await Session.ClearLogAsync();
        }
        catch (Exception ex)
        {
            Debug(LogSource, $"Could not clear Dymola's log: {ex.Message}");
        }
    }

    private protected override async Task<CheckAnswer> IssueCheckAsync(IDymolaInterface session, string modelId,
        CancellationToken token)
    {
        if (await session.CheckModelAsync(modelId, false, false, cancellationToken: token))
            return CheckAnswer.Checked(true);

        // False is the model's verdict only if Dymola gave it. A check that ran out of time or was
        // cancelled answered nothing - and asking for the log then would wait out the limit again,
        // against a Dymola still busy with the check (B263).
        switch (session.LastOutcome)
        {
            case CommandOutcome.Cancelled:
                return CheckAnswer.WasCancelled;
            case CommandOutcome.TimedOut:
                return CheckAnswer.NoVerdict(
                    ToolTimeLimit.CheckTimedOut(ToolName, modelId, _commandTimeout, StillBusy));

            // Not an answer either, and possibly no Dymola at all: a closed window gives Failed,
            // because nothing marks a session offline partway through. Asked rather than
            // assumed, since Failed also covers Dymola reporting an error about this one command.
            case CommandOutcome.Offline or CommandOutcome.Failed when await IsGoneAsync():
                return CheckAnswer.NoVerdict(UnavailableTool.WentAway(ToolName, modelId));
        }

        return CheckAnswer.Checked(false);
    }

    /// <summary>Dymola's <c>getLastError()</c>: what it said about the last command.</summary>
    private protected override Task<string> ReadLogAsync(IDymolaInterface session) => session.GetLastErrorAsync();

    private protected override async Task<string?> ReadLogOrNullAsync()
    {
        try
        {
            var log = Session is null ? null : await Session.GetLastErrorAsync();
            return string.IsNullOrWhiteSpace(log) ? null : log;
        }
        catch (Exception ex)
        {
            Debug(LogSource, $"Could not read Dymola's log: {ex.Message}");
            return null;
        }
    }

    /// <summary>Whether the session has gone - not busy, not starting: nothing there. Never throws.</summary>
    private async Task<bool> IsGoneAsync()
    {
        try
        {
            return Session is not null && await Session.GetSessionStateAsync() == DymolaSessionState.Gone;
        }
        catch (Exception ex)
        {
            Debug(LogSource, $"Could not ask whether Dymola is still there: {ex.Message}");
            return false;
        }
    }
}
