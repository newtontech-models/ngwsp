# ngwsp

WebSocket proxy for a gRPC Speech API with streaming mechanics and a custom InitConfig schema.

## Quickstart

1) Run the server

```bash
dotnet run --project ngwsp -- proxy --grpc-target https://usr:psw@xxx.yyy.zz
```

2) Connect a WebSocket client to the proxy

- WebSocket endpoint: `ws://localhost:8080/ws`
- First message: InitConfig JSON (text)
- Audio: binary frames
- End of stream: empty binary frame

3) Environment variable prefix

- All env vars are prefixed with `NGWSP_` (e.g., `NGWSP_LISTEN_URL`).

4) Run the CLI client (defaults to `ws://localhost:8080/ws`)

```bash
dotnet run --project ngwsp -- client --model <model> -i <audio-file>
```

Auth (optional):
- `--auth-type none|header|subprotocol|query` (default: `none`)
- `--api-key <key>` (default: `test_api_key`)
  - When `--auth-type none`, the client does not send any API key.

5) Test the browser UI (live dictation)

- Start the proxy and open `http://localhost:8080/ui` in a web browser.
- Click **Start**, allow microphone access, and speak to verify live transcription.


## Documentation

- [`docs/api.md`](docs/api.md) (WebSocket API)
- [`docs/proxy.md`](docs/proxy.md) (proxy CLI options and env vars)
- [`docs/client.md`](docs/client.md) (client CLI options and lexicon file format)
- [`docs/client-implementation.md`](docs/client-implementation.md) (client integration guidance)
- [`docs/agent-client-howto.md`](docs/agent-client-howto.md) (agent/robot client quickstart)

## Defaults

- Listen URL: `http://0.0.0.0:8080`
- Health endpoints:
  - `GET /health/live`
  - `GET /health/ready`
- Metrics endpoint:
  - `GET /metrics`
