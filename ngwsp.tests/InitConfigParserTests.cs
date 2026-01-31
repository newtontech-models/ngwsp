using ngwsp;
using Xunit;

namespace ngwsp.tests;

public class InitConfigParserTests
{
    [Fact]
    public void ParsesModelOnlyConfig()
    {
        var payload = "{\"model\":\"alpha\"}"u8.ToArray();

        var result = InitConfigParser.Parse(payload);

        Assert.True(result.Success);
        Assert.NotNull(result.Config);
        Assert.Equal("alpha", result.Config!.Model);
        Assert.Null(result.Config.Lexicon);
    }

    [Fact]
    public void ParsesLexicon()
    {
        var payload = "{\"model\":\"alpha\",\"lexicon\":{\"rewrite_terms\":[{\"source\":\"foo\",\"target\":\"bar\"}]}}"u8.ToArray();

        var result = InitConfigParser.Parse(payload);

        Assert.True(result.Success);
        Assert.NotNull(result.Config);
        Assert.NotNull(result.Config.Lexicon);
        Assert.Single(result.Config.Lexicon!.RewriteTerms);
        Assert.Equal("foo", result.Config.Lexicon.RewriteTerms[0].Source);
        Assert.Equal("bar", result.Config.Lexicon.RewriteTerms[0].Target);
    }

    [Fact]
    public void MissingModelReturnsInvalidInitConfig()
    {
        var payload = "{\"lexicon\":{\"rewrite_terms\":[{\"source\":\"x\",\"target\":\"y\"}]}}"u8.ToArray();

        var result = InitConfigParser.Parse(payload);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidInitConfig, result.Error?.Code);
    }

    [Fact]
    public void InvalidLexiconReturnsError()
    {
        var payload = "{\"model\":\"alpha\",\"lexicon\":{\"rewrite_terms\":[{\"source\":\"x\"}]}}"u8.ToArray();

        var result = InitConfigParser.Parse(payload);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UnsupportedLexicon, result.Error?.Code);
    }
}
