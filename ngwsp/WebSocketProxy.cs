using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ngwsp;

public sealed class WebSocketProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private readonly ILogger<WebSocketProxy> _logger;
    private readonly MetricsStore _metrics;
    private readonly ServerOptions _options;
    private readonly IUpstreamReadiness _upstreamReadiness;
    private readonly IGrpcSpeechAdapter _grpcAdapter;

    public WebSocketProxy(
        ILogger<WebSocketProxy> logger,
        MetricsStore metrics,
        ServerOptions options,
        IUpstreamReadiness upstreamReadiness,
        IGrpcSpeechAdapter grpcAdapter)
    {
        _logger = logger;
        _metrics = metrics;
        _options = options;
        _upstreamReadiness = upstreamReadiness;
        _grpcAdapter = grpcAdapter;
    }

    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!TryAuthorize(context, out var selectedSubProtocol))
        {
            return;
        }

        using var socket = selectedSubProtocol is null
            ? await context.WebSockets.AcceptWebSocketAsync()
            : await context.WebSockets.AcceptWebSocketAsync(selectedSubProtocol);
        var sessionId = Guid.NewGuid().ToString("n");
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["session_id"] = sessionId });

        _metrics.SessionStarted();
        try
        {
            await RunSessionAsync(socket, cancellationToken);
        }
        finally
        {
            _metrics.SessionEnded();
        }
    }

    private bool TryAuthorize(HttpContext context, out string? selectedSubProtocol)
    {
        selectedSubProtocol = null;
        if (!string.Equals(_options.ClientAuthMode, "api_key", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var allowedKeys = ParseApiKeys(_options.ClientApiKey);
        if (allowedKeys.Length == 0)
        {
            _logger.LogError("Client auth is enabled but no API keys are configured");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return false;
        }

        if (TryMatchBearerHeader(context.Request.Headers, allowedKeys, out var headerKey))
        {
            return true;
        }

        if (context.Request.Query.TryGetValue("api_key", out var queryValue))
        {
            foreach (var value in queryValue)
            {
                if (allowedKeys.Contains(value, StringComparer.Ordinal))
                {
                    return true;
                }
            }
        }

        if (!context.Request.Headers.TryGetValue("Sec-WebSocket-Protocol", out var headerValues))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }

        foreach (var value in headerValues)
        {
            foreach (var protocol in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (allowedKeys.Contains(protocol, StringComparer.Ordinal))
                {
                    selectedSubProtocol = protocol;
                    return true;
                }
            }
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

    private static bool TryMatchBearerHeader(IHeaderDictionary headers, string[] allowedKeys, out string? key)
    {
        key = null;
        if (!headers.TryGetValue("Authorization", out var authValues))
        {
            return false;
        }

        foreach (var value in authValues)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (!trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = trimmed[7..].Trim();
            if (allowedKeys.Contains(candidate, StringComparer.Ordinal))
            {
                key = candidate;
                return true;
            }
        }

        return false;
    }

    private static string[] ParseApiKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task RunSessionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var initReceived = false;
        IGrpcSpeechSession? session = null;
        InitConfig? initConfig = null;
        Task? upstreamTask = null;
        using var sendLock = new SemaphoreSlim(1, 1);
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(socket, cancellationToken);
                if (message.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (message.MessageType == WebSocketMessageType.Text)
                {
                    if (initReceived)
                    {
                        await SendErrorAsync(socket,
                            new ProxyError(ErrorCodes.ProtocolError, "InitConfig already received"),
                            cancellationToken,
                            sendLock);
                        return;
                    }

                    var parseResult = InitConfigParser.Parse(message.Payload);
                    if (!parseResult.Success)
                    {
                        var error = parseResult.Error
                            ?? new ProxyError(ErrorCodes.InvalidInitConfig, "InitConfig validation failed");
                        await SendErrorAsync(socket, error, cancellationToken, sendLock);
                        return;
                    }

                    try
                    {
                    var config = parseResult.Config!;
                    initConfig = config;
                    session = await _grpcAdapter.StartSessionAsync(config, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await SendErrorAsync(socket,
                            new ProxyError(ErrorCodes.ProtocolError, $"Upstream error: {ex.Message}"),
                            cancellationToken,
                            sendLock);
                        return;
                    }

                    if (!TrySelectTranscriptTrack(initConfig.Model, out var selectedTrack, out var trackError))
                    {
                        await SendErrorAsync(socket,
                            new ProxyError(ErrorCodes.ProtocolError, trackError),
                            cancellationToken,
                            sendLock);
                        return;
                    }

                    upstreamTask = ForwardTranscriptsAsync(socket, session, initConfig, selectedTrack, sendLock, sessionCts.Token);
                    initReceived = true;
                    continue;
                }

                if (message.MessageType == WebSocketMessageType.Binary)
                {
                    _metrics.AddBytesIn(message.Payload.LongLength);

                    if (!initReceived)
                    {
                        await SendErrorAsync(socket,
                            new ProxyError(ErrorCodes.ProtocolError, "InitConfig must be the first message"),
                            cancellationToken,
                            sendLock);
                        return;
                    }

                    if (message.Payload.Length == 0)
                    {
                        if (session is not null)
                        {
                            await session.CompleteAsync(cancellationToken);
                        }

                        if (upstreamTask is not null)
                        {
                            await upstreamTask;
                        }

                        await SendFinishedAsync(socket, cancellationToken, sendLock);
                        return;
                    }

                    if (!_upstreamReadiness.IsReady)
                    {
                        await SendErrorAsync(socket,
                            new ProxyError(ErrorCodes.BufferOverflow, "Upstream not ready"),
                            cancellationToken,
                            sendLock);
                        return;
                    }

                    if (session is null)
                    {
                        await SendErrorAsync(socket,
                            new ProxyError(ErrorCodes.ProtocolError, "Upstream session not initialized"),
                            cancellationToken,
                            sendLock);
                        return;
                    }

                    await session.SendAudioAsync(message.Payload, cancellationToken);
                    continue;
                }
            }
        }
        finally
        {
            sessionCts.Cancel();
            if (upstreamTask is not null)
            {
                try
                {
                    await upstreamTask;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Upstream task failed during shutdown");
                }
            }
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
    }

    private static async Task<WebSocketMessage> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new WebSocketMessage(WebSocketMessageType.Close, Array.Empty<byte>());
            }
            catch (ObjectDisposedException)
            {
                return new WebSocketMessage(WebSocketMessageType.Close, Array.Empty<byte>());
            }
            catch (WebSocketException)
            {
                return new WebSocketMessage(WebSocketMessageType.Close, Array.Empty<byte>());
            }
            catch (IOException)
            {
                return new WebSocketMessage(WebSocketMessageType.Close, Array.Empty<byte>());
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new WebSocketMessage(result.MessageType, Array.Empty<byte>());
            }

            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }
        }
        while (!result.EndOfMessage);

        return new WebSocketMessage(result.MessageType, stream.ToArray());
    }

    private async Task SendFinishedAsync(WebSocket socket, CancellationToken cancellationToken, SemaphoreSlim sendLock)
    {
        var payload = JsonSerializer.Serialize(new { finished = true }, JsonOptions);
        await SendTextAsync(socket, payload, cancellationToken, sendLock);
        await SafeCloseAsync(socket, WebSocketCloseStatus.NormalClosure, "finished", cancellationToken);
    }

    private async Task SendErrorAsync(WebSocket socket, ProxyError error, CancellationToken cancellationToken)
    {
        using var sendLock = new SemaphoreSlim(1, 1);
        await SendErrorAsync(socket, error, cancellationToken, sendLock);
    }

    private async Task SendErrorAsync(WebSocket socket, ProxyError error, CancellationToken cancellationToken, SemaphoreSlim sendLock)
    {
        var payload = JsonSerializer.Serialize(new { error_code = error.Code, error_message = error.Message }, JsonOptions);
        await SendTextAsync(socket, payload, cancellationToken, sendLock);
        await SafeCloseAsync(socket, WebSocketCloseStatus.ProtocolError, error.Code, cancellationToken);
    }

    private async Task SendTextAsync(WebSocket socket, string payload, CancellationToken cancellationToken, SemaphoreSlim sendLock)
    {
        await sendLock.WaitAsync(cancellationToken);
        var bytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            _metrics.AddBytesOut(bytes.Length);
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task SafeCloseAsync(
        WebSocket socket,
        WebSocketCloseStatus closeStatus,
        string statusDescription,
        CancellationToken cancellationToken)
    {
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task ForwardTranscriptsAsync(
        WebSocket socket,
        IGrpcSpeechSession session,
        InitConfig initConfig,
        string transcriptTrack,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var transcript in session.ReadTranscriptsAsync(transcriptTrack, cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    track = transcript.Track,
                    tokens = transcript.Tokens.Select(token => new
                    {
                        text = token.Text,
                        start_ms = token.StartMs,
                        end_ms = token.EndMs,
                        is_final = token.IsFinal,
                        nonspeech = token.NonSpeech ? (bool?)true : null
                    }),
                    final_audio_proc_ms = transcript.FinalAudioProcMs,
                    total_audio_proc_ms = transcript.TotalAudioProcMs
                }, JsonOptions);
                await SendTextAsync(socket, payload, cancellationToken, sendLock);
            }
        }
        catch (Exception ex)
        {
            await SendErrorAsync(socket,
                new ProxyError(ErrorCodes.ProtocolError, $"Upstream error: {ex.Message}"),
                cancellationToken,
                sendLock);
        }
    }

    private static bool TrySelectTranscriptTrack(string model, out string track, out string error)
    {
        track = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(model))
        {
            error = "Model is required";
            return false;
        }

        var parts = model
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant())
            .ToArray();

        var hasAtran = parts.Contains("atran", StringComparer.Ordinal);
        var hasDtran = parts.Contains("dtran", StringComparer.Ordinal);

        if (hasAtran && hasDtran)
        {
            error = $"Unsupported model '{model}': contains both atran and dtran";
            return false;
        }

        if (hasAtran)
        {
            track = "pnc";
            return true;
        }

        if (hasDtran)
        {
            track = "tpc";
            return true;
        }

        error = $"Unsupported model '{model}': only atran-* (pnc) and dtran-* (tpc) are supported";
        return false;
    }

    private sealed record WebSocketMessage(WebSocketMessageType MessageType, byte[] Payload);
    
}
