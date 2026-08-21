using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// Whether a nested class is public or protected is captured when it is parsed. It used to be
/// re-derived by re-parsing the enclosing package's stored source, which is trimmed of its standalone
/// children as a memory optimisation — so the answer depended on whether the trim had run, and the
/// unused-class rule reported different counts on a fresh load and on a file reload.
/// </summary>
public class ClassVisibilityTests
{
    private static bool IsPublic(string code, string className)
    {
        var model = Assert.Single(ModelicaParserHelper.ExtractModels(code), m => m.Name == className);
        return model.IsPublic;
    }

    private const string Package = """
        package P "p"
          model A "public by default — no keyword needed"
          end A;
        protected
          model B "after the protected keyword"
          end B;
          model C "still protected — the section continues"
          end C;
        public
          model D "a public section reopens it"
          end D;
        end P;
        """;

    [Fact]
    public void LeadingSection_IsPublic() => Assert.True(IsPublic(Package, "A"));

    [Fact]
    public void AfterProtectedKeyword_IsNotPublic() => Assert.False(IsPublic(Package, "B"));

    [Fact]
    public void ProtectedSectionContinuesUntilTheNextKeyword() => Assert.False(IsPublic(Package, "C"));

    [Fact]
    public void PublicKeyword_ReopensAPublicSection() => Assert.True(IsPublic(Package, "D"));

    [Fact]
    public void TopLevelClass_IsPublic()
        => Assert.True(IsPublic("model M \"m\"\n  Real x;\nend M;", "M"));

    [Fact]
    public void PackageItself_IsPublic() => Assert.True(IsPublic(Package, "P"));

    [Fact]
    public void VisibilityIsPerClass_NotInheritedByNestedContents()
    {
        // E is protected in P; the class nested inside E starts a fresh public section of its own.
        const string code = """
            package P "p"
            protected
              package E "protected package"
                model Inner "public within E"
                end Inner;
              end E;
            end P;
            """;

        Assert.False(IsPublic(code, "E"));
        Assert.True(IsPublic(code, "Inner"));
    }
}
