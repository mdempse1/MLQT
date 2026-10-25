using System.Collections.Frozen;

namespace ModelicaParser.Helpers;

/// <summary>
/// The names a Modelica tool provides itself, beyond the language's own (<see cref="ModelicaLanguage"/>):
/// functions and flags that no library declares, which a class written for that tool may use without
/// importing anything. Code that uses one works in that tool and nowhere else, which is why
/// <c>MLQT.Reference.Undeclared</c> accepts them only for the tools a repository says it accepts.
/// </summary>
/// <remarks>
/// <para><b>Names only.</b> The lists hold names and nothing else; no tool's documentation text is
/// used.</para>
/// <para><b>Asked after a name has been looked up, never before.</b> A library may declare a class
/// called <c>plot</c> or <c>list</c>, and where it does that is what the name means.</para>
/// </remarks>
public static class ToolBuiltIns
{
    /// <summary>The tool name for Dymola, as a repository's settings store it.</summary>
    public const string Dymola = "Dymola";

    /// <summary>The tool name for OpenModelica, as a repository's settings store it.</summary>
    public const string OpenModelica = "OpenModelica";

    /// <summary>The tools there is a list for, in the order a settings dialog offers them.</summary>
    public static IReadOnlyList<string> Tools { get; } = [Dymola, OpenModelica];

    /// <summary>
    /// Dymola's own functions, by the name a class calls them with, compared ordinally. A dotted one
    /// (<c>Utilities.getenv</c>) is the whole name. Read-only: a frozen set, so no caller can change
    /// what every other caller is told.
    /// </summary>
    /// <remarks>
    /// Not the whole of what <see cref="ToolOf"/> counts as Dymola's: that also takes a name inside
    /// <see cref="DymolaFlagPackages"/>, one starting with <see cref="DymolaFunctionPrefix"/>, and a
    /// listed name as the first segment of a longer one (<c>LinePattern.Solid</c>). Ask
    /// <see cref="ToolOf"/> to classify a name; read this for the list itself.
    /// </remarks>
    public static IReadOnlySet<string> DymolaFunctions => DymolaNames;

    /// <summary>The packages Dymola's flags are members of: <c>Advanced.Translation.X</c>, <c>Hidden.Y</c>.</summary>
    public static IReadOnlySet<string> DymolaFlagPackages => FlagPackages;

    /// <summary>
    /// The prefix of a family of Dymola functions nobody has the whole list of: every name starting
    /// with it is Dymola's.
    /// </summary>
    public const string DymolaFunctionPrefix = "Dymola_AST_";

    private static readonly FrozenSet<string> DymolaNames = new[]
    {
        "AddModelicaPath", "animationColor", "animationFollow", "animationFrames", "animationGrid",
        "animationLightning", "animationOnline", "animationPerspective", "animationPosition",
        "animationRedraw", "animationRemove", "animationSelected", "animationSetup", "animationSpeed",
        "animationSubdivisions", "animationTime", "animationTrace", "animationTransparency",
        "animationVectorScaling", "animationView", "animationViewing", "BaseFontSize",
        "calculateNumberPrecision", "cd", "ChangeDefaultDirectory", "checkConversion", "checkModel",
        "classDirectory", "classExists", "clear", "clearFlags", "clearlog", "clearPlot", "closeModel",
        "compile", "conditionalConstraint", "constrain", "continue", "createPlot", "createTable",
        "CreateWindow3D", "DefaultModelicaVersion", "defineBranch", "defineDefaultDisplayUnit",
        "definePotentialRoot", "defineRoot", "defineUnitConversion", "deleteVariables", "direction",
        "document", "Dymola_getLastResultFileName", "DymolaLicenseInfo", "DymolaVersion",
        "DymolaVersionNumber", "eraseClasses", "Evaluate", "Execute", "existTrajectoryNames", "exit",
        "exitDelayed", "experiment", "experimentGetOutput", "experimentSetupOutput", "exportAnimation",
        "exportDependencyGraph", "exportDiagram", "exportDocumentation", "exportEquations",
        "exportHTMLDirectory", "exportIcon", "exportInitial", "exportInitialDsin", "exportModelicaText",
        "ExportPlotAsImage", "exportSSP", "findFast", "findLastFast", "generateTranslationFile",
        "getClassText", "getDependentLibraries", "GetDymolaCompiler", "getExperiment", "getLastError",
        "getModelParameters", "GetPlotAsSVG", "getRegistryValue", "getTempFolder",
        "GetVariableStartValue", "help", "importFMU", "importInitial", "importInitialResult",
        "importScalarVariables", "importSSP", "importSSV", "initialized", "instantiateModel",
        "internal_twister_setseed", "internal_twister_setseedvector", "internal_twister_uniform",
        "interpolateTrajectory", "isRoot", "linearize", "linearizeModel", "LinePattern", "list",
        "listfunctions", "loadAnimation", "loadResult", "MarkerStyle", "messageBox", "messageDialog",
        "nthRoot", "openModel", "openModelFile", "PackMaterial", "PackShape", "plot", "plotArray",
        "plotArrays", "plotDiscretized", "plotDocumentation", "plotExpression", "plotExternal",
        "plotGetFilesToKeep", "plotHeading", "plotMovingAverage", "plotParametricCurve",
        "plotParametricCurves", "plotRowColumnLabels", "plotScatter", "plotSetErase",
        "plotSetFilesToKeep", "plotSetup", "plotSignalDifference", "plotSignalOperator",
        "plotSignalOperatorHarmonic", "plotText", "plotTitle", "plotWindowSetup", "printPlot",
        "printPlotArray", "printPlotArrays", "promote", "readComponentsFromXML",
        "readCSVHeadersInternal", "readCSVmatrixInternal", "readCSVsizes", "readDataFromFile",
        "readMATmatrices", "readMatrix", "readMatrixSize", "readSDFsizes", "readStringMatrix",
        "readTrajectory", "readTrajectoryNames", "readTrajectorySize", "removeMessageBox", "removePlots",
        "removeResults", "RequestOption", "residue", "RestoreWindow", "RunAnimation", "RunScript",
        "saveEncryptedModel", "savelog", "saveModel", "saveSettings", "saveTotalModel", "setClassText",
        "SetDymolaCompiler", "SetVariable", "ShowComponent", "showMessageWindow", "signalOperatorValue",
        "simulate", "simulateExtendedModel", "simulateModel", "simulateMultiExtendedModel",
        "simulateMultiResultsModel", "spliceFunction", "spliceFunction_der", "startHttpServer",
        "steadyStateInitialization", "stopHttpServer", "stopSimulation", "Subtask_activated",
        "Subtask_lastInterval", "switchLayer", "switchToModelingMode", "switchToSimulationMode", "system",
        "trace", "translateModel", "translateModelExport", "translateModelFMU",
        "updateModelicaAnnotations", "updateModelicaFlags", "updateModelicaFormatting",
        "updateModelicaURIreference", "Utilities.getenv", "Utilities.ModelicaMessage", "Utilities.setenv",
        "variables", "verifyCompiler", "visualize3dModel", "WaitSpecifiedTime", "writeCSVFileInternal",
        "writeMatrix", "writeToFile", "writeTrajectory",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FlagPackages =
        new[] { "Advanced", "Hidden" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The tool that provides <paramref name="reference"/>, or null when no tool here does. A function
    /// is matched by its name as written (<c>classDirectory</c>, <c>Utilities.getenv</c>), or as the
    /// first segment (<c>LinePattern.Solid</c>, a literal of one of Dymola's enumerations); a flag or
    /// a command by the package it is in (<c>Advanced.X</c>, <c>OpenModelica.Scripting.f</c>).
    /// </summary>
    public static string? ToolOf(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        var name = reference.Trim().TrimStart('.');
        var segments = ModelicaName.Segments(name);
        if (segments.Count == 0)
            return null;
        var first = segments[0];

        // A listed name is matched as the first segment too: `LinePattern.Solid`, `MarkerStyle.None`
        // are literals of Dymola's own plot enumerations.
        if (DymolaNames.Contains(name)
            || DymolaNames.Contains(first)
            || first.StartsWith(DymolaFunctionPrefix, StringComparison.Ordinal)
            || (segments.Count > 1 && FlagPackages.Contains(first)))
            return Dymola;
        if (first == OpenModelica)
            return OpenModelica;
        return null;
    }

    /// <summary>
    /// True when <paramref name="reference"/> is provided by one of <paramref name="acceptedTools"/>.
    /// The tool names compare ignoring case, as a settings file may be written by hand.
    /// </summary>
    public static bool IsAccepted(string? reference, IEnumerable<string>? acceptedTools) =>
        ToolOf(reference) is { } tool
        && acceptedTools?.Contains(tool, StringComparer.OrdinalIgnoreCase) == true;
}
