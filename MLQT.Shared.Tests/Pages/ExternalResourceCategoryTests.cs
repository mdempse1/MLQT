using System.Text.RegularExpressions;
using MLQT.Shared.Pages;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// <see cref="ExternalResources.CategoryOf"/>: which file-type filter a resource falls under.
/// </summary>
public class ExternalResourceCategoryTests
{
    [Theory]
    [InlineData(".mat", "data")]
    [InlineData(".csv", "data")]
    [InlineData(".h5", "data")]
    [InlineData(".c", "ccode")]
    [InlineData(".hpp", "ccode")]
    [InlineData(".dll", "lib")]
    [InlineData(".so", "lib")]
    [InlineData(".png", "images")]
    [InlineData(".svg", "images")]
    [InlineData(".pdf", "documents")]
    [InlineData(".md", "documents")]
    public void KnownExtensions_LandInTheirCategory(string extension, string expected)
    {
        Assert.Equal(expected, ExternalResources.CategoryOf(extension));
    }

    [Theory]
    [InlineData(".MAT")]
    [InlineData(".Png")]
    [InlineData(".DLL")]
    public void ExtensionsAreMatchedRegardlessOfCase(string extension)
    {
        // Windows hands back whatever case the file was created with, and a Modelica annotation can
        // spell it either way.
        Assert.NotEqual("other", ExternalResources.CategoryOf(extension));
    }

    [Theory]
    [InlineData(".mo")]
    [InlineData(".zip")]
    [InlineData(".unheard-of")]
    public void UnknownExtensions_AreOther(string extension)
    {
        Assert.Equal("other", ExternalResources.CategoryOf(extension));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoExtensionAtAll_IsOther(string? extension)
    {
        // A resource with no extension — a directory reference, or a file named "LICENSE" — still
        // has to be reachable through some filter.
        Assert.Equal("other", ExternalResources.CategoryOf(extension));
    }

    [Fact]
    public void EveryCategoryTheClassifierCanReturn_HasAFilterChip()
    {
        // The promise this holds: a category with no chip is a set of the user's own files that
        // nothing can display, however the filters are set. The two lists are written in different
        // files - one in C#, one in markup - so only a test keeps them in step.
        var chips = FileTypeChips().ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(chips);
        Assert.Empty(ExternalResources.AllCategories.Except(chips));
    }

    [Fact]
    public void EveryFilterChip_IsACategoryTheClassifierCanReturn()
    {
        // And the other direction: a chip for a category nothing is ever classified as is a filter
        // that does nothing, which reads as "there are no files of this kind".
        var chips = FileTypeChips();

        Assert.NotEmpty(chips);
        Assert.Empty(chips.Except(ExternalResources.AllCategories));
    }

    [Fact]
    public void AllCategories_MatchesWhatTheClassifierActuallyReturns()
    {
        // Guards the list itself: it is hand-written beside the classifier and would otherwise be
        // one edit away from disagreeing with it.
        string[] samples = [".mat", ".c", ".dll", ".png", ".pdf", ".unheard-of"];

        Assert.Equal(
            ExternalResources.AllCategories.OrderBy(c => c, StringComparer.Ordinal),
            samples.Select(ExternalResources.CategoryOf).Distinct().OrderBy(c => c, StringComparer.Ordinal));
    }

    /// <summary>
    /// The values of the file-type chip set, read from the markup.
    /// </summary>
    /// <remarks>
    /// Scoped to the chip set bound to <c>_selectedFileTypes</c>: the page has a second one beside
    /// it for the warning filters, whose values are not categories and would otherwise be read as
    /// categories the classifier has forgotten.
    /// </remarks>
    private static List<string> FileTypeChips()
    {
        var markup = File.ReadAllText(Path.Combine(SharedDirectory(), "Pages", "ExternalResources.razor"));

        var set = Regex.Match(markup,
            @"<MudChipSet[^>]*SelectedValues=""_selectedFileTypes""[\s\S]*?</MudChipSet>");
        Assert.True(set.Success, "could not find the file-type chip set in ExternalResources.razor");

        return Regex.Matches(set.Value, @"<MudChip[^>]*Value=""@\(""(\w+)""\)""")
                    .Select(m => m.Groups[1].Value)
                    .ToList();
    }

    private static string SharedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared");
            if (File.Exists(Path.Combine(candidate, "_Imports.razor")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("MLQT.Shared sources not found");
    }
}
