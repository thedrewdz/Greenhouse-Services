# Skill: .NET Clean Architecture

## Purpose

Guide agents to implement Main Unit services features with clear boundaries between the API/transport layer, application use cases, domain/core, and infrastructure.

## Use This Skill When

- Adding new use cases or services in C#.
- Refactoring code that mixes transport, persistence, and business logic.
- Defining dependency injection and project boundaries.

## Do Not Use This Skill When

- The task is edge firmware implementation (`/edge`).
- The task is UI work (`/ui`).
- The task is pure documentation formatting.

## Architecture Rules

- Keep the core/domain project technology-neutral.
- Keep the REST/transport (API) concerns in the host/API layer only.
- Keep MQTT transport details in the messaging project.
- Keep storage details in the storage project.
- Depend inward: the API layer and infrastructure depend on core contracts, not the reverse.

## Service Design Rules

- API endpoints call application services, not repositories or transport clients directly.
- Use interfaces for orchestration boundaries (`IDeviceService`, `ITelemetryService`, etc.).
- Keep message parsing and validation out of API endpoints.

## Quality Gate

- No infrastructure types leak into core abstractions.
- No API endpoint directly publishes MQTT commands or touches storage details.
- Use cases are testable without starting the web host.
