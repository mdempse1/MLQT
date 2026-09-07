using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Shared.Models;
using MudBlazor;
using MudBlazor.Services;

namespace MLQT.Shared.Tests;

/// <summary>
/// A bUnit context preconfigured for MLQT's MudBlazor components: MudBlazor's services, loose JS
/// interop, and a real <see cref="AppState"/>.
///
/// <para><b>Most component tests should not need this.</b> Since phase 7a-1 the logic lives in
/// <c>.razor.cs</c> partial classes, so a test can construct the component, assign its
/// <c>[Inject]</c> and <c>[Parameter]</c> properties and call the handler as a method — no renderer,
/// no provider tree, no interop stand-ins. Use this base class only where the behaviour under test
/// is the render tree itself: lazy-loaded trees, dialog results, parameter reactivity, two-way
/// binding, and the interop call sequence of a wrapper component.</para>
///
/// <para><b>bUnit 2 names, for anyone porting a v1 example:</b> the base type is
/// <c>BunitContext</c> (v1's <c>TestContext</c> collides with xUnit v3's <c>Xunit.TestContext</c>),
/// <c>Render&lt;T&gt;()</c> replaces <c>RenderComponent&lt;T&gt;()</c>, and re-rendering with new
/// parameters is <c>cut.Render(p =&gt; ...)</c> rather than <c>SetParametersAndRender</c>. Raising a
/// component's own callback from a test has to go through <c>cut.InvokeAsync(...)</c>, or the
/// renderer refuses it as off-dispatcher.</para>
///
/// <para>Loose JS interop is safe here because none of MLQT's 17 interop functions is meaningful in
/// a headless renderer — all are async global functions, and there is no <c>IJSInProcessRuntime</c>
/// use anywhere in the project. A test that cares what was called asserts it with
/// <c>JSInterop.VerifyInvoke</c>.</para>
/// </summary>
// BunitContext, not bUnit 1.x's TestContext: xUnit v3 introduced Xunit.TestContext, and the two
// collide in any file with both usings. bUnit 2 renamed its own for exactly that reason.
public abstract class MlqtComponentTestBase : BunitContext
{
    protected MlqtComponentTestBase()
    {
        Services.AddMudServices(options =>
        {
            // MudBlazor otherwise warns on every test that renders a component containing a popover.
            // The provider is rendered explicitly by RenderProviders() where a test needs it.
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        JSInterop.Mode = JSRuntimeMode.Loose;

        // AppState is a concrete class with no dependencies, and its events are the thing under test
        // in most component tests. Register the real one and assert on what it raises.
        Services.AddSingleton<AppState>();
    }

    /// <summary>
    /// The <see cref="AppState"/> the component under test will be given.
    /// </summary>
    protected AppState NavState => Services.GetRequiredService<AppState>();

    /// <summary>
    /// Opens a dialog the way the application does — through <see cref="IDialogService"/>, inside a
    /// rendered <see cref="MudDialogProvider"/> — and hands back both the provider to query and the
    /// reference to await.
    /// </summary>
    /// <remarks>
    /// <para>This is the only way to reach a dialog's result. <c>MudDialog.Close(DialogResult.Ok(x))</c>
    /// goes to the cascaded <c>IMudDialogInstance</c>, which exists only when the provider put it
    /// there; a dialog rendered directly with <c>Render&lt;T&gt;()</c> has none, so every Close and
    /// Cancel in it is a silent no-op and a test asserting on the result waits for ever.</para>
    ///
    /// <para>The show has to go through <c>InvokeAsync</c> for the same reason a callback does: it
    /// renders a component, and the renderer refuses that off its own dispatcher.</para>
    /// </remarks>
    protected async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Reference)>
        ShowDialogAsync<TDialog>(DialogParameters? parameters = null)
        where TDialog : ComponentBase
    {
        var provider = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();

        IDialogReference? reference = null;
        await provider.InvokeAsync(async () =>
            reference = await dialogService.ShowAsync<TDialog>("test", parameters ?? []));

        return (provider, reference!);
    }

    /// <summary>
    /// Renders MudBlazor's provider components. Required before any test that opens a dialog, a
    /// select or menu popover, or asserts on a snackbar — without them the component renders but the
    /// overlay never appears, which reads as "the dialog did not open" rather than as a missing
    /// provider.
    /// </summary>
    protected void RenderProviders()
    {
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }
}
