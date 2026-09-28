using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Shared.Components;
using MudBlazor;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// An application-driven dialog closes when it is told to, even when that is before it has finished
/// opening - the Format All journey's "Formatting all files" dialog that stayed up on CI (B414).
/// </summary>
/// <remarks>
/// <para>An inline MudDialog opens through <see cref="IDialogService.ShowAsync{T}(string, DialogParameters, DialogOptions)"/>,
/// which in a browser waits for a round trip and here, headless, does not. The dialog service is
/// therefore wrapped so a test can hold the first show at the door, withdraw the dialog while it is
/// held, and then let it finish - the order of events CI met by chance.</para>
///
/// <para>The control is the plain <c>&lt;MudDialog @bind-Visible&gt;</c> MainLayout used, under the
/// same sequence: it writes <c>true</c> back into the flag and stays open. If MudBlazor ever fixes
/// that, the control fails, and <see cref="ProgressDialog"/> is no longer needed.</para>
/// </remarks>
public class ProgressDialogTests : MlqtComponentTestBase
{
    private const string Title = "Formatting all files";

    private readonly HeldDialogService _held;
    private readonly IRenderedComponent<MudDialogProvider> _provider;

    public ProgressDialogTests()
    {
        _held = HeldDialogService.Wrap(new DialogService());
        Services.AddSingleton<IDialogService>((IDialogService)(object)_held);
        _provider = Render<MudDialogProvider>();
    }

    private static RenderFragment TitleFragment => b => b.AddContent(0, Title);

    [Fact]
    public async Task ADialogWithdrawnBeforeItHasOpened_ClosesOnceItHas()
    {
        var dialog = Render<ProgressDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.TitleContent, TitleFragment));
        await _held.Arrived.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);

        // The work ended before the dialog had opened.
        dialog.Render(p => p.Add(x => x.Visible, false));
        await _held.ReleaseAsync(_provider);

        _provider.WaitForAssertion(() => Assert.DoesNotContain(Title, _provider.Markup));
    }

    [Fact]
    public async Task ADialogWithdrawnAndAskedForAgainBeforeItHasOpened_StaysOpen()
    {
        var dialog = Render<ProgressDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.TitleContent, TitleFragment));
        await _held.Arrived.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);

        dialog.Render(p => p.Add(x => x.Visible, false));
        dialog.Render(p => p.Add(x => x.Visible, true));
        await _held.ReleaseAsync(_provider);

        _provider.WaitForAssertion(() => Assert.Contains(Title, _provider.Markup));
    }

    [Fact]
    public async Task ADialogThatHasOpened_ClosesWhenWithdrawn()
    {
        var dialog = Render<ProgressDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.TitleContent, TitleFragment));
        await _held.ReleaseAsync(_provider);
        _provider.WaitForAssertion(() => Assert.Contains(Title, _provider.Markup));

        dialog.Render(p => p.Add(x => x.Visible, false));

        _provider.WaitForAssertion(() => Assert.DoesNotContain(Title, _provider.Markup));
    }

    [Fact]
    public async Task Control_ABoundMudDialogWithdrawnBeforeItHasOpened_StaysOpenAndSetsItsFlagAgain()
    {
        var flag = true;
        var dialog = Render<MudDialog>(p => p
            .Add(x => x.Visible, flag)
            .Add(x => x.VisibleChanged, (bool v) => flag = v)
            .Add(x => x.TitleContent, TitleFragment));
        await _held.Arrived.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);

        flag = false;
        dialog.Render(p => p.Add(x => x.Visible, flag));
        await _held.ReleaseAsync(_provider);

        _provider.WaitForAssertion(() => Assert.Contains(Title, _provider.Markup));
        Assert.True(flag);
    }

    /// <summary>
    /// The real dialog service, with the first <c>ShowAsync</c> held until the test lets it go.
    /// </summary>
    public class HeldDialogService : DispatchProxy
    {
        private IDialogService _inner = null!;
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _held;

        public Task Arrived => _arrived.Task;

        public static HeldDialogService Wrap(IDialogService inner)
        {
            var proxy = Create<IDialogService, HeldDialogService>();
            var held = (HeldDialogService)(object)proxy;
            held._inner = inner;
            return held;
        }

        /// <summary>Lets the held show go on, and waits until it has returned.</summary>
        public async Task ReleaseAsync(IRenderedComponent<MudDialogProvider> provider)
        {
            await provider.InvokeAsync(() => _released.TrySetResult());
            await _completed.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IDialogService.ShowAsync)
                && targetMethod.ReturnType == typeof(Task<IDialogReference>)
                && Interlocked.Exchange(ref _held, 1) == 0)
            {
                return HoldAsync(targetMethod, args);
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        private async Task<IDialogReference> HoldAsync(MethodInfo targetMethod, object?[]? args)
        {
            _arrived.TrySetResult();
            await _released.Task;
            try
            {
                return await (Task<IDialogReference>)targetMethod.Invoke(_inner, args)!;
            }
            finally
            {
                _completed.TrySetResult();
            }
        }
    }
}
