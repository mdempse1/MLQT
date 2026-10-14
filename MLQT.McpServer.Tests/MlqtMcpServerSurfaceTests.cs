using System.Reflection;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The surface a server built on this one is promised: <see cref="MlqtMcpServerExtensions"/> and
/// <see cref="MlqtMcpServerOptions"/>, and the <see cref="Services.ToolUsageLogger"/> the options name.
///
/// <para>The assemblies other projects build on track their surface with the public-API analyzer
/// (<c>TrackPublicApi</c>). This one does not: the analyzer cannot track two types without listing
/// every public type in the assembly, and the tool classes and their result types are public because
/// the SDK binds them, not as something a host may build against. So the promise is held here
/// instead, and a change to it fails until this list is changed with it - the same reviewed diff the
/// analyzer gives.</para>
/// </summary>
public sealed class MlqtMcpServerSurfaceTests
{
    [Fact]
    public void TheSupportedSurfaceIsWhatAHostWasPromised()
    {
        Assert.Equal(
        [
            "MLQT.McpServer.MlqtMcpServerExtensions.AddMlqtMcpServer(IServiceCollection, MlqtMcpServerOptions) -> IMcpServerBuilder",
            "MLQT.McpServer.MlqtMcpServerOptions..ctor()",
            "MLQT.McpServer.MlqtMcpServerOptions.ConfigureServer { get; init; } : Action`1",
            "MLQT.McpServer.MlqtMcpServerOptions.ToolUsageLogger { get; init; } : ToolUsageLogger",
            "MLQT.McpServer.Services.ToolUsageLogger..ctor()",
            "MLQT.McpServer.Services.ToolUsageLogger..ctor(String)",
            "MLQT.McpServer.Services.ToolUsageLogger.EnableMarkerFileName = mcp-tool-logging.enabled",
            "MLQT.McpServer.Services.ToolUsageLogger.EnableMarkerPath { get; } : String",
            "MLQT.McpServer.Services.ToolUsageLogger.IsEnabled { get; } : Boolean",
            "MLQT.McpServer.Services.ToolUsageLogger.LogPath { get; } : String",
            "MLQT.McpServer.Services.ToolUsageLogger.Record(String, IEnumerable`1, Int64, Boolean) -> Void",
        ],
        Surface(typeof(MlqtMcpServerExtensions), typeof(MlqtMcpServerOptions), typeof(Services.ToolUsageLogger)));
    }

    [Fact]
    public void TheSupportedTypesAreSealedOrStatic()
    {
        // A host may construct them, not derive from them: a derived options class is a second place
        // for a promise to be kept.
        Assert.True(typeof(MlqtMcpServerExtensions).IsAbstract && typeof(MlqtMcpServerExtensions).IsSealed);
        Assert.True(typeof(MlqtMcpServerOptions).IsSealed);
        Assert.True(typeof(Services.ToolUsageLogger).IsSealed);
    }

    private static List<string> Surface(params Type[] types) =>
    [
        .. types.SelectMany(type => type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => Describe(type, member))
            .Where(line => line is not null)
            .Select(line => line!))
            .Order(StringComparer.Ordinal),
    ];

    private static string? Describe(Type type, MemberInfo member) => member switch
    {
        ConstructorInfo c => $"{type.FullName}..ctor({Parameters(c)})",
        MethodInfo { IsSpecialName: true } => null, // property accessors, described with their property
        MethodInfo m => $"{type.FullName}.{m.Name}({Parameters(m)}) -> {m.ReturnType.Name}",
        PropertyInfo p => $"{type.FullName}.{p.Name} {{ {Accessors(p)} }} : {p.PropertyType.Name}",
        FieldInfo { IsLiteral: true } f => $"{type.FullName}.{f.Name} = {f.GetRawConstantValue()}",
        FieldInfo f => $"{type.FullName}.{f.Name} : {f.FieldType.Name}",
        _ => $"{type.FullName}.{member.Name} ({member.MemberType})",
    };

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));

    private static string Accessors(PropertyInfo property)
    {
        var parts = new List<string>();
        if (property.GetMethod?.IsPublic == true)
            parts.Add("get;");
        if (property.SetMethod?.IsPublic == true)
            parts.Add(property.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit") ? "init;" : "set;");
        return string.Join(" ", parts);
    }
}
