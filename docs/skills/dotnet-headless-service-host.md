# Skill: .NET Headless Service Host

## Purpose

Guide agents to build the Main Unit services as a headless ASP.NET Core daemon that exposes a loopback REST + OpenAPI contract and a WebSocket/SSE push channel, and that runs independently of the UI.

This pack encodes the architecture from the canonical ADRs (`adr/0001-main-unit-ui-services-separation.md`, `adr/0002-main-unit-flutter-flutter-pi-ui.md`). Read those first for the why.

## Use This Skill When

- Establishing or changing the host/startup, the API surface, or the push channel.
- Defining the OpenAPI contract or the DTOs the UI is generated from.
- Wiring MQTT and other long-running work into the daemon lifecycle.

## Do Not Use This Skill When

- The task is UI work (that is the `/ui` repository - Flutter/flutter-pi).
- The task is firmware (that is `/edge`).

## Host Rules

- Build a long-lived headless daemon on the ASP.NET Core Generic Host. No Razor, no Blazor, no server-rendered UI.
- Run long-running work (MQTT lifecycle, schedulers) as hosted/background services, not inside request handlers.
- Honor host lifetime and graceful shutdown (SIGTERM) so the process unit restarts cleanly.
- Keep startup as the single composition root; register dependencies explicitly (see `dotnet-di-without-service-locator.md`).

## API and Contract Rules

- Expose a REST API for request/response. Keep endpoints thin: translate between transport DTOs and application services; no domain or MQTT logic in endpoints.
- Publish **OpenAPI** from the daemon. The OpenAPI document is the contract the UI's client is generated from - keep it accurate and versioned with behavior changes.
- Keep transport DTOs separate from domain models and from MQTT payloads. Map explicitly at the boundary.
- Provide a WebSocket or SSE endpoint for the limited live-push use cases. SignalR is permitted but not required; prefer the simplest option that meets the need on a single-client loopback link.

## Binding and Safety Rules

- Bind the API to the **loopback interface only** (`127.0.0.1`). The UI runs on the same device.
- Do not force HTTPS redirection on loopback; keep loopback transport plain to avoid certificate friction on the appliance.
- Do not expose the API on external interfaces without an explicit, documented decision (canonical ADR).

## Independence Rules

- This repository must have no compile-time or runtime dependency on the UI.
- The daemon must start, serve, and run with no UI present.
- Deployment is a dedicated process unit (e.g. `greenhouse-services`) that is enabled independently of the UI unit.

## Resource Rules

- Target a Raspberry Pi 4. Keep allocations modest in hot paths; avoid unnecessary background churn.
- Trimming/AOT are optional optimizations; do not let them obscure the composition root or break reflection-based serialization without tests.

## Quality Gate

- No UI/Razor/Blazor types or dependencies anywhere in the repository.
- The API binds to loopback and publishes an accurate OpenAPI document.
- MQTT and other long-running work run as hosted services with graceful shutdown.
- The daemon starts and runs with no UI present, verified by a smoke test.
