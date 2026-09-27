using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using OpenModelicaInterface.Interfaces;
using static MLQT.Services.LoggingService;
using OpenModelicaInterface;
using MLQT.Services.Helpers;


namespace MLQT.Services;

/// <summary>
/// Service for checking Modelica models using OpenModelica. The run, its progress and cancellation
/// are <see cref="ModelCheckingServiceBase{TSession}"/>'s; this is what omc does differently.
/// </summary>
public class OpenModelicaCheckingService : ModelCheckingServiceBase<IOpenModelicaInterface>
{
    private readonly IOpenModelicaInterfaceFactory _omcFactory;

    public override string ToolName => "OpenModelica";

    /// <summary>
    /// The one failure that is not the user's model. Singled out by its text so the result can say
    /// "buy a licence" rather than "your model is broken".
    /// </summary>
    private const string DemoLicenceLimit = "Error: the model is too complex for the current license";

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

    public override async Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath)
    {
        var load = await LoadLibraryAsync(filePath, CancellationToken.None);
        return (load.Success, load.ErrorMessage);
    }

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
                _ = await SafeErrorStringAsync();
                if (File.Exists(filePath))
                {
                    // File exists so maybe OpenModelica already had a version open
                    await Session.ClearAsync();
                    isOpen = await Session.LoadFileAsync(filePath, token);
                    if (!isOpen)
                    {
                        return LibraryLoad.Failed(LibraryLoad.WouldNotOpen(ToolName, await SafeErrorStringAsync()));
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
            Error("OpenModelicaCheckingService", "Error connecting to OpenModelica", ex);
            return LibraryLoad.Failed($"Error connecting to OpenModelica: {ex.Message}");
        }
    }

    /// <summary>
    /// The tool's accumulated messages, or null when they cannot be read. Never throws: this is
    /// asked on the success path too, and losing a clean result because the log could not be
    /// fetched would be a worse answer than a result with no log on it.
    /// </summary>
    private async Task<string?> SafeErrorStringAsync()
    {
        try
        {
            var log = Session is null ? null : await Session.GetErrorStringAsync();
            return string.IsNullOrWhiteSpace(log) ? null : log;
        }
        catch (Exception ex)
        {
            Debug("OpenModelicaCheckingService", $"Could not read the OpenModelica log: {ex.Message}");
            return null;
        }
    }

    public override async Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Session ??= await _omcFactory.GetOrCreateAsync(cancellationToken);

            // The library's own package.mo, not the class's file. OpenModelica will not load a
            // class out of the middle of a package: handed Integrator.mo it sees a class called
            // Integrator with nothing to resolve its `within` against, and refuses. Dymola accepts
            // the file and finds the enclosing package itself, which is why the same code worked
            // for one tool and not the other (B170).
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
            Error("OpenModelicaCheckingService", $"Error preparing to check model: {modelNode.Id}", ex);
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
    /// of this, and the copy the package path used had neither the drain-the-log-first step nor the
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
            // Drained first, so what comes back afterwards belongs to *this* check. `getErrorString`
            // returns the accumulated messages and empties the buffer, so reading it here discards
            // anything left by an earlier command — without which a model that checked cleanly could
            // be shown the error from one checked before it. (B116 established that omc 1.26's
            // `clear()` resets the loaded classes and not the error buffer, which is why this drains
            // the buffer rather than relying on anything else to have emptied it.)
            _ = await SafeErrorStringAsync();

            bool checkResult;
            try
            {
                checkResult = await Session!.CheckModelAsync(modelNode.Id, token);
            }
            catch (OperationCanceledException)
            {
                // The session is closed if the check had been sent; the factory replaces it.
                Session = null;
                return null;
            }
            catch (TimeoutException)
            {
                // Not the model's verdict, and not worth a log read: the session that would answer
                // it has been closed (B263).
                Session = null;
                return ToolTimeLimit.CheckTimedOut(ToolName, modelNode.Id, _commandTimeout, SessionClosed);
            }
            catch (OpenModelicaExitedException)
            {
                // omc died under the check. Before the wait watched the process, this waited out the
                // whole time limit and blamed it - or, with no limit, waited until Stop (B334).
                Session = null;
                return UnavailableTool.WentAway(ToolName, modelNode.Id);
            }

            if (checkResult)
            {
                result.Success = true;

                // Checked is not the same as had nothing to say: omc reports warnings through the
                // same error string, and a model that passes with six of them returns true. Read on
                // success as well, so the result dialog can show what the tool actually said (B170).
                result.Log = await SafeErrorStringAsync();
            }
            else
            {
                var error = await Session!.GetErrorStringAsync();
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
            Error("OpenModelicaCheckingService", $"Error checking model: {modelNode.Id}", ex);
            return await FailedResultAsync(modelNode.Id, ex);
        }

        return result;
    }

    /// <summary>
    /// The result for a check that threw: the tool's own account of what went wrong where it can
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
                ? await Session.GetErrorStringAsync()
                : failure.Message;
        }
        catch (Exception innerEx)
        {
            Warn("OpenModelicaCheckingService", $"Failed to get OpenModelica error message: {innerEx.Message}");
            result.ErrorMessage = failure.Message;
        }

        return result;
    }
}
