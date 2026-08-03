# QA Report — edge-unit-configuration (Stage 5, on-device)

- **Spec:** `specs/edge-unit-configuration/spec.md` (Greenhouse-Documentation)
- **BLE contract:** `specs/edge-unit-onboarding/spec.md`
- **Tracking issue:** Greenhouse-Services#54
- **Date:** 2026-08-03
- **Verdict:** **No-Go for the BLE onboarding path. Go for the MQTT configuration path.**

This is the first Stage 5 artifact produced for any spec in this repository. It is a **partial** pass:
the MQTT configuration lifecycle is validated end to end against real Mosquitto, and BLE discovery is
validated against a real Edge Unit, but **BLE provisioning is blocked by a defect found in this pass**
and could not be completed.

## Environment

| | |
|---|---|
| Main Unit | Raspberry Pi 4 (8 GB), Debian Bookworm, kernel 6.12 arm64, `192.168.4.94` |
| Services build | `main` at `c20cbf3` — published to `/opt/greenhouse-services`, run as the `greenhouse-services` systemd unit |
| BlueZ | `bluetoothctl` 5.66, adapter `hci0` `E4:5F:01:8E:47:93`, UP RUNNING |
| Broker | Mosquitto on `localhost:1883` (the daemon's own broker) |
| Edge Unit | Real ESP32 advertising `GH-Edge-704BCA69CC00` (`70:4B:CA:69:CC:02`), unprovisioned |
| API | `127.0.0.1:5150` |

The deployed artifact was brought into line with `main` for this pass: the Pi's long-standing
uncommitted `Cache=Shared` connection-string edit (#56) was stashed before publishing, so what was
tested is what `main` says. The daemon started clean without it and served requests throughout — see
"Incidental findings" below.

## Scenario results

Numbering follows #54's scope list.

### 1. Onboarding end to end — **Fail (blocked)**

| Step | Result |
|---|---|
| `POST /api/onboarding/scan` | **Pass** — `202 {"status":"scanning"}` |
| Candidate appears | **Pass** — real unit surfaced in ~2–6 s: `{"deviceId":"704BCA69CC00","advertisedName":"GH-Edge-704BCA69CC00","rssi":null}` |
| Window closes | **Pass** — `scanning` → `candidates-ready` exactly 30 s after the window opened |
| `connect` to the unit | **Pass** — `Connection successful`, `ServicesResolved: yes`; the onboarding service `00014452-414f-424e-4f2d-454744454847` is exposed |
| Write provisioning payload | **Fail — #72** |
| Read provisioning status | **Fail — #72** |
| First `gh/heartbeat` → `mapping-required` → mapping → `acknowledged` | **Not reached** |

`select-attribute`, `read` and `write` are `bluetoothctl` **`gatt` submenu** commands. The transport
issues them in the main menu, where every one is rejected (`Invalid command in menu main: read`). With
`menu gatt` first, the same UUID reads the firmware's real status payload
(`{"result":"error","error_code":2099,"error_message":"no provisioning payload received"}`), so the
firmware and the GATT contract are correct and only the Main Unit's command sequence is wrong.

Worse, `WriteCharacteristicAsync` decides failure by matching `Failed to write`, which the rejection
text does not contain — so a write that transmitted nothing is reported as a success. The
operator-visible result of provisioning a real unit today is error **2099 "Empty provisioning status
response."**, blaming the Edge Unit for a Main Unit fault, with the payload never having left the Pi.

Filed as **#72**. This is the blocking defect for the whole BLE path.

A design assumption worth recording as validated: the transport uses **one `bluetoothctl` process per
operation**, so a connection must survive the connecting process exiting. It does — a separate process
observed `Connected: yes` afterwards. `connect` does require the device to be in bluetoothd's cache,
which the preceding scan provides.

### 2. Scan window (#47) — **Partial pass**

| Check | Result |
|---|---|
| Scan does not park past its window | **Pass** — closed at +30 s to the second |
| Cancel mid-scan returns to idle promptly, no `bluetoothctl` left | **Pass** — child `pid 2856` confirmed alive, cancel returned in **75 ms**, child reaped, zero `bluetoothctl` remaining |
| Daemon shutdown not blocked by an in-flight scan | **Pass** — child `pid 2955` alive, `systemctl stop` completed in **219 ms**, no orphan |
| Empty RF environment reaches `no-device-found` inside the window | **Not run** — a `GH-Edge-` unit was advertising throughout |

#47 is **not** closed on this evidence. The park it describes needs a *silent* `bluetoothctl`; with a
unit advertising, stdout kept producing lines, so the timer-only abandonment path was never the thing
under test. Needs one run with no unit powered.

### 3. `bluetoothctl` stderr / #41 — **Pass; deadlock not reproducible**

Every session was driven as the transport drives it, with stdout and stderr captured separately:

| Session | exit | stdout | **stderr** |
|---|---|---|---|
| 20 s scan | 0 | 1173 B | **0 B** |
| connect, nonexistent device | 0 | 589 B | **0 B** |
| connect, real unconnectable device | 0 | 589 B | **0 B** |
| `select-attribute` + `read` | 0 | 1156 B | **0 B** |
| `select-attribute` + `write` | 0 | 1170 B | **0 B** |
| disconnect an unconnected device | 0 | 411 B | **0 B** |
| invalid command | 0 | 589 B | **0 B** |

`bluetoothctl` 5.66 **writes nothing to stderr and exits 0 on every path, including every failure**.
Errors appear on stdout as prose. Therefore:

- The #41 deadlock is unreachable on this build — the hazard was real in code, but the writer never
  writes. The drain stays as defence in depth. #41 **closed** on this measurement.
- #67 (unfiltered stderr excerpt reaching operator state) — the excerpt is always empty and the throw
  carrying it is unreachable. **Closed, not reproducible.**
- #68 (non-zero exit discarding a valid read) — the exit code is never non-zero. **Closed, premise
  refuted.**
- The inverse is the real problem: with no failure signal from either exit code or stderr, every
  failure check is a stdout string match, and one is wrong. Rehomed to **#72**.

### 4. Configuration round trip over Mosquitto — **Pass**

Exercised with a **simulated Edge Unit peer** (`mosquitto_pub`/`mosquitto_sub` impersonating device
`AABBCCDDEEFF`), because the real unit could not be provisioned. The Main Unit side, the broker, and
the wire contract are real; the peer is not.

`gh/heartbeat` from an unknown device registered it at `pending-mapping`. `PUT
/api/edge-units/{id}/mapping` published to `ghcfg/wr-AABBCCDDEEFF`:

```json
{"schema_version":1,"message_id":797194001,"device_id":"AABBCCDDEEFF","mapping_version":1,
 "mapping_reason":"initial_registration","unit_name":"QA Bench Unit","location":"Bench",
 "slots":[{"slot_id":1,"role":"sensor","i2c_address":"0x44","capability":"temperature","label":"Air Temp"},
          {"slot_id":2,"role":"sensor","i2c_address":"0x48","capability":"humidity","label":"Air RH"}]}
```

`i2c_address` values were carried through from the heartbeat, not from the request — correct.

| Case | Result |
|---|---|
| Successful ack on `(message_id, mapping_version)` | **Pass** — acked `797194002` / v2 → status `acknowledged` |
| Retry budget with no ack | **Pass** — 3 publishes, **same `message_id`** each time, ack timeouts logged at `18:49:27` / `18:49:36` / `18:49:46` (8 s timeout plus 1 s and 2 s delays), terminal status `failed` |
| Non-retryable rejection (`error_code` 3003) | **Pass** — **1** publish, 1 ack, immediate `failed`, no retries spent; logged `rejected configuration (… error_code=3003)` |

### 5. Topology drift — **Pass**

| Check | Result |
|---|---|
| Drift raised on a changed slot module | **Pass** — slot 2 `0x48` → `0x4A` produced exactly one `differs from mapping version` warning |
| Raised **once**, not per heartbeat | **Pass** — a second identical drifted heartbeat produced no further warning |
| Unchanged slots keep their mapping; changed slots come back unassigned | **Pass** — slot 1 kept `role=sensor label=T`; slot 2 returned `role=null label=null` |
| Cleared only by a successful ack | **Pass** — after remap + successful ack (`acknowledged` v4), a *new* topology change (`0x4C`) raised the warning again, proving the flag had been cleared |

`TopologyDriftDetectedAt` is **not exposed through the REST API**, so drift was observed via the
daemon's log rather than a contract. The drift notification contract is an open documentation gap
(Greenhouse-Documentation#32).

### 6. Restart behaviour — **Fail**

| Check | Result |
|---|---|
| `mapping-required` survives a restart | **Pass** — a `pending-mapping` unit was unchanged after `systemctl restart` |
| Restart clears transient statuses | **Fail — #74** |

A restart during the publish window leaves the mapping at `published` **permanently**: watched for
40 s with `mosquitto_sub -t 'ghcfg/#'` — no republish, no retry, no timeout, no startup reconciliation,
status never moved. The honest path (no ack → 3 attempts → `failed`) is replaced by a status that looks
like progress and never resolves. Filed as **#74**.

## Defects filed

| Issue | Severity | Summary |
|---|---|---|
| **#72** | **Blocking** | GATT read/write issued in `bluetoothctl`'s main menu; provisioning cannot succeed, and the write failure is silent |
| **#74** | High | Restart mid-publish orphans a mapping at `published` with no retry or recovery |
| **#73** | Medium | Scan RSSI is always null on-device; `bluetoothctl` emits no RSSI lines, so the descending-RSSI ordering is inert |

Closed on this pass's evidence: **#41** (characterised, deadlock unreachable), **#67** and **#68**
(premises refuted). Left open with partial evidence: **#47** (needs an empty-RF run).

## Incidental findings

- **#56, partially advanced.** The daemon ran correctly on `main` without the Pi's local
  `Cache=Shared` edit, and `/opt/greenhouse-services/appsettings.json` now matches `main`. This does
  **not** satisfy #56's clean-database first-run migration criterion — the database was already
  migrated — so #56 stays open.
- Status vocabulary drift: #54 and the spec speak of `mapping-required`, while the API reports
  `pending-mapping`. Worth reconciling in the spec or the contract.

## Not validated

- **BLE provisioning end to end.** Blocked by #72. Cannot be attempted again until that is fixed.
- **First heartbeat from a real Edge Unit**, and therefore the real onboarding completion path. Every
  heartbeat in section 4 came from a simulated peer.
- **Empty-RF `no-device-found`** (section 2), which is the decisive case for #47.
- **Main Unit setup path.** `POST /api/setup/wifi-config` performs a live `nmcli dev wifi connect`, and
  the Pi is reachable only over that WiFi link, so it was deliberately not exercised. Provisioning is
  gated on stored WiFi credentials, which is a second reason section 1 could not complete.
