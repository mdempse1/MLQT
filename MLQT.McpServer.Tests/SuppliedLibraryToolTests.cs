using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Every tool answers on a library a host supplies from memory, and none of them writes to it.
///
/// <para>The point of supplying classes rather than recovering them from documentation is that
/// they are ordinary Modelica in the graph, so every existing tool reads them without knowing where
/// they came from. That is only true if nothing on the way assumes a class has a file on disk — and
/// the edit tools must refuse them as firmly as they refuse an encrypted package, without being
/// taught about them one by one. So the sweep calls <b>every</b> tool that names a class, by
/// reflection, rather than a list someone remembered to extend.</para>
/// </summary>
public class SuppliedLibraryToolTests : IAsyncLifetime
{
    private const string Note = "Supplied by a test host - this is NOT the vendor's source.";

    private const string VendorPackage = """
        within;
        package Vendor "A supplied library"
          package Interfaces "Connectors"
            connector Flange "A flange"
              Real s "position";
              flow Real f "force";
              annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, fillColor={0,0,0}, fillPattern=FillPattern.Solid)}));
            end Flange;
          end Interfaces;

          model Spring "A linear spring"
            parameter Real c(unit = "N/m") = 1e3 "spring constant";
            Interfaces.Flange flange_a "left flange"
              annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
            Interfaces.Flange flange_b "right flange"
              annotation (Placement(transformation(extent={{90,-10},{110,10}})));
          equation
            flange_b.f = c*(flange_b.s - flange_a.s);
            flange_a.f = -flange_b.f;
            annotation (Icon(graphics={Line(points={{-90,0},{90,0}})}),
              Documentation(info="<html><p>A spring between two flanges.</p></html>"));
          end Spring;

          model Example "Two springs in series"
            Spring spring1 annotation (Placement(transformation(extent={{-40,-10},{-20,10}})));
            Spring spring2 annotation (Placement(transformation(extent={{20,-10},{40,10}})));
          equation
            connect(spring1.flange_b, spring2.flange_a);
            annotation (experiment(StopTime = 1));
          end Example;
          annotation (version = "2.1.0",
            Diagram(graphics={Text(extent={{-100,-20},{100,20}}, textString="Vendor")}));
        end Vendor;
        """;

    private const string UserPackage = """
        within;
        package MyLib "The user's own library"
          model Rig "Uses a vendor spring"
            Vendor.Spring spring(c = 5e3) "the spring";
          end Rig;
        end MyLib;
        """;

    private sealed class Source : IReadOnlyClassSource
    {
        public string LibraryName => "Vendor";
        public string? LibraryVersion => "2.1.0";
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => Note;

        public ReadOnlySourceContent Read(CancellationToken cancellationToken) =>
            new() { Texts = [new SuppliedText("Vendor/package.mo", VendorPackage)] };
    }

    private readonly TestHost _host = new();
    private readonly ServiceProvider _services;
    private readonly string _userDir;

    public SuppliedLibraryToolTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILibraryDataService>(_host.Libraries)
            .AddSingleton<IRepositoryService>(_host.Repositories)
            .AddSingleton<IExternalResourceService>(_host.Resources)
            .AddSingleton<ICodeReviewService>(_host.CodeReview)
            .AddSingleton<IStyleCheckingService>(_host.StyleChecking)
            .AddSingleton<IImpactAnalysisService>(_host.Impact)
            .AddSingleton<ICustomDictionaryService>(_host.CustomDictionary)
            .AddSingleton<IDictionaryManagerService>(_host.DictionaryManager)
            .AddSingleton<ISettingsService>(_host.Settings)
            .AddSingleton(_host.Session)
            .BuildServiceProvider();

        _userDir = _host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = UserPackage });
    }

    public async ValueTask InitializeAsync()
    {
        await _host.Libraries.AddLibraryFromSourceAsync(new Source());
        await _host.Libraries.AddLibraryFromDirectoryAsync(_userDir);

        // As an agent would before asking what a class uses: the dependency tools answer only once
        // it has run, and running it over supplied classes is part of what is being tested.
        Assert.IsNotType<ToolError>(await Tool<DependencyTools>().AnalyzeDependencies());
    }

    public ValueTask DisposeAsync()
    {
        _services.Dispose();
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private T Tool<T>() where T : class => ActivatorUtilities.CreateInstance<T>(_services);

    private ModelicaGraph.DataTypes.ModelNode Spring()
    {
        var spring = _host.Libraries.GetModelById("Vendor.Spring");
        Assert.NotNull(spring);
        return spring;
    }

    private static string Json(object result) =>
        System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerOptions.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    // ---------------------------------------------------------------- the sweep

    /// <summary>Parameter names that name a class the tool reads or acts on.</summary>
    private static bool NamesAClass(ParameterInfo p) =>
        p.ParameterType == typeof(string)
        && p.Name is { } name
        && (name.Equals("classId", StringComparison.Ordinal)
            || name.EndsWith("ClassId", StringComparison.Ordinal)
            || name.Equals("modelId", StringComparison.Ordinal)
            || name.Equals("packageId", StringComparison.Ordinal)
            || name.Equals("parentId", StringComparison.Ordinal));

    private static IEnumerable<MethodInfo> ToolsNamingAClass() =>
        typeof(ClassQueryTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Where(m => m.GetParameters().Any(NamesAClass));

    /// <summary>
    /// A value for every parameter: the supplied class for one naming a class, the parameter's own
    /// default where it has one, and otherwise something of the right shape. The aim is to reach
    /// each tool's handling of the class, not to exercise its options.
    /// </summary>
    private static object? ArgumentFor(ParameterInfo p, string classId)
    {
        if (NamesAClass(p))
            return classId;
        if (p.HasDefaultValue)
            return p.DefaultValue;
        if (p.ParameterType == typeof(CancellationToken))
            return CancellationToken.None;
        if (p.ParameterType == typeof(string))
        {
            var name = (p.Name ?? "").ToLowerInvariant();
            return name.Contains("source") || name.Contains("code") || name.Contains("modelica")
                ? "model Probe end Probe;"
                : name.Contains("type") ? "Vendor.Spring"
                : name.Contains("query") || name.Contains("text") || name.Contains("pattern") ? "Spring"
                : "probe";
        }
        if (p.ParameterType == typeof(bool))
            return false;
        if (p.ParameterType.IsValueType)
            return Activator.CreateInstance(p.ParameterType);
        if (p.ParameterType.IsArray)
            return Array.CreateInstance(p.ParameterType.GetElementType() ?? typeof(object), 0);
        return null;
    }

    private static async Task<object?> InvokeAsync(object tool, MethodInfo method, string classId)
    {
        var result = method.Invoke(tool, method.GetParameters().Select(p => ArgumentFor(p, classId)).ToArray());
        if (result is Task task)
        {
            await task;
            result = task.GetType().GetProperty("Result")?.GetValue(task);
        }

        return result;
    }

    /// <summary>A model, a model wiring two others, and the package: each has something to draw.</summary>
    public static TheoryData<string> SuppliedClasses => new() { "Vendor.Spring", "Vendor.Example", "Vendor" };

    /// <summary>The refusal every editing tool gives a supplied class (<c>ReadOnlyClassGuard</c>).</summary>
    private const string Refusal = "belongs to a read-only library supplied from memory";

    [Theory]
    [MemberData(nameof(SuppliedClasses))]
    public async Task EveryToolNamingAClass_AnswersOrRefusesAsReadOnly_AndChangesNothing(string classId)
    {
        var tools = ToolsNamingAClass().ToList();
        Assert.True(tools.Count > 30, $"only {tools.Count} tools found; the reflection query is wrong");

        var before = SuppliedSources();
        var userFiles = UserFiles();
        var failures = new List<string>();
        var refused = 0;

        foreach (var method in tools)
        {
            var name = method.GetCustomAttribute<McpServerToolAttribute>()?.Name ?? method.Name;
            try
            {
                var tool = ActivatorUtilities.CreateInstance(_services, method.DeclaringType
                    ?? throw new InvalidOperationException($"{method.Name} has no declaring type"));

                // Exactly two outcomes are acceptable: an answer, or the read-only refusal. Any other
                // error means the tool reached something about the class before asking whether it may
                // edit it - or could not read it - and either is what this test is for.
                switch (await InvokeAsync(tool, method, classId))
                {
                    case ToolError error when error.Error.Contains(Refusal, StringComparison.Ordinal):
                        refused++;
                        break;
                    case ToolError error:
                        failures.Add($"{name}: neither answered nor refused as read-only: {error.Error}");
                        break;
                }
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } e } ? e : ex;
                failures.Add($"{name}: {inner.GetType().Name}: {inner.Message}");
            }

            // Checked after each tool, so a failure names the tool that did it - and taken again after
            // one, so the tools after it are not blamed for the same change.
            if (SuppliedSources() is var sources && !sources.SequenceEqual(before))
            {
                failures.Add($"{name}: changed a supplied class in the graph");
                before = sources;
            }

            if (UserFiles() is var files && !files.SequenceEqual(userFiles))
            {
                failures.Add($"{name}: changed the user's files while acting on a supplied class");
                userFiles = files;
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // Not zero, or the refusal text no longer matches what the guard says and every editing tool
        // is being counted as one that answered.
        Assert.True(refused > 15, $"only {refused} tools refused; has the refusal's wording changed?");
    }

    private List<string> SuppliedSources() =>
        [.. _host.Libraries.CombinedGraph.ModelNodes
            .Where(m => m.Id.StartsWith("Vendor", StringComparison.Ordinal))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => $"{m.Id}|{m.IsExternalStub}|{m.Definition.ModelicaCode}")];

    private List<string> UserFiles() =>
        [.. Directory.GetFiles(_userDir, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => $"{f}|{File.ReadAllText(f)}")];

    // ---------------------------------------------------------------- reading

    [Fact]
    public void TheLibrary_IsListedWithItsVersionAndWhyItIsReadOnly()
    {
        var vendor = Tool<SessionTools>().ListLibraries().Single(l => l.Name == "Vendor");

        Assert.Equal("2.1.0", vendor.Version);
        Assert.Equal("Supplied", vendor.ReadOnlySource);
        Assert.Equal("Supplied", vendor.SourceType);
        Assert.Null(Tool<SessionTools>().ListLibraries().Single(l => l.Name == "MyLib").ReadOnlySource);
    }

    [Fact]
    public void ItsSource_OpensWithTheBanner_AndShowsTheVisibleEquations()
    {
        var json = Json(Tool<ClassQueryTools>().GetClassSource("Vendor.Spring"));

        Assert.Contains(Note, json);
        Assert.Contains("flange_b.f = c*(flange_b.s - flange_a.s)", json);
    }

    [Fact]
    public void ItsInterface_IsReadFromItsDeclarations_NotFromDocumentation()
    {
        var json = Json(Tool<ViewTools>().GetClassInterface("Vendor.Spring"));

        Assert.Contains("\"c\"", json);
        Assert.Contains("flange_a", json);
        Assert.Contains("N/m", json);
        Assert.DoesNotContain("\"recoveredFromDocumentation\":true", json);
    }

    [Fact]
    public void ItIsReportedAsNotWritable()
    {
        var json = Json(Tool<ClassQueryTools>().GetClassInfo("Vendor.Spring"));

        Assert.Contains("\"writable\":false", json);
    }

    [Fact]
    public void SearchFindsIt()
    {
        Assert.Contains("Vendor.Spring", Json(Tool<ClassQueryTools>().SearchClasses("Spring")));
        Assert.Contains("Vendor.Spring", Json(Tool<SearchTools>().SearchText("linear spring")));
    }

    [Fact]
    public void TheUsersReferenceIntoIt_Resolves()
    {
        var result = ToolAssert.Ok<ReferenceValidationResult>(Tool<ViewTools>().ValidateClassReferences("MyLib.Rig"));

        Assert.Equal(0, result.UnresolvedCount);
    }

    [Fact]
    public void ItsDiagram_IsDrawn()
    {
        var result = Tool<DiagramTools>().GetDiagramImage("Vendor.Example");

        Assert.IsNotType<ToolError>(result);
    }

    // ---------------------------------------------------------------- writing

    [Fact]
    public async Task AnEditToIt_IsRefused_AndSaysWhy()
    {
        var result = await Tool<EditTools>().UpdateClassSource(
            "Vendor.Spring", "model Spring \"replaced\" end Spring;");

        var message = Assert.IsType<ToolError>(result).Error;
        Assert.Contains("read-only library supplied from memory", message);
        Assert.Contains(Note, Spring().Definition.ModelicaCode);
    }

    [Fact]
    public void ItsFilePath_IsNeverWritable()
    {
        var fileId = Spring().ContainingFileId;
        Assert.NotNull(fileId);
        var file = _host.Libraries.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(fileId);
        Assert.NotNull(file);

        Assert.False(FileWritability.IsWritable(file.FilePath));
        Assert.NotNull(FileWritability.PreflightWritable([file.FilePath], "edit"));
    }

    [Fact]
    public async Task TheUsersOwnClass_IsStillEditable_UsingTheSuppliedOnes()
    {
        var result = await Tool<StructureEditTools>().AddComponent(
            "MyLib.Rig", "Vendor.Spring", "spring2");

        Assert.IsNotType<ToolError>(result);
        Assert.Contains("Vendor.Spring spring2", File.ReadAllText(Path.Combine(_userDir, "package.mo")));
    }
}
