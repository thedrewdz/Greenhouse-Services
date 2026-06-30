# Skill: MQTT Contract Integration in .NET

## Purpose

Guide agents to implement MQTT publish, subscribe, and routing behavior in .NET while preserving canonical topic and payload contracts.

## Use This Skill When

- Implementing connected-service, command-publisher, or message-router behavior.
- Adding heartbeat, ack, or read-response handling.
- Extending command publishing and response ingestion.

## Do Not Use This Skill When

- The task is UI work (`/ui`) or edge firmware (`/edge`).

## Contract Rules

- Use canonical topics from `mqtt-topics.md` in the documentation repository.
- Keep payload naming in snake_case at the MQTT boundary.
- Validate required fields before processing payloads.
- Normalize malformed payloads into diagnostics, not fatal exceptions.

## Integration Rules

- Run MQTT as a hosted/background service in the daemon; reconnect with bounded backoff without crashing the daemon.
- Re-subscribe required topics after reconnect.
- Keep serializer/codec logic centralized.
- Do not call the MQTT client from the API/transport layer; go through application services.

## Quality Gate

- Published command payloads match the documented schema in `mqtt-topics.md`.
- Heartbeat routing updates runtime state and topology decisions.
- Connection failures are observable and recoverable, and never take down the daemon.
