# Agent Handoff

This file is for local, time-bound session state only.

Durable policy, canonical context, architecture, MQTT contracts, ADRs, and skill guidance live in the Greenhouse Documentation repository:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

## Current Workspace State

- Repository purpose: Greenhouse Main Unit services (headless C#/.NET brain).
- Status: repository scaffolded for agentic assistants (AGENTS.md, CLAUDE.md, skill packs, ADR index). No application code yet.

## Current Progress Snapshot

- Agent setup created. The .NET solution and projects have not been generated.

## Open Questions

- None recorded.

## Next Actions

- Per ADR 0002's validation spike, a stub services daemon is needed: one REST endpoint plus one WebSocket/SSE message on loopback, used to validate the Flutter/flutter-pi UI on the Raspberry Pi 4.
- Establish the solution structure (headless ASP.NET Core host, core/domain, MQTT, storage, contracts) following `docs/skills/dotnet-headless-service-host.md` and the canonical architecture docs.

## Resume Prompt

```text
Read AGENTS.md, agent-handoff.md, and the Greenhouse Documentation README, then continue Main Unit services work using the relevant canonical architecture, device model, and MQTT documentation. Honor canonical ADRs 0001 and 0002.
```
