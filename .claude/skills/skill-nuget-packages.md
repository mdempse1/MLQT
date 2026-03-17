# NuGet Packages Skill

This skill documents all NuGet packages used in the MLQT solution.

## Package Summary

All packages use permissive open-source licenses (MIT, BSD, Apache 2.0).

## Parsing and Language Processing

### Antlr4.Runtime.Standard (v4.13.1)
- **Purpose**: ANTLR runtime library for parsing Modelica grammar
- **Used in**: ModelicaParser
- **License**: [BSD 3-Clause](https://github.com/antlr/antlr4/blob/master/LICENSE.txt)
- **NuGet**: https://www.nuget.org/packages/Antlr4.Runtime.Standard

### Antlr4BuildTasks (v12.14.0)
- **Purpose**: MSBuild tasks for generating parser code from ANTLR grammar files
- **Used in**: ModelicaParser
- **License**: [BSD 3-Clause](https://github.com/kaby76/Antlr4BuildTasks/blob/master/LICENSE)
- **NuGet**: https://www.nuget.org/packages/Antlr4BuildTasks

## Version Control Integration

### LibGit2Sharp (v0.31.0)
- **Purpose**: .NET wrapper for libgit2 - enables Git repository operations
- **Used in**: RevisionControl
- **License**: [MIT](https://github.com/libgit2/libgit2sharp/blob/master/LICENSE.md)
- **NuGet**: https://www.nuget.org/packages/LibGit2Sharp

### SharpSvn (v1.14005.390)
- **Purpose**: .NET wrapper for Subversion (SVN) - enables SVN repository operations
- **Used in**: RevisionControl
- **License**: [Apache 2.0](https://sharpsvn.open.collab.net/)
- **NuGet**: https://www.nuget.org/packages/SharpSvn.1.14-x64

## UI Framework

### MudBlazor (v9.0.0)
- **Purpose**: Blazor component library for Material Design UI
- **Used in**: MLQT.Shared
- **License**: [MIT](https://github.com/MudBlazor/MudBlazor/blob/dev/LICENSE)
- **NuGet**: https://www.nuget.org/packages/MudBlazor
- **Components used**: MudTreeView, MudChipSet, MudTable, MudDialog, MudAlert, etc.

### MudBlazor.Extensions (v8.15.1)
- **Purpose**: Extended components and utilities for MudBlazor
- **Used in**: MLQT.Shared
- **License**: [MIT](https://github.com/fgilde/MudBlazor.Extensions)
- **NuGet**: https://www.nuget.org/packages/MudBlazor.Extensions

## .NET MAUI

### Microsoft.Maui.Controls
- **Purpose**: Core controls for .NET MAUI cross-platform applications
- **Used in**: MLQT (MAUI project)
- **License**: [MIT](https://github.com/dotnet/maui/blob/main/LICENSE)
- **Version**: `$(MauiVersion)` variable for synchronization

### Microsoft.AspNetCore.Components.WebView.Maui
- **Purpose**: Enables hosting Blazor components in MAUI applications
- **Used in**: MLQT (MAUI project)
- **License**: [MIT](https://github.com/dotnet/maui/blob/main/LICENSE)
- **Version**: `$(MauiVersion)` variable for synchronization

## ASP.NET Core

### Microsoft.AspNetCore.Components.Web (v10.0.3)
- **Purpose**: Blazor components for web applications
- **Used in**: MLQT.Shared
- **License**: [MIT](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)
- **NuGet**: https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Web

## Dependency Injection

### Microsoft.Extensions.DependencyInjection (v10.0.3)
- **Purpose**: Dependency injection abstractions and container for .NET
- **Used in**: DymolaInterface
- **License**: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection

## Logging

### NLog (v6.1.0)
- **Purpose**: Flexible logging framework for .NET
- **Used in**: RevisionControl, MLQT.Services, MLQT.Shared
- **License**: [BSD 3-Clause](https://github.com/NLog/NLog/blob/master/LICENSE.txt)
- **NuGet**: https://www.nuget.org/packages/NLog

## Messaging

### NetMQ (v4.0.2.2)
- **Purpose**: .NET port of ZeroMQ messaging library
- **Used in**: OpenModelicaInterface
- **License**: [LGPL-3.0](https://github.com/zeromq/netmq/blob/master/LICENSE)
- **NuGet**: https://www.nuget.org/packages/NetMQ
- **Note**: Used for REQ-REP communication with OpenModelica Compiler

## Testing

### xunit (v2.9.3)
- **Purpose**: Core xUnit testing framework
- **Used in**: All test projects
- **License**: [Apache 2.0](https://github.com/xunit/xunit/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/xunit

### xunit.runner.visualstudio (v3.1.5)
- **Purpose**: Visual Studio test runner for xUnit
- **Used in**: All test projects
- **License**: [Apache 2.0](https://github.com/xunit/visualstudio.xunit/blob/main/License.txt)
- **NuGet**: https://www.nuget.org/packages/xunit.runner.visualstudio

### Microsoft.NET.Test.Sdk (v18.3.0) for running tests
- **Used in**: All test projects
- **License**: [MIT](https://github.com/microsoft/vstest/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/Microsoft.NET.Test.Sdk

### coverlet.collector (v8.0.0)
- **Purpose**: Code coverage collector for .NET
- **Used in**: All test projects
- **License**: [MIT](https://github.com/coverlet-coverage/coverlet/blob/master/LICENSE)
- **NuGet**: https://www.nuget.org/packages/coverlet.collector

### Moq (v4.20.72)
- **Purpose**: Mocking framework for unit tests
- **Used in**: ModelicaComparer.Tests
- **License**: [BSD 3-Clause](https://github.com/moq/moq4/blob/main/License.txt)
- **NuGet**: https://www.nuget.org/packages/Moq

## Logging and Diagnostics

### Microsoft.Extensions.Logging.Debug (v10.0.3)
- **Purpose**: Debug output provider for Microsoft.Extensions.Logging
- **Used in**: MLQT (MAUI project)
- **License**: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.Logging.Debug

## Version Management

### MAUI Version Synchronization

MAUI packages use `$(MauiVersion)` variable defined in project files to ensure version consistency:

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="$(MauiVersion)" />
</ItemGroup>
```

### Development Dependencies

Test packages are marked as development dependencies and don't ship with the application:

```xml
<PackageReference Include="xunit" Version="2.9.2">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

## Adding New Packages

1. Add package reference to appropriate .csproj file
2. Update this skill file with package details
3. Verify license is permissive (MIT, BSD, Apache 2.0 preferred)
4. Note any special considerations (native dependencies, platform restrictions)

## Package Locations by Project

| Project | Key Packages |
|---------|--------------|
| ModelicaParser | Antlr4.Runtime.Standard, Antlr4BuildTasks |
| ModelicaGraph | _(project reference to ModelicaParser only)_ |
| RevisionControl | LibGit2Sharp, SharpSvn, NLog |
| DymolaInterface | Microsoft.Extensions.DependencyInjection |
| OpenModelicaInterface | NetMQ |
| MLQT.Services | MudBlazor, NLog |
| MLQT.Shared | MudBlazor, MudBlazor.Extensions, NLog |
| MLQT | Microsoft.Maui.*, Microsoft.AspNetCore.Components.WebView.Maui |
| Test Projects | xunit, Microsoft.NET.Test.Sdk, coverlet.collector |
