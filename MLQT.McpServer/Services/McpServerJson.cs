using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MLQT.McpServer.Dtos;
using ModelContextProtocol;

namespace MLQT.McpServer.Services;

/// <summary>
/// The serializer options the tools are registered with: the SDK's own defaults, with every result and
/// argument type MLQT defines taken from contracts compiled into this assembly.
/// </summary>
/// <remarks>
/// <para><b>Why compiled in.</b> A tool result's field names are what a client reads, and reflection
/// takes them from the record's members, which an obfuscated build renames. The source generator
/// writes them into the assembly when it is built, before any obfuscator runs (B502). The SDK's
/// resolver comes after this one, for its own protocol types.</para>
/// <para><b>Every type in <c>Dtos/</c> must be listed below</b>, and <c>ObfuscationReadinessTests</c> fails
/// for one that is not - otherwise it would quietly go to the SDK's resolver, which reflects. A tool returns
/// <c>object</c>, so the compiler cannot see which types reach the wire; a tool must return one of these
/// records, never an anonymous object, which a test also holds.</para>
/// </remarks>
internal static class McpServerJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                McpServerJsonContext.Default,
                McpJsonUtilities.DefaultOptions.TypeInfoResolver!),
        };
        options.MakeReadOnly();
        return options;
    }
}

[JsonSerializable(typeof(AnalyzeDependenciesResult))]
[JsonSerializable(typeof(BatchEditResult))]
[JsonSerializable(typeof(BatchFileChange))]
[JsonSerializable(typeof(BatchOperation))]
[JsonSerializable(typeof(IReadOnlyList<BatchOperation>))]
[JsonSerializable(typeof(BehaviorLineView))]
[JsonSerializable(typeof(ChangeImpactResult))]
[JsonSerializable(typeof(ChangedClassesResult))]
[JsonSerializable(typeof(ChangedFileDto))]
[JsonSerializable(typeof(CheckResult))]
[JsonSerializable(typeof(ClassBehaviorResult))]
[JsonSerializable(typeof(ClassDocumentationResult))]
[JsonSerializable(typeof(ClassElementView))]
[JsonSerializable(typeof(ClassElementsResult))]
[JsonSerializable(typeof(ClassInfo))]
[JsonSerializable(typeof(ClassInterfaceView))]
[JsonSerializable(typeof(ClassListItem))]
[JsonSerializable(typeof(ClassListResult))]
[JsonSerializable(typeof(ClassRef))]
[JsonSerializable(typeof(ClassResourcesResult))]
[JsonSerializable(typeof(ClassSearchItem))]
[JsonSerializable(typeof(ClassSearchResult))]
[JsonSerializable(typeof(ClassSourceResult))]
[JsonSerializable(typeof(ConnectionView))]
[JsonSerializable(typeof(ConnectionsResult))]
[JsonSerializable(typeof(ConnectorView))]
[JsonSerializable(typeof(CorrectSpellingResult))]
[JsonSerializable(typeof(CreateClassResult))]
[JsonSerializable(typeof(CreateLibraryResult))]
[JsonSerializable(typeof(DeleteClassResult))]
[JsonSerializable(typeof(DependencyResult))]
[JsonSerializable(typeof(DiagramComponent))]
[JsonSerializable(typeof(DiagramLayoutResult))]
[JsonSerializable(typeof(DiscoveredLibrarySummary))]
[JsonSerializable(typeof(FindingItem))]
[JsonSerializable(typeof(FindingsResult))]
[JsonSerializable(typeof(FormatClassResult))]
[JsonSerializable(typeof(FormatCodeResult))]
[JsonSerializable(typeof(FunctionSignatureView))]
[JsonSerializable(typeof(GuidanceResult))]
[JsonSerializable(typeof(ImpactDetailDto))]
[JsonSerializable(typeof(ImpactResult))]
[JsonSerializable(typeof(InterfaceSearchItem))]
[JsonSerializable(typeof(InterfaceSearchResult))]
[JsonSerializable(typeof(LibraryDependency))]
[JsonSerializable(typeof(LibrarySummary))]
[JsonSerializable(typeof(IReadOnlyList<LibrarySummary>))]
[JsonSerializable(typeof(LoadRepositoryResult))]
[JsonSerializable(typeof(MemberView))]
[JsonSerializable(typeof(MoveClassResult))]
[JsonSerializable(typeof(PackageTreeNode))]
[JsonSerializable(typeof(ParameterView))]
[JsonSerializable(typeof(RedeclarationView))]
[JsonSerializable(typeof(ReferenceValidationResult))]
[JsonSerializable(typeof(ReloadResult))]
[JsonSerializable(typeof(RenameClassResult))]
[JsonSerializable(typeof(RenameFileChange))]
[JsonSerializable(typeof(RepositorySummary))]
[JsonSerializable(typeof(IReadOnlyList<RepositorySummary>))]
[JsonSerializable(typeof(ResourceRefDto))]
[JsonSerializable(typeof(ResourceUsagesResult))]
[JsonSerializable(typeof(ResourceWarningDto))]
[JsonSerializable(typeof(ResourceWarningsResult))]
[JsonSerializable(typeof(ServerInfoResult))]
[JsonSerializable(typeof(SetStyleSettingsResult))]
[JsonSerializable(typeof(SpellCheckResult))]
[JsonSerializable(typeof(SpellSuggestionsResult))]
[JsonSerializable(typeof(StructureEditResult))]
[JsonSerializable(typeof(StyleFindingDto))]
[JsonSerializable(typeof(StyleSettingsInput))]
[JsonSerializable(typeof(StyleSettingsResult))]
[JsonSerializable(typeof(TextSearchItem))]
[JsonSerializable(typeof(TextSearchResult))]
[JsonSerializable(typeof(ToolError))]
[JsonSerializable(typeof(UnloadLibraryResult))]
[JsonSerializable(typeof(UnresolvedReference))]
[JsonSerializable(typeof(UpdateClassSourceResult))]
internal sealed partial class McpServerJsonContext : JsonSerializerContext;
