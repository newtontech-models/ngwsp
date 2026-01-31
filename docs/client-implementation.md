# Client Implementation Guide

This document gives practical guidance for building clients that speak to the ngwsp WebSocket proxy.

Related documentation:
- [`docs/api.md`](api.md) (WebSocket API and schemas)
- [`docs/client.md`](client.md) (CLI client options and lexicon file format)
- [`docs/proxy.md`](proxy.md) (proxy CLI options and env vars)
- [`docs/agent-client-howto.md`](agent-client-howto.md) (agent/robot quickstart)

## Audio Capture and Chunking

Recommended defaults:
- Send binary audio frames in **small, regular chunks**.
- Aim for **100–250ms** per chunk to balance latency and overhead.
- Keep chunk sizes stable to reduce jitter in downstream processing.

Browser capture (MediaRecorder):
- Use `audio/webm; codecs=opus` where supported.
- Call `recorder.start(100)` for ~100ms cadence.

## InitConfig First

Always send InitConfig JSON as the **first** WebSocket message:

```json
{ "model": "your-model" }
```

If InitConfig is missing, invalid, or sent twice, the server will return an error and close.

## Backpressure and Buffering

The proxy does **not** buffer large amounts of audio.
If the upstream is not ready, the proxy returns:

```json
{ "error_code": "buffer_overflow", "error_message": "Upstream not ready" }
```

Recommended behavior:
- Stop sending audio on any error.
- Reconnect only when you can safely restart the session.

## Reconnect Strategy

Do reconnect on:
- transient network failures
- WebSocket timeouts

Do **not** reconnect on:
- `invalid_init_config`
- `unsupported_*` errors
- `protocol_error`

Those indicate a client-side issue; fix and restart instead.

## Final vs Non-Final Rendering (Quick Rule)

- Final tokens: permanent transcript
- Non-final tokens: temporary overlay

Render as:

```
final_text + lookahead_text
```

When final tokens arrive, append them and clear the lookahead.
If no non-final tokens arrive, keep the overlay empty.

## Examples

### Browser Client Outline

1) Open WebSocket connection
2) Send InitConfig JSON (text frame)
3) Start MediaRecorder
4) Send binary audio frames
5) Render transcript messages
6) Send empty binary frame to finish

### Generic WebSocket Client Outline

1) Connect to `/ws`
2) Send InitConfig JSON
3) Stream binary audio frames
4) Parse transcript events as JSON
5) On `{ "finished": true }`, close the connection

## Gotchas

- **InitConfig must be first** (text frame).
- **Empty binary frame** is the end-of-stream signal.
- **Non-final tokens can change**; do not append them permanently.
- **Auth mode**: browser WebSocket APIs cannot send `Authorization` headers.
- **Lexicon**: rewrite rules are `source: target` per line.

## Troubleshooting

- `protocol_error`: verify InitConfig-first and no extra text frames.
- `buffer_overflow`: reduce chunk size or wait for upstream readiness.
- Empty transcript: check model name and auth settings.
- No lookahead updates: your model might not emit non-final tokens.
