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

### SharpSvn (v1.14005.390) — test-only
- **Purpose**: .NET wrapper for Subversion (SVN). **No longer used by the shipped product** — the SVN implementation talks to the `svn` CLI directly (see `RevisionControl/SvnCli.cs`). Retained only as a test dependency to set up and validate repository state in the integration tests.
- **Used in**: RevisionControl.Tests (test project only)
- **License**: [Apache 2.0](https://sharpsvn.open.collab.net/)
- **NuGet**: https://www.nuget.org/packages/SharpSvn.1.14-x64

### SlikSVN command-line client (bundled, not a NuGet package)
- **Purpose**: `svn.exe` used for **all** SVN operations (much faster than the previously-used SharpSvn on large libraries). Resolved at runtime by `RevisionControl/SvnToolLocator.cs`.
- **Used in**: bundled into the MLQT app output under `svn/` on Windows; staged by `build/fetch-svn-tools.ps1` into `svn-tools/win-x64` at the repository root (not committed to source control). On Linux the `.deb` declares `subversion` rather than bundling it.
- **License**: Apache 2.0 (SlikSVN is a distribution of [Apache Subversion](https://subversion.apache.org/)). Redistribution requires retaining the Apache license/NOTICE; keep these with the bundled binaries.

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

## The desktop host

MLQT was a .NET MAUI application until phase 7b-8. The `Microsoft.Maui.*` packages went with it;
these two are what replaced them, and they are the whole of the host's dependency list.

### Photino.Blazor (v4.0.13)
- **Purpose**: Hosts Blazor components in a native webview — WebView2 on Windows, WebKitGTK on Linux
- **Used in**: MLQT.Photino, MLQT.McpTester
- **License**: [Apache 2.0](https://github.com/tryphotino/photino.Blazor/blob/master/LICENSE)
- **NuGet**: https://www.nuget.org/packages/Photino.Blazor
- **Note**: brings `Photino.Native`, which carries the per-platform native library. On Linux that
  links `libwebkit2gtk-4.1`, `libgtk-3` and `libnotify`, none of which arrive with .NET — the `.deb`
  declares them and `Documentation/installation.md` says so

### Microsoft.AspNetCore.Components.WebView (v10.0.9)
- **Purpose**: The webview-hosted Blazor renderer Photino.Blazor builds on
- **Used in**: MLQT.Photino, MLQT.McpTester
- **License**: [MIT](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)
- **NuGet**: https://www.nuget.org/packages/Microsoft.AspNetCore.Components.WebView
- **Note**: referenced explicitly to pin the graph forward onto the 10.0.x line `MLQT.Shared` uses,
  rather than the 9.0.1 that Photino's `net9.0` asset group would bring

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

### xunit.v3 (v4.0.0)
- **Purpose**: Core xUnit testing framework
- **Used in**: All test projects
- **License**: [Apache 2.0](https://github.com/xunit/xunit/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/xunit.v3
- **Note**: v3 runs on **Microsoft.Testing.Platform**, not VSTest. On the .NET 10 SDK the VSTest
  target refuses to run such a project at all, so `xunit.runner.visualstudio` and
  `Microsoft.NET.Test.Sdk` are **not referenced** and `global.json` opts the whole repository into
  the MTP-based `dotnet test`. That opt-in is all-or-nothing: every test project must be on it.
  Test projects are `OutputType=Exe` and can also be run directly as executables.

### bunit (v2.9.0)
- **Purpose**: Blazor component rendering for tests
- **Used in**: MLQT.Shared.Tests
- **License**: [MIT](https://github.com/bUnit-dev/bUnit/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/bunit
- **Note**: v2 requires xUnit v3. Its context type is `BunitContext` (v1's `TestContext` collides
  with xUnit v3's own `Xunit.TestContext`), and `Render<T>()` replaces `RenderComponent<T>()`.

### Microsoft.Testing.Extensions.TrxReport (v2.3.3)
- **Purpose**: TRX result files under Microsoft.Testing.Platform (`--report-trx`), which CI uploads
- **Used in**: All test projects
- **License**: [MIT](https://github.com/microsoft/testfx/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Testing.Extensions.TrxReport
- **Note**: Must match the `Microsoft.Testing.Platform` version `xunit.v3` brings in. A mismatched
  version fails at run time with a `MissingMethodException`, not at restore.

### coverlet.MTP (v10.0.1)
- **Purpose**: Code coverage under Microsoft.Testing.Platform (`--coverlet`)
- **Used in**: All test projects
- **License**: [MIT](https://github.com/coverlet-coverage/coverlet/blob/master/LICENSE)
- **NuGet**: https://www.nuget.org/packages/coverlet.collector

### Moq (v4.20.72)
- **Purpose**: Mocking framework for unit tests
- **Used in**: ModelicaComparer.Tests
- **License**: [BSD 3-Clause](https://github.com/moq/moq4/blob/main/License.txt)
- **NuGet**: https://www.nuget.org/packages/Moq

## Logging and Diagnostics

### Microsoft.Extensions.Logging.Debug (v10.0.0)
- **Purpose**: Debug output provider for Microsoft.Extensions.Logging
- **Used in**: MLQT.McpTester
- **License**: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.Logging.Debug

## Version Management

### Development Dependencies

Test packages are marked as development dependencies and don't ship with the application:

```xml
<PackageReference Include="xunit.v3" Version="4.0.0">
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
| RevisionControl | LibGit2Sharp, NLog _(SVN via bundled svn CLI)_ |
| DymolaInterface | Microsoft.Extensions.DependencyInjection |
| OpenModelicaInterface | NetMQ |
| MLQT.Services | MudBlazor, NLog |
| MLQT.Shared | MudBlazor, MudBlazor.Extensions, NLog |
| MLQT.Photino | Photino.Blazor, Microsoft.AspNetCore.Components.WebView |
| MLQT.McpTester | Photino.Blazor, Microsoft.AspNetCore.Components.WebView, MudBlazor, ModelContextProtocol |
| Test Projects | xunit.v3, coverlet.MTP, Microsoft.Testing.Extensions.TrxReport (MLQT.Shared.Tests also: bunit; RevisionControl.Tests also: SharpSvn) |
