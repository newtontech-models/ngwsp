# PLAN.md — WebSocket Proxy for gRPC Speech API
**Streaming behavior + custom init config (model/lexicon)**

## 0. Goal

Build a **WebSocket proxy server** that exposes **streaming behavior** (audio framing, end-of-stream semantics, result flow), while using a **custom initial configuration message** aligned with our capabilities (**model, lexicon**). The proxy bridges downstream WebSocket clients to an upstream **gRPC Speech API** using **bidirectional streaming** semantics.

Primary deliverables:
- A production-quality **CLI app** named `ngwsp` that runs the proxy (CLI + env params).
- A **browser dictation UI** (model selection + Start/Stop) that uses the proxy.
- Clear, implementation-ready **Markdown documentation** describing the protocol for end users and for another agent implementing clients.

---

## 1. Key Protocol Decision: Custom Initial Config

### 1.1 Rationale
Our upstream server capabilities differ from external configuration schemas. Therefore:
- The **first message** from client is **InitConfig JSON** with our schema:
  - `model`
  - `lexicon`

### 1.2 Compatibility scope
We aim to be compatible in:
- Streaming mechanics:
  - binary audio frames
  - empty frame end-of-stream
- Result flow:
  - partial/final transcript events
  - error and finished close behavior

We are **not compatible** in the **initial config payload**.

---

## 2. High-level Architecture

### 2.1 Components

1) **Proxy CLI (server)**
- Hosts inbound WebSocket server (WSS recommended).
- Requires InitConfig JSON as first message.
- Streams audio (binary frames) to upstream gRPC bidi stream.
- Relays upstream transcript events as JSON to client.

2) **gRPC Speech API Adapter**
- The only module that knows `.proto` and upstream call details.
- Public internal interface (example):
  - `StartSession(SessionConfig config)`
  - `SendAudio(ReadOnlyMemory<byte> chunk)`
  - `Complete()`
  - `Abort(Exception? reason)`
  - events/callbacks: `OnTranscript(TranscriptEvent)`, `OnError(ProxyError)`, `OnCompleted()`

3) **Browser dictation UI**
- Model selector, Start/Stop, transcript display.
- Captures microphone audio and streams to WebSocket.
- Sends InitConfig JSON first.

4) **Documentation**
- `docs/api.md` — protocol spec for users + client developers.
- `docs/client-implementation.md` — detailed guidance for implementing clients/SDKs.
- `docs/reference-ntx20.md` — what we borrowed from `ntx20` and what we changed.

---

## 3. Authoritative Reference: `newtontech-models/ntx20` (Mandatory)

The repository `newtontech-models/ntx20` is the **source of truth** for:
- protobuf message definitions
- bidi streaming semantics
- existing websocket proxying patterns and message flow

Required workflow:
- Copy `playground/ntx20-0.2.0` into the playground manually for reference.
- Use it to answer questions about protocol details and proxying logic.
- If we diverge from `ntx20`, document it explicitly in `docs/reference-ntx20.md`.

Decision to document:
- Use the manually copied `playground/ntx20-0.2.0` as the reference repo (not vendored).

---

## 4. Public WSS API (v1)

### 4.1 Connection lifecycle
1) Client connects to WebSocket endpoint.
2) Client sends **InitConfig JSON** (text message) as the **first** message.
3) Client sends audio as **binary WebSocket frames**.
4) Server sends transcript events as JSON (partial/final).
5) Client sends **empty binary frame** to signal end-of-stream.
6) Server sends a final `{ "finished": true }` message and closes.

### 4.2 InitConfig JSON schema (custom)
Exact schema to finalize in Iteration 2. Draft requirements:

Required:
- `model: string`

Optional:
- `lexicon: string | object` (path/preset name/inline definition; final behavior defined in Iteration 2)
- `audio: { format?, sample_rate_hz?, channels? }` (only if needed; do not require if we can infer)

Validation rules:
- If InitConfig missing required fields or contains unsupported values:
  - send error JSON and close
- If binary audio is received before InitConfig:
  - send error JSON and close
- If a second InitConfig is received:
  - send error JSON and close (protocol violation)

### 4.3 Transcript event JSON (target)
Emit a stable event model where feasible.

Minimum fields:
- `type: "transcript"`
- `is_final: boolean`
- `text: string`
Optional:
- `tokens: [{ text, start_ms?, end_ms?, confidence?, speaker? }]`

Completion:
- `{ "finished": true }` then close.

Errors:
- `{ "error_code": "…", "error_message": "…" }` then close.

---

## 5. CLI & Configuration

### 5.1 CLI parser
Use **System.CommandLine** (unless changed by explicit decision).

### 5.1.1 Runtime target
- .NET 9

### 5.1.2 CLI entrypoints
- The app binary name is `ngwsp`.
- Proxy server is invoked via subcommand: `ngwsp proxy`.
- A CLI test client will be invoked via subcommand: `ngwsp client`.

### 5.2 Configuration sources (priority)
1) CLI options
2) `.env` file via `--env-file` (defaults to `.env` in current directory)
3) Environment variables
4) Defaults

### 5.2.1 Environment variables (additional)
- `NGWSP_GRPC_TARGET` includes credentials in URL form, e.g. `https://user:password@cluster`
- `NG_MODELS` comma-separated list of allowed models

### 5.3 Draft CLI options
Server:
- `--listen-url`
- `--cors-origins`
- `--log-level`

TLS (optional):
- `--tls-cert-path`, `--tls-key-path`

Upstream gRPC:
- `--grpc-target`
- `--grpc-use-tls`
- `--grpc-ca-path`
- `--grpc-timeout-ms`

Client auth:
- `--client-auth-mode` (`none` | `api_key`)
- `--client-api-key` (or env)

Capabilities (defaults for clients, and/or allowed values list):
- `--models` (comma-separated list)
- `--default-model`
- `--lexicon` (path/preset/inline; define)

---

## 6. Internal Models & Mapping

### 6.1 Canonical internal model
Define canonical types:
- `SessionConfig { Model, Features, Lexicon, AudioFormat? }`
- `TranscriptEvent { IsFinal, Text, Tokens? }`
- `ProxyError { Code, Message, Details? }`

Mappings:
- InitConfig JSON ↔ `SessionConfig`
- `SessionConfig` ↔ upstream gRPC config messages (based on `ntx20` protos and upstream requirements)
- Upstream transcript events ↔ transcript JSON events

### 6.2 Proxying logic requirements
- Bounded buffering:
  - allow a small queue of audio frames before upstream is ready
  - clear behavior if buffer is exceeded (error, or backpressure via slower reads)
- Cancellation and shutdown:
  - when client disconnects, abort upstream cleanly
  - when upstream fails, inform client and close
- Ordering guarantees:
  - audio frames forwarded in order
  - transcript events forwarded in order
- Low latency:
  - minimize per-frame allocations
  - avoid blocking the receive loop

---

## 7. Browser Dictation UI (v1)

UI requirements:
- Model dropdown (from server-provided list or build-time list)
- Feature selection:
- Lexicon selection:
  - preset list and/or upload (define in Iteration 4)
- Start/Stop
- Transcript output area + connection status

Implementation approach:
- Serve static UI assets from the proxy (simplest) OR keep as separate dev workflow but deploy together in v1.

---

## 8. Documentation Deliverables

### 8.1 `docs/api.md` (Public API)
Must include:
- endpoint(s), TLS notes, auth
- lifecycle: InitConfig → binary audio frames → transcript events → empty frame end → finished + close
- InitConfig schema + examples
- transcript event schema + examples (partial and final)
- error schema + examples
- compatibility section:
  - what matches expected client behavior
  - what differs (InitConfig)

### 8.2 `docs/client-implementation.md` (For client implementers / other agents)
Must include:
- how to choose audio format and chunking sizes
- recommended reconnect logic and when NOT to reconnect
- backpressure/buffering guidance
- examples:
  - browser client outline
  - generic WS client outline
- “Gotchas” section (ordering, empty-frame semantics, timeouts)

### 8.3 `docs/reference-ntx20.md`
Must include:
- exact `ntx20` tag/commit used
- which proto files and code paths were referenced
- bidi streaming details extracted
- what we mirrored exactly vs what we changed

---

## 9. Quality, Observability, Security

Observability:
- structured logs
- session correlation id
- session id is **log-only** (do not send to clients)
- basic metrics:
  - active sessions
  - bytes in/out
  - time to first transcript event
  - upstream error counts

Health:
- `/health/live`
- `/health/ready` (includes upstream connectivity check if feasible)
- expose metrics for Prometheus scraping (HTTP endpoint; exact path TBD)

Security:
- WSS recommended (TLS termination via reverse proxy for now)
- API key auth for WSS:
  - client passes API key via `Sec-WebSocket-Protocol`
  - server reads allowed keys from `NGWSP_APIKEYS` (comma-separated)
- never leak secrets to browser clients

---

## 10. Testing Strategy (Applies to Every Iteration)

**Rule:** Every iteration must add or update tests that validate the new behavior.  
**Minimum bar:** at least one automated test (unit or integration) per iteration.

Tooling (recommended):
- xUnit
- FluentAssertions (optional)
- Integration tests:
  - start server on random port
  - connect using `ClientWebSocket`
- CI:
  - `dotnet test` runs on every push/PR

---

## 11. Iterations (Goals + Requirements + Tests)

### Iteration 1 — DONE: Repo skeleton + CLI + minimal WS server
**Goal:** A runnable CLI server that accepts WS connections and enforces “InitConfig first”.

Implementation requirements:
- solution + project layout
- CLI parsing + env mapping
- WS endpoint with:
  - first message must be InitConfig JSON
  - accept binary frames (no-op)
  - treat empty binary frame as end-of-stream
  - close behavior is deterministic:
    - empty binary frame from client triggers server close
    - error close uses JSON with underlying gRPC status or other error string/code
- logging + session id
- HTTP endpoints:
  - `/health/live`
  - `/health/ready`
  - Prometheus metrics endpoint (path TBD)
- README with quickstart (run, WS endpoint, env var prefix)
- .gitignore for .NET build artifacts

Tests (minimum):
- Integration: server starts and accepts WS connection.
- Integration: sending valid InitConfig first does not close with error.
- Integration: sending binary frame before InitConfig returns error and closes.
- Integration: sending empty binary frame triggers graceful close sequence.

Exit criteria:
- `dotnet run` starts server
- basic integration tests pass in CI

---

### Iteration 2 — DONE: Protocol hardening: InitConfig schema + response schema
**Goal:** Finalize and implement the custom InitConfig schema and stable response events.

Implementation requirements:
- strict InitConfig schema (model/lexicon) + validation
- stable JSON response event schema
- error messages are consistent and documented
- bounded buffering/backpressure policy defined and implemented

Tests (minimum):
- Unit: InitConfig parsing (valid cases).
- Unit: InitConfig validation failures (missing model, invalid lexicon).
- Integration: invalid InitConfig → error JSON + close.
- Integration: buffer overflow/backpressure behavior (defined expected outcome).

Exit criteria:
- protocol behavior documented in `docs/api.md` draft (may be incomplete)
- tests cover validation + error flow

---

### Iteration 3 — DONE: gRPC integration: bidi bridging based on `ntx20`
**Goal:** Real end-to-end proxying: WS audio → upstream gRPC → transcript events → WS.

Implementation requirements:
- reference `ntx20`:
  - identify `.proto` and bidi semantics
  - implement adapter accordingly
- import or generate proto stubs
- map SessionConfig → upstream config messages
- forward audio frames to upstream stream
- map upstream transcript events → JSON transcript events
- handle upstream failures and propagate errors to WS client

Tests (minimum):
- Unit: mapping tests (InitConfig → canonical → upstream request messages).
- Integration: run against a fake/in-process gRPC server:
  - verify audio frames are received upstream in order
  - upstream emits partial/final events
  - proxy forwards transcript JSON with correct sequencing
- Integration: upstream failure produces error JSON + close.

Exit criteria:
- successful transcription path verified by integration test
- `docs/reference-ntx20.md` created and filled with concrete references

---

### Iteration 3.5 — DONE: CLI test client for proxy
**Goal:** Provide a CLI test client to validate the proxy with audio files and JSONL output.

Implementation requirements:
- add `ngwsp client` subcommand
- ensure output binary name is `ngwsp`
- accept CLI params matching `playground/ntx20-0.2.0/ntx20/command/run/atran/Command.cs`:
  - `-i|--input`
  - `-o|--output`
  - `-r|--reader`
  - `-w|--writer`
  - `-f|--flush`
  - `-m|--mode`
  - `--retry`
  - `--audio_format`
  - `--audio_channel`
  - `-p|--pipe`
  - `--lexicon`
- add `--model` for InitConfig selection
- add `--log-level` for both proxy and client
- fail on startup if `--grpc-target` (or `NGWSP_GRPC_TARGET`) is missing
- remove `NG_MODELS` validation and pass model directly to gRPC
- align gRPC target parsing and metadata with `playground/ntx20-0.2.0/ntx20/command/ws/Command.cs`
- use a shared `GrpcChannel` in the adapter (constructed once, reused across sessions)
- select transcript track from InitConfig `model` (split by `-`): contains `atran` → `pnc`, contains `dtran` → `tpc`, both/other → protocol error
- derive token `is_final` from gRPC `la` tag (lookahead → `is_final=false`)
- align client `text` and `console` writers with `ntx20` semantics (ignore lookahead for text; console shows lookahead as non-final; no inserted spaces/newlines)
- emit transcript JSON with `tokens`, `final_audio_proc_ms`, and `total_audio_proc_ms` derived from `ts`/`txt` chunks
- emit a WS transcript message for every upstream payload, even when no tokens are released (empty `tokens` array)
- emit noise tokens with `nonspeech: true` instead of dropping them
- stream audio to the proxy over WebSocket
- emit transcript events as JSONL to stdout
- respect InitConfig schema (model/lexicon)
- enforce WebSocket API key auth when `--client-auth-mode api_key` (reject handshake before gRPC if missing/invalid `Sec-WebSocket-Protocol`)
- support API key auth variants for proxy + client (`Authorization: Bearer <key>`, `Sec-WebSocket-Protocol`, and `?api_key=<key>`)
- set auth defaults: proxy `client-auth-mode=none`, `client-api-key=test_api_key`; client `auth-type=none`, `api-key=test_api_key` (no auth sent unless auth-type is not `none`)

Tests (minimum):
- Integration: client connects to test proxy and sends InitConfig + audio frames
- Integration: client writes JSONL output for transcript events
- Integration: unauthorized WebSocket client is rejected when API key auth is enabled
- Integration: each API key auth variant is accepted when enabled

Exit criteria:
- `ngwsp client` can run against a local proxy and produce JSONL

---

### Iteration 4 — Browser dictation UI
**Goal:** A working browser UI demonstrating the protocol and enabling quick manual verification.

Implementation requirements:
- UI served (or packaged) with the proxy
- model selection + Start/Stop
- audio capture + streaming
- transcript rendering
- basic error handling and connection status

Tests (minimum):
- Integration: proxy serves the static UI assets (HTTP GET).
- Unit (optional but recommended): InitConfig generation in UI matches documented schema.
- E2E smoke (optional): Playwright test that loads page and connects.

Exit criteria:
- manual demo works end-to-end
- basic automated smoke coverage exists

---

### Iteration 5 — Documentation + polish (API + client implementer guidance)
**Goal:** Documentation is complete enough that:
- end users can integrate
- another agent can implement clients reliably

Implementation requirements:
- `docs/api.md` complete with examples and error cases
- `docs/client-implementation.md` complete with chunking/backpressure/reconnect guidance
- compatibility statement is explicit and unambiguous
- troubleshooting section added

Tests (minimum):
- Documentation build check if applicable (or lint for broken links)
- (Optional) “protocol conformance” integration test that asserts:
  - config-first enforced
  - empty frame end works
  - finished message is emitted

Exit criteria:
- docs reviewed for completeness and clarity
- CI green, no flaky tests

---

## 12. Open Questions (Must resolve before Iteration 3)
Resolved from `playground/ntx20-0.2.0` (see `docs/README.md` and ws command):

1) Upstream gRPC contract:
   - Bidi streaming via `EngineService.Streaming` with `Payload` messages (see `engine.proto`).
   - `Payload` contains `repeated Item chunk` and optional `track` string.
2) Auth and model selection:
   - gRPC metadata header `Authorization: Basic base64(username:password)` derived from `NGWSP_GRPC_TARGET` user info.
   - gRPC metadata header `service: <model>` (or `service: <model>:<version>`).
3) Audio formats (ATRAN):
   - `audio-format` string: `auto:0` or `pcm:<pcmFormat>:<sampleFormat>:<sampleRate>:<channelLayout>`
   - `pcmFormat`: `s16le`, `alaw`, `mulaw`
   - `sampleFormat`: `i2`
   - `sampleRate`: `8000`, `16000`, `32000`, `48000`, `96000`, `11025`, `22050`, `44100`
   - `channelLayout`: `mono`, `stereo`
   - `audio-channel`: `downmix`, `left`, `right`
4) Features (ATRAN):
   - comma-separated string list (e.g., `lookahead`, `latency`, `novad`, `noppc`, `nopnc`, `nospk`, `novprint`)
5) Lexicon (ATRAN):
   - gRPC config item `lexicon` of type `m` (array of strings) with optional `labels.hint`
6) TLS:
   - upstream uses `https://user:password@cluster` in reference project (GrpcChannel.ForAddress).

Still open / needs explicit decision:
- None.

---

## Plan Validation Gate (Mandatory)

- The phrase **"validate plan"** has a special meaning.

When the user says **"validate plan"**:
1) Do NOT write or modify any code.
2) Re-read the entire `PLAN.md`.
3) Verify that:
   - goals are clear and non-contradictory
   - ACTIVE iteration is well-defined
   - requirements, tests, and exit criteria exist
   - dependencies and assumptions are explicit
4) Identify:
   - missing details
   - ambiguities
   - risks
   - inconsistencies
5) Respond ONLY with:
   - validation feedback
   - suggested plan improvements
   - concrete questions (if needed)

Absolutely forbidden during plan validation:
- writing code
- modifying files
- starting implementation
