using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class DocumentationExtractorTests
{
    [Fact]
    public void ExtractsInfoAndRevisions()
    {
        const string code = """
            model M
              Real x;
              annotation (Documentation(info="<html><p>What it does</p></html>",
                revisions="<html><p>2026 - created</p></html>"));
            end M;
            """;
        var (info, revisions) = DocumentationExtractor.ExtractFromCode(code);
        Assert.Equal("<html><p>What it does</p></html>", info);
        Assert.Equal("<html><p>2026 - created</p></html>", revisions);
    }

    [Fact]
    public void InfoOnly_RevisionsNull()
    {
        const string code = "model M\n  annotation (Documentation(info=\"<html>hi</html>\"));\nend M;";
        var (info, revisions) = DocumentationExtractor.ExtractFromCode(code);
        Assert.Equal("<html>hi</html>", info);
        Assert.Null(revisions);
    }

    [Fact]
    public void ConcatenatedInfo_IsJoined()
    {
        const string code = "model M\n  annotation (Documentation(info=\"<html>\" + \"body\" + \"</html>\"));\nend M;";
        var (info, _) = DocumentationExtractor.ExtractFromCode(code);
        Assert.Equal("<html>body</html>", info);
    }

    [Fact]
    public void NoDocumentation_YieldsNulls()
    {
        var (info, revisions) = DocumentationExtractor.ExtractFromCode("model M\n  Real x;\nend M;");
        Assert.Null(info);
        Assert.Null(revisions);
    }

    [Fact]
    public void NoComposition_YieldsNulls()
    {
        var (info, revisions) = DocumentationExtractor.ExtractFromCode("type Voltage = Real;");
        Assert.Null(info);
        Assert.Null(revisions);
    }
}
