---
name: nuget-packages
description: "Every NuGet package the solution uses, with its purpose and licence. Use when adding, removing or upgrading a package."
---

# NuGet Packages Skill

This skill documents all NuGet packages used in the MLQT solution.

## Package Summary

All packages use open-source licenses: permissive (MIT, BSD, Apache 2.0) apart from NetMQ, which
is LGPL-3.0, and WeCantSpell.Hunspell, which carries Hunspell's MPL/GPL/LGPL tri-licence. Both are
consumed unmodified as NuGet-delivered libraries.

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
- **Note**: referenced with `PrivateAssets="all"` - a build-time tool, it ships nothing

### WeCantSpell.Hunspell (v7.0.1)
- **Purpose**: Managed Hunspell implementation behind `SpellChecker` (the spelling style rules and
  the Code Review spelling workflow; see the `spell-checking` skill)
- **Used in**: ModelicaParser
- **License**: MPL 1.1 / GPL 2.0 / LGPL 2.1 tri-licence, inherited from Hunspell (`license.txt` in the package); used under MPL 1.1, unmodified
- **NuGet**: https://www.nuget.org/packages/WeCantSpell.Hunspell

## Version Control Integration

### LibGit2Sharp (v0.31.0)
- **Purpose**: .NET wrapper for libgit2 - enables Git repository operations
- **Used in**: RevisionControl, MLQT.Journeys (builds the Git working copies the journeys drive)
- **License**: [MIT](https://github.com/libgit2/libgit2sharp/blob/master/LICENSE.md)
- **NuGet**: https://www.nuget.org/packages/LibGit2Sharp

### SharpSvn (v1.14005.390) — test-only
- **Purpose**: .NET wrapper for Subversion (SVN). **No longer used by the shipped product** — the SVN implementation talks to the `svn` CLI directly (see `RevisionControl/SvnCli.cs`). Retained only as a test dependency to set up and validate repository state in the integration tests.
- **Used in**: RevisionControl.Tests (test project only)
- **License**: [Apache 2.0](https://sharpsvn.open.collab.net/)
- **NuGet**: https://www.nuget.org/packages/SharpSvn

### SlikSVN command-line client (bundled, not a NuGet package)
- **Purpose**: `svn.exe` used for **all** SVN operations (much faster than the previously-used SharpSvn on large libraries). Resolved at runtime by `RevisionControl/SvnToolLocator.cs`.
- **Used in**: bundled into the MLQT app output under `svn/` on Windows; staged by `build/fetch-svn-tools.ps1` into `svn-tools/win-x64` at the repository root (not committed to source control). On Linux the `.deb` declares `subversion` rather than bundling it.
- **License**: Apache 2.0 (SlikSVN is a distribution of [Apache Subversion](https://subversion.apache.org/)). Redistribution requires retaining the Apache license/NOTICE; keep these with the bundled binaries.

## UI Framework

### MudBlazor (v9.11.0)
- **Purpose**: Blazor component library for Material Design UI
- **Used in**: MLQT.UI.Theme (MLQT.Shared and MLQT.McpTester take it from there)
- **License**: [MIT](https://github.com/MudBlazor/MudBlazor/blob/dev/LICENSE)
- **Upgrading it is a change to the theme**: MLQT.UI.Theme is the only project in the repository
  that references it, because its stylesheets override MudBlazor's rules by name and a consumer of
  the theme must get the version they were written against. Regenerate the documentation
  screenshots after an upgrade (the `user-documentation` skill) and compare them
- **NuGet**: https://www.nuget.org/packages/MudBlazor
- **Components used**: MudTreeView, MudChipSet, MudTable, MudDialog, MudAlert, MudSplitPanel, etc.
- **Note**: 9.11.0 is the first version with `MudSplitPanel`, which is why it is the floor. It
  replaced MudBlazor.Extensions' `MudExSplitPanel`, the only thing that package was used for, and
  removing it took `SixLabors.ImageSharp` 3.1.12 out of the tree: five advisories (NU1902/NU1903)
  with no 3.x fix, and an ImageSharp 4 that checks for a licence key at build time

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

### Microsoft.AspNetCore.Components.Web (v10.0.9)
- **Purpose**: Blazor components for web applications
- **Used in**: MLQT.Shared
- **License**: [MIT](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)
- **NuGet**: https://www.nuget.org/packages/Microsoft.AspNetCore.Components.Web

## Dependency Injection

### Microsoft.Extensions.DependencyInjection (v10.0.9)
- **Purpose**: Dependency injection abstractions and container for .NET
- **Used in**: DymolaInterface, OpenModelicaInterface, DymolaInterface.Tests
- **License**: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection

## Logging

### NLog (v6.1.3)
- **Purpose**: Flexible logging framework for .NET
- **Used in**: RevisionControl, MLQT.Services, MLQT.Shared
- **License**: [BSD 3-Clause](https://github.com/NLog/NLog/blob/master/LICENSE.txt)
- **NuGet**: https://www.nuget.org/packages/NLog

## Messaging

### NetMQ (v4.0.4.2)
- **Purpose**: .NET port of ZeroMQ messaging library
- **Used in**: OpenModelicaInterface
- **License**: [LGPL-3.0](https://github.com/zeromq/netmq/blob/master/COPYING.LESSER)
- **NuGet**: https://www.nuget.org/packages/NetMQ
- **Note**: Used for REQ-REP communication with OpenModelica Compiler

### System.Security.Cryptography.Xml (v10.0.13)
- **Purpose**: Not used directly - a security pin. NetMQ (through `System.ServiceModel.Primitives`)
  pulls a vulnerable version transitively; 10.0.13 is the first fixing its denial-of-service
  advisories, and it brings a patched `System.Security.Cryptography.Pkcs` with it
- **Used in**: OpenModelicaInterface, the single upstream place, so the fix reaches every project
  built on it here and outside this repository (the csproj comment lists the advisories)
- **License**: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/System.Security.Cryptography.Xml

## MCP

### ModelContextProtocol (v1.4.1)
- **Purpose**: The official C# MCP SDK - the stdio server and tool attributes in MLQT.McpServer,
  and the client MLQT.McpTester drives a server with
- **Used in**: MLQT.McpServer, MLQT.McpTester
- **License**: [Apache 2.0](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/ModelContextProtocol

### Microsoft.Extensions.Hosting (v10.0.7)
- **Purpose**: The generic host (`Host.CreateApplicationBuilder`) the MCP server runs in. Once came
  through the `Microsoft.AspNetCore.App` framework reference, dropped when MLQT.Services became UI-free
- **Used in**: MLQT.McpServer
- **License**: [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Extensions.Hosting

## Graphics

### Svg.Skia (v5.2.3)
- **Purpose**: Rasterises an SVG to a bitmap. Behind `mlqt_get_diagram_image`, which draws a class's
  diagram and returns it as a PNG so an agent can look at a layout rather than read its coordinates
  back (B196)
- **Used in**: MLQT.McpServer
- **License**: [MIT](https://github.com/wieslawsoltes/Svg.Skia/blob/master/LICENSE.TXT)
- **NuGet**: https://www.nuget.org/packages/Svg.Skia
- **Note**: Pulls **SkiaSharp**, whose drawing is done by a **native** library published per runtime
  identifier. SkiaSharp ships the Windows and macOS assets itself; the Linux one is referenced
  explicitly below because MLQT ships Linux and would otherwise publish a tree that answers every
  other tool and fails the moment a diagram is asked for. `build/publish-tools.sh` checks the file is
  there for the runtime being published

### SkiaSharp.NativeAssets.Linux (v4.148.0)
- **Purpose**: The `libSkiaSharp.so` for `linux-x64`. Its version must match the SkiaSharp that
  Svg.Skia resolves, not be picked independently
- **Used in**: MLQT.McpServer
- **License**: [MIT](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)
- **NuGet**: https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux
- **Note**: The default (not `.NoDependencies`) build, which uses the system fontconfig — MLQT's
  Linux target is a desktop `.deb` whose WebKitGTK dependency guarantees it, and the alternative
  enumerates no fonts, so diagram text would come out blank

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
- **NuGet**: https://www.nuget.org/packages/coverlet.MTP

### Moq (v4.20.72)
- **Purpose**: Mocking framework for unit tests
- **Used in**: DymolaInterface.Tests, OpenModelicaInterface.Tests, MLQT.Services.Tests, MLQT.Shared.Tests, MLQT.McpServer.Tests
- **License**: [BSD 3-Clause](https://github.com/moq/moq4/blob/main/License.txt)
- **NuGet**: https://www.nuget.org/packages/Moq

### Microsoft.Playwright (v1.56.0)
- **Purpose**: Drives a real browser against `MLQT.TestHost` for the journey tests and the
  generated documentation screenshots (see the `gui-testing` and `user-documentation` skills)
- **Used in**: MLQT.Journeys
- **License**: [MIT](https://github.com/microsoft/playwright-dotnet/blob/main/LICENSE)
- **NuGet**: https://www.nuget.org/packages/Microsoft.Playwright

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

### Microsoft.CodeAnalysis.PublicApiAnalyzers (v5.6.0)
- **Purpose**: Holds a project's public surface to `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`, so a change to it is a reviewed diff (RS0016 for an unlisted addition, RS0017 for a listed symbol that is gone)
- **Used in**: ModelicaParser, ModelicaGraph, RevisionControl, DymolaInterface, OpenModelicaInterface, MLQT.Services, the assemblies built from source outside this repository. Referenced once in `Directory.Build.props`, for any project setting `<TrackPublicApi>true</TrackPublicApi>`
- **Development dependency** (`PrivateAssets="all"`): an analyzer only, it ships nothing
- **License**: [MIT](https://github.com/dotnet/roslyn/blob/main/License.txt)
- **NuGet**: https://www.nuget.org/packages/Microsoft.CodeAnalysis.PublicApiAnalyzers
- **Workflow**: `CODING_GUIDELINES.md` §Public API of the Assemblies Others Build On

## Adding New Packages

1. Add package reference to appropriate .csproj file
2. Update this skill file with package details
3. Verify license is permissive (MIT, BSD, Apache 2.0 preferred)
4. Note any special considerations (native dependencies, platform restrictions)

## Package Locations by Project

| Project | Key Packages |
|---------|--------------|
| ModelicaParser | Antlr4.Runtime.Standard, Antlr4BuildTasks, WeCantSpell.Hunspell, PublicApiAnalyzers |
| ModelicaGraph | PublicApiAnalyzers _(project references to ModelicaParser and RevisionControl)_ |
| RevisionControl | LibGit2Sharp, NLog, PublicApiAnalyzers _(SVN via bundled svn CLI)_ |
| DymolaInterface | Microsoft.Extensions.DependencyInjection, PublicApiAnalyzers |
| OpenModelicaInterface | NetMQ, Microsoft.Extensions.DependencyInjection, System.Security.Cryptography.Xml, PublicApiAnalyzers |
| MLQT.Services | NLog, PublicApiAnalyzers _(UI-free: no MudBlazor)_ |
| MLQT.UI.Theme | MudBlazor, Microsoft.AspNetCore.Components.Web, PublicApiAnalyzers |
| MLQT.Shared | NLog, Microsoft.AspNetCore.Components.Web _(MudBlazor through MLQT.UI.Theme)_ |
| MLQT.Photino | Photino.Blazor, Microsoft.AspNetCore.Components.WebView |
| MLQT.Cli, MLQT.TestHost | none of their own _(project references only)_ |
| MLQT.McpServer | ModelContextProtocol, Microsoft.Extensions.Hosting, Svg.Skia, SkiaSharp.NativeAssets.Linux |
| MLQT.McpTester | Photino.Blazor, Microsoft.AspNetCore.Components.WebView, ModelContextProtocol, Microsoft.Extensions.Logging.Debug _(MudBlazor through MLQT.UI.Theme)_ |
| Test Projects | xunit.v3, coverlet.MTP, Microsoft.Testing.Extensions.TrxReport (Moq in DymolaInterface/OpenModelicaInterface/Services/Shared/McpServer tests; MLQT.Shared.Tests also bunit; RevisionControl.Tests also SharpSvn; DymolaInterface.Tests also DependencyInjection; MLQT.Journeys also Microsoft.Playwright and LibGit2Sharp) |
