using System.Reflection;
using System.Runtime.CompilerServices;

namespace MLQT.Services.Tests;

/// <summary>
/// The checking services file their log entries under a name spelled from the tool's, not read from
/// the type, which an obfuscated build renames (B502). Spelled so that it is what the type's name
/// gave before, and the log reads as it always did.
/// </summary>
public class ModelCheckingLogSourceTests
{
    [Theory]
    [InlineData(typeof(DymolaCheckingService))]
    [InlineData(typeof(OpenModelicaCheckingService))]
    public void IsTheServicesOwnName(Type service)
    {
        var instance = RuntimeHelpers.GetUninitializedObject(service);
        var logSource = service.GetProperty("LogSource", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(service.Name, logSource.GetValue(instance));
    }
}
