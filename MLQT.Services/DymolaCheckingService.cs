using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using DymolaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using DymolaInterface;
using MLQT.Services.Helpers;

namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using Dymola. The run, its progress and cancellation are
/// <see cref="ModelCheckingServiceBase{TSession}"/>'s; this is what Dymola does differently.
/// </summary>
public class DymolaCheckingService : ModelCheckingServiceBase<IDymolaInterface>
{
    private readonly IDymolaInterfaceFactory _dymolaFactory;

    public override string ToolName => "Dymola";

    /// <summary>
    /// The one failure that is not the user's model. Singled out by its text so the result can say
    /// "buy a licence" rather than "your model is broken".
    /// </summary>
    private const string DemoLicenceLimit = "Error: the model is too complex for the current license";

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

    public override async Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath)
    {
        var load = await LoadLibraryAsync(filePath, CancellationToken.None);
        return (load.Success, load.ErrorMessage);
    }

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
                    await SafeClearLogAsync();
                    isOpen = await Session.OpenModelAsync(filePath, false, false, cancellationToken: token);
                    if (!isOpen && Interrupted(filePath) is { } interruptedAgain)
                        return interruptedAgain;

                    if (!isOpen && await IsGoneAsync())
                        return LibraryLoad.Gone(
                            $"Dymola stopped answering while opening {Path.GetFileName(filePath)} - its window " +
                            "was closed, or its process has exited. The next check starts a new session.");

                    if (!isOpen)
                    {
                        return LibraryLoad.Failed(LibraryLoad.WouldNotOpen(ToolName, await SafeLastErrorAsync()));
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
            Error("DymolaCheckingService", "Error connecting to Dymola", ex);
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
    /// Empties Dymola's log so the next read belongs to the command that follows it. Never throws:
    /// failing to clear is not a reason to fail the check.
    /// </summary>
    private async Task SafeClearLogAsync()
    {
        try
        {
            if (Session is not null)
                await Session.ClearLogAsync();
        }
        catch (Exception ex)
        {
            Debug("DymolaCheckingService", $"Could not clear Dymola's log: {ex.Message}");
        }
    }

    /// <summary>
    /// Dymola's log for the last command, or null when it cannot be read. Never throws: this is
    /// asked on the success path too, and losing a clean result because the log could not be
    /// fetched would be a worse answer than a result with no log on it.
    /// </summary>
    private async Task<string?> SafeLastErrorAsync()
    {
        try
        {
            var log = Session is null ? null : await Session.GetLastErrorAsync();
            return string.IsNullOrWhiteSpace(log) ? null : log;
        }
        catch (Exception ex)
        {
            Debug("DymolaCheckingService", $"Could not read Dymola's log: {ex.Message}");
            return null;
        }
    }

    public override async Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Session ??= await _dymolaFactory.GetOrCreateAsync(cancellationToken);

            // The library's own package.mo, not the class's file. Dymola would find the enclosing
            // package itself from the class's file, but OpenModelica will not — and the two tools
            // answering "which file do I open?" differently is how one of them came to be broken
            // for a release while the other worked (B170).
            var rootFile = LibraryRootFile.For(graph, modelNode);
            if (rootFile != null)
            {
                var load = await LoadLibraryAsync(rootFile, cancellationToken);

                // A load the caller stopped is not a library that failed to load.
                cancellationToken.ThrowIfCancellationRequested();
                if (!load.Success)
                    return load.FailureFor(modelNode.Id, ToolName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", $"Error preparing to check model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        // Null is a check the caller cancelled: the token reaches the one in flight, as it does for
        // a run (B397). Thrown rather than returned, because a result saying "Cancelled" was a
        // failed check to anything that read it - IsModelFailure is true of it.
        return await CheckSingleModelAsync(modelNode, cancellationToken)
               ?? throw new OperationCanceledException(cancellationToken);
    }

    /// <remarks>
    /// <para><b>It was not always the only place (B229).</b> The two paths each had their own copy
    /// of this, and the copy the package path used had neither the clear-the-log-first step nor the
    /// read-the-log-on-success step. So the warnings B170 exists to surface were shown when the
    /// user checked one class and lost when they checked the package containing it, and the error
    /// reported against a class could be the one the class before it produced. Neither is visible
    /// from either path on its own, which is why the tests for this are a contract run against both
    /// tools rather than a test per service.</para>
    /// </remarks>
    private protected override async Task<ModelCheckResult?> CheckSingleModelAsync(ModelNode modelNode,
        CancellationToken token = default)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelNode.Id
        };

        try
        {
            // Cleared first so that what comes back afterwards belongs to *this* check. Dymola's log
            // accumulates, so without this a model that checked cleanly could be shown the error
            // from something checked before it — a wrong answer, and a worse one than no log at all.
            await SafeClearLogAsync();

            var checkResult = await Session!.CheckModelAsync(modelNode.Id, false, false, cancellationToken: token);

            // False is the model's verdict only if Dymola gave it. A check that ran out of time or was
            // cancelled answered nothing - and asking for the log then would wait out the limit again,
            // against a Dymola still busy with the check (B263).
            if (!checkResult)
            {
                switch (Session.LastOutcome)
                {
                    case CommandOutcome.Cancelled:
                        return null;
                    case CommandOutcome.TimedOut:
                        return ToolTimeLimit.CheckTimedOut(ToolName, modelNode.Id, _commandTimeout, StillBusy);

                    // Not an answer either, and possibly no Dymola at all: a closed window gives Failed,
                    // because nothing marks a session offline partway through. Asked rather than
                    // assumed, since Failed also covers Dymola reporting an error about this one command.
                    case CommandOutcome.Offline or CommandOutcome.Failed when await IsGoneAsync():
                        return UnavailableTool.WentAway(ToolName, modelNode.Id);
                }
            }

            if (checkResult)
            {
                result.Success = true;

                // `checkModel` returning true means it checked, not that it had nothing to say: a
                // model that is fine and one that is fine apart from six warnings both return true,
                // and getLastError() is where the difference is. Asked on success as well so the
                // result dialog can show what Dymola actually reported (B170).
                result.Log = await SafeLastErrorAsync();
            }
            else
            {
                var error = await Session.GetLastErrorAsync();
                result.Success = false;
                result.Log = error;
                result.Summary = error.Contains(DemoLicenceLimit)
                    ? "Model too complex for demo license"
                    : $"{ToolName} Check Failed";
                result.ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            Error("DymolaCheckingService", $"Error checking model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        return result;
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
            Debug("DymolaCheckingService", $"Could not ask whether Dymola is still there: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The result for a check that threw: Dymola's own account of what went wrong where it can
    /// still be asked for one, and the exception's where it cannot.
    /// </summary>
    private async Task<ModelCheckResult> FailedResultAsync(string modelId, Exception failure)
    {
        var result = new ModelCheckResult
        {
            ModelId = modelId,
            Success = false,
            Summary = $"{ToolName} Check Failed"
        };

        try
        {
            result.ErrorMessage = Session != null
                ? await Session.GetLastErrorAsync()
                : failure.Message;
        }
        catch (Exception innerEx)
        {
            Warn("DymolaCheckingService", $"Failed to get Dymola error message: {innerEx.Message}");
            result.ErrorMessage = failure.Message;
        }

        return result;
    }
}
