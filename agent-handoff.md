# Agent Handoff

This file is for local, time-bound session state only.

Durable policy, canonical context, architecture, MQTT contracts, ADRs, and skill guidance live in the Greenhouse Documentation repository:

- https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

## Current Workspace State

- Repository purpose: Greenhouse Main Unit services (headless C#/.NET brain).
- Branch: `feature/25-edge-unit-onboarding-and-configuration`.
- Epic #25 (Edge Unit Onboarding and Configuration — Services) implemented across all seven
  sub-issues (#30–#36). Solution builds clean; 235 tests pass.

## Current Progress Snapshot

Epic #7 (Main Unit Setup — Services) was completed in an earlier session; see git history.

Epic #25 — Edge Unit Onboarding and Configuration:

- **#30** `EdgeUnitEntity` / `SlotTopologyEntity` / `OnboardingSessionEntity` (internal to
  Storage) + `EdgeUnitRegistration` migration. `EdgeUnits.DeviceId` uniquely indexed;
  `SlotTopologies` cascade-deletes and is unique per `(EdgeUnitId, SlotId)`.
- **#31** `IEdgeUnitRepository` (Core.EdgeUnits) and `IOnboardingSessionRepository`
  (Core.Onboarding) + EF implementations.
- **#32** `INetworkConnector.GetLocalAddressAsync` + `nmcli -t -f IP4.ADDRESS device show`
  parsing; loopback and link-local addresses are rejected.
- **#33** `OnboardingWorkflow` (singleton, Core.Onboarding) owning the single session; SignalR
  hub `/hubs/onboarding` with `DeviceDiscovered` / `OnboardingStateChanged`, wired through the
  `IOnboardingNotifier` port so Core stays transport-free.
- **#34** `OnboardingController` — scan / GET / start / cancel.
- **#35** `EdgeUnitsController` — list / detail / PUT mapping; `UpdateEdgeUnitMapping` use case
  and `EdgeUnitConfigurationPublisher` (publish, ack correlation, 8s × 3 retry budget).
- **#36** `ProcessHeartbeat` + `HeartbeatSubscriptionService` (hosted service in Runtime).

## Decisions Worth Knowing

- **`ProvisionableUnit` carries both identities.** `DeviceId` is the Edge Unit hardware id
  (parsed from the `GH-Edge-{device_id}` advertised name by the BLE adapter) and is what the API,
  hub, and heartbeats use. `TransportAddress` is the opaque BlueZ handle. The API's
  `{device_id}` path segment is the hardware id, per the spec's payload examples.
- **BLE scanning now streams.** `IBleTransport.ScanAsync` returns `IAsyncEnumerable` and
  `BlueZBleTransport` reads `bluetoothctl` output line by line, so candidates reach the hub as
  they advertise instead of after the 30s window. A unit may be yielded more than once (name
  first, RSSI later); the workflow dedupes and only announces first sightings.
- **`GreenhouseDatabase` seam (new).** All repositories now take it instead of a `DbContext`.
  It creates a short-lived context per operation and serialises operations. This was required,
  not cosmetic: the host keeps one SQLite connection open for the process lifetime, and this
  epic adds background writers (heartbeat ingestion, configuration publishing) that would
  otherwise collide with API reads. It also lets repositories be singletons, which the
  long-lived workflow and publisher need. `MainConfigRepository` and `WifiCredentialsRepository`
  were converted to the same seam; `Program.cs` no longer registers a scoped `DbContext`.
- **`Microsoft.Extensions.Logging.Abstractions` added to Core.** Contract-only dependency,
  taken so the publish/ack audit trail required by the spec's Reliability section can exist.
  Publish attempts log at Debug; failures at Warning (Pi storage is limited).
- **Capability vocabulary** is defined in `Core.EdgeUnits.Capabilities` from the `device-model.md`
  examples plus the capabilities used in canonical payloads. The spec says the Main Unit owns
  this vocabulary, so it lives here rather than in the docs repo.
- **Onboarding timeouts and the publish retry budget are injected** (`OnboardingTimeouts`,
  `ConfigurationPublishPolicy`) so tests exercise timeout paths without waiting. The defaults
  are the canonical 30s / 90s and 8s × 3 (1s, 2s).
- **A persisted session left mid-flight by a restart is cleared to idle.** `scanning`,
  `candidates-ready`, `provisioning`, and `awaiting-heartbeat` describe work in a process that no
  longer exists; `mapping-required`, `complete`, `failed`, and `no-device-found` survive.

## Open Questions

- **Topology-drift notification has no documented channel.** Filed as a documentation issue (see
  below). The Drift Flag is detected and persisted (`EdgeUnits.TopologyDriftDetectedAt`), but the
  hub contract defines only `DeviceDiscovered` and `OnboardingStateChanged`, and neither
  `GET /api/edge-units` nor `GET /api/edge-units/{device_id}` exposes drift. Nothing was invented
  to fill the gap — no undocumented event or field was added.
- **Heartbeat-timeout failures carry no canonical `errorCode`.** The 2001–2099 set is the Edge
  Unit's BLE response vocabulary; a Main Unit-side 90-second timeout has no code, so the session
  fails with `errorCode: null` and a diagnostic `errorMessage`. Raised in the same doc issue.
- Adapter naming reconciliation still pending from the previous session: implemented as
  `NmcliNetworkAdapter`; spec/#22 say `NetworkManagerAdapter`.
- Storage skill (`dotnet-storage-and-persistence.md`) says "WiFi credentials are not stored in
  the app database" — still contradicts the spec's `WifiCredentials` table.

## Code Review of PR #45 — findings filed and fixed on the branch

Reviewed the full PR #45 diff. Five issues filed (#46–#50) and all fixed on this branch; suite now
**251 passed, 0 failed** (was 235). Also commented on #41: its "does not affect the merged
scaffolding" assessment no longer holds, because the streaming scan holds the undrained-stderr
subprocess open for the whole scan window.

- **#46 (blocking) — heartbeat ingestion could erase an accepted mapping.** `ProcessHeartbeat` read
  the unit in one database operation and wrote the whole row in another, so a mapping `PUT` landing
  in between was reverted — silently, on the onboarding hot path. Fixed by moving the decision into
  a pure `HeartbeatReconciliation.Reconcile` and adding `IEdgeUnitRepository.RecordHeartbeatAsync`,
  which reads, reconciles, and writes inside one `GreenhouseDatabase.ExecuteAsync`. `UpsertAsync`
  was **removed from the port** — a whole-unit write is what made the bug reachable, so heartbeat
  ingestion is now structurally incapable of touching mapping columns.
- **#47 (blocking) — BLE scan could park past its window.** The window was bounded only by
  cancelling `ReadLineAsync`, which a pipe read does not honour on Linux, so a silent
  `bluetoothctl` hung the session in `scanning`, ignored cancel, and blocked shutdown. Each read is
  now raced against a window task; teardown is bounded and then kills the subprocess. This also
  mitigates #41's deadlock, because the window closes regardless of whether the child is wedged.
- **#48 (blocking, part 1) — a publish throw stranded the mapping at `publish-pending`.** An
  offline broker made `PublishAsync` throw straight past the retry budget into the pump's
  catch-all: no retry, no terminal status. Attempts are now a single `AttemptAsync` returning
  `Acknowledged | Retry | Rejected`, so a transport failure spends an attempt, honours the backoff,
  and ends at `failed`. **Part 2 (re-publish pending mappings on broker reconnect) is still open on
  #48** — it needs a reconnect hook `IMessagingService` does not expose yet.
- **#49 — `FailAsync` could resurrect a cancelled session.** It now takes the status the failing
  work owns and returns early if the session has moved on.
- **#50 — hardening cluster (8 items), all applied**: `_loaded` set only after a successful load;
  `IsRoutableIpv4` uses `IPAddress.TryParse` and rejects `0.0.0.0`/whitespace/signed octets;
  `MappingValidation` doc comment corrected (the use case is the only validation layer);
  `_lastMessageId` seeded from the clock; duplicate reported `slot_id`s now count as drift;
  `HeartbeatSubscriptionService` no longer disposes its CTS out from under in-flight handlers;
  `_background` tracks unfinished predecessors via `Track`; the `SqliteConnection` is registered
  for container disposal.

Re-verified: `dotnet build` clean, 251 tests pass, and the daemon still starts with no UI present,
migrates on first run, serves `GET /api/onboarding` and `GET /api/edge-units` with the documented
shapes, and publishes all seven new paths in OpenAPI.

**#47 is not covered by automated tests** — it needs `bluetoothctl`, so it is verified by
construction and belongs to the on-device work below.

## Second Review Round (041c86f re-review)

Re-reviewed PR #45 with 041c86f in scope. #46, #47 and #48 part 1 hold up. Three further issues
filed (#51, #52, #53) and all fixed on this branch; suite now **254 passed, 0 failed** (was 251).

- **#51 — a stale failure could still fail a *newly started* session.** #49's fix guards `FailAsync`
  on status alone, and status is not session identity: cancel a provisioning session, restart, and
  the second session sits in exactly the status the abandoned work still expects. Reproduced with a
  test (`provisioning` → `failed`). Fixed by giving each session a monotonic `_session` id — bumped
  wherever a session token is created and on reset to idle — which background work captures and
  `FailAsync` checks alongside the status.
- **#52 — the `SqliteConnection` was still never disposed.** #50 item 8 was recorded as fixed via
  `AddSingleton(sqliteConnection)`, but DI only disposes what it *creates*; an instance registration
  is never captured. Verified against `Microsoft.Extensions.DependencyInjection` 8.0.0 — only the
  factory-registered probe was disposed. `GreenhouseDatabase` had the same gap. The connection is
  now `await using` in `Program.cs` (a `try`/`finally` around `app.Run()`, so it closes on a faulted
  host too) and `GreenhouseDatabase` is registered via a factory. The misleading comment is gone.
- **#53 — three follow-ups.** (1) `published` was gated on `attempt == 1`, so a mapping that reached
  the broker on a *retry* stayed at `publish-pending`; `AttemptOutcome.NotDelivered` now
  distinguishes "never left the daemon" from the other retryable outcomes, and the status goes to
  whichever attempt gets through. (2) `selectedDeviceId` was retained "so the operator can retry
  without reselecting", but the idempotency guard swallowed the retry and answered 202 doing
  nothing; re-selecting the same device from `failed` now re-provisions. (3) #50 item 1 shipped
  without the test its acceptance criteria required — `FakeOnboardingSessionRepository` gained a
  `FailNextRead` seam and the load is now proven to be retried rather than latched.

Re-verified after these fixes: `dotnet build` clean, 254 tests pass, and the daemon still starts with
no UI present, migrates on first run, serves `GET /api/onboarding` and `GET /api/edge-units` with the
documented shapes, and publishes all seven new paths in OpenAPI.

## Next Actions

- On-device verification on the test Pi: BLE scan/provision against a real Edge Unit, and the
  `ghcfg/wr-` → `ghcfg/ack-` round trip against Mosquitto. Neither path can be exercised on a
  development host; unit coverage substitutes fakes at the transport seam. The #47 scan-window fix
  and #41's stderr drain both need this pass.
- **#48 part 2** — re-publish `publish-pending`/`failed` mappings after a broker reconnect.
- **#41** — drain stderr in `StartProcess` so both the session and streaming scan paths are safe.
- UI epic (Main Unit Setup — UI, Greenhouse-WebUI) is unblocked by this work.

## Resume Prompt

```text
Read AGENTS.md, agent-handoff.md, and the Greenhouse Documentation README, then continue Main Unit services work using the relevant canonical architecture, device model, and MQTT documentation. Honor canonical ADRs 0001 and 0002.
```
