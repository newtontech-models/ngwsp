namespace ngwsp;

public sealed record ClientOptions(
    string? ProxyUrl,
    string? Input,
    string? Output,
    string? Reader,
    string? Writer,
    bool Flush,
    string? Mode,
    string? Retry,
    string? AudioFormat,
    string? AudioChannel,
    bool Pipe,
    string? LexiconPath,
    string? Model,
    string? LogLevel,
    string? AuthType,
    string? ApiKey);
