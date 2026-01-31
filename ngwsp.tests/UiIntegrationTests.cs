using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using ngwsp;
using Xunit;

namespace ngwsp.tests;

public class UiIntegrationTests
{
    [Fact]
    public async Task UiAssetsAreServed()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestServer().CreateClient();

        var html = await client.GetStringAsync("/ui");
        Assert.Contains("Dictation UI", html);

        var js = await client.GetStringAsync("/ui/app.js");
        Assert.Contains("MediaRecorder", js);

        var css = await client.GetStringAsync("/ui/app.css");
        Assert.Contains("Dictation UI", html);
        Assert.Contains("--bg", css);
    }

    private static async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        var options = ServerOptions.Default() with
        {
            GrpcTarget = "http://localhost:5001",
            GrpcUseTls = false
        };

        var app = AppBuilder.Build(builder, options);
        await app.StartAsync();
        return app;
    }
}
