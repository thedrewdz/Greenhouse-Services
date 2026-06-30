# Local Skill Packs

This folder contains local, implementation-focused skill packs for the Greenhouse Main Unit **services** repository.

These packs are tool-neutral: they work for both OpenAI Codex (via `AGENTS.md`) and Claude / Claude Code (via `CLAUDE.md`).

They are **supplemental**. Canonical skills, context, architecture, and contracts live in the Greenhouse Documentation repository:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

Review relevant canonical guidance first, then apply these repository-local packs for Main Unit services specifics. Local packs must never override canonical policy, architecture, contracts, or terminology.

## Partial Disclosure

Load only the pack that matches the current task. Do not read all packs up front. Each pack states when to use it and when not to.

## Packs

| Pack | Use when |
|---|---|
| `dotnet-headless-service-host.md` | Building the daemon host, the loopback REST/OpenAPI contract, the WebSocket/SSE push channel, or anything touching UI-independence. **Start here for structure.** |
| `dotnet-clean-architecture.md` | Adding use cases/services, or separating transport, persistence, and business logic. |
| `dotnet-di-without-service-locator.md` | Wiring startup registrations or reviewing for hidden dependencies. |
| `mqtt-contract-integration-dotnet.md` | Implementing MQTT publish/subscribe/routing, heartbeat, ack, or read-response handling. |
| `dotnet-storage-and-persistence.md` | Adding repositories/persistence for configuration, telemetry, or command history. |
| `dotnet-testing-strategy.md` | Adding unit, integration, or contract tests for changed behavior. |

## Note on History

These packs were adapted from the sunset `services-old` repository. The old `blazor-ui-backend-patterns` pack was intentionally **not** carried over: this repository has no UI (see canonical ADR 0001). UI guidance lives in `/ui`.

## Gaps

If a documentation, knowledge, or skill gap is identified, do not make things up - bring it to the user's attention to be addressed properly, per `AGENTS.md`.
