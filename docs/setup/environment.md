# Configuration and environment variables

## How it works

Production defaults are **compiled in** — an installed Turn One Link needs no configuration file.
Settings can be overridden with environment variables, loaded at startup by `App.xaml.cs` with the
**DotNetEnv** package, in this order (later wins):

1. OS environment variables.
2. **Debug builds only:** `.env`, then `.env.local`, next to the exe (copied there from the project
   folder on a Debug build). Both are gitignored and never published or installed.
3. **Every build:** `%LOCALAPPDATA%\TurnOneLink\overrides.env` — per-user overrides that survive
   updates. The "Local devices" port editor writes `LOCAL_WS_PORT` here.

Per-user state lives in `%LOCALAPPDATA%\TurnOneLink\` (`AppPaths.DataDir`): `settings.json`,
`authstate.bin` (DPAPI), `sessions\`, `turnone_debug.log`, `overrides.env`. Nothing is written next
to the exe, because the install folder is replaced on every update.

## Variables the app reads

| Variable | Default | Purpose |
|---|---|---|
| `API_URL` | `https://backend.t1f1.com/api` | Backend REST base; the WSS URL is derived from it |
| `USE_LOCAL_API` | `false` | `true` switches to `API_URL_LOCAL` |
| `API_URL_LOCAL` | `http://localhost:5271/api` | Local backend for development |
| `LOCAL_WS_PORT` (or `WEBSOCKET_PORT`) | `8080` | Port of the LAN server for phones |
| `F1_UDP_PORT` | `20777` | EA F1 25/26 UDP listener |
| `PROTOCOL_V1` | `false` | `true` falls back to the legacy v1 ACC-shaped cloud stream (`docs/telemetry/backend_contract.md`) |
| `STREAM_HZ` | `20` | v2 tick rate, hard-capped at 60 |
| `SENTRY_DSN` | baked in by CI | Overrides the crash-report DSN (empty = off) |

Nothing else is read. Older docs listed `LOG_LEVEL`, `DEBUG_MODE`, `CONNECTION_*`, `TELEMETRY_*` and
`SECURE_TOKEN_STORE_ENABLED`; those were never implemented.

## Example `.env.local` for local development

```dotenv
USE_LOCAL_API=true
API_URL_LOCAL=http://localhost:5271/api
```

## Adding a variable

Read it with `Environment.GetEnvironmentVariable("NAME")`, give it a production-safe default in
code, and add it to the table above.
