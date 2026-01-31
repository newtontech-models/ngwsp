using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using ntx20.api.proto;

namespace ngwsp;

public interface IGrpcSpeechAdapter
{
    Task<IGrpcSpeechSession> StartSessionAsync(InitConfig config, CancellationToken cancellationToken);
}

public interface IGrpcSpeechSession : IAsyncDisposable
{
    Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptEvent> ReadTranscriptsAsync(string track,CancellationToken cancellationToken);
}

public sealed class GrpcSpeechAdapter : IGrpcSpeechAdapter
{
    private readonly ServerOptions _options;
    private readonly ILogger<GrpcSpeechAdapter> _logger;
    private readonly GrpcChannel _channel;
    private readonly Uri _target;

    public GrpcSpeechAdapter(ServerOptions options, ILogger<GrpcSpeechAdapter> logger)
    {
        _options = options;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(options.GrpcTarget))
        {
            throw new InvalidOperationException("GrpcTarget is required");
        }

        _target = ResolveTarget(options.GrpcTarget);
        _channel = CreateChannel(_target, options);
    }

    public async Task<IGrpcSpeechSession> StartSessionAsync(InitConfig config, CancellationToken cancellationToken)
    {
        var client = new EngineService.EngineServiceClient(_channel);
        var headers = CreateHeaders(config, _target.UserInfo);
        var call = client.Streaming(headers, deadline: CreateDeadline(_options), cancellationToken: cancellationToken);

        var configPayload = GrpcConfigMapper.BuildConfigPayload(config);
        await call.RequestStream.WriteAsync(configPayload, cancellationToken);

        try
        {
            await call.ResponseHeadersAsync;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read gRPC response headers");
        }

        try
        {
            if (await call.ResponseStream.MoveNext(cancellationToken))
            {
                _ = call.ResponseStream.Current;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read gRPC config response");
        }

        return new GrpcSpeechSession(call, _logger);
    }

    private static DateTime? CreateDeadline(ServerOptions options)
    {
        if (options.GrpcTimeoutMs is null)
        {
            return null;
        }

        return DateTime.UtcNow.AddMilliseconds(options.GrpcTimeoutMs.Value);
    }

    private static GrpcChannel CreateChannel(Uri target, ServerOptions options)
    {
        if (options.GrpcUseTls == false)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }

        var channelOptions = new GrpcChannelOptions();
        return GrpcChannel.ForAddress(target, channelOptions);
    }

    private static Metadata CreateHeaders(InitConfig config, string? userInfo)
    {
        var metadata = new Metadata();
        if (!string.IsNullOrWhiteSpace(userInfo))
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(userInfo));
            metadata.Add("Authorization", $"Basic {encoded}");
        }

        metadata.Add("service", config.Model);
        return metadata;
    }

    private static Uri ResolveTarget(string target)
    {
        var value = target;
        if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var envValue = Environment.GetEnvironmentVariable(value);
            if (string.IsNullOrWhiteSpace(envValue))
            {
                throw new InvalidOperationException("GrpcTarget must be a URL or environment variable name");
            }

            value = envValue;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("GrpcTarget must be a valid absolute URL");
        }

        return uri;
    }

    private sealed class GrpcSpeechSession : IGrpcSpeechSession
    {
        private readonly AsyncDuplexStreamingCall<Payload, Payload> _call;
        private readonly ILogger _logger;
        private readonly Dictionary<string, TrackState> _tracks = new(StringComparer.Ordinal);

        private readonly Regex firstalpha = new Regex(@"^(\s*)(\S)(.*)$");
        public GrpcSpeechSession(AsyncDuplexStreamingCall<Payload, Payload> call, ILogger logger)
        {
            _call = call;
            _logger = logger;
        }

        public async Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        {
            var payload = new Payload { Track = "aud" };
            payload.Chunk.Add(new Item { B = ByteString.CopyFrom(audio.Span) });
            await _call.RequestStream.WriteAsync(payload, cancellationToken);
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await _call.RequestStream.CompleteAsync();
            _ = cancellationToken;
        }

        public async IAsyncEnumerable<TranscriptEvent> ReadTranscriptsAsync(string track, [EnumeratorCancellation] CancellationToken cancellationToken)
        {   
            var lastFinalTs = 0.0;
            var lastFinalText = "";
            double totalAudioProcMs = 0.0;
            var currentText = lastFinalText;
            var ret = new List<TranscriptToken>();
            while (await _call.ResponseStream.MoveNext(cancellationToken))
            {
                var payload = _call.ResponseStream.Current;
                // Buffer of final items from previous step that became lookahead due to word concatenation

                if (payload.Track != track)
                {
                    continue;
                }

                if (payload.Chunk.Count == 0)
                {
                    continue;
                }
                var lastLookahead = false;
                currentText = lastFinalText;
                var currentStartTs = lastFinalTs;


                ret = new List<TranscriptToken>();
                foreach (var item in payload.Chunk)
                {
                    if (item.Key == "ts")
                    {
                        var ts = item.D;
                        if (ts > totalAudioProcMs)
                        {
                            totalAudioProcMs = ts;
                        }
                        if (currentText.Length > 0 && lastFinalTs != ts)
                        {
                            var isFinal = !item.Tags.Contains("la");
                            ret.Add(new TranscriptToken(
                                currentText,
                                currentStartTs,
                                ts,
                                isFinal,
                                currentText.Contains("[n::")));

                            if (isFinal)
                            {
                                lastFinalText = "";
                                lastFinalTs = ts;
                            }
                        }

                        currentText = "";
                        currentStartTs = ts;
                        lastLookahead = false;
                    }
                    if (item.Key == "txt")
                    {
                        var la = item.Tags.Contains("la");
                        var text = item.S;
                        if (item.Tags.Contains("sos"))
                        {

                            text = firstalpha.Replace(text, m =>
                            m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                            );

                        }

                        if (la && !lastLookahead)
                        {
                            lastFinalText = currentText;
                        }
                        currentText += text;
                        lastLookahead = la;
                    }
                }


                yield return new TranscriptEvent(
                    track,
                    ret,
                    lastFinalTs,
                    totalAudioProcMs);

            }

            if (currentText.Length > 0 && lastFinalTs != totalAudioProcMs)
            {
                 
                 ret.Add(new TranscriptToken(
                                currentText,
                                lastFinalTs,
                                totalAudioProcMs,
                                true,
                                currentText.Contains("[n::")));
                 
                 yield return new TranscriptEvent(
                    track,
                    ret,
                    totalAudioProcMs,
                    totalAudioProcMs);
            }
        
            
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _call.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose gRPC call");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackState
    {
        public List<Item> BufferedItems { get; } = new();
        public long FinalAudioProcMs { get; set; }
        public long TotalAudioProcMs { get; set; }
    }
    
}
