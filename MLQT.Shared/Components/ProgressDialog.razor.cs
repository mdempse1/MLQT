using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MLQT.Shared.Components;

/// <summary>
/// An inline dialog whose visibility the application sets - a progress dialog - which closes when it
/// is told to however soon that is after it was told to open.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>&lt;MudDialog @bind-Visible="_running"&gt;</c>.</b> MudBlazor (9.6) opens an
/// inline dialog from its <c>OnAfterRenderAsync</c>, and <c>ShowAsync</c> awaits the dialog's first
/// render in the provider - a round trip to the browser. A <c>Visible = false</c> that arrives in
/// that window finds no dialog reference to close and does nothing; the show then completes, and
/// ends by setting the dialog's own visibility back to <c>true</c> and raising
/// <c>VisibleChanged(true)</c>. Bound, that writes <c>true</c> back into the flag the application had
/// just cleared. The dialog stays up for good, and a progress dialog that cannot be dismissed wedges
/// the window.</para>
///
/// <para>Seen on CI (the Format All journey, B414): Format All over a small library finished before
/// its dialog had opened, and "Formatting all files" stayed on screen under everything that followed.
/// Any work that can finish in less than a browser round trip can do the same - a small library, a
/// fast machine, a slow one drawing the dialog.</para>
///
/// <para>So the flag is passed one way, and a show that completes after it was withdrawn is closed
/// here. Only for dialogs the application drives: one the user opens and closes with a button cannot
/// be withdrawn before it has opened.</para>
/// </remarks>
public partial class ProgressDialog
{
    private MudDialog? _dialog;

    /// <summary>Whether the dialog should be showing. One way: the dialog never writes it back.</summary>
    [Parameter]
    public bool Visible { get; set; }

    [Parameter]
    public RenderFragment? TitleContent { get; set; }

    [Parameter]
    public RenderFragment? DialogContent { get; set; }

    [Parameter]
    public RenderFragment? DialogActions { get; set; }

    [Parameter]
    public DialogOptions? Options { get; set; }

    /// <summary>
    /// The dialog reports it is showing. When it is not wanted any more, that is a show which finished
    /// after it was withdrawn, and it is closed.
    /// </summary>
    internal Task OnDialogVisibleChanged(bool shown)
    {
        if (shown && !Visible)
            _ = CloseWithdrawnShowAsync();
        return Task.CompletedTask;
    }

    private async Task CloseWithdrawnShowAsync()
    {
        // Not from inside the callback: it is raised from within MudDialog.ShowAsync, which goes on to
        // use the reference a close would clear. Yielding queues the close behind the rest of the show
        // on the renderer's dispatcher.
        await Task.Yield();

        // Asked for again in the meantime - leave it open.
        if (!Visible && _dialog is not null)
            await _dialog.CloseAsync();
    }
}
