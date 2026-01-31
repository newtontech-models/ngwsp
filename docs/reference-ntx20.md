# Reference: ntx20

Reference copy used: `playground/ntx20-0.2.0` (tag/version 0.2.0).

## Files consulted

- `ntx20.api.proto/ntx/core20/engine.proto`
  - `Payload`, `Item`, `EngineService.Streaming` definitions used for gRPC integration.
- `docs/README.md`
- gRPC metadata headers (`Authorization`, `service`), config items (`audio-format`, `audio-channel`, `lexicon`), track semantics.
- `files/userlex.json`
  - lexicon item shape and `labels.hint` mapping.
- `ntx20/command/ws/Command.cs`
  - upstream header construction and `service` selection.

## What is mirrored

- gRPC bidi streaming over `EngineService.Streaming` with `Payload` messages.
- Config message structure (audio-format, audio-channel, lexicon).
- Auth header format (Basic auth) and `service` header usage.

## What differs

- WebSocket InitConfig schema (model/lexicon) is custom.
- Lexicon mapping uses `rewrite_terms` with `source` → `labels.hint` and `target` → item `s`.
- Proxy emits transcript JSON events instead of raw Payloads.
