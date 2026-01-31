namespace ngwsp;

public sealed record InitConfig(string Model, LexiconDefinition? Lexicon);

public sealed record LexiconDefinition(IReadOnlyList<LexiconRewriteTerm> RewriteTerms);

public sealed record LexiconRewriteTerm(string Source, string Target);
