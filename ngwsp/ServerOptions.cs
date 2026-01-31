using Microsoft.Extensions.Logging;

namespace ngwsp;

public sealed record ServerOptions(
    string ListenUrl,
    string[] CorsOrigins,
    LogLevel LogLevel,
    string? TlsCertPath,
    string? TlsKeyPath,
    string? GrpcTarget,
    bool? GrpcUseTls,
    string? GrpcCaPath,
    int? GrpcTimeoutMs,
    string ClientAuthMode,
    string? ClientApiKey,
    string[] Models,
    string? DefaultModel,
    string? Lexicon,
    string UiWsUrl,
    int AudioBufferFrames)
{
    public static ServerOptions Default() => new(
        ListenUrl: "http://0.0.0.0:8080",
        CorsOrigins: Array.Empty<string>(),
        LogLevel: LogLevel.Information,
        TlsCertPath: null,
        TlsKeyPath: null,
        GrpcTarget: null,
        GrpcUseTls: null,
        GrpcCaPath: null,
        GrpcTimeoutMs: null,
        ClientAuthMode: "none",
        ClientApiKey: "test_api_key",
        Models: Array.Empty<string>(),
        DefaultModel: null,
        Lexicon: null,
        UiWsUrl: "ws://localhost:8080/ws",
        AudioBufferFrames: 32);
}
