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

        // Console target for debugging
        var consoleTarget = new ConsoleTarget("console")
        {
            Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${message}"
        };

        // Add targets and rules
        config.AddTarget(fileTarget);
        config.AddTarget(consoleTarget);

        config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);

        LogManager.Configuration = config;
        _isInitialized = true;

        Info("LoggingService", "Logging initialized. Log file location: " + logFolder);
    }

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
