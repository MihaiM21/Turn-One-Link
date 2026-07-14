# AI Agent Guide for Turn One Link

Hello AI Assistant! Welcome to the Turn One Link codebase. Use this document to quickly orient yourself.

## What is this project?
Turn One Link is a Windows desktop application (WPF / C# / .NET 9) that reads telemetry data from sim racing games (like Assetto Corsa Competizione) via Windows shared memory, serializes it to JSON, and streams it over a WebSocket to a cloud backend. 

## Where to find things:

- **High-Level Architecture:** Check out [Architecture & Data Flow](architecture.md) to understand how the desktop app reads from memory and sends to the backend.
- **Environment & Config:** See [Setup & Environment](setup/environment.md) for how `.env` variables are handled.
- **Telemetry Formats:**
  - [Overview](telemetry/overview.md): Basic JSON schema, coordinate systems, and frontend considerations.
  - [Physics Schema](telemetry/physics.md): High-frequency telemetry (speed, inputs, temps).
  - [Graphics Schema](telemetry/graphics.md): Session state, lap times, positions.
  - [Static Schema](telemetry/static.md): One-off session info (track, car, weather).
- **Backend Communication:** If you need to know how sessions are managed (`session_start`, `session_end`), see the [Backend Contract](telemetry/backend_contract.md).

When answering developer questions or writing code, refer back to these documents for the exact schemas and data flows.
