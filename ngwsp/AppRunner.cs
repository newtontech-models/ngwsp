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
        await app.RunAsync(cancellationToken);
    }
}
