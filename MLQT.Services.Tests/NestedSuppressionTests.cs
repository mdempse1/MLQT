using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// A class-level <c>__MLQT(suppress=…)</c> on a package waives the rule for every class nested in it,
/// through the pipeline every surface shares.
///
/// <para>The case it exists for is generated code: the Dymola <c>_fmu</c> import models were ~65% of a
/// Claytex check's findings, and before this a waiver had to be written on each of their classes. Each
/// nested class here is a node of its own, whose tree does not hold the package's annotation — so
/// this is the path through <see cref="StyleCheckContext.EnclosingSuppressions"/>, not the one a
/// single class's own directives take.</para>
/// </summary>
public class NestedSuppressionTests
{
    private const string Library = """
        package Lib "Library"
          package Generated "Generated code"
            model A
            end A;

            package Deeper "Deeper"
              model B
              end B;
            end Deeper;
            annotation(__MLQT(suppress="Doc.ClassDescription", reason="generated"));
          end Generated;

          package Written "Hand-written"
            model C
            end C;
          end Written;
        end Lib;
        """;

    private static IReadOnlyList<Finding> Check(bool honorSuppressions = true)
    {
        var data = new LibraryDataService();
        data.AddLibraryFromFileAsync("Lib.mo", Library).GetAwaiter().GetResult();
        var graph = data.CombinedGraph;

        return LibraryCheckSession.Check(
            graph, graph.ModelNodes.ToList(), new StyleCheckingSettings { ClassHasDescription = true },
            new CustomDictionaryService(), new DictionaryManagerService(),
            honorSuppressions: honorSuppressions);
    }

    private static HashSet<string> Described(IEnumerable<Finding> findings) =>
        findings.Where(f => f.RuleId == RuleIds.ClassDescription)
            .Select(f => f.ModelId)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void APackagesWaiver_ReachesEveryClassNestedInIt()
    {
        var reported = Described(Check());

        Assert.DoesNotContain("Lib.Generated.A", reported);
        Assert.DoesNotContain("Lib.Generated.Deeper.B", reported);
    }

    [Fact]
    public void APackagesWaiver_LeavesItsSiblingsAlone()
    {
        Assert.Contains("Lib.Written.C", Described(Check()));
    }

    [Fact]
    public void WithSuppressionsIgnored_TheNestedFindingsAreReported()
    {
        // The audit pass (mlqt check --no-suppress) shows what the waiver is hiding.
        var reported = Described(Check(honorSuppressions: false));

        Assert.Contains("Lib.Generated.A", reported);
        Assert.Contains("Lib.Generated.Deeper.B", reported);
    }
}
