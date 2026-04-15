using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace DymolaInterface.Examples;

/// <summary>
/// Example usage of the DymolaInterface class.
/// These examples demonstrate common scenarios for interacting with Dymola.
/// They are illustrative and are excluded from code coverage measurement.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Examples
{
    /// <summary>
    /// Example 1: Connect to an already running Dymola instance and simulate a model.
    /// </summary>
    public static async Task SimulateModelExample()
    {
        // Assumes Dymola is already running with -serverport 8082
        using var dymola = new DymolaInterface();

        if (dymola.IsOfflineMode())
        {
            Console.WriteLine("Dymola is not running. Start Dymola with: Dymola.exe -serverport 8082");
            return;
        }

        var version = await dymola.DymolaVersionAsync();
        Console.WriteLine($"Connected to Dymola version: {version}");

        // Simulate a model from the Modelica Standard Library
        var modelName = "Modelica.Mechanics.Rotational.Examples.CoupledClutches";
        Console.WriteLine($"Simulating {modelName}...");

        var success = await dymola.SimulateModelAsync(
            problem: modelName,
            startTime: 0.0,
            stopTime: 1.5,
            numberOfIntervals: 500,
            method: "Dassl",
            tolerance: 0.0001
        );

        if (success)
        {
            Console.WriteLine("Simulation completed successfully!");
        }
        else
        {
            var error = await dymola.GetLastErrorAsync();
            Console.WriteLine($"Simulation failed: {error}");
        }
    }

    /// <summary>
    /// Example 2: Start Dymola programmatically and check a model.
    /// </summary>
    public static async Task StartDymolaAndCheckModel(string dymolaPath)
    {
        using var dymola = new DymolaInterface(dymolaPath, portNumber: 8082);

        Console.WriteLine("Starting Dymola...");
        await dymola.StartDymolaProcessAsync();

        var versionInfo = await dymola.DymolaVersionAsync();
        Console.WriteLine($"Dymola started. Version: {versionInfo}");

        // Check a model without simulating
        var modelName = "Modelica.Electrical.Analog.Examples.ChuaCircuit";
        Console.WriteLine($"Checking {modelName}...");

        var success = await dymola.CheckModelAsync(modelName, simulate: false);

        if (success)
        {
            Console.WriteLine("Model check passed!");
        }
        else
        {
            var error = await dymola.GetLastErrorAsync();
            Console.WriteLine($"Model check failed: {error}");
        }

        // Clean shutdown
        await dymola.ExitAsync();
    }

    /// <summary>
    /// Example 3: Open a custom library and simulate a model from it.
    /// </summary>
    public static async Task OpenLibraryAndSimulate(string libraryPath, string modelName)
    {
        using var dymola = new DymolaInterface();

        if (dymola.IsOfflineMode())
        {
            Console.WriteLine("Dymola is not running.");
            return;
        }

        // Open the library
        Console.WriteLine($"Opening library: {libraryPath}");
        var opened = await dymola.OpenModelAsync(libraryPath);

        if (!opened)
        {
            Console.WriteLine("Failed to open library.");
            return;
        }

        Console.WriteLine($"Library opened successfully.");

        // Simulate the model
        Console.WriteLine($"Simulating {modelName}...");
        var success = await dymola.SimulateModelAsync(
            problem: modelName,
            stopTime: 10.0
        );

        if (success)
        {
            Console.WriteLine("Simulation successful!");
        }
        else
        {
            var error = await dymola.GetLastErrorAsync();
            Console.WriteLine($"Simulation failed:\n{error}");
        }
    }

    /// <summary>
    /// Example 4: Translate a model and export plots.
    /// </summary>
    public static async Task TranslateAndExportPlot()
    {
        using var dymola = new DymolaInterface();

        if (dymola.IsOfflineMode())
        {
            Console.WriteLine("Dymola is not running.");
            return;
        }

        var modelName = "Modelica.Blocks.Examples.PID_Controller";

        // Translate (compile) the model without simulating
        Console.WriteLine($"Translating {modelName}...");
        var translated = await dymola.TranslateModelAsync(modelName);

        if (!translated)
        {
            Console.WriteLine("Translation failed.");
            var error = await dymola.GetLastErrorAsync();
            Console.WriteLine(error);
            return;
        }

        Console.WriteLine("Translation successful!");

        // Now simulate
        Console.WriteLine("Simulating...");
        var simulated = await dymola.SimulateModelAsync(modelName);
    }

    /// <summary>
    /// Example 5: Execute custom Dymola commands.
    /// </summary>
    public static async Task ExecuteCustomCommands()
    {
        using var dymola = new DymolaInterface();

        if (dymola.IsOfflineMode())
        {
            Console.WriteLine("Dymola is not running.");
            return;
        }

        // Execute arbitrary Dymola commands
        Console.WriteLine("Setting advanced solver options...");
        await dymola.ExecuteCommandAsync("Advanced.Define.DAEsolver = true");

        // Set variables
        await dymola.SetVariableAsync("simulationStopTime", 100.0);

        // Change directory
        await dymola.CdAsync(@"C:\Projects\Modelica");

        // Add library paths
        await dymola.AddModelicaPathAsync(@"C:\Libraries\CustomLib");

        Console.WriteLine("Custom commands executed successfully.");
    }

    /// <summary>
    /// Example 6: Error handling and logging.
    /// </summary>
    public static async Task ErrorHandlingExample()
    {
        using var dymola = new DymolaInterface();

        if (dymola.IsOfflineMode())
        {
            Console.WriteLine("Dymola is not running.");
            return;
        }

        // Try to simulate an invalid model
        var modelName = "Invalid.Model.Name";
        Console.WriteLine($"Attempting to simulate {modelName}...");

        try
        {
            var success = await dymola.SimulateModelAsync(modelName);

            if (!success)
            {
                // Get detailed error information
                Console.WriteLine("\n=== Error Information ===");

                var errorMessage = await dymola.GetLastErrorAsync();
                Console.WriteLine($"Error message: {errorMessage}");

                var errorLog = await dymola.GetLastErrorAsync();
                Console.WriteLine($"\nFull error log:\n{errorLog}");

                // Save the log to a file
                await dymola.SaveLogAsync(@"C:\temp\dymola_error.log");
                Console.WriteLine("\nLog saved to: C:\\temp\\dymola_error.log");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Example 7: Batch simulation of multiple models.
    /// </summary>
    public static async Task BatchSimulationExample()
    {
        using var dymola = new DymolaInterface();

        if (dymola.IsOfflineMode())
        {
            Console.WriteLine("Dymola is not running.");
            return;
        }

        var models = new[]
        {
            "Modelica.Electrical.Analog.Examples.ChuaCircuit",
            "Modelica.Mechanics.Rotational.Examples.First",
            "Modelica.Thermal.HeatTransfer.Examples.Motor"
        };

        var results = new List<(string Model, bool Success, string ResultFile)>();

        foreach (var model in models)
        {
            Console.WriteLine($"\nSimulating {model}...");

            var success = await dymola.SimulateModelAsync(
                problem: model,
                stopTime: 5.0,
                resultFile: $"result_{Path.GetFileName(model)}"
            );

            if (success)
            {
                Console.WriteLine($"  ✓ Success!");
            }
            else
            {
                var error = await dymola.GetLastErrorAsync();
                Console.WriteLine($"  ✗ Failed: {error}");
            }

            results.Add((model, success, $"result_{Path.GetFileName(model)}"));

            // Clear between simulations
            await dymola.ClearAsync(fast: true);
        }

        // Summary
        Console.WriteLine("\n=== Simulation Summary ===");
        var successCount = results.Count(r => r.Success);
        Console.WriteLine($"Successful: {successCount}/{results.Count}");

        foreach (var (model, success, resultFile) in results)
        {
            var status = success ? "✓" : "✗";
            Console.WriteLine($"{status} {model}");
        }
    }
}
