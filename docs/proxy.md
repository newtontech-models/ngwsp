# Proxy Server CLI

This document covers the `ngwsp proxy` command, its CLI options, and related
environment variables.

## Command

```bash
dotnet run --project ngwsp -- proxy --grpc-target <grpc-url>
```

Required:
- `--grpc-target` or `NGWSP_GRPC_TARGET`

## Options

- `--listen-url` (default: `http://0.0.0.0:8080`)
  - HTTP listen URL.
- `--cors-origins`
  - Comma-separated CORS origins.
- `--log-level`
  - Log level (Trace, Debug, Information, Warning, Error).
- `--tls-cert-path`
  - TLS certificate path.
- `--tls-key-path`
  - TLS key path.
- `--grpc-target`
  - Upstream gRPC target URL (required).
- `--grpc-use-tls`
  - Upstream gRPC TLS setting (`true`/`false`).
- `--grpc-ca-path`
  - Upstream gRPC CA path.
- `--grpc-timeout-ms`
  - Upstream gRPC timeout in milliseconds.
- `--client-auth-mode`
  - Client auth mode: `none` or `api_key`.
- `--client-api-key`
  - Client API key value when `--client-auth-mode api_key` is enabled.
- `--models`
  - Comma-separated allowed models.
- `--default-model`
  - Default model name for clients.
- `--lexicon`
  - Lexicon preset/path/inline definition (server-side default).
- `--env-file` (default: `.env`)
  - Path to a `.env` file (global option).

## Configuration Sources (Priority Order)

1) CLI options
2) `.env` file via `--env-file` (defaults to `.env`)
3) Environment variables
4) Defaults

## Environment Variables

All variables are prefixed with `NGWSP_`:

- `NGWSP_LISTEN_URL`
- `NGWSP_CORS_ORIGINS`
- `NGWSP_LOG_LEVEL`
- `NGWSP_TLS_CERT_PATH`
- `NGWSP_TLS_KEY_PATH`
- `NGWSP_GRPC_TARGET`
- `NGWSP_GRPC_USE_TLS`
- `NGWSP_GRPC_CA_PATH`
- `NGWSP_GRPC_TIMEOUT_MS`
- `NGWSP_CLIENT_AUTH_MODE`
- `NGWSP_CLIENT_API_KEY`
- `NGWSP_MODELS`
- `NGWSP_DEFAULT_MODEL`
- `NGWSP_LEXICON`

Additionally, `NG_MODELS` is accepted as an alias for `NGWSP_MODELS`.
