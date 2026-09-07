// The namespaces a component's markup half already sees through _Imports.razor, made global so the
// code-behind half sees the same set. A .razor.cs does not read _Imports.razor: without this, every
// one of the 31 files converted in phase 7a-1 would open with the same twenty-line using block, and
// the two halves of one class would disagree about what is in scope.
//
// Keep this list in step with _Imports.razor. The entries there that only markup references
// (Microsoft.AspNetCore.Components.Routing, MudBlazor.Extensions.Components*) are deliberately
// absent. Components.Web is NOT one of them despite looking like it: the DOM event-argument types
// live there and appear in handler signatures, which are C#.

global using Microsoft.AspNetCore.Components;
global using Microsoft.AspNetCore.Components.Web;
global using Microsoft.JSInterop;
global using MudBlazor;
global using MLQT.Services;
global using MLQT.Services.Checking;
global using MLQT.Services.DataTypes;
global using MLQT.Services.Helpers;
global using MLQT.Services.Interfaces;
global using MLQT.Shared.Components;
global using MLQT.Shared.Dialogs;
global using MLQT.Shared.Helpers;
global using MLQT.Shared.Models;
global using ModelicaGraph;
global using ModelicaGraph.DataTypes;
global using ModelicaParser;
global using ModelicaParser.DataTypes;
global using ModelicaParser.Helpers;
global using ModelicaParser.Visitors;
