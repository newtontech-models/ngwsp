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

        app.Map("/ws", async context =>
        {
            var proxy = context.RequestServices.GetRequiredService<WebSocketProxy>();
            await proxy.HandleAsync(context, context.RequestAborted);
        });

        return app;
    }
}
