using Xunit;

namespace RevisionControl.Tests;

/// <summary>
/// Showing a revision identifier: a Git hash is shortened, an SVN revision number is not.
/// </summary>
public class RevisionIdTests
{
    private const string Sha1 = "9f2c1b7a4e6d8f0c3b5a7e9d1f3c5b7a9e1d3f5c";
    private const string Sha256 =
        "9f2c1b7a4e6d8f0c3b5a7e9d1f3c5b7a9e1d3f5c9f2c1b7a4e6d8f0c3b5a7e9d";

    [Fact]
    public void Shorten_CommitHash_KeepsTheFirstSevenCharacters()
    {
        Assert.Equal("9f2c1b7", RevisionId.Shorten(Sha1));
        Assert.Equal("9f2c1b7", RevisionId.Shorten(Sha256));
    }

    [Fact]
    public void Shorten_SvnRevisionNumber_IsLeftAlone()
    {
        // The one that matters: an SVN revision is hex by accident, and shortening "1234567" to
        // "123" would name a different revision rather than the same one written shorter.
        Assert.Equal("1234567", RevisionId.Shorten("1234567"));
        Assert.Equal("42", RevisionId.Shorten("42"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("9f2c1b7", "9f2c1b7")]                        // already shortened
    [InlineData("main", "main")]                              // a branch name
    [InlineData("HEAD", "HEAD")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]  // 40 chars, not hex
    public void Shorten_AnythingThatIsNotAFullHash_IsReturnedAsItIs(string? revision, string expected)
        => Assert.Equal(expected, RevisionId.Shorten(revision));

    [Fact]
    public void Shorten_HonoursARequestedLength()
    {
        Assert.Equal("9f2c1b7a", RevisionId.Shorten(Sha1, 8));
        Assert.Equal(Sha1, RevisionId.Shorten(Sha1, Sha1.Length));
        Assert.Equal(Sha1, RevisionId.Shorten(Sha1, 0));   // nothing asked for, nothing removed
    }

    [Theory]
    [InlineData(Sha1, true)]
    [InlineData(Sha256, true)]
    [InlineData("9F2C1B7A4E6D8F0C3B5A7E9D1F3C5B7A9E1D3F5C", true)]   // upper case
    [InlineData("1234567", false)]
    [InlineData("9f2c1b7a4e6d8f0c3b5a7e9d1f3c5b7a9e1d3f5", false)]   // 39 characters
    [InlineData(null, false)]
    public void IsCommitHash_RecognisesAFullObjectId(string? revision, bool expected)
        => Assert.Equal(expected, RevisionId.IsCommitHash(revision));
}
