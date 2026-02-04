# Agent/Robot Client How-To (WSS)

This is a short, explicit guide for building an automated client (bot/agent/robot) that talks to the ngwsp WebSocket proxy.

Related documentation:
- [`docs/api.md`](api.md) (full protocol schema)
- [`docs/client-implementation.md`](client-implementation.md) (implementation details and gotchas)

## Minimum Viable Flow

1) Open a WebSocket to `/ws`.
2) Send InitConfig JSON **as the first message** (text frame).
3) Stream audio as **binary** frames.
4) Receive transcript JSON events.
5) Send an **empty binary frame** to finish.
6) Read `{ "finished": true }`, then close.

## Required InitConfig

```json
{ "model": "your-model" }
```

Optional (lexicon):

```json
{
  "model": "your-model",
  "lexicon": {
    "rewrite_terms": [
      { "source": "acme", "target": "ACME" }
    ]
  }
}
```

## Rendering Rule (Final vs Non-Final)

- `is_final: true` → permanent text
- `is_final: false` → temporary overlay (replace each update)

Render as:

```
final_text + lookahead_text
```

## Pseudocode

```text
ws = connect("ws://host:port/ws")

send_text(ws, {"model": "your-model"})

while audio:
  send_binary(ws, audio_chunk)

send_binary(ws, empty)

for msg in ws:
  if msg.error_code:
    stop
  if msg.finished:
    close
  else:
    render_tokens(msg.tokens)
```

## Common Failure Reasons

- InitConfig missing/invalid → `invalid_init_config` or `protocol_error`.
- Binary audio before InitConfig → `protocol_error`.
- Upstream not ready → `buffer_overflow`.

## Auth Notes (Automated Clients)

If proxy auth is enabled (`--client-auth-mode api_key`), send the API key using one of:
- `Authorization: <key>`
- `Sec-WebSocket-Protocol: <key>` (URI-escaped; proxy decodes before comparing)
- `?authorization=<key>`

Pick exactly one method for a given client.
