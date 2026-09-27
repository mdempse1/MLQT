using OpenModelicaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using OpenModelicaInterface;
using MLQT.Services.Helpers;

namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using OpenModelica. The run, its progress and cancellation,
/// and the shape of a check are <see cref="ModelCheckingServiceBase{TSession}"/>'s; this is what omc
/// does differently.
/// </summary>
/// <remarks>
/// omc throws where Dymola answers <c>false</c>: a command that ran out of time or was cancelled
/// closes its session, which is dropped here so the factory replaces it on the next request - and
/// its error string empties itself when it is read.
/// </remarks>
public class OpenModelicaCheckingService : ModelCheckingServiceBase<IOpenModelicaInterface>
{
    private readonly IOpenModelicaInterfaceFactory _omcFactory;

    public override string ToolName => "OpenModelica";

    /// <summary>
    /// What a timed-out command leaves omc doing — the part of the message that differs from Dymola.
    /// </summary>
    private const string SessionClosed =
        "OpenModelica cannot be interrupted, so its session was closed; the next check starts a new one.";

    /// <summary>The time limit the factory is applying, kept here to say it in a message.</summary>
    private TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);

    public OpenModelicaCheckingService(IOpenModelicaInterfaceFactory omcFactory)
    {
        _omcFactory = omcFactory;
    }

    public void UpdateSettings(OpenModelicaSettings settings)
    {
        _commandTimeout = settings.CommandTimeout;
        _omcFactory.UpdateSettings(settings);
    }

    private protected override Task<IOpenModelicaInterface> GetSessionAsync(CancellationToken token) =>
        _omcFactory.GetOrCreateAsync(token);

    private protected override Task ResetSessionAsync() => _omcFactory.ResetAsync();

    private protected override async Task<LibraryLoad> LoadLibraryAsync(string filePath, CancellationToken token)
    {
        try
        {
            Session = await _omcFactory.GetOrCreateAsync(token);

            var isOpen = await Session.LoadFileAsync(filePath, token);
            if (!isOpen)
            {
                // Drained, so what omc says after the retry is about the retry. This read used to be
                // kept in a variable nothing looked at (B336).
                await ClearLogAsync();
                if (File.Exists(filePath))
                {
                    // File exists so maybe OpenModelica already had a version open
                    await Session.ClearAsync();
                    isOpen = await Session.LoadFileAsync(filePath, token);
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
        catch (OperationCanceledException)
        {
            // Sent and abandoned closes the session; dropped here so the next request asks the
            // factory, which replaces a closed one.
            Session = null;
            return LibraryLoad.Failed("Cancelled");
        }
        catch (OpenModelicaExitedException ex)
        {
            Session = null;
            return LibraryLoad.Gone($"{ex.Message} The next check starts a new session.");
        }
        catch (TimeoutException)
        {
            Session = null;
            return LibraryLoad.RanOutOfTime(
                ToolTimeLimit.LoadTimedOut(ToolName, filePath, _commandTimeout, SessionClosed));
        }
        catch (Exception ex)
        {
            Error(LogSource, "Error connecting to OpenModelica", ex);
            return LibraryLoad.Failed($"Error connecting to OpenModelica: {ex.Message}");
        }
    }

    /// <summary>
    /// Drains omc's error buffer. <c>getErrorString</c> returns the accumulated messages and empties
    /// the buffer, so reading it is how it is cleared. (B116 established that omc 1.26's
    /// <c>clear()</c> resets the loaded classes and not the error buffer, which is why this drains the
    /// buffer rather than relying on anything else to have emptied it.) Never throws.
    /// </summary>
    private protected override async Task ClearLogAsync() => _ = await ReadLogOrNullAsync();

    private protected override async Task<CheckAnswer> IssueCheckAsync(IOpenModelicaInterface session,
        string modelId, CancellationToken token)
    {
        try
        {
            return CheckAnswer.Checked(await session.CheckModelAsync(modelId, token));
        }
        catch (OperationCanceledException)
        {
            // The session is closed if the check had been sent; the factory replaces it.
            Session = null;
            return CheckAnswer.WasCancelled;
        }
        catch (TimeoutException)
        {
            // Not the model's verdict, and not worth a log read: the session that would answer
            // it has been closed (B263).
            Session = null;
            return CheckAnswer.NoVerdict(
                ToolTimeLimit.CheckTimedOut(ToolName, modelId, _commandTimeout, SessionClosed));
        }
        catch (OpenModelicaExitedException)
        {
            // omc died under the check. Before the wait watched the process, this waited out the
            // whole time limit and blamed it - or, with no limit, waited until Stop (B334).
            Session = null;
            return CheckAnswer.NoVerdict(UnavailableTool.WentAway(ToolName, modelId));
        }
    }

    /// <summary>omc's <c>getErrorString()</c>: warnings and errors both, emptied as they are read.</summary>
    private protected override Task<string> ReadLogAsync(IOpenModelicaInterface session) =>
        session.GetErrorStringAsync();

    private protected override async Task<string?> ReadLogOrNullAsync()
    {
        try
        {
            var log = Session is null ? null : await Session.GetErrorStringAsync();
            return string.IsNullOrWhiteSpace(log) ? null : log;
        }
        catch (Exception ex)
        {
            Debug(LogSource, $"Could not read the OpenModelica log: {ex.Message}");
            return null;
        }
    }
}
