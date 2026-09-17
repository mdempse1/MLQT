using Bunit;
using Microsoft.AspNetCore.Components;
using MLQT.Shared.Components;
using MudBlazor;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="ColorPicker"/>: what it accepts as a colour, and that it round-trips a change back to
/// its parent.
///
/// <para>The binding half is a Layer 1b test — a two-way binding is a conversation between two
/// components, so there is nothing to call directly. It stands in for the same pattern in
/// <c>NamingStyleSelect</c> and <c>RuleSeverityPicker</c>.</para>
/// </summary>
public class ColorPickerTests : MlqtComponentTestBase
{
    [Theory]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    [InlineData("#FFFFFF")]
    [InlineData("#AbCdEf")]
    [InlineData("#1a2B3c")]
    public void IsValidHexColor_AcceptsSixDigitHex_InEitherCase(string value)
    {
        Assert.True(ColorPicker.IsValidHexColor(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#fff")]           // three-digit shorthand: MudColor takes it, this does not
    [InlineData("#ffffffff")]      // eight digits with alpha
    [InlineData("ffffff")]         // no hash
    [InlineData("#12345g")]        // g is not a hex digit
    [InlineData("#12 456")]        // a space is not a hex digit
    [InlineData("red")]            // a colour name
    [InlineData("rgba(0,0,0,1)")]
    public void IsValidHexColor_RejectsAnythingElse(string? value)
    {
        Assert.False(ColorPicker.IsValidHexColor(value));
    }

    [Fact]
    public void AnInvalidValue_IsLeftAloneRatherThanNormalised()
    {
        // A settings file holding "red" keeps "red": the guard means OnParametersSet does not touch
        // _mudColor, so nothing is written back over the user's value. Rendering with it must also
        // not throw, which is the other half of why the guard is there.
        var changes = new List<string>();

        var cut = Render<ColorPicker>(p => p
            .Add(c => c.Value, "red")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, changes.Add)));

        Assert.NotNull(cut.Instance);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task ChangingTheColour_RaisesValueChangedWithSixDigitHex()
    {
        // The round trip a parent depends on: whatever the picker produces has to come back in the
        // form the parent stores and hands back in, or the next render fails the validity guard and
        // the control silently stops tracking.
        var changes = new List<string>();

        var cut = Render<ColorPicker>(p => p
            .Add(c => c.Value, "#000000")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, changes.Add)));

        // Through the component's own dispatcher: raising a callback from the test thread is not
        // allowed to touch component state.
        await cut.InvokeAsync(() => cut.FindComponent<MudColorPicker>().Instance.ValueChanged
           .InvokeAsync(new MudBlazor.Utilities.MudColor(0x12, 0x34, 0x56, 255)));

        var raised = Assert.Single(changes);
        Assert.Equal("#123456", raised);
        Assert.True(ColorPicker.IsValidHexColor(raised),
            "the value handed back must be one the picker would accept on the next render");
    }

    [Fact]
    public async Task TheColourItRaises_IsLowerCaseAndAlwaysSixDigits()
    {
        // Single-digit channels have to be padded, or "#102030" comes back as "#123" shaped text
        // that fails the guard on the way in.
        var changes = new List<string>();

        var cut = Render<ColorPicker>(p => p
            .Add(c => c.Value, "#000000")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, changes.Add)));

        await cut.InvokeAsync(() => cut.FindComponent<MudColorPicker>().Instance.ValueChanged
           .InvokeAsync(new MudBlazor.Utilities.MudColor(0x0a, 0x0b, 0x0c, 255)));

        Assert.Equal("#0a0b0c", Assert.Single(changes));
    }
}
