using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MLQT.Services.Interfaces;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Suppressing rules for a class and everything nested in it from the Code Review toolbar.
/// </summary>
/// <remarks>
/// <para>The case it was built for: a Dymola <c>_fmu</c> import model whose findings are in the
/// classes nested in it. The row action waives one finding's rule on one class; the rules worth
/// waiving on the model were never in the table while the model was selected. The dialog lists them
/// from the nested classes, and a class-level waiver reaches every class below it.</para>
///
/// <para>A journey because what matters is the chain the unit tests each hold one link of: the
/// button opens the dialog for the selected class, the choice is written into the right file, and
/// the rows it waived leave the table.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class SuppressRulesJourney(TestHostFixture host) : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-journey-" + Guid.NewGuid().ToString("N"));

    private string LibraryPath => Path.Combine(_root, "Lib");

    private string ModelFile => Path.Combine(LibraryPath, "Fmu.mo");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(LibraryPath);
        File.WriteAllText(Path.Combine(LibraryPath, "package.mo"), "within;\npackage Lib\nend Lib;\n");
        File.WriteAllText(Path.Combine(LibraryPath, "package.order"), "Fmu\n");
        File.WriteAllText(ModelFile, """
            within Lib;
            model Fmu "Generated"
              record R1
                Real x;
              end R1;
              record R2
                Real y;
              end R2;
              parameter Real p = 1;
            end Fmu;

            """);

        await host.ResetLibrariesAsync();
        await host.Services.GetRequiredService<ILibraryDataService>().AddLibraryFromDirectoryAsync(LibraryPath);

        var review = host.Services.GetRequiredService<ICodeReviewService>();
        review.ClearLogMessages();
        review.AddLogMessages([
            new LogMessage("Lib.Fmu", "Style warning", 9, "Open the model") { RuleId = RuleIds.ParameterDescription },
            new LogMessage("Lib.Fmu.R1", "Style warning", 1, "Nested finding one") { RuleId = RuleIds.ClassDescription },
            new LogMessage("Lib.Fmu.R2", "Style warning", 1, "Nested finding two") { RuleId = RuleIds.ClassDescription },
        ]);
        await host.WaitForIdleAsync();
    }

    public async ValueTask DisposeAsync()
    {
        host.Services.GetRequiredService<ICodeReviewService>().ClearLogMessages();
        await host.ResetLibrariesAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* the temp directory will be swept */ }
    }

    [Fact]
    public async Task ARuleFoundOnlyInNestedClasses_IsSuppressedOnTheModel_AndItsRowsGo()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-tab").Nth(4).WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        await ShellReadiness.WaitUntilClickableAsync(page);
        await page.Locator(".mud-tab").Nth(0).ClickAsync();

        var rows = page.Locator(".mlqt-findings-pane tbody tr");
        await rows.Filter(new LocatorFilterOptions { HasTextString = "Open the model" }).First.ClickAsync();

        var button = page.Locator(".mlqt-suppress-rules-button");
        await Assertions.Expect(button).ToBeEnabledAsync(new() { Timeout = 20_000 });
        await button.ClickAsync();

        // The nested classes' rule is offered, counted, though the model itself has no such finding.
        var rule = page.Locator(".mlqt-suppress-rule").Filter(new LocatorFilterOptions { HasTextString = RuleIds.ClassDescription });
        await Assertions.Expect(rule).ToContainTextAsync("2 findings", new() { Timeout = 10_000 });

        await rule.Locator("input").CheckAsync();
        await page.Locator(".mlqt-suppress-save").ClickAsync();

        // Written into the model's own file, which reaches both records...
        await Assertions.Expect(rows.Filter(new LocatorFilterOptions { HasTextString = "Nested finding" }))
                        .ToHaveCountAsync(0, new() { Timeout = 10_000 });
        Assert.Contains($"__MLQT(suppress=\"{RuleIds.ClassDescription}\")", File.ReadAllText(ModelFile));

        // ...and nothing else: the model's own finding for another rule is still there.
        await Assertions.Expect(rows.Filter(new LocatorFilterOptions { HasTextString = "Open the model" })).ToHaveCountAsync(1);
    }
}
