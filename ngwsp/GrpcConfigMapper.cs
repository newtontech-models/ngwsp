using ntx20.api.proto;

namespace ngwsp;

public static class GrpcConfigMapper
{
    public static Payload BuildConfigPayload(InitConfig config)
    {
        var payload = new Payload();
        payload.Chunk.Add(new Item { Key = "audio-format", Type = "s", S = "auto:0" });
        payload.Chunk.Add(new Item { Key = "audio-channel", Type = "s", S = "downmix" });

        if (config.Lexicon is not null)
        {
            var lexiconItem = new Item { Key = "lexicon", Type = "m" };
            foreach (var term in config.Lexicon.RewriteTerms)
            {
                var entry = new Item { Type = "s", S = term.Target };
                entry.Labels.Add("hint", term.Source);
                lexiconItem.M.Add(entry);
            }

            payload.Chunk.Add(lexiconItem);
        }

        return payload;
    }
}
