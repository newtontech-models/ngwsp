using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ngwsp;
using ntx20.api.proto;
using Xunit;

namespace ngwsp.tests;

public class GrpcIntegrationTests
{
    [Fact]
    public async Task ProxyForwardsAudioAndTranscripts()
    {
        var receivedAudio = new List<byte[]>();
        await using var grpcHost = await StartGrpcServerAsync(async (requestStream, responseStream, context) =>
        {
            await requestStream.MoveNext();
            _ = requestStream.Current;
            await responseStream.WriteAsync(new Payload());

            var index = 0;
            while (await requestStream.MoveNext())
            {
                var payload = requestStream.Current;
                var chunk = payload.Chunk.FirstOrDefault();
                if (chunk is not null && chunk.B.Length > 0)
                {
                    receivedAudio.Add(chunk.B.ToByteArray());
                }

                var txt = new Item { Key = "txt", Type = "s", S = $"word{index}" };

                await responseStream.WriteAsync(new Payload
                {
                    Track = "pnc",
                    Chunk =
                    {
                        new Item { Key = "ts", Type = "d", D = index * 100 },
                        txt,
                        new Item { Key = "ts", Type = "d", D = index * 100 + 100 }
                    }
                });

                index++;
            }
        });

        await using var app = await StartProxyAsync(grpcHost.Address);
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);
        await socket.SendAsync(new byte[] { 4, 5 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        var first = await ReceiveTextAsync(socket, CancellationToken.None);
        var second = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        using var firstDoc = JsonDocument.Parse(first!);
        var firstToken = firstDoc.RootElement.GetProperty("tokens")[0];
        Assert.Equal("word0", firstToken.GetProperty("text").GetString());
        Assert.True(firstToken.GetProperty("is_final").GetBoolean());
        var firstFinalMs = firstDoc.RootElement.GetProperty("final_audio_proc_ms").GetInt64();
        var firstTotalMs = firstDoc.RootElement.GetProperty("total_audio_proc_ms").GetInt64();
        Assert.Equal(100, firstTotalMs);
        Assert.True(firstFinalMs <= firstTotalMs);

        using var secondDoc = JsonDocument.Parse(second!);
        var secondToken = secondDoc.RootElement.GetProperty("tokens")[0];
        Assert.True(secondToken.GetProperty("is_final").GetBoolean());
        Assert.Equal("word1", secondToken.GetProperty("text").GetString());
        Assert.Equal(200, secondDoc.RootElement.GetProperty("final_audio_proc_ms").GetInt64());
        Assert.Equal(200, secondDoc.RootElement.GetProperty("total_audio_proc_ms").GetInt64());

        await socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, true, CancellationToken.None);

        Assert.Equal(2, receivedAudio.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, receivedAudio[0]);
        Assert.Equal(new byte[] { 4, 5 }, receivedAudio[1]);
    }

    [Fact]
    public async Task UpstreamFailureReturnsError()
    {
        await using var grpcHost = await StartGrpcServerAsync(async (requestStream, responseStream, context) =>
        {
            await requestStream.MoveNext();
            _ = requestStream.Current;
            await responseStream.WriteAsync(new Payload());
            throw new RpcException(new Status(StatusCode.Internal, "boom"));
        });

        await using var app = await StartProxyAsync(grpcHost.Address);
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await socket.SendAsync(new byte[] { 1 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        var errorPayload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(errorPayload);
        using var doc = JsonDocument.Parse(errorPayload!);
        Assert.Equal(ErrorCodes.ProtocolError, doc.RootElement.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task TranscriptChunksDoNotInsertExtraSpaces()
    {
        await using var grpcHost = await StartGrpcServerAsync(async (requestStream, responseStream, context) =>
        {
            await requestStream.MoveNext();
            _ = requestStream.Current;
            await responseStream.WriteAsync(new Payload());

            while (await requestStream.MoveNext())
            {
                await responseStream.WriteAsync(new Payload
                {
                    Track = "pnc",
                    Chunk =
                    {
                        new Item { Key = "ts", Type = "d", D = 0 },
                        new Item { Key = "txt", Type = "s", S = "hello " },
                        new Item { Key = "txt", Type = "s", S = "world" },
                        new Item { Key = "ts", Type = "d", D = 10 }
                    }
                });
            }
        });

        await using var app = await StartProxyAsync(grpcHost.Address);
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await socket.SendAsync(new byte[] { 1 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        var payload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        var tokens = doc.RootElement.GetProperty("tokens");
        Assert.Equal(1, tokens.GetArrayLength());
        Assert.Equal("hello world", tokens[0].GetProperty("text").GetString());

        await socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, true, CancellationToken.None);
        await ReceiveTextAsync(socket, CancellationToken.None);
    }

    [Fact]
    public async Task LookaheadSegmentsAreSplitIntoSeparateTranscriptEvents()
    {
        await using var grpcHost = await StartGrpcServerAsync(async (requestStream, responseStream, context) =>
        {
            await requestStream.MoveNext();
            _ = requestStream.Current;
            await responseStream.WriteAsync(new Payload());

            while (await requestStream.MoveNext())
            {
                var final = new Item { Key = "txt", Type = "s", S = "final" };
                var lookahead = new Item { Key = "txt", Type = "s", S = "partial" };
                lookahead.Tags.Add("la");
                var end = new Item { Key = "ts", Type = "d", D = 10 };
                end.Tags.Add("la");

                await responseStream.WriteAsync(new Payload
                {
                    Track = "pnc",
                    Chunk =
                    {
                        new Item { Key = "ts", Type = "d", D = 0 },
                        final,
                        lookahead,
                        end
                    }
                });
            }
        });

        await using var app = await StartProxyAsync(grpcHost.Address);
        var server = app.GetTestServer();
        using var socket = await server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendTextAsync(socket, "{\"model\":\"atran-test\"}");
        await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        var payload = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.NotNull(payload);

        using var doc = JsonDocument.Parse(payload!);
        var tokens = doc.RootElement.GetProperty("tokens");
        Assert.Equal(1, tokens.GetArrayLength());
        Assert.False(tokens[0].GetProperty("is_final").GetBoolean());
        Assert.Equal("finalpartial", tokens[0].GetProperty("text").GetString());
        Assert.Equal(0, tokens[0].GetProperty("start_ms").GetInt32());
        Assert.Equal(10, tokens[0].GetProperty("end_ms").GetInt32());

        await socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, true, CancellationToken.None);
        await ReceiveTextAsync(socket, CancellationToken.None);
    }

    private static async Task<WebApplication> StartProxyAsync(Uri grpcAddress)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        var options = ServerOptions.Default() with
        {
            GrpcTarget = grpcAddress.ToString(),
            GrpcUseTls = false
        };

        var app = AppBuilder.Build(builder, options);
        await app.StartAsync();
        return app;
    }

    private static async Task<GrpcTestHost> StartGrpcServerAsync(
        Func<IAsyncStreamReader<Payload>, IServerStreamWriter<Payload>, ServerCallContext, Task> handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGrpc();
        var service = new TestEngineService(handler);
        builder.Services.AddSingleton(service);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        var app = builder.Build();
        app.MapGrpcService<TestEngineService>();
        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.Single();
        if (address is null)
        {
            throw new InvalidOperationException("Failed to determine gRPC server address");
        }

        return new GrpcTestHost(app, new Uri(address));
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

    private sealed class TestEngineService : EngineService.EngineServiceBase
    {
        private readonly Func<IAsyncStreamReader<Payload>, IServerStreamWriter<Payload>, ServerCallContext, Task> _handler;

        public TestEngineService(Func<IAsyncStreamReader<Payload>, IServerStreamWriter<Payload>, ServerCallContext, Task> handler)
        {
            _handler = handler;
        }

        public override Task Streaming(IAsyncStreamReader<Payload> requestStream, IServerStreamWriter<Payload> responseStream, ServerCallContext context)
            => _handler(requestStream, responseStream, context);
    }

    private sealed class GrpcTestHost : IAsyncDisposable
    {
        public GrpcTestHost(WebApplication app, Uri address)
        {
            App = app;
            Address = address;
        }

        public WebApplication App { get; }
        public Uri Address { get; }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
