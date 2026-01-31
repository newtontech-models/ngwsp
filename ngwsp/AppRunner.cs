using Microsoft.Extensions.Logging;

namespace ngwsp;

public static class AppRunner
{
    public static async Task RunAsync(ServerOptions options, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>()
        });

        var app = AppBuilder.Build(builder, options);
        LogStartupInfo(app.Logger, options);
        await app.RunAsync(cancellationToken);
    }

    private static void LogStartupInfo(ILogger logger, ServerOptions options)
    {
        var baseUrl = options.ListenUrl.TrimEnd('/');
        var wsBase = ToWebSocketBase(baseUrl);

        logger.LogInformation("ngwsp proxy starting");
        logger.LogInformation("HTTP base: {BaseUrl}", baseUrl);
        logger.LogInformation("WebSocket: {WebSocket}", $"{wsBase}/ws");
        logger.LogInformation("UI: {UiUrl}", $"{baseUrl}/ui");
        logger.LogInformation("Health: {LiveUrl} {ReadyUrl}", $"{baseUrl}/health/live", $"{baseUrl}/health/ready");
        logger.LogInformation("Metrics: {MetricsUrl}", $"{baseUrl}/metrics");

        if (string.Equals(options.ClientAuthMode, "api_key", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Client auth: api_key (key: {ApiKey})", options.ClientApiKey ?? string.Empty);
        }
        else
        {
            logger.LogInformation("Client auth: {Mode}", options.ClientAuthMode);
        }
    }

    private static string ToWebSocketBase(string httpBase)
    {
        if (httpBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "wss://" + httpBase[8..];
        }

        if (httpBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "ws://" + httpBase[7..];
        }

        return httpBase;
    }
}
