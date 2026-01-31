using ngwsp;
using Xunit;

namespace ngwsp.tests;

public class GrpcConfigMapperTests
{
    [Fact]
    public void MapsLexiconToPayload()
    {
        var config = new InitConfig(
            "model",
            new LexiconDefinition(new[] { new LexiconRewriteTerm("source", "target") }));

        var payload = GrpcConfigMapper.BuildConfigPayload(config);

        Assert.DoesNotContain(payload.Chunk, item => item.Key == "features");
        var lexicon = payload.Chunk.Single(item => item.Key == "lexicon");
        Assert.Equal("m", lexicon.Type);
        Assert.Single(lexicon.M);
        Assert.Equal("target", lexicon.M[0].S);
        Assert.Equal("source", lexicon.M[0].Labels["hint"]);
    }
}
