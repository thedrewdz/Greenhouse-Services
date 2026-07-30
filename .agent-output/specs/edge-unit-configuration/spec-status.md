# Execution Spec Status: Edge Unit Configuration

## Scope

- Spec name: `edge-unit-configuration`
- Source spec: `specs/edge-unit-configuration/spec.md`
- Repository: `thedrewdz/Greenhouse-Services`

This file tracks execution lifecycle status in implementation repositories only.

> **Back-filled 2026-07-30.** This file did not exist while epic #25 was implemented and reviewed.
> The whole `.agent-output/specs/edge-unit-configuration/` chain was absent, so every stage entry
> gate passed by absence rather than by check. History below is reconstructed from the PR #45 record,
> the epic #25 comments, and issues #46–#50; dates before 2026-07-30 are attributed from those
> sources, not observed live. See `retrospective.md` for the root-cause analysis.

## Current Status

- Status: `ready-for-qa`
- Updated At: `2026-07-30`
- Updated By: `Retrospective Agent (Round 2)`
- Reason: `Gate 1 satisfied: 041c86f received the independent review it was waiting on (review-report.md, Round 2). It produced three findings - #51, #52, #53 - all non-blocking, all fixed in 1be33f4 with regression coverage; suite 254 passed, 0 failed. No blocking code findings remain. Gates 2 and 3 no longer belong to this spec's exit: #47 is on-device verification, which is Stage 5 QA work rather than a review gate, and #48 was detached from epic #25 and rescoped to part 2, a separate IMessagingService seam decision. Moving to ready-for-qa. Complete remains blocked on Stage 5 QA never having run and on PR #45 having no approval and no CI checks - see gates below. 1be33f4 is itself unreviewed by anyone but its author; that is not tracked as a code gate but as the unresolved process conflict in retrospective Round 2, root cause 3.`

## Allowed Status Values

- `new`
- `ready-for-implementation`
- `implementation-in-progress`
- `ready-for-test`
- `test-in-progress`
- `ready-for-review`
- `review-in-progress`
- `ready-for-qa`
- `qa-in-progress`
- `complete`
- `blocked`

## Outstanding Gates

Blocking a move to `ready-for-qa`: **none.** All three prior gates are resolved or reassigned:

1. ~~Independent review of 041c86f.~~ **Satisfied 2026-07-30** — `review-report.md` § Review Round 2.
   Three findings (#51, #52, #53), none blocking, all fixed in `1be33f4`, all closed.
2. ~~`#47` on-device verification.~~ **Reassigned to gate 6.** It is Stage 5 QA work, not a review
   gate, and #47 is no longer a sub-issue of epic #25.
3. ~~`#48` part 2.~~ **Reassigned out of this spec.** Detached from epic #25 and rescoped; the
   reconnect seam on `IMessagingService` is separate work, and part 1 shipped.

Blocking a move to `complete`:

4. **Stage 5 QA has never run for this spec.** The PR's "smoke-tested with no UI present" is Stage 2
   local verification, not a QA pass against acceptance criteria.
5. **PR #45 has no approval and no CI checks.** `reviewDecision` is empty, `reviews` is 0, and
   `statusCheckRollup` is 0 — the repository has no workflow beyond `add-to-project.yml`. The
   retrospective skill's step 2 ("confirm a PR exists **and has been approved**, else comment and
   stop") cannot currently be satisfied by any mechanism this repository has. Tracked as
   Greenhouse-Documentation#39.
6. **On-device QA on the test Pi**, covering `#47`'s scan window, `#41`'s stderr drain, BLE
   provisioning against a real Edge Unit, and the `ghcfg/wr-` → `ghcfg/ack-` round trip.

Noted, not tracked as a code gate:

7. `1be33f4` was written by the pass that raised its findings, exactly as `041c86f` was. Treating this
   as a gate a third time would not converge — each round of fixes would need a round that needs a
   round. It is recorded instead as the unresolved process conflict in `retrospective.md` Round 2,
   root cause 3, and filed as Greenhouse-Documentation#39 for a decision.

## Status History

- `2026-07-30` | `(absent) -> ready-for-review` | `Retrospective Agent` | `Back-filled. Reconstructed lifecycle: implementation of all seven sub-issues #30-#36 completed on feature/25-edge-unit-onboarding-and-configuration (5dfdb13); code review raised five defects #46-#50; fixes applied in 041c86f; suite 235 -> 251 passing. No stage artifacts were produced during any of it.`
- `2026-07-30` | `ready-for-review -> ready-for-qa` | `Retrospective Agent (Round 2)` | `Independent review of 041c86f performed (review-report.md Round 2). Found #51 (stale failure can fail a newly started session - #49 fixed the instance, not the class), #52 (SqliteConnection still never disposed - #50 item 8 was closed on a false claim, disproved by a DI probe), #53 (three tech-debt follow-ups). All non-blocking, all fixed in 1be33f4, suite 251 -> 254 passing. No blocking code findings remain. Gates 2 and 3 reassigned: #47 is QA work, #48 is detached from the epic.`
