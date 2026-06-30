# AGENTS

## Purpose

This repository contains the Greenhouse **Main Unit services** - the headless "brain" of the platform: domain logic, MQTT, storage, and onboarding, implemented in C#/.NET.

This is not the Main Unit UI repository (`/ui`) and not the ESP32 Edge Unit firmware repository (`/edge`).

This file is the canonical, cross-agent policy for this repository.

## First Action

Before taking any other action in this repository, read the central documentation entry point:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

The Greenhouse Documentation repository is the source of truth for durable project documentation, terminology, architecture, MQTT contracts, ADRs, journeys, skills, and quality gates. Do not recreate that material here. If this file conflicts with the documentation repository, follow the documentation repository and update this file to remove the conflict.

## Governing Architecture

This repository implements the services side of two canonical ADRs in the documentation repository. Read them before structural work:

- `adr/0001-main-unit-ui-services-separation.md` - services and UI run as **independent processes**; services is headless, autostarts, and has **zero dependency on the UI**.
- `adr/0002-main-unit-flutter-flutter-pi-ui.md` - the UI is a separate Flutter/flutter-pi process (context for the boundary; not implemented here).

Non-negotiables from those ADRs:

- The services daemon owns all domain logic, MQTT, storage, and onboarding. No UI rendering, no Razor, no Blazor in this repository.
- The daemon exposes its API on the **loopback interface only**: REST for request/response, plus a WebSocket/SSE channel for limited live push (SignalR is permitted for that channel but not required).
- The API is a stable contract. The daemon **publishes OpenAPI** so the UI can be generated from and verified against it.
- The daemon must start, run, and stop independently of the UI.

## Scope Boundaries

- Use this repository only for Main Unit services code, tests, project/solution files, and local operational assets the daemon needs.
- Do not add UI code, firmware code, or local copies of durable documentation from the documentation repository.
- Keep cloud-first assumptions out of Phase 1 work; the platform is local-first and MQTT-centered.

## Instruction Precedence

Use this order when instructions overlap:

1. AGENTS.md (this file)
2. Greenhouse Documentation repository instructions, ADRs, and docs (canonical)
3. `docs/skills/*.md` (local skill packs - supplemental only)
4. `docs/adr/README.md` (local ADR index; canonical ADRs still win)
5. `agent-handoff.md` (session scratchpad only - never durable guidance)

If guidance conflicts, follow the highest-precedence source. Local docs and skill packs never override canonical policy, architecture, contracts, or terminology.

## Incremental Loading and Partial Disclosure

Do not read all documentation or all skill packs up front.

1. Read the documentation repository README first; from it, load only the canonical docs the task needs (for example `mqtt-topics.md` and `device-model.md` for messaging work, onboarding/configuration specs for provisioning work).
2. In this repository, load only the matching local skill packs in `docs/skills/` (see `docs/skills/README.md`).
3. Load local ADRs and canonical ADRs only for the area you are touching.

## Coding Standards

- Clean architecture: keep the domain/core technology-neutral; keep MQTT, storage, and the REST/transport layer in their own boundaries depending inward on core contracts.
- Explicit constructor dependency injection; no service-locator patterns. Composition root lives in the host startup.
- Keep API endpoints thin: they translate between transport DTOs and application services. No domain or MQTT logic in endpoints.
- Keep DTOs/contracts separate from domain models; keep MQTT payload naming snake_case at the broker boundary per `mqtt-topics.md`.

## Runtime Rules

- Run as a long-lived headless daemon (ASP.NET Core Generic Host). Use hosted/background services for MQTT lifecycle and long-running work.
- Bind the API to loopback only. Do not force HTTPS redirection on loopback.
- Treat MQTT connectivity as resilient: reconnect with bounded backoff, re-subscribe after reconnect, and never crash the daemon on transport failure.
- Honor graceful shutdown (SIGTERM / host lifetime) so the unit restarts cleanly.
- Keep the footprint modest; the target device is a Raspberry Pi 4.

## Quality Gates

Before finalizing changes, verify:

1. No UI, Razor, or Blazor types or dependencies exist in this repository.
2. No infrastructure (MQTT/storage/HTTP) types leak into core abstractions.
3. The REST/OpenAPI contract is updated alongside behavior changes; published payloads match documented schemas.
4. MQTT reconnect/re-subscribe and offline paths are explicit and recoverable.
5. New behavior has tests at the right layer (unit, integration, contract), including negative paths.
6. The daemon still starts and runs with no UI present.

## Session Closeout (Definition of Done)

1. Confirm changes stay within Main Unit services scope.
2. Re-check canonical docs/ADRs for conflicts.
3. Run the canonical verification commands (once the solution exists; adjust the solution path if named differently):
   - `dotnet restore`
   - `dotnet build --no-restore`
   - `dotnet test --no-build --no-restore`
4. Confirm tests for changed behavior exist and pass.
5. Update `agent-handoff.md` with factual current-session state.
6. Propose durable guidance changes in the documentation repository, not here.

## Handoff

`agent-handoff.md` is for local, time-bound session state only. Do not treat it as durable guidance or duplicate canonical policy there.
