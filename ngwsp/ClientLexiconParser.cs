namespace ngwsp;

public static class ClientLexiconParser
{
    public static LexiconDefinition? Parse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Lexicon file not found", path);
        }

        var terms = new List<LexiconRewriteTerm>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator <= 0)
            {
                throw new FormatException($"Invalid lexicon line: {line}");
            }

            var source = trimmed[..separator].Trim().Trim('"');
            var target = trimmed[(separator + 1)..].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                throw new FormatException($"Invalid lexicon line: {line}");
            }

            terms.Add(new LexiconRewriteTerm(source, target));
        }

        return terms.Count == 0 ? null : new LexiconDefinition(terms);
    }
}
