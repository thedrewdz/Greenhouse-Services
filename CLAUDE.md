# CLAUDE

This file is the Claude / Claude Code entry point for the Greenhouse Main Unit **services** repository.

## First Action

Before taking any other action in this repository, read the central documentation entry point:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

The Greenhouse Documentation repository is canonical for durable documentation, platform context, architecture, MQTT contracts, ADRs, and skill guidance. This repository is a consumer of that documentation. Treat it as authoritative whenever guidance overlaps.

## Canonical Policy

@AGENTS.md

AGENTS.md is the canonical, cross-agent policy for this repository. Its scope boundaries, governing architecture, instruction precedence, coding standards, runtime rules, and quality gates apply fully to Claude and Claude Code.

## What This Repository Is

This repository contains the Main Unit **services** - the headless brain (domain logic, MQTT, storage, onboarding) in C#/.NET.

It is not the Main Unit UI (`/ui`) and not the ESP32 edge firmware (`/edge`). Claude Code may write and edit services code, tests, and local supplemental docs here. Claude Code must not add UI/Razor/Blazor code or firmware code.

## Governing ADRs

This repository implements the services side of two canonical ADRs in the documentation repository:

- `adr/0001-main-unit-ui-services-separation.md` - independent UI/services processes; services is headless with zero dependency on the UI; loopback REST + WebSocket/SSE; services publishes OpenAPI.
- `adr/0002-main-unit-flutter-flutter-pi-ui.md` - the UI is a separate Flutter/flutter-pi process (boundary context).

Read these before structural work. They are canonical and override local guidance.

## Incremental Loading and Partial Disclosure

**Do not read all documentation or all skill packs up front.** Load only what the task needs:

1. Read the documentation repository README first; from it, load only the canonical docs the task needs (e.g. `mqtt-topics.md`, `device-model.md`, onboarding/configuration specs).
2. In this repository, load only the matching local skill packs in `docs/skills/` (see below).
3. Load canonical and local ADRs only for the area you touch.

## Instruction Precedence

1. `AGENTS.md` (canonical cross-agent policy)
2. `CLAUDE.md` (this file - Claude-specific operational guidance; never overrides AGENTS.md)
3. Greenhouse Documentation repository instructions, ADRs, and docs (canonical)
4. `docs/skills/*.md` (local skill packs - supplemental only)
5. `docs/adr/README.md` (local ADR index; canonical ADRs still win)
6. `agent-handoff.md` (session scratchpad only - never durable guidance)

## Local Skill Packs

Local, implementation-focused skill packs live under `docs/skills/`. They are supplemental to the canonical skills in the documentation repository. Read `docs/skills/README.md` to select one, and load only the matching pack.

Available skill packs:

- `docs/skills/dotnet-headless-service-host.md` - the headless ASP.NET Core daemon, loopback REST + OpenAPI contract, WebSocket/SSE push, and UI-independence rules (the ADR 0001 architecture).
- `docs/skills/dotnet-clean-architecture.md` - boundaries between core/domain, MQTT, storage, and the API/transport layer.
- `docs/skills/dotnet-di-without-service-locator.md` - explicit constructor DI; no service-locator patterns.
- `docs/skills/mqtt-contract-integration-dotnet.md` - MQTT publish/subscribe/routing while preserving canonical topic and payload contracts.
- `docs/skills/dotnet-storage-and-persistence.md` - configuration, telemetry, and history persistence behind core abstractions.
- `docs/skills/dotnet-testing-strategy.md` - unit, integration, and contract tests, including negative paths.

If a documentation, knowledge, or skill gap is identified, do not make things up - bring it to the user's attention to be addressed properly, per AGENTS.md.

## Local ADRs

`docs/adr/README.md` is the local ADR index and loading rules. Repository-specific decisions live under `docs/adr/`; the cross-cutting architecture is governed by the canonical ADRs in the documentation repository. Read the index first and load only relevant ADRs.

## Handoff File

`agent-handoff.md` is for local, time-bound session state only. Do not treat it as durable guidance or duplicate canonical policy there.

## Repository Tool Bridges

| Tool | File |
|---|---|
| Claude / Claude Code | `CLAUDE.md` (this file) |
| OpenAI Codex | `AGENTS.md` |
| GitHub Copilot | `.github/copilot-instructions.md` |

All bridges point to `AGENTS.md` as the canonical source of policy, and to the Greenhouse Documentation repository as the canonical source of project documentation.
