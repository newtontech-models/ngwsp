using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ngwsp;
using Xunit;

namespace ngwsp.tests;

public class WebSocketIntegrationTests
{
    [Fact]
    public async Task ServerAcceptsWebSocketConnection()
    {
        await using var app = await StartTestAppAsync(grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task UnauthorizedClientIsRejectedWhenApiKeyRequired()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizedClientIsAcceptedWhenApiKeyProvidedAsSubprotocol()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Add("Sec-WebSocket-Protocol", "secret");

        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task AuthorizedClientIsAcceptedWhenApiKeyProvidedAsUriEscapedSubprotocol()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "a+b/c" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Add("Sec-WebSocket-Protocol", "a%2Bb%2Fc");

        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task AuthorizedClientIsAcceptedWhenApiKeyProvidedAsAuthorizationHeader()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Add("Authorization", "secret");

        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task AuthorizedClientIsAcceptedWhenApiKeyProvidedAsBearerHeader()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Add("Authorization", "Bearer secret");

        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task AuthorizedClientIsAcceptedWhenApiKeyProvidedInQuery()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();

        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws?authorization=secret"), CancellationToken.None);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task UnauthorizedClientIsRejectedWhenApiKeyProvidedInQueryUsingApiKeyParameterName()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        await using var app = await StartTestAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        var client = server.CreateWebSocketClient();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.ConnectAsync(new Uri("ws://localhost/ws?api_key=secret"), CancellationToken.None));
    }

    [Fact]
    public async Task InitConfigFirstDoesNotCloseWithError()
    {
        await using var app = await StartTestAppAsync(grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await Task.Delay(50);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task InitConfigAcceptsAtranAsDashSeparatedPart()
    {
        await using var app = await StartTestAppAsync(grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"xxx-yyy-zzz:0.0.1-atran-cz\"}");
        await Task.Delay(50);

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task BinaryBeforeInitConfigReturnsErrorAndCloses()
    {
        await using var app = await StartTestAppAsync(grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, true, CancellationToken.None);

        var errorPayload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(errorPayload);
        using var doc = JsonDocument.Parse(errorPayload!);
        Assert.Equal(ErrorCodes.ProtocolError, doc.RootElement.GetProperty("error_code").GetString());

        var closeStatus = await ReceiveCloseAsync(socket, CancellationToken.None);
        Assert.NotNull(closeStatus);
    }

    [Fact]
    public async Task EmptyBinaryFrameTriggersFinishedAndClose()
    {
        await using var app = await StartTestAppAsync(grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, true, CancellationToken.None);

        var finishedPayload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(finishedPayload);
        using var doc = JsonDocument.Parse(finishedPayload!);
        Assert.True(doc.RootElement.TryGetProperty("finished", out var finished));
        Assert.True(finished.GetBoolean());

        var closeStatus = await ReceiveCloseAsync(socket, CancellationToken.None);
        Assert.NotNull(closeStatus);
    }

    [Fact]
    public async Task InvalidInitConfigReturnsErrorAndCloses()
    {
        await using var app = await StartTestAppAsync(grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{}");

        var errorPayload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(errorPayload);
        using var doc = JsonDocument.Parse(errorPayload!);
        Assert.Equal(ErrorCodes.InvalidInitConfig, doc.RootElement.GetProperty("error_code").GetString());

        var closeStatus = await ReceiveCloseAsync(socket, CancellationToken.None);
        Assert.NotNull(closeStatus);
    }

    [Fact]
    public async Task BackpressureReturnsBufferOverflow()
    {
        var readiness = new TestUpstreamReadiness { IsReady = false };
        await using var app = await StartTestAppAsync(readiness: readiness, grpcAdapter: new NoopGrpcSpeechAdapter());
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await socket.SendAsync(new byte[] { 1 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        var errorPayload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(errorPayload);
        using var doc = JsonDocument.Parse(errorPayload!);
        Assert.Equal(ErrorCodes.BufferOverflow, doc.RootElement.GetProperty("error_code").GetString());

        var closeStatus = await ReceiveCloseAsync(socket, CancellationToken.None);
        Assert.NotNull(closeStatus);
    }

    private static async Task<WebApplication> StartTestAppAsync(
        ServerOptions? options = null,
        IUpstreamReadiness? readiness = null,
        IGrpcSpeechAdapter? grpcAdapter = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        if (readiness is not null)
        {
            builder.Services.AddSingleton<IUpstreamReadiness>(readiness);
        }
        if (grpcAdapter is not null)
        {
            builder.Services.AddSingleton<IGrpcSpeechAdapter>(grpcAdapter);
        }

        var app = AppBuilder.Build(builder, options ?? ServerOptions.Default());
        await app.StartAsync();
        return app;
    }

    private static async Task<(WebApplication App, Uri BaseUri)> StartKestrelAppAsync(
        ServerOptions? options = null,
        IUpstreamReadiness? readiness = null,
        IGrpcSpeechAdapter? grpcAdapter = null)
    {
        var port = GetFreeTcpPort();
        var configured = (options ?? ServerOptions.Default()) with { ListenUrl = $"http://127.0.0.1:{port}" };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseKestrel();

        if (readiness is not null)
        {
            builder.Services.AddSingleton<IUpstreamReadiness>(readiness);
        }
        if (grpcAdapter is not null)
        {
            builder.Services.AddSingleton<IGrpcSpeechAdapter>(grpcAdapter);
        }

        var app = AppBuilder.Build(builder, configured);
        await app.StartAsync();

        return (app, new Uri($"http://127.0.0.1:{port}"));
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Task SendTextAsync(WebSocket socket, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static async Task<WebSocketCloseStatus?> ReceiveCloseAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return result.CloseStatus;
            }
        }
    }

    [Fact]
    public async Task ClientPrintsErrorWhenApiKeyIsInvalid()
    {
        var options = ServerOptions.Default() with { ClientAuthMode = "api_key", ClientApiKey = "secret" };
        var (app, baseUri) = await StartKestrelAppAsync(options, grpcAdapter: new NoopGrpcSpeechAdapter());
        await using var _ = app;

        var wsUri = new UriBuilder(baseUri) { Scheme = "ws", Path = "/ws" }.Uri.ToString();

        var originalError = Console.Error;
        var errorWriter = new StringWriter();
        Console.SetError(errorWriter);
        try
        {
            var exit = await ClientRunner.RunAsync(new ClientOptions(
                ProxyUrl: wsUri,
                Input: "-",
                Output: "-",
                Reader: "raw:4096",
                Writer: "json",
                Flush: false,
                Mode: null,
                Retry: null,
                AudioFormat: null,
                AudioChannel: null,
                Pipe: false,
                LexiconPath: null,
                Model: "atran-test",
                LogLevel: null,
                AuthType: "header",
                ApiKey: "wrong_key"), CancellationToken.None);

            Assert.NotEqual(0, exit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var errorText = errorWriter.ToString();
        Assert.Contains("rejected", errorText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api key", errorText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestUpstreamReadiness : IUpstreamReadiness
    {
        public bool IsReady { get; set; } = true;
    }

    private sealed class NoopGrpcSpeechAdapter : IGrpcSpeechAdapter
    {
        public Task<IGrpcSpeechSession> StartSessionAsync(InitConfig config, CancellationToken cancellationToken)
            => Task.FromResult<IGrpcSpeechSession>(new NoopSession());

        private sealed class NoopSession : IGrpcSpeechSession
        {
            public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task CompleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public async IAsyncEnumerable<TranscriptEvent> ReadTranscriptsAsync(
                string track,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                _ = track;
                await Task.CompletedTask;
                _ = cancellationToken;
                yield break;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
