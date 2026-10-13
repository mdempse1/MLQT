using Xunit;

namespace MLQT.Journeys;

/// <summary>The names traces are written under, which the nightly WebKit run uploads on failure.</summary>
public class TraceNameTests
{
    [Fact]
    public void ATheorysDisplayName_BecomesANameEveryPlatformAndTheUploadAccepts()
    {
        // The name that failed the upload step: upload-artifact refuses " : < > | * ? \r \n.
        var name = TestHostFixture.Sanitise(
            "MLQT.Journeys.CytoscapeJourney.EveryLayoutTheUiOffers_Runs(layout: \"breadthfirst\")\r\n<a|b*c?d/e\\f>");

        Assert.Equal("MLQT.Journeys.CytoscapeJourney.EveryLayoutTheUiOffers_Runs(layout_ _breadthfirst_)___a_b_c_d_e_f_", name);
    }

    [Fact]
    public void ALongName_IsCutTo120Characters()
    {
        Assert.Equal(120, TestHostFixture.Sanitise(new string('x', 300)).Length);
    }
}
