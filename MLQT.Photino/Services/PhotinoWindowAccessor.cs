using Photino.NET;

namespace MLQT.Photino.Services;

/// <summary>
/// Holds the application's window so services registered before it exists can reach it afterwards.
/// </summary>
/// <remarks>
/// Photino builds the window during <c>Build()</c>, after the service collection is closed, so the
/// file picker cannot take a <see cref="PhotinoWindow"/> in its constructor. One mutable holder is
/// less confusing than a factory that resolves a window nobody has made yet.
/// </remarks>
internal sealed class PhotinoWindowAccessor
{
    public PhotinoWindow? Window { get; set; }
}
