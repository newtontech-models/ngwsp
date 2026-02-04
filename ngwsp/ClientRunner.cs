using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ngwsp;

public static class ClientRunner
{
    private static ILoggerFactory? LoggerFactory { get; set; }
    private static ILogger? Logger { get; set; }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> RunAsync(ClientOptions options, CancellationToken cancellationToken)
    {
        ConfigureLogging(options.LogLevel);
        Logger?.LogInformation("Starting client session");
        if (string.IsNullOrWhiteSpace(options.ProxyUrl))
        {
            Console.Error.WriteLine("--proxy-url is required");
            Logger?.LogWarning("Missing proxy URL");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Input))
        {
            Console.Error.WriteLine("--input is required");
            Logger?.LogWarning("Missing input path");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            Console.Error.WriteLine("--model is required");
            Logger?.LogWarning("Missing model");
            return 1;
        }

        if (!TryParseChunkSize(options.Reader, out var chunkSize, out var readerError))
        {
            Console.Error.WriteLine(readerError);
            Logger?.LogWarning("Invalid reader format: {Error}", readerError);
            return 1;
        }

        if (!TryParseWriter(options.Writer, out var writerMode, out var writerError))
        {
            Console.Error.WriteLine(writerError);
            Logger?.LogWarning("Invalid writer format: {Error}", writerError);
            return 1;
        }

        LexiconDefinition? lexicon;
        try
        {
            lexicon = ClientLexiconParser.Parse(options.LexiconPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Logger?.LogError(ex, "Failed to parse lexicon");
            return 1;
        }

        await using var input = OpenInput(options.Input);
        var (output, ownsOutput) = OpenOutput(options.Output);
        using var socket = new ClientWebSocket();

        if (!TryConfigureAuth(options, socket, out var proxyUri, out var authError))
        {
            Console.Error.WriteLine(authError);
            Logger?.LogWarning("Invalid auth settings: {Error}", authError);
            return 1;
        }

        try
        {
            await socket.ConnectAsync(proxyUri, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException)
        {
            var connectError = FormatConnectFailure(ex);
            Console.Error.WriteLine(connectError);
            Logger?.LogWarning(ex, "WebSocket connect failed: {Error}", connectError);
            return 1;
        }

        var initPayload = BuildInitConfig(options.Model, lexicon);
        var initBytes = Encoding.UTF8.GetBytes(initPayload);
        await socket.SendAsync(initBytes, WebSocketMessageType.Text, true, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var outputWriter = new ClientOutputWriter(output, ownsOutput, writerMode, options.Flush);
        var receiveTask = ReceiveLoopAsync(socket, outputWriter, cts);

        await SendAudioAsync(socket, input, chunkSize, cts.Token);
        await socket.SendAsync(Array.Empty<byte>(), WebSocketMessageType.Binary, true, cts.Token);

        var exitCode = await receiveTask;
        await outputWriter.DisposeAsync();
        Logger?.LogInformation("Client session finished with code {ExitCode}", exitCode);
        return exitCode;
    }

    private static string FormatConnectFailure(Exception exception)
    {
        if (TryFindHttpStatus(exception, out var status))
        {
            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
            {
                return $"Proxy rejected the WebSocket upgrade ({(int)status} {status}). Check your API key and auth type.";
            }

            return $"Proxy rejected the WebSocket upgrade ({(int)status} {status}).";
        }

        var message = exception.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            message = exception.GetType().Name;
        }

        return $"WebSocket connect failed: {message}";
    }

    private static bool TryFindHttpStatus(Exception exception, out HttpStatusCode status)
    {
        status = default;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http && http.StatusCode is not null)
            {
                status = http.StatusCode.Value;
                return true;
            }

            var message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            // Common .NET ClientWebSocket failure format:
            // "The server returned status code '401' when status code '101' was expected."
            var idx = message.IndexOf("status code '", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            idx += "status code '".Length;
            var end = message.IndexOf('\'', idx);
            if (end <= idx)
            {
                continue;
            }

            var codePart = message[idx..end];
            if (int.TryParse(codePart, out var code) && Enum.IsDefined(typeof(HttpStatusCode), code))
            {
                status = (HttpStatusCode)code;
                return true;
            }
        }

        return false;
    }

    private static bool TryConfigureAuth(
        ClientOptions options,
        ClientWebSocket socket,
        out Uri proxyUri,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(options.ProxyUrl))
        {
            proxyUri = new Uri("ws://localhost:8080/ws");
            error = "--proxy-url is required";
            return false;
        }

        if (!Uri.TryCreate(options.ProxyUrl, UriKind.Absolute, out var baseUri))
        {
            proxyUri = new Uri("ws://localhost:8080/ws");
            error = "--proxy-url must be a valid absolute URL";
            return false;
        }

        proxyUri = baseUri;

        var normalized = string.IsNullOrWhiteSpace(options.AuthType)
            ? "none"
            : options.AuthType.Trim().ToLowerInvariant();

        if (normalized == "none")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            error = "--api-key is required when --auth-type is set";
            return false;
        }

        switch (normalized)
        {
            case "none":
                return true;
            case "header":
                socket.Options.SetRequestHeader("Authorization", options.ApiKey);
                return true;
            case "subprotocol":
                socket.Options.AddSubProtocol(options.ApiKey);
                return true;
            case "query":
                var builder = new UriBuilder(baseUri);
                var query = builder.Query;
                var prefix = string.IsNullOrWhiteSpace(query) ? string.Empty : query.TrimStart('?') + "&";
                builder.Query = $"{prefix}authorization={Uri.EscapeDataString(options.ApiKey)}";
                proxyUri = builder.Uri;
                return true;
            default:
                error = "--auth-type must be header|subprotocol|query";
                return false;
        }
    }

    private static void ConfigureLogging(string? logLevel)
    {
        if (!LogLevelParser.TryParse(logLevel, out var level))
        {
            return;
        }

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.SetMinimumLevel(level);
        });
        Logger = LoggerFactory.CreateLogger("ngwsp.client");
    }

    private static async Task SendAudioAsync(ClientWebSocket socket, Stream input, int chunkSize, CancellationToken cancellationToken)
    {
        var buffer = new byte[chunkSize];
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            var chunk = buffer.AsMemory(0, bytesRead);
            await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, cancellationToken);
        }
    }

    private static async Task<int> ReceiveLoopAsync(ClientWebSocket socket, ClientOutputWriter writer, CancellationTokenSource cts)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0;
            }

            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(stream.ToArray());
            stream.SetLength(0);

            if (TryParseError(json, out var error))
            {
                await writer.WriteAsync(json, cts.Token);
                Console.Error.WriteLine($"{error.Code}: {error.Message}");
                cts.Cancel();
                return 2;
            }

            if (IsFinished(json))
            {
                await writer.WriteAsync(json, cts.Token);
                return 0;
            }

            await writer.WriteAsync(json, cts.Token);
        }

        return 0;
    }

    private static string BuildInitConfig(string model, LexiconDefinition? lexicon)
    {
        var payload = new
        {
            model,
            lexicon = lexicon is null
                ? null
                : new
                {
                    rewrite_terms = lexicon.RewriteTerms.Select(term => new
                    {
                        source = term.Source,
                        target = term.Target
                    })
                }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static Stream OpenInput(string input)
    {
        if (input == "-")
        {
            return Console.OpenStandardInput();
        }

        return File.OpenRead(input);
    }

    private static (Stream Stream, bool OwnsStream) OpenOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || output == "-")
        {
            return (Console.OpenStandardOutput(), false);
        }

        return (File.Create(output), true);
    }

    private static bool TryParseChunkSize(string? reader, out int chunkSize, out string? error)
    {
        chunkSize = 4096;
        error = null;
        var value = string.IsNullOrWhiteSpace(reader) ? "raw:4096" : reader;
        if (!value.StartsWith("raw:", StringComparison.OrdinalIgnoreCase))
        {
            error = "--reader must be raw:<chunkSize>";
            return false;
        }

        var sizePart = value[4..];
        if (!int.TryParse(sizePart, out chunkSize) || chunkSize <= 0)
        {
            error = "--reader chunk size must be a positive integer";
            return false;
        }

        return true;
    }

    private static bool TryParseWriter(string? writer, out OutputMode mode, out string? error)
    {
        mode = OutputMode.Json;
        error = null;
        var value = string.IsNullOrWhiteSpace(writer) ? "json" : writer;
        var normalized = value.Split(':', 2)[0].ToLowerInvariant();
        switch (normalized)
        {
            case "json":
                mode = OutputMode.Json;
                return true;
            case "text":
                mode = OutputMode.Text;
                return true;
            case "console":
                mode = OutputMode.Console;
                return true;
            default:
                error = "--writer must be json|text|console";
                return false;
        }
    }

    private static bool IsFinished(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("finished", out var finished))
            {
                return finished.ValueKind == JsonValueKind.True;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryParseError(string json, out ProxyError error)
    {
        error = new ProxyError(string.Empty, string.Empty);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error_code", out var codeElement))
            {
                return false;
            }

            var message = doc.RootElement.TryGetProperty("error_message", out var messageElement)
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;

            error = new ProxyError(codeElement.GetString() ?? string.Empty, message);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class ClientOutputWriter : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly StreamWriter _writer;
        private readonly OutputMode _mode;
        private readonly bool _flush;
        private readonly bool _ownsStream;
        private readonly bool _useInteractiveConsole;
        private readonly ConsoleWriter? _consoleWriter;

        public ClientOutputWriter(Stream stream, bool ownsStream, OutputMode mode, bool flush)
        {
            _stream = stream;
            _writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            _mode = mode;
            _flush = flush;
            _ownsStream = ownsStream;
            _useInteractiveConsole = mode == OutputMode.Console && !ownsStream && !Console.IsOutputRedirected;
            _consoleWriter = _useInteractiveConsole ? new ConsoleWriter() : null;
        }

        public async Task WriteAsync(string json, CancellationToken cancellationToken)
        {
            switch (_mode)
            {
                case OutputMode.Json:
                    await _writer.WriteLineAsync(json);
                    break;
                case OutputMode.Text:
                    if (TryParseTokens(json, out var finalText, out _, out _))
                    {
                        if (!string.IsNullOrEmpty(finalText))
                        {
                            await _writer.WriteAsync(finalText);
                        }
                    }
                    break;
                case OutputMode.Console:
                    if (TryParseConsoleTokens(json, out var finalConsoleText, out var lookaheadConsoleText))
                    {
                        if (_consoleWriter is null)
                        {
                            if (!string.IsNullOrEmpty(finalConsoleText))
                            {
                                await _writer.WriteAsync(finalConsoleText);
                            }
                        }
                        else
                        {
                            await _consoleWriter.WriteAsync(finalConsoleText, lookaheadConsoleText, cancellationToken);
                        }
                    }
                    break;
            }

            if (_flush)
            {
                if (_useInteractiveConsole)
                {
                    await Console.Out.FlushAsync(cancellationToken);
                }
                else
                {
                    await _writer.FlushAsync(cancellationToken);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_useInteractiveConsole)
            {
                if (_consoleWriter is not null)
                {
                    await _consoleWriter.CompleteAsync();
                }
                await Console.Out.FlushAsync();
            }
            await _writer.FlushAsync();
            _writer.Dispose();
            if (_ownsStream)
            {
                _stream.Dispose();
            }
        }

        private static bool TryParseConsoleTokens(string json, out string finalDeltaText, out string lookaheadText)
        {
            finalDeltaText = string.Empty;
            lookaheadText = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("tokens", out var tokensElement) ||
                    tokensElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var token in tokensElement.EnumerateArray())
                {
                    if (!token.TryGetProperty("text", out var textElement))
                    {
                        continue;
                    }

                    var text = textElement.GetString();
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var nonSpeech = token.TryGetProperty("nonspeech", out var nonSpeechElement)
                        && nonSpeechElement.ValueKind == JsonValueKind.True;
                    if (nonSpeech)
                    {
                        continue;
                    }

                    var tokenIsFinal = true;
                    if (token.TryGetProperty("is_final", out var tokenFinalElement))
                    {
                        tokenIsFinal = tokenFinalElement.ValueKind == JsonValueKind.True;
                    }

                    if (tokenIsFinal)
                    {
                        finalDeltaText += text;
                    }
                    else
                    {
                        lookaheadText += text;
                    }
                }

                return finalDeltaText.Length > 0 || lookaheadText.Length > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryParseTokens(string json, out string finalText, out string lookaheadText, out bool hasLookahead)
        {
            finalText = string.Empty;
            lookaheadText = string.Empty;
            hasLookahead = false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("tokens", out var tokensElement) ||
                    tokensElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var token in tokensElement.EnumerateArray())
                {
                    if (!token.TryGetProperty("text", out var textElement))
                    {
                        continue;
                    }

                    var text = textElement.GetString();
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var tokenIsFinal = true;
                    if (token.TryGetProperty("is_final", out var tokenFinalElement))
                    {
                        tokenIsFinal = tokenFinalElement.ValueKind == JsonValueKind.True;
                    }

                    if (tokenIsFinal)
                    {
                        finalText += text;
                    }
                    else
                    {
                        lookaheadText += text;
                        hasLookahead = true;
                    }
                }

                return finalText.Length > 0 || lookaheadText.Length > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private enum OutputMode
    {
        Json,
        Text,
        Console
    }
}
