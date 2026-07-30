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

## Next Actions

- On-device verification on the test Pi: BLE scan/provision against a real Edge Unit, and the
  `ghcfg/wr-` → `ghcfg/ack-` round trip against Mosquitto. Neither path can be exercised on a
  development host; unit coverage substitutes fakes at the transport seam.
- UI epic (Main Unit Setup — UI, Greenhouse-WebUI) is unblocked by this work.

## Resume Prompt

```text
Read AGENTS.md, agent-handoff.md, and the Greenhouse Documentation README, then continue Main Unit services work using the relevant canonical architecture, device model, and MQTT documentation. Honor canonical ADRs 0001 and 0002.
```
