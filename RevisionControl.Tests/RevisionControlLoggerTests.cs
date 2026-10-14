using System.Text.RegularExpressions;
using NLog;
using NLog.Config;
using NLog.Targets;
using RevisionControl;
using Xunit;

namespace RevisionControl.Tests;

/// <summary>
/// What a failed revision-control operation leaves behind: a message a caller can show, and the
/// exception itself in a log a production host keeps.
///
/// <para>About twenty failure paths turned the exception into <c>ex.Message</c> on the result and
/// logged nothing, and the ones that did log did so at Debug, which a production host does not
/// record. A failure whose cause was a wrapper - a type initializer that threw, which an obfuscated
/// build is prone to - read only as "The type initializer for 'x' threw an exception", with nothing
/// anywhere to say what had.</para>
/// </summary>
public class RevisionControlLoggerTests
{
    [Fact]
    public void TheMessage_CarriesTheCauseAWrapperHides()
    {
        var wrapped = new TypeInitializationException("Some.Type", new IndexOutOfRangeException("Index was outside the bounds of the array."));

        var message = RevisionControlLogger.Describe(wrapped);

        Assert.StartsWith(wrapped.Message, message);
        Assert.Contains("Index was outside the bounds of the array.", message);
    }

    [Fact]
    public void AnAggregate_IsDescribedByEveryExceptionItHolds()
    {
        // What a failure in a parallel loop arrives as: the wrapper's message names nothing useful.
        var aggregate = new AggregateException(
            new TypeInitializationException("Kinds", new IndexOutOfRangeException("Index was outside the bounds of the array.")),
            new IOException("the remote hung up"));

        var message = RevisionControlLogger.Describe(aggregate);

        Assert.DoesNotContain("One or more errors occurred", message);
        Assert.Contains("Index was outside the bounds of the array.", message);
        Assert.Contains("the remote hung up", message);
    }

    [Fact]
    public void AnExceptionWithNoInner_IsDescribedByItsOwnMessage()
    {
        Assert.Equal("the remote hung up", RevisionControlLogger.Describe(new IOException("the remote hung up")));
    }

    [Fact]
    public void AFailure_IsLoggedWithItsExceptionAtALevelAProductionHostKeeps()
    {
        var target = new MemoryTarget { Layout = "${level}|${message}|${exception:format=type}" };
        var previous = LogManager.Configuration;
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Info, LogLevel.Fatal, target);   // a production host: Info and above
        LogManager.Configuration = config;
        try
        {
            var operation = $"LoggerTest-{Guid.NewGuid():N}";
            RevisionControlLogger.Failed(operation, new InvalidOperationException("boom"));

            var line = Assert.Single(target.Logs, l => l.Contains(operation));
            Assert.StartsWith("Warn|", line);
            Assert.EndsWith("System.InvalidOperationException", line);
        }
        finally
        {
            LogManager.Configuration = previous;
        }
    }

    /// <summary>
    /// Every operation that turns an exception into a result's message goes through
    /// <see cref="RevisionControlLogger.Failed"/>, which logs it. Read from the source, because the
    /// shape it forbids compiles and works on every path that does not fail.
    /// </summary>
    [Fact]
    public void NoOperationReportsABareExceptionMessage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "RevisionControl", "Interfaces")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "RevisionControl"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(x => Regex.IsMatch(x.line, @"ErrorMessage\s*=\s*\w+\.Message\s*;"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These set a result's ErrorMessage from an exception without logging it - use " +
            "RevisionControlLogger.Failed(operation, ex): " + string.Join(", ", offenders));
    }
}
