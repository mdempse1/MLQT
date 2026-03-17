namespace OpenModelicaInterface;

/// <summary>
/// Usage examples for OpenModelicaInterface.
/// These examples demonstrate common workflows with OpenModelica.
/// </summary>
public static class Examples
{
    /// <summary>
    /// Example 1: Basic connection and version check.
    /// </summary>
    public static async Task Example1_BasicConnection()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        // Start OMC
        await omc.StartAsync();

        // Get version
        var version = await omc.GetVersionAsync();
        Console.WriteLine($"OpenModelica Version: {version}");
    }

    /// <summary>
    /// Example 2: Load and check a model from the Modelica Standard Library.
    /// </summary>
    public static async Task Example2_LoadAndCheckModel()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Load Modelica Standard Library
        Console.WriteLine("Loading Modelica library...");
        var loaded = await omc.LoadModelAsync("Modelica");
        Console.WriteLine($"Modelica library loaded: {loaded}");

        // Check a specific model
        var modelName = "Modelica.Electrical.Analog.Examples.ChuaCircuit";
        Console.WriteLine($"\nChecking model: {modelName}");
        var checkResult = await omc.CheckModelAsync(modelName);
        Console.WriteLine($"Check result: {checkResult}");

        // Get error messages if any
        var errors = await omc.GetErrorStringAsync();
        if (!string.IsNullOrEmpty(errors))
        {
            Console.WriteLine($"Errors: {errors}");
        }
    }

    /// <summary>
    /// Example 3: Simulate a model.
    /// </summary>
    public static async Task Example3_SimulateModel()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Load library
        await omc.LoadModelAsync("Modelica");

        // Simulate a model
        var modelName = "Modelica.Blocks.Examples.PID_Controller";
        Console.WriteLine($"Simulating {modelName}...");

        var result = await omc.SimulateModelAsync(
            modelName: modelName,
            startTime: 0.0,
            stopTime: 4.0,
            numberOfIntervals: 500,
            tolerance: 1e-6,
            method: "dassl"
        );

        Console.WriteLine($"Simulation success: {result.Success}");
        Console.WriteLine($"Result file: {result.ResultFile}");
        if (!string.IsNullOrEmpty(result.Messages))
        {
            Console.WriteLine($"Messages: {result.Messages}");
        }
    }

    /// <summary>
    /// Example 4: Load a custom Modelica file.
    /// </summary>
    public static async Task Example4_LoadCustomFile()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Set working directory
        var workDir = @"C:\MyModels";
        await omc.SetWorkingDirectoryAsync(workDir);
        Console.WriteLine($"Working directory: {await omc.GetWorkingDirectoryAsync()}");

        // Load a custom .mo file
        var filePath = @"C:\MyModels\MyPackage.mo";
        var loaded = await omc.LoadFileAsync(filePath);
        Console.WriteLine($"File loaded: {loaded}");

        // Get all loaded classes
        var classes = await omc.GetClassNamesAsync();
        Console.WriteLine($"Loaded classes: {string.Join(", ", classes)}");
    }

    /// <summary>
    /// Example 5: Explore a package structure.
    /// </summary>
    public static async Task Example5_ExplorePackage()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Load Modelica library
        await omc.LoadModelAsync("Modelica");

        // Get classes in Modelica.Blocks package
        var packageName = "Modelica.Blocks";
        var classes = await omc.GetClassNamesInPackageAsync(packageName);
        Console.WriteLine($"Classes in {packageName}:");
        foreach (var cls in classes.Take(10)) // Show first 10
        {
            Console.WriteLine($"  - {cls}");
        }

        // Get information about a specific class
        var className = "Modelica.Blocks.Continuous.PID";
        var info = await omc.GetClassInformationAsync(className);
        Console.WriteLine($"\nClass information for {className}:");
        Console.WriteLine(info);

        // Get documentation
        var comment = await omc.GetClassCommentAsync(className);
        Console.WriteLine($"\nDocumentation: {comment}");
    }

    /// <summary>
    /// Example 6: Build a model without simulating.
    /// </summary>
    public static async Task Example6_BuildModel()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Load library
        await omc.LoadModelAsync("Modelica");

        // Build (compile) a model
        var modelName = "Modelica.Mechanics.Rotational.Examples.First";
        Console.WriteLine($"Building {modelName}...");

        var buildSuccess = await omc.BuildModelAsync(modelName);
        Console.WriteLine($"Build success: {buildSuccess}");

        // Get any error messages
        var errors = await omc.GetErrorStringAsync();
        if (!string.IsNullOrEmpty(errors))
        {
            Console.WriteLine($"Messages: {errors}");
        }
    }

    /// <summary>
    /// Example 7: Instantiate and inspect a model.
    /// </summary>
    public static async Task Example7_InstantiateModel()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Load library
        await omc.LoadModelAsync("Modelica");

        // Instantiate a model (flattens the model to show all equations)
        var modelName = "Modelica.Electrical.Analog.Basic.Resistor";
        Console.WriteLine($"Instantiating {modelName}...");

        var flatModel = await omc.InstantiateModelAsync(modelName);
        Console.WriteLine("Flattened model:");
        Console.WriteLine(flatModel);
    }

    /// <summary>
    /// Example 8: Get model components.
    /// </summary>
    public static async Task Example8_GetComponents()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Load library
        await omc.LoadModelAsync("Modelica");

        // Get components of a model
        var modelName = "Modelica.Blocks.Continuous.PID";
        Console.WriteLine($"Components of {modelName}:");

        var components = await omc.GetComponentsAsync(modelName);
        Console.WriteLine(components);
    }

    /// <summary>
    /// Example 9: Error handling and recovery.
    /// </summary>
    public static async Task Example9_ErrorHandling()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Try to load a non-existent model
        Console.WriteLine("Attempting to load non-existent model...");
        var loaded = await omc.LoadFileAsync("NonExistent.mo");
        Console.WriteLine($"Load result: {loaded}");

        // Get error messages
        var errors = await omc.GetErrorStringAsync();
        Console.WriteLine($"Errors: {errors}");

        // Clear errors and state
        await omc.ClearAsync();
        Console.WriteLine("Cleared OMC state");

        // Verify errors are cleared
        var errorsAfterClear = await omc.GetErrorStringAsync();
        Console.WriteLine($"Errors after clear: '{errorsAfterClear}'");
    }

    /// <summary>
    /// Example 10: Send custom commands.
    /// </summary>
    public static async Task Example10_CustomCommands()
    {
        var omcPath = @"C:\Program Files\OpenModelica1.26.0-64bit\bin\omc.exe";
        using var omc = new OpenModelicaInterface(omcPath);

        await omc.StartAsync();

        // Send custom OMC scripting commands
        Console.WriteLine("Sending custom commands...");

        // Get list of available functions
        var helpResult = await omc.SendCommandAsync("help()");
        Console.WriteLine($"Help (truncated): {helpResult.Substring(0, Math.Min(200, helpResult.Length))}...");

        // Check OMC flags
        var flags = await omc.SendCommandAsync("getCommandLineOptions()");
        Console.WriteLine($"Command line options available: {flags.Contains("help")}");

        // Get installation directory
        var installDir = await omc.SendCommandAsync("getInstallationDirectoryPath()");
        Console.WriteLine($"Installation directory: {installDir}");
    }

    /// <summary>
    /// Run all examples in sequence.
    /// </summary>
    public static async Task RunAllExamples()
    {
        Console.WriteLine("=== Example 1: Basic Connection ===\n");
        await Example1_BasicConnection();

        Console.WriteLine("\n\n=== Example 2: Load and Check Model ===\n");
        await Example2_LoadAndCheckModel();

        Console.WriteLine("\n\n=== Example 3: Simulate Model ===\n");
        await Example3_SimulateModel();

        Console.WriteLine("\n\n=== Example 5: Explore Package ===\n");
        await Example5_ExplorePackage();

        Console.WriteLine("\n\n=== Example 6: Build Model ===\n");
        await Example6_BuildModel();

        Console.WriteLine("\n\n=== Example 7: Instantiate Model ===\n");
        await Example7_InstantiateModel();

        Console.WriteLine("\n\n=== Example 8: Get Components ===\n");
        await Example8_GetComponents();

        Console.WriteLine("\n\n=== Example 9: Error Handling ===\n");
        await Example9_ErrorHandling();

        Console.WriteLine("\n\n=== Example 10: Custom Commands ===\n");
        await Example10_CustomCommands();

        Console.WriteLine("\n\n=== All examples completed ===");
    }
}
