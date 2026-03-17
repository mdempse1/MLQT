# Test Resources

This folder contains Modelica model files used for testing complex scenarios in the ModelicaParser.

## Files

- **EquilibriumDrumBoiler.mo** - Complex example model for testing parser capabilities

## Usage

Test files in this folder are automatically copied to the output directory during build.

Access them in tests using:
```csharp
var testModel = File.ReadAllText(GetResourcePath("EquilibriumDrumBoiler.mo"));
```

The `GetResourcePath()` method in `ComplexModelTests.cs` handles finding the file regardless of the test execution context.
