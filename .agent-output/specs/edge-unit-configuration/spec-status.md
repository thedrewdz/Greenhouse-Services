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

- Status: `complete`
- Updated At: `2026-07-30`
- Updated By: `Retrospective Agent (Round 2)`
- Reason: `Closed by user decision at merge, on the condition that every finding still needing work has its own independent board item. Verified before merging: 13 items, all on Greenhouse Delivery. Stage 5 QA has still NEVER run for this spec - it is carried out of the epic as Greenhouse-Services#54 rather than performed, so complete here means "delivery scope merged and remaining work independently tracked", not "verified against acceptance criteria on hardware". PR #45 merged with no approval and no CI checks, because the repository has no mechanism to produce either; that gap is Greenhouse-Services#55. Prior reason retained below.`
- Prior Reason (`ready-for-qa`): `Gate 1 satisfied: 041c86f received the independent review it was waiting on (review-report.md, Round 2). It produced three findings - #51, #52, #53 - all non-blocking, all fixed in 1be33f4 with regression coverage; suite 254 passed, 0 failed. No blocking code findings remain. Gates 2 and 3 no longer belong to this spec's exit: #47 is on-device verification, which is Stage 5 QA work rather than a review gate, and #48 was detached from epic #25 and rescoped to part 2, a separate IMessagingService seam decision. Moving to ready-for-qa. Complete remains blocked on Stage 5 QA never having run and on PR #45 having no approval and no CI checks - see gates below. 1be33f4 is itself unreviewed by anyone but its author; that is not tracked as a code gate but as the unresolved process conflict in retrospective Round 2, root cause 3.`

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

Gates 4–6 were **carried out of this spec, not cleared**, by user decision at merge. Each is now an
independent board item, which was the stated condition for merging:

4. ~~Stage 5 QA has never run for this spec.~~ → **Greenhouse-Services#54.** Still true; the PR's
   "smoke-tested with no UI present" is Stage 2 local verification, not a QA pass against acceptance
   criteria. No `qa-report.md` exists for this spec, or for any spec in this repository.
5. ~~PR #45 has no approval and no CI checks.~~ → **Greenhouse-Services#55** (the repository change)
   and **Greenhouse-Documentation#39** (what "approved" means for a single-maintainer repo). PR #45
   merged with `reviewDecision` empty, 0 reviews and 0 check runs — recorded plainly rather than
   worked around.
6. ~~On-device QA on the test Pi.~~ → folded into **Greenhouse-Services#54**, covering `#47`'s scan
   window, `#41`'s stderr drain, BLE provisioning against a real Edge Unit, and the `ghcfg/wr-` →
   `ghcfg/ack-` round trip.

Noted, not tracked as a code gate:

7. `1be33f4` was written by the pass that raised its findings, exactly as `041c86f` was. Treating this
   as a gate a third time would not converge — each round of fixes would need a round that needs a
   round. It is recorded instead as the unresolved process conflict in `retrospective.md` Round 2,
   root cause 3, and filed as Greenhouse-Documentation#39 for a decision.

## Status History

- `2026-07-30` | `(absent) -> ready-for-review` | `Retrospective Agent` | `Back-filled. Reconstructed lifecycle: implementation of all seven sub-issues #30-#36 completed on feature/25-edge-unit-onboarding-and-configuration (5dfdb13); code review raised five defects #46-#50; fixes applied in 041c86f; suite 235 -> 251 passing. No stage artifacts were produced during any of it.`
- `2026-07-30` | `ready-for-qa -> complete` | `Retrospective Agent (Round 2)` | `User decision: merge and close, conditional on every finding still needing work having an independent board item. Verified 13 items on Greenhouse Delivery before merging - Greenhouse-Services #41, #47, #48, #54, #55 and Greenhouse-Documentation #32-#39. Two were filed to satisfy the condition: #54 (Stage 5 QA, never run) and #55 (repository has no CI). PR #45 merged; #25 closed; board item Done.`
- `2026-07-30` | `ready-for-review -> ready-for-qa` | `Retrospective Agent (Round 2)` | `Independent review of 041c86f performed (review-report.md Round 2). Found #51 (stale failure can fail a newly started session - #49 fixed the instance, not the class), #52 (SqliteConnection still never disposed - #50 item 8 was closed on a false claim, disproved by a DI probe), #53 (three tech-debt follow-ups). All non-blocking, all fixed in 1be33f4, suite 251 -> 254 passing. No blocking code findings remain. Gates 2 and 3 reassigned: #47 is QA work, #48 is detached from the epic.`
