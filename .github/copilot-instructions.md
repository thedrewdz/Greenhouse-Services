# Copilot Instructions

## Canonical Agent Policy

Use `AGENTS.md` at the repository root as the canonical source of long-lived agent instructions.

Before taking any other action in this repository, read the central documentation entry point:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

## Repository Focus

This repository is the Greenhouse Main Unit **services** (headless C#/.NET brain: domain logic, MQTT, storage, onboarding).

It is not the Main Unit UI (`/ui`) and not the ESP32 edge firmware (`/edge`). Do not add UI, Razor, or Blazor code here. Services must run headless, independently of the UI, exposing a loopback REST + WebSocket/SSE API and publishing OpenAPI (see canonical ADRs 0001 and 0002 in the documentation repository).

## Required References

Read and follow:

- `AGENTS.md`

Use the dedicated Greenhouse Documentation repository for durable project documentation, canonical context, architecture, MQTT contracts, ADRs, and skill guidance.
