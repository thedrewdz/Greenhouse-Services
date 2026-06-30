# Skill: .NET Testing Strategy

## Purpose

Guide agents to verify Main Unit services behavior through focused unit, integration, and contract tests.

## Use This Skill When

- Adding features that affect setup, onboarding, routing, messaging, or persistence.
- Refactoring service boundaries or message contracts.
- Validating regressions in API-to-service workflows.

## Do Not Use This Skill When

- The task is UI work (`/ui`) or edge firmware (`/edge`).

## Test Layers

### 1) Unit Tests

- Validate use-case logic and input validation.
- Validate message parsing and contract checks.
- Validate routing decisions for setup, onboarding, and reconfiguration.

### 2) Integration Tests

- Validate repository behavior with representative test data.
- Validate messaging orchestration and reconnect behavior.
- Validate end-to-end service flows without HTTP transport coupling.

### 3) Contract Tests

- Validate JSON shape and required fields against `mqtt-topics.md` for MQTT payloads.
- Validate the published REST/OpenAPI contract that the UI is generated from.
- Validate error-code handling for malformed or invalid payloads.

## Quality Gate

- New behavior includes tests at the right layer.
- Negative-path tests exist for invalid payloads and offline cases.
- Critical flow regressions are covered: setup, network recovery, onboarding routing.
- The daemon's start-with-no-UI behavior has a smoke test.
