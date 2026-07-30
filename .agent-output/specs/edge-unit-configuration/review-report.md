# Review Report: Edge Unit Configuration

> **Back-filled 2026-07-30.** The review was performed and its findings were filed as GitHub issues
> #46–#50 plus a roll-up comment on epic #25, but this artifact — mandated by harness Stage 4 — was
> never created. Recorded here so Stage 6 has something to consolidate. The issues remain the
> authoritative per-finding record; this file is the stage artifact and index.

## Inputs Reviewed

- Canonical spec: `specs/edge-unit-configuration/spec.md` (Greenhouse Documentation).
- Related: `specs/edge-unit-onboarding/spec.md` (BLE provisioning contract), `adr/0004-ble-first-onboarding.md`.
- Local docs: `AGENTS.md` (Greenhouse-Services), canonical ADRs 0001 and 0002.
- Code diff: `gh pr diff 45` — 76 files, +6484/−206, branch `feature/25-edge-unit-onboarding-and-configuration` at `5dfdb13`.
- Test artifacts: **none existed.** No `test-gap-report.md` was produced for this spec; Stage 3 did
  not run as a stage. Tests were written alongside implementation, which Stage 2 requires, but no
  independent coverage pass preceded review.
- Verification commands run during review:
  - `dotnet build --nologo`
  - `dotnet test --nologo` → 235 passed, 0 failed (confirmed the PR's claim)
  - Daemon smoke test on loopback: `GET /api/onboarding`, `GET /api/edge-units`, OpenAPI path list

## Blocking Findings

1. **#46 — Heartbeat ingestion could silently erase a just-accepted runtime mapping.**
   `ProcessHeartbeat.RefreshAsync` read the unit in one `GreenhouseDatabase` operation and wrote the
   whole row in another. `EdgeUnitRepository.UpsertAsync` wrote `UnitName`, `Location`,
   `MappingVersion`, `MappingStatus` and every slot assignment, so a mapping `PUT` landing between
   the read and the write was reverted — then published to the Edge Unit as an empty mapping at
   version 0, and marked `acknowledged`. Concurrency is real, not theoretical:
   `MqttMessagingService` dispatches each message via `_ = Task.Run(...)`.
   *Risk:* persisted state corruption on the primary happy path, silent.
   *Fix applied:* decision moved to pure `HeartbeatReconciliation.Reconcile`; new
   `RecordHeartbeatAsync` does read-reconcile-write in one gated operation; `UpsertAsync` removed
   from the port.
   *Prevention note:* `code-review-gate` has no never event covering a read-modify-write spanning
   two persistence operations on a row a concurrent writer owns. Proposed as a guardrail addition.

2. **#47 — BLE streaming scan could park past its window and ignore cancellation.**
   `BlueZBleTransport.ScanAsync` bounded the window only by cancelling `ReadLineAsync`, which a
   child-process pipe read does not honour on Linux. A silent `bluetoothctl` hung the session in
   `scanning` indefinitely, ignored explicit cancel, and blocked graceful shutdown via
   `OnboardingWorkflow.DisposeAsync`.
   *Risk:* unrecoverable session state and a daemon that will not stop cleanly.
   *Fix applied:* each read raced against a window task; teardown bounded, then kill the process tree.
   *Prevention note:* second subprocess-lifetime defect in this file (see #41, stderr never drained).
   No skill covers external-process discipline. Proposed as a guardrail addition.

3. **#48 — Publish failure left a mapping stuck in `publish-pending` with no recovery.**
   `PublishAsync` throws when the broker is offline; the throw escaped the retry budget into the
   pump's catch-all, so the unit never reached a terminal status and nothing re-published.
   *Risk:* mapping stored but never delivered; misses the AGENTS.md gate that offline paths be
   "explicit and recoverable".
   *Fix applied (part 1):* attempts extracted to `AttemptAsync` returning
   `Acknowledged | Retry | Rejected`; a transport throw spends an attempt and honours the backoff.
   *Outstanding (part 2):* re-publish pending mappings on reconnect — `IMessagingService` exposes no
   reconnect hook. Issue left open.
   *Prevention note:* propose a rule that a retry budget must cover transport failure, not only
   protocol-level rejection.

## Non-Blocking Findings

4. **#49 — `FailAsync` could resurrect a cancelled session as `failed`.** No guard on current status,
   unlike every other transition in `OnboardingWorkflow`. Fixed by passing the owning status.
5. **#50 — hardening cluster, 8 items.** `_loaded` set before the load; `IsRoutableIpv4` accepting
   `0.0.0.0` and malformed octets; `MappingValidation` doc comment describing a validation layer that
   does not exist; `_lastMessageId` fixed seed; duplicate `slot_id`s reading as no-drift;
   `HeartbeatSubscriptionService` disposing its CTS under in-flight handlers; `_background`
   overwritten without awaiting; unclosed `SqliteConnection`. All fixed.

## Architecture Boundary Checks

Evidence recorded per `code-review-gate`:

- Each changed file is in the correct layer. `IOnboardingNotifier` keeps SignalR types out of
  `Greenhouse.Core`; `SignalROnboardingNotifier` is the only place hub event names live.
- Presentation depends on application abstractions only. Controllers take `IEdgeUnitRepository` /
  `IOnboardingWorkflow` / `UpdateEdgeUnitMapping`, never storage or MQTT implementations.
- Infrastructure stays in infrastructure modules. `IBleTransport` and `BleDeviceInfo` are `internal`
  to `Greenhouse.Bluetooth`; no EF entity type escapes `Greenhouse.Storage`.
- Dependency direction points inward. `Microsoft.Extensions.Logging.Abstractions` added to
  `Greenhouse.Core` is contract-only.
- Recurring message streams go through the shared abstraction: heartbeat and `ghcfg/#` are wired via
  `IMessagingService` from `IHostedService` registrations, not from a lifecycle-scoped feature service.

**Never events: none present.** Explicitly checked all six, including "lifecycle-scoped services
owning recurring heartbeat ingestion" and "feature-specific application services directly subscribing
to transport channels" — both correctly avoided by `HeartbeatSubscriptionService`.

## Contract Compliance

- REST: kebab-case routes, camelCase JSON. All seven new paths publish in OpenAPI.
- MQTT: snake_case at the broker boundary; topics built only via `EdgeUnitTopics`.
- Retry budget matches the documented 8s × 3 at 1s/2s; asserted separately from the scaled-down
  test policy.

## Residual Risks and Testing Gaps

- **#47 is unverifiable off-device.** `FakeBleTransport` enumerates a list, so no unit test can
  exercise the pipe-read behaviour. Verified by construction only.
- **The review fixes themselves are unreviewed.** 041c86f was written by the same pass that raised
  the findings. No independent review or test-gap pass has run over it.
- **`ghcfg/wr-` → `ghcfg/ack-` round trip and BLE provisioning against real hardware** remain
  unexercised, as the PR states.
- Publisher still reads the unit at publish time, so a mapping accepted mid-publish leaves the status
  transiently describing the previous version. Converges; not filed.

## Merge Decision

**Not safe to merge at review time.** Three blocking findings. After 041c86f the blocking findings
are addressed, but merge remains gated on an independent review of that commit, plus #47's on-device
verification and a decision on #48 part 2. Recorded in `spec-status.md`.

## Documentation Feedback Items

See `doc-feedback.md`. Systemic items were filed as issues in `Greenhouse-Documentation`; the
pre-existing gap (drift notification channel, Main Unit onboarding error codes) was already filed by
the implementation pass as Greenhouse-Documentation#32.
