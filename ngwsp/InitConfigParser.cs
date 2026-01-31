using System.Text.Json;

namespace ngwsp;

public static class InitConfigParser
{
    public static InitConfigParseResult Parse(byte[] payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return InitConfigParseResult.FromError(ErrorCodes.InvalidInitConfig, "InitConfig must be valid JSON object");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return InitConfigParseResult.FromError(ErrorCodes.InvalidInitConfig, "InitConfig must be a JSON object");
            }

            var root = document.RootElement;

            if (!root.TryGetProperty("model", out var modelElement) || modelElement.ValueKind != JsonValueKind.String)
            {
                return InitConfigParseResult.FromError(ErrorCodes.InvalidInitConfig, "InitConfig requires a model string");
            }

            var model = modelElement.GetString();
            if (string.IsNullOrWhiteSpace(model))
            {
                return InitConfigParseResult.FromError(ErrorCodes.InvalidInitConfig, "InitConfig requires a model string");
            }

            LexiconDefinition? lexicon = null;
            if (root.TryGetProperty("lexicon", out var lexiconElement))
            {
                if (lexiconElement.ValueKind != JsonValueKind.Object)
                {
                    return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "Lexicon must be an object");
                }

                if (!lexiconElement.TryGetProperty("rewrite_terms", out var rewriteTermsElement))
                {
                    return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "Lexicon must include rewrite_terms");
                }

                if (rewriteTermsElement.ValueKind != JsonValueKind.Array)
                {
                    return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "rewrite_terms must be an array");
                }

                var terms = new List<LexiconRewriteTerm>();
                foreach (var termElement in rewriteTermsElement.EnumerateArray())
                {
                    if (termElement.ValueKind != JsonValueKind.Object)
                    {
                        return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "rewrite_terms entries must be objects");
                    }

                    if (!termElement.TryGetProperty("source", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.String)
                    {
                        return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "rewrite_terms entries require source string");
                    }

                    if (!termElement.TryGetProperty("target", out var targetElement) || targetElement.ValueKind != JsonValueKind.String)
                    {
                        return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "rewrite_terms entries require target string");
                    }

                    var source = sourceElement.GetString();
                    var target = targetElement.GetString();
                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                    {
                        return InitConfigParseResult.FromError(ErrorCodes.UnsupportedLexicon, "rewrite_terms entries require source and target strings");
                    }

                    terms.Add(new LexiconRewriteTerm(source, target));
                }

                lexicon = new LexiconDefinition(terms);
            }
            return InitConfigParseResult.FromConfig(new InitConfig(model, lexicon));
        }
    }
}

public sealed record InitConfigParseResult(bool Success, InitConfig? Config, ProxyError? Error)
{
    public static InitConfigParseResult FromError(string code, string message)
        => new(false, null, new ProxyError(code, message));

    public static InitConfigParseResult FromConfig(InitConfig config)
        => new(true, config, null);
}
