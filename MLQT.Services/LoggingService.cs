using NLog;
using NLog.Config;
using NLog.Targets;

namespace MLQT.Services;

/// <summary>
/// Centralized logging service using NLog.
/// Logs are written to AppData/MLQT folder.
/// </summary>
public static class LoggingService
{
    private static readonly Logger _logger;
    private static bool _isInitialized = false;

    static LoggingService()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    /// <summary>
    /// Initializes the logging configuration.
    /// Should be called once at application startup.
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized)
            return;

        var config = new LoggingConfiguration();

        // Get the AppData folder path and create MLQT subfolder
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logFolder = Path.Combine(appDataPath, "MLQT");
        Directory.CreateDirectory(logFolder);

        LogDirectory = logFolder;

        var logFilePath = Path.Combine(logFolder, "mlqt-${shortdate}.log");

        // File target for logging
        var fileTarget = new FileTarget("logfile")
        {
            FileName = logFilePath,
            Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${logger} | ${message} ${exception:format=tostring}",
            ArchiveFileName = Path.Combine(logFolder, "mlqt-{#}.log"),
            ArchiveSuffixFormat = "_{1:yyyyMMdd}_{0:00}",
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveFiles = 30,
            KeepFileOpen = false
        };

        config.AddTarget(fileTarget);
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);

        // The console target is OFF unless asked for. It used to be on at Info, and nobody saw it for
        // as long as MLQT was a Windows-only WinExe with no console attached - so a desktop
        // application started from a Linux terminal printed its entire startup sequence and every
        // step of the analysis pipeline into the terminal it was launched from.
        //
        // Two reasons it is off rather than quieter. It is the B121 shape: every console write is
        // synchronous and lands on the thread producing it, and MLQT logs heavily throughout the
        // pipeline, which is exactly the cost we removed from Photino's own message logging. And it
        // leaves **one place to look when something is wrong** - the log file - rather than an answer
        // that depends on how the application happened to be started.
        var consoleLevel = ConsoleLevel(Environment.GetEnvironmentVariable(ConsoleLevelVariable));

        if (consoleLevel is not null)
        {
            var consoleTarget = new ConsoleTarget("console")
            {
                Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${message}"
            };

            config.AddTarget(consoleTarget);
            config.AddRule(consoleLevel, LogLevel.Fatal, consoleTarget);
        }

        LogManager.Configuration = config;
        _isInitialized = true;

        Info("LoggingService", "Logging initialized. Log file location: " + logFolder);
    }

    /// <summary>
    /// Set to a level name — or to anything at all — to have log lines written to the console as well
    /// as the file. Unset, nothing is written to the console.
    /// </summary>
    /// <remarks>
    /// The same shape as <c>MLQT_PHOTINO_LOG</c>: a diagnostic an ordinary run does not pay for.
    /// </remarks>
    public const string ConsoleLevelVariable = "MLQT_LOG_CONSOLE";

    /// <summary>
    /// The level to log to the console at, or <c>null</c> for no console logging at all.
    /// </summary>
    /// <remarks>
    /// <para>Separated from <see cref="Initialize"/> because that method configures NLog globally and
    /// runs once per process, so the decision inside it cannot be tested. This can.</para>
    ///
    /// <para><b>An unrecognised value means Info, not off.</b> <c>1</c> and <c>true</c> are what a
    /// person actually types, and someone who sets this variable is trying to leave silence — a typo
    /// that silently returned them to it would be the least helpful reading available. <c>Off</c> is
    /// a real NLog level and is honoured, so it is the way to say so deliberately.</para>
    /// </remarks>
    internal static LogLevel? ConsoleLevel(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
            return null;

        try
        {
            var level = LogLevel.FromString(setting.Trim());
            return level == LogLevel.Off ? null : level;
        }
        catch (ArgumentException)
        {
            return LogLevel.Info;
        }
    }

    /// <summary>
    /// The directory log files are written to, or <c>null</c> until <see cref="Initialize"/> has run.
    /// </summary>
    /// <remarks>
    /// Exposed so that callers asking "where do the logs go?" read the value that was configured
    /// rather than recomputing it. The <c>/selftest</c> route's <c>logging.writes</c> probe used to
    /// recompute it, which meant the probe could agree with itself while disagreeing with NLog.
    /// </remarks>
    public static string? LogDirectory { get; private set; }

    /// <summary>
    /// Blocks until buffered log events have been written, or the timeout elapses.
    /// </summary>
    /// <remarks>
    /// For callers that need to observe the file immediately after writing to it. Ordinary logging
    /// never needs this.
    /// </remarks>
    public static void Flush() => LogManager.Flush(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Gets a logger for a specific class/component.
    /// </summary>
    public static Logger GetLogger(string name) => LogManager.GetLogger(name);

    /// <summary>
    /// Gets a logger for a specific type.
    /// </summary>
    public static Logger GetLogger<T>() => LogManager.GetLogger(typeof(T).FullName ?? typeof(T).Name);

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public static void Info(string source, string message)
    {
        LogManager.GetLogger(source).Info(message);
    }

    /// <summary>
    /// Logs a debug message.
    /// </summary>
    public static void Debug(string source, string message)
    {
        LogManager.GetLogger(source).Debug(message);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public static void Warn(string source, string message)
    {
        LogManager.GetLogger(source).Warn(message);
    }

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public static void Error(string source, string message)
    {
        LogManager.GetLogger(source).Error(message);
    }

    /// <summary>
    /// Logs an exception with full stack trace.
    /// </summary>
    public static void Error(string source, string message, Exception ex)
    {
        LogManager.GetLogger(source).Error(ex, message);
    }

    /// <summary>
    /// Logs an exception with full stack trace.
    /// </summary>
    public static void Error(string source, Exception ex)
    {
        LogManager.GetLogger(source).Error(ex, ex.Message);
    }

    /// <summary>
    /// Logs a fatal error message.
    /// </summary>
    public static void Fatal(string source, string message, Exception ex)
    {
        LogManager.GetLogger(source).Fatal(ex, message);
    }

    /// <summary>
    /// Logs the start of a major processing step.
    /// </summary>
    public static void LogProcessStart(string source, string processName)
    {
        LogManager.GetLogger(source).Info($">>> STARTING: {processName}");
    }

    /// <summary>
    /// Logs the successful completion of a major processing step.
    /// </summary>
    public static void LogProcessEnd(string source, string processName)
    {
        LogManager.GetLogger(source).Info($"<<< COMPLETED: {processName}");
    }

    /// <summary>
    /// Logs the failed completion of a major processing step.
    /// </summary>
    public static void LogProcessFailed(string source, string processName, Exception ex)
    {
        LogManager.GetLogger(source).Error(ex, $"<<< FAILED: {processName}");
    }

    /// <summary>
    /// Shuts down the logging system gracefully.
    /// Should be called when the application is closing.
    /// </summary>
    public static void Shutdown()
    {
        Info("LoggingService", "Logging shutting down");
        LogManager.Shutdown();
    }
}
