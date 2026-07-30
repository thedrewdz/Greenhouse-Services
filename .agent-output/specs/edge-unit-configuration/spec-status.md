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

- Status: `ready-for-review`
- Updated At: `2026-07-30`
- Updated By: `Retrospective Agent`
- Reason: `Review findings #46-#50 were fixed in 041c86f by the same pass that raised them, so those fixes have had no independent review or test gate. Harness Stage 4 exit for fixable findings is ready-for-implementation, then Test, then Review again. Returning to ready-for-review rather than ready-for-qa: a second, separate review pass over 041c86f is the outstanding gate. #47 and #48 remain open defect sub-issues.`

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

Blocking a move to `ready-for-qa`:

1. Independent review of 041c86f (the review-fix commit). Reviewer and fixer were the same actor.
2. `#47` — BLE scan window fix has no automated coverage and cannot get any; needs on-device
   verification on the test Pi.
3. `#48` — part 2 (re-publish pending mappings on broker reconnect) not implemented; needs a seam
   decision on `IMessagingService`.

Blocking a move to `complete`:

4. Stage 5 QA has never run for this spec. The PR's "smoke-tested with no UI present" is Stage 2
   local verification, not a QA pass against acceptance criteria.
5. PR #45 has no recorded review and no approval.

## Status History

- `2026-07-30` | `(absent) -> ready-for-review` | `Retrospective Agent` | `Back-filled. Reconstructed lifecycle: implementation of all seven sub-issues #30-#36 completed on feature/25-edge-unit-onboarding-and-configuration (5dfdb13); code review raised five defects #46-#50; fixes applied in 041c86f; suite 235 -> 251 passing. No stage artifacts were produced during any of it.`
