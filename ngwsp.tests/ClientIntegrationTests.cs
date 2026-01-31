using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ngwsp;
using Xunit;

namespace ngwsp.tests;

public class ClientIntegrationTests
{
    [Fact]
    public async Task ClientStreamsAudioAndWritesJsonl()
    {
        var adapter = new FakeGrpcSpeechAdapter();
        await using var host = await StartProxyAsync(adapter);
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(inputPath, new byte[] { 1, 2, 3, 4, 5 });

        var options = new ClientOptions(
            ProxyUrl: BuildWebSocketUrl(host.Address, "/ws"),
            Input: inputPath,
            Output: outputPath,
            Reader: "raw:2",
            Writer: "json",
            Flush: true,
            Mode: null,
            Retry: null,
            AudioFormat: null,
            AudioChannel: null,
            Pipe: false,
            LexiconPath: null,
            Model: "atran-test",
            LogLevel: null,
            AuthType: null,
            ApiKey: null);

        var exitCode = await ClientRunner.RunAsync(options, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.Equal(4, lines.Length);
        using var doc = JsonDocument.Parse(lines[0]);
        var token = doc.RootElement.GetProperty("tokens")[0];
        Assert.Equal("bytes:2", token.GetProperty("text").GetString());
        using var finishedDoc = JsonDocument.Parse(lines[^1]);
        Assert.True(finishedDoc.RootElement.GetProperty("finished").GetBoolean());
        Assert.True(adapter.SessionStarted);
    }

    [Fact]
    public async Task ClientTextWriterSkipsLookahead()
    {
        var adapter = new PartialFinalGrpcSpeechAdapter();
        await using var host = await StartProxyAsync(adapter);
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(inputPath, new byte[] { 1, 2, 3, 4 });

        var options = new ClientOptions(
            ProxyUrl: BuildWebSocketUrl(host.Address, "/ws"),
            Input: inputPath,
            Output: outputPath,
            Reader: "raw:2",
            Writer: "text",
            Flush: true,
            Mode: null,
            Retry: null,
            AudioFormat: null,
            AudioChannel: null,
            Pipe: false,
            LexiconPath: null,
            Model: "atran-test",
            LogLevel: null,
            AuthType: null,
            ApiKey: null);

        var exitCode = await ClientRunner.RunAsync(options, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Equal("final:2final:2", content);
    }

    private static async Task<ProxyHost> StartProxyAsync(IGrpcSpeechAdapter adapter)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, 0);
        });
        builder.Services.AddSingleton<IGrpcSpeechAdapter>(adapter);

        var app = AppBuilder.Build(builder, ServerOptions.Default());
        await app.StartAsync();
        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addresses = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var address = addresses?.Addresses.Single();
        if (address is null)
        {
            throw new InvalidOperationException("Failed to determine proxy address");
        }

        return new ProxyHost(app, new Uri(address));
    }

    private static string BuildWebSocketUrl(Uri baseAddress, string path)
    {
        var builder = new UriBuilder(baseAddress)
        {
            Scheme = baseAddress.Scheme == "https" ? "wss" : "ws",
            Path = path
        };
        return builder.Uri.ToString();
    }

    private sealed class ProxyHost : IAsyncDisposable
    {
        public ProxyHost(WebApplication app, Uri address)
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

    private sealed class FakeGrpcSpeechAdapter : IGrpcSpeechAdapter
    {
        private readonly FakeSession _session = new();
        public bool SessionStarted { get; private set; }

        public Task<IGrpcSpeechSession> StartSessionAsync(InitConfig config, CancellationToken cancellationToken)
        {
            SessionStarted = true;
            return Task.FromResult<IGrpcSpeechSession>(_session);
        }
    }

    private sealed class PartialFinalGrpcSpeechAdapter : IGrpcSpeechAdapter
    {
        private readonly PartialFinalSession _session = new();

        public Task<IGrpcSpeechSession> StartSessionAsync(InitConfig config, CancellationToken cancellationToken)
            => Task.FromResult<IGrpcSpeechSession>(_session);
    }

    private sealed class PartialFinalSession : IGrpcSpeechSession
    {
        private readonly Channel<TranscriptEvent> _channel = Channel.CreateUnbounded<TranscriptEvent>();

        public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(new TranscriptEvent("pnc",
                new[] { new TranscriptToken($"partial:{audio.Length}", 0, 1, false, false) }, 0, 1));
            _channel.Writer.TryWrite(new TranscriptEvent("pnc",
                new[] { new TranscriptToken($"final:{audio.Length}", 1, 2, true, false) }, 2, 2));
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<TranscriptEvent> ReadTranscriptsAsync(
            string track,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = track;
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession : IGrpcSpeechSession
    {
        private readonly Channel<TranscriptEvent> _channel = Channel.CreateUnbounded<TranscriptEvent>();

        public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(new TranscriptEvent("pnc",
                new[] { new TranscriptToken($"bytes:{audio.Length}", 0, 1, true, false) }, 1, 1));
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<TranscriptEvent> ReadTranscriptsAsync(
            string track,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = track;
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
