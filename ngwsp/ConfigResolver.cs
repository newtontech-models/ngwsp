using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ngwsp;

public static class ConfigResolver
{
    private const string Prefix = "NGWSP_";

    public static ServerOptions Resolve(
        string? listenUrl,
        string? corsOrigins,
        string? logLevel,
        string? tlsCertPath,
        string? tlsKeyPath,
        string? grpcTarget,
        string? grpcUseTls,
        string? grpcCaPath,
        string? grpcTimeoutMs,
        string? clientAuthMode,
        string? clientApiKey,
        string? models,
        string? defaultModel,
        string? lexicon,
        string? uiWsUrl,
        string? audioBufferFrames)
    {
        var defaults = ServerOptions.Default();

        return defaults with
        {
            ListenUrl = ResolveString(listenUrl, Env("LISTEN_URL"), defaults.ListenUrl) ?? defaults.ListenUrl,
            CorsOrigins = ResolveCsv(corsOrigins, Env("CORS_ORIGINS"), defaults.CorsOrigins),
            LogLevel = ResolveLogLevel(logLevel, Env("LOG_LEVEL"), defaults.LogLevel),
            TlsCertPath = ResolveString(tlsCertPath, Env("TLS_CERT_PATH"), defaults.TlsCertPath),
            TlsKeyPath = ResolveString(tlsKeyPath, Env("TLS_KEY_PATH"), defaults.TlsKeyPath),
            GrpcTarget = ResolveString(grpcTarget, Env("GRPC_TARGET"), defaults.GrpcTarget),
            GrpcUseTls = ResolveBool(grpcUseTls, Env("GRPC_USE_TLS"), defaults.GrpcUseTls),
            GrpcCaPath = ResolveString(grpcCaPath, Env("GRPC_CA_PATH"), defaults.GrpcCaPath),
            GrpcTimeoutMs = ResolveInt(grpcTimeoutMs, Env("GRPC_TIMEOUT_MS"), defaults.GrpcTimeoutMs),
            ClientAuthMode = ResolveString(clientAuthMode, Env("CLIENT_AUTH_MODE"), defaults.ClientAuthMode) ?? defaults.ClientAuthMode,
            ClientApiKey = ResolveString(clientApiKey, Env("CLIENT_API_KEY"), defaults.ClientApiKey),
            Models = ResolveCsv(models, Env("MODELS") ?? EnvRaw("NG_MODELS"), defaults.Models),
            DefaultModel = ResolveString(defaultModel, Env("DEFAULT_MODEL"), defaults.DefaultModel),
            Lexicon = ResolveString(lexicon, Env("LEXICON"), defaults.Lexicon),
            UiWsUrl = ResolveString(uiWsUrl, Env("UI_WS_URL"), defaults.UiWsUrl) ?? defaults.UiWsUrl,
            AudioBufferFrames = ResolveInt(audioBufferFrames, Env("AUDIO_BUFFER_FRAMES"), defaults.AudioBufferFrames) ?? defaults.AudioBufferFrames
        };
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(Prefix + name);
    private static string? EnvRaw(string name) => Environment.GetEnvironmentVariable(name);

    private static string? ResolveString(string? cli, string? env, string? fallback)
        => !string.IsNullOrWhiteSpace(cli) ? cli : !string.IsNullOrWhiteSpace(env) ? env : fallback;

    private static string[] ResolveCsv(string? cli, string? env, string[] fallback)
    {
        var value = ResolveString(cli, env, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static LogLevel ResolveLogLevel(string? cli, string? env, LogLevel fallback)
    {
        var value = ResolveString(cli, env, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse(value, true, out LogLevel parsed) ? parsed : fallback;
    }

    private static bool? ResolveBool(string? cli, string? env, bool? fallback)
    {
        var value = ResolveString(cli, env, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int? ResolveInt(string? cli, string? env, int? fallback)
    {
        var value = ResolveString(cli, env, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
