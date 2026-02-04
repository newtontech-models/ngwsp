# CLI Client Guide

This document describes the `ngwsp client` command, its CLI options, and the lexicon
rewrite rules file format.

Related documentation:
- [`docs/api.md`](api.md) (WebSocket API)
- [`docs/proxy.md`](proxy.md) (proxy CLI options and env vars)
- [`docs/client-implementation.md`](client-implementation.md) (client integration guidance)
- [`docs/agent-client-howto.md`](agent-client-howto.md) (agent/robot quickstart)

## Command

```bash
dotnet run --project ngwsp -- client --model <model> --input <audio-file>
```

Required:
- `--model` (InitConfig model)
- `--input` (path to audio file, or `-` for stdin)

## Options

- `--proxy-url` (default: `ws://localhost:8080/ws`)
  - WebSocket proxy URL.
- `-i, --input`
  - Input audio file path. Use `-` for stdin.
- `-o, --output` (default: stdout)
  - Output path for transcript events. Use `-` for stdout.
- `-r, --reader` (default: `raw:4096`)
  - Reader format. Only `raw:<chunkSize>` is supported.
- `-w, --writer` (default: `json`)
  - Output mode: `json`, `text`, or `console`.
- `-f, --flush`
  - Flush output on each write.
- `-m, --mode` (unused)
- `--retry` (unused)
- `--audio_format` (unused)
- `--audio_channel` (unused)
- `-p, --pipe` (unused)
- `--lexicon`
  - Path to a lexicon rewrite rules file (see below).
- `--model`
  - Model name used in InitConfig.
- `--auth-type` (default: `none`)
  - `none`, `header`, `subprotocol`, or `query`.
- `--api-key` (default: `test_api_key`)
  - API key used when `--auth-type` is not `none`.
- `--log-level`
  - Log level (Trace, Debug, Information, Warning, Error).
- `--env-file` (default: `.env`)
  - Path to a `.env` file (global option).

## Auth Behavior

- `--auth-type none` sends no API key.
- `--auth-type header` uses `Authorization: <key>` (raw API key).
- `--auth-type subprotocol` sends the URI-escaped key via `Sec-WebSocket-Protocol` (the proxy decodes before comparing).
- `--auth-type query` appends `?authorization=<key>` to the URL.

## Lexicon Rewrite Rules File Format (Required for `--lexicon`)

Each non-empty line must define a rewrite rule in the form:

```
source: target
```

Rules:
- Lines starting with `#` are comments and ignored.
- Empty lines are ignored.
- The first `:` splits source and target.
- Leading/trailing whitespace is trimmed.
- Optional surrounding quotes are stripped from each side.
- Both source and target must be non-empty after trimming.

### Example

```
# Replace product names with branded versions
acme: ACME
"road runner": "RoadRunner"
```

The client converts these rules into InitConfig `lexicon.rewrite_terms`.
