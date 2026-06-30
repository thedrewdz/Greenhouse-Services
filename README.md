# Greenhouse Main Unit Services

Headless C#/.NET services for the Greenhouse Main Unit - the platform "brain": domain logic, MQTT, storage, and onboarding.

Before working in this repository, read the central documentation entry point:

- [Greenhouse Documentation README](https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md)

All durable project documentation lives in the dedicated Greenhouse Documentation repository. Local documentation here under `docs/` is supplemental and implementation-focused; when guidance overlaps, the documentation repository is canonical.

## Architecture

This repository implements the services side of the Main Unit, per the canonical ADRs in the documentation repository:

- **Independent processes** - services and UI run as separate processes on the same device. Services is headless, autostarts, and has zero dependency on the UI (`adr/0001-main-unit-ui-services-separation.md`).
- **Loopback API** - the daemon exposes REST (request/response) plus a WebSocket/SSE channel (limited live push) on the loopback interface only, and publishes OpenAPI as the contract the UI is generated against.
- **No UI here** - no Razor, no Blazor. The UI is a separate Flutter/flutter-pi repository (`/ui`, `adr/0002-main-unit-flutter-flutter-pi-ui.md`).

## Repository Responsibility

Owns: Main Unit services code, tests, project/solution files, and local operational assets the daemon needs.

Does not own: UI code, ESP32 firmware, or replacement copies of canonical documentation.

## Build and Test

Once the solution exists, use the canonical command sequence (adjust the solution path if named differently):

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build --no-restore
```

## Agent Setup

This repository is configured for agentic assistants:

- Claude / Claude Code: `CLAUDE.md`
- OpenAI Codex: `AGENTS.md`
- GitHub Copilot: `.github/copilot-instructions.md`
- Local supplemental skill packs: `docs/skills/`
- Local ADR index: `docs/adr/README.md`
- Session continuity: `agent-handoff.md`
