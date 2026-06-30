# Skill: .NET Storage and Persistence

## Purpose

Guide agents to implement reliable Main Unit persistence for configuration, telemetry, and command history.

## Use This Skill When

- Adding repositories and persistence models.
- Mapping domain models to SQLite or JSON persistence.
- Designing migration paths from temporary storage to SQLite.

## Do Not Use This Skill When

- The task is UI work (`/ui`) or edge firmware (`/edge`).

## Persistence Rules

- Main Unit configuration is the source of truth.
- Edge Unit runtime mappings are persisted centrally.
- WiFi credentials are not stored in the app database.
- Store received UTC timestamps for inbound MQTT messages.

## Repository Rules

- Keep repository interfaces in core abstractions.
- Keep concrete persistence details in the storage project.
- Use explicit model mapping at boundaries.
- Keep write operations idempotent where possible.

## Quality Gate

- Repository methods are unit-testable with fakes or in-memory substitutes.
- Persistence failures are surfaced as recoverable application errors.
- No storage-specific dependencies leak into core abstractions or the API/transport layer.
