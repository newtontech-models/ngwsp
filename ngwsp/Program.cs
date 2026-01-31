using System.CommandLine;

namespace ngwsp;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var envFile = new Option<string?>("--env-file", () => ".env", "Path to .env file");
        var logLevel = new Option<string?>("--log-level", "Log level (Trace, Debug, Information, Warning, Error)");

        var root = new RootCommand("ngwsp command line interface");
        root.AddGlobalOption(envFile);

        root.AddCommand(BuildProxyCommand(envFile, logLevel));
        root.AddCommand(BuildClientCommand(envFile, logLevel));

        return await root.InvokeAsync(args);
    }

    private static Command BuildProxyCommand(Option<string?> envFile, Option<string?> logLevelOption)
    {
        var listenUrl = new Option<string?>("--listen-url", "HTTP listen URL, e.g. http://0.0.0.0:8080");
        var corsOrigins = new Option<string?>("--cors-origins", "Comma-separated CORS origins");

        var tlsCertPath = new Option<string?>("--tls-cert-path", "TLS certificate path");
        var tlsKeyPath = new Option<string?>("--tls-key-path", "TLS key path");

        var grpcTarget = new Option<string?>("--grpc-target", "Upstream gRPC target");
        var grpcUseTls = new Option<string?>("--grpc-use-tls", "Upstream gRPC TLS setting (true/false)");
        var grpcCaPath = new Option<string?>("--grpc-ca-path", "Upstream gRPC CA path");
        var grpcTimeoutMs = new Option<string?>("--grpc-timeout-ms", "Upstream gRPC timeout in milliseconds");

        var clientAuthMode = new Option<string?>("--client-auth-mode", "Client auth mode (none|api_key)");
        var clientApiKey = new Option<string?>("--client-api-key", "Client API key");

        var models = new Option<string?>("--models", "Comma-separated allowed models");
        var defaultModel = new Option<string?>("--default-model", "Default model name");
        var lexicon = new Option<string?>("--lexicon", "Lexicon preset/path/inline definition");
        var uiWsUrl = new Option<string?>("--ui-ws-url", "UI WebSocket endpoint (ws://...)");
        var audioBufferFrames = new Option<string?>("--audio-buffer-frames", "Max number of audio frames to buffer between WS receive and upstream gRPC write");

        var proxyCommand = new Command("proxy", "Run the WebSocket proxy server")
        {
            listenUrl,
            corsOrigins,
            logLevelOption,
            tlsCertPath,
            tlsKeyPath,
            grpcTarget,
            grpcUseTls,
            grpcCaPath,
            grpcTimeoutMs,
            clientAuthMode,
            clientApiKey,
            models,
            defaultModel,
            lexicon,
            uiWsUrl,
            audioBufferFrames
        };

        proxyCommand.SetHandler(async context =>
        {
            EnvFileLoader.Load(context.ParseResult.GetValueForOption(envFile));
            var result = context.ParseResult;
            var options = ConfigResolver.Resolve(
                result.GetValueForOption(listenUrl),
                result.GetValueForOption(corsOrigins),
                result.GetValueForOption(logLevelOption),
                result.GetValueForOption(tlsCertPath),
                result.GetValueForOption(tlsKeyPath),
                result.GetValueForOption(grpcTarget),
                result.GetValueForOption(grpcUseTls),
                result.GetValueForOption(grpcCaPath),
                result.GetValueForOption(grpcTimeoutMs),
                result.GetValueForOption(clientAuthMode),
                result.GetValueForOption(clientApiKey),
                result.GetValueForOption(models),
                result.GetValueForOption(defaultModel),
                result.GetValueForOption(lexicon),
                result.GetValueForOption(uiWsUrl),
                result.GetValueForOption(audioBufferFrames));

            if (string.IsNullOrWhiteSpace(options.GrpcTarget))
            {
                Console.Error.WriteLine("--grpc-target is required (or set NGWSP_GRPC_TARGET).");
                context.ExitCode = 1;
                return;
            }

            await AppRunner.RunAsync(options, context.GetCancellationToken());
        });

        return proxyCommand;
    }

    private static Command BuildClientCommand(Option<string?> envFile, Option<string?> logLevelOption)
    {
        var input = new Option<string?>("--input", "Input audio file path");
        input.AddAlias("-i");

        var output = new Option<string?>("--output", "Output path (default: stdout)");
        output.AddAlias("-o");

        var reader = new Option<string?>("--reader", () => "raw:4096", "Reader format (raw:<chunkSize>)");
        reader.AddAlias("-r");

        var writer = new Option<string?>("--writer", () => "json", "Writer format (json|text|console)");
        writer.AddAlias("-w");

        var flush = new Option<bool>("--flush", "Flush output on each write");
        flush.AddAlias("-f");

        var mode = new Option<string?>("--mode", "Processing mode (unused)");
        mode.AddAlias("-m");
        var retry = new Option<string?>("--retry", "Retry config (unused)");
        var audioFormat = new Option<string?>("--audio_format", "Audio format override (unused)");
        var audioChannel = new Option<string?>("--audio_channel", "Audio channel override (unused)");
        var pipe = new Option<bool>("--pipe", "Pipe mode (unused)");
        pipe.AddAlias("-p");
        var lexicon = new Option<string?>("--lexicon", "Lexicon file with 'source:target' lines");
        var model = new Option<string?>("--model", "Model name for InitConfig");
        var proxyUrl = new Option<string?>("--proxy-url", () => "ws://localhost:8080/ws", "WebSocket proxy URL");
        var authType = new Option<string?>("--auth-type", () => "none", "Auth type (none|header|subprotocol|query)");
        var apiKey = new Option<string?>("--api-key", () => "test_api_key", "API key for proxy auth");

        var clientCommand = new Command("client", "Run the CLI test client")
        {
            input,
            output,
            reader,
            writer,
            flush,
            mode,
            retry,
            audioFormat,
            audioChannel,
            pipe,
            lexicon,
            model,
            proxyUrl,
            authType,
            apiKey,
            logLevelOption
        };

        clientCommand.SetHandler(async context =>
        {
            EnvFileLoader.Load(context.ParseResult.GetValueForOption(envFile));
            var result = context.ParseResult;

            var options = new ClientOptions(
                ProxyUrl: result.GetValueForOption(proxyUrl),
                Input: result.GetValueForOption(input),
                Output: result.GetValueForOption(output),
                Reader: result.GetValueForOption(reader),
                Writer: result.GetValueForOption(writer),
                Flush: result.GetValueForOption(flush),
                Mode: result.GetValueForOption(mode),
                Retry: result.GetValueForOption(retry),
                AudioFormat: result.GetValueForOption(audioFormat),
                AudioChannel: result.GetValueForOption(audioChannel),
                Pipe: result.GetValueForOption(pipe),
                LexiconPath: result.GetValueForOption(lexicon),
                Model: result.GetValueForOption(model),
                LogLevel: result.GetValueForOption(logLevelOption),
                AuthType: result.GetValueForOption(authType),
                ApiKey: result.GetValueForOption(apiKey));

            context.ExitCode = await ClientRunner.RunAsync(options, context.GetCancellationToken());
        });

        return clientCommand;
    }
}
