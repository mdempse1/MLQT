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

    // Dymola's own functions, by the name a class calls them with. Dotted ones are matched whole.
    private static readonly HashSet<string> DymolaNames = new(StringComparer.Ordinal)
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
    };

    // Dymola's flags are members of these two packages: `Advanced.Translation.X`, `Hidden.Y`.
    private static readonly HashSet<string> DymolaFlagPackages = new(StringComparer.Ordinal) { "Advanced", "Hidden" };

    // A family of Dymola functions nobody has the whole list of; the prefix is what they share.
    private const string DymolaAstPrefix = "Dymola_AST_";

    /// <summary>
    /// The tool that provides <paramref name="reference"/>, or null when no tool here does. A function
    /// is matched by its name as written (<c>classDirectory</c>, <c>Utilities.getenv</c>); a flag or a
    /// command by the package it is in (<c>Advanced.X</c>, <c>OpenModelica.Scripting.f</c>).
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

        if (DymolaNames.Contains(name)
            || first.StartsWith(DymolaAstPrefix, StringComparison.Ordinal)
            || (segments.Count > 1 && DymolaFlagPackages.Contains(first)))
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
