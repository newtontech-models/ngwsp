using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ngwsp;

public static class AppBuilder
{
    public static WebApplication Build(WebApplicationBuilder builder, ServerOptions options)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(options.LogLevel);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<MetricsStore>();
        builder.Services.AddSingleton<WebSocketProxy>();
        builder.Services.TryAddSingleton<IUpstreamReadiness, AlwaysReadyUpstreamReadiness>();
        builder.Services.TryAddSingleton<IGrpcSpeechAdapter, GrpcSpeechAdapter>();

        builder.Services.AddCors(cors =>
        {
            cors.AddDefaultPolicy(policy =>
            {
                if (options.CorsOrigins.Length == 0)
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
                else
                {
                    policy.WithOrigins(options.CorsOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });

        builder.WebHost.UseUrls(options.ListenUrl);

        var app = builder.Build();

        app.UseCors();
        app.UseWebSockets();

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));
        app.MapGet("/metrics", (MetricsStore metrics) => Results.Text(metrics.RenderPrometheus(), "text/plain"));

        app.MapGet("/ui", () => Results.Text(UiAssets.GetText("index.html"), "text/html; charset=utf-8"));
        app.MapGet("/ui/app.js", () => Results.Text(UiAssets.GetText("app.js"), "application/javascript; charset=utf-8"));
        app.MapGet("/ui/app.css", () => Results.Text(UiAssets.GetText("app.css"), "text/css; charset=utf-8"));
        app.MapGet("/ui/config.json", (ServerOptions options) => Results.Json(new { ws_url = options.UiWsUrl }));

        app.Map("/ws", async context =>
        {
            var proxy = context.RequestServices.GetRequiredService<WebSocketProxy>();
            await proxy.HandleAsync(context, context.RequestAborted);
        });

        return app;
    }
}
