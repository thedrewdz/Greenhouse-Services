# Retrospective: Edge Unit Configuration (epic #25)

Date: `2026-07-30` · Role: `Retrospective Agent` · Repo: `thedrewdz/Greenhouse-Services`

## Inputs

- Review findings: issues #46–#50; `review-report.md` (back-filled this session).
- Test findings: **none produced.** Stage 3 never ran as a stage for this spec.
- QA findings: **none produced.** Stage 5 never ran.
- Implementation deviations: recorded in the epic #25 comment and the PR #45 body — `GreenhouseDatabase`
  seam, streaming BLE scan, dual identity on `ProvisionableUnit`, extra `TopologyDriftDetectedAt`
  column, `Logging.Abstractions` added to Core.
- Documentation feedback: Greenhouse-Documentation#32 (drift notification channel; Main Unit
  onboarding error codes), filed by the implementation pass.
- Loop evidence: PR #45 (`5dfdb13`), review-fix commit `041c86f`, epic #25 comments, `agent-handoff.md`.

## What Worked

- **Contract-first seams made the review tractable.** Every one of the five findings localised to a
  single layer, because `IOnboardingNotifier`, `IEdgeUnitProvisioningTransport`, `IMessagingService`
  and `IEdgeUnitRepository` were real boundaries rather than pass-throughs. Zero architecture
  never-events in a 6,484-line diff is the seams working, not luck.
- **Injected policy objects paid for themselves during the fix.** `ConfigurationPublishPolicy` and
  `OnboardingTimeouts` already existed, so #48's regression tests ran in milliseconds without
  inventing a seam. A fix is cheap exactly where the original author left a test seam.
- **The port was the only coupling, and the compiler proved it.** Removing `UpsertAsync` from
  `IEdgeUnitRepository` broke 9 call sites in one test file and nothing else. That is the payoff of
  "no entity type escapes Storage" being enforced rather than aspirational.
- **The #41 issue template was directly reusable.** Problem / Impact / Fix / Acceptance criteria +
  `Source: Identified during code review of #NN` needed no decisions — I copied a precedent. Cheap
  conventions that survive one use are the ones worth having.
- **The add-to-project guardrail worked silently and completely.** All five new issues landed on the
  board with zero manual adds, as designed.
- **The implementation pass's own honesty shortened the review.** The PR body named what was
  unverifiable on a dev host and filed the doc gap instead of inventing a hub event. I spent no
  effort rediscovering known limits, and no effort unwinding an invented contract.

## What Failed or Repeated

- **No `.agent-output/` existed for this spec — at all.** Stages 2–6 mandate
  `implementation-plan.md`, `test-gap-report.md`, `review-report.md`, `qa-report.md`,
  `doc-feedback.md`, `spec-status.md`. Zero of six were present. The `edge-unit-onboarding` dossier
  has four of them from an earlier cycle, so this is a **regression in harness adherence**, not a
  practice that never started.
- **Canonical status was stale by an entire delivery cycle.** `specs/edge-unit-configuration/status.md`
  read `ready-for-dev` with "Last observed execution status: `not started`" while the epic was
  implemented, reviewed, and patched.
- **Stage 3 (Test) and Stage 5 (QA) never ran.** Tests shipped with the implementation, which Stage 2
  requires — but no independent coverage pass preceded review. Consequence: **three of five review
  findings were untested paths** (#46, #48, #49) that a test-gap pass targeting negative and
  degraded-state paths would plausibly have caught first, and more cheaply.
- **Review and fix collapsed into one pass.** The harness routes fixable findings to
  `ready-for-implementation` → Test → Review again. I raised the findings, fixed them, wrote their
  tests, and declared them done. **041c86f has had no independent review.**
- **The same defect class appeared twice in one file, plus once before.** #47 (read cancellation
  assumed to interrupt a pipe read) and #41 (stderr never drained) are both subprocess-lifetime
  defects in `BlueZBleTransport`. Two independent findings, one blind spot.
- **I filed the defect issues with prose linkage only.** "Part of #25" in the body, while #30–#36
  were properly linked via the sub-issues API. The retrospective skill gates Done on
  `GET /issues/25/sub_issues` — so my own five defects were **invisible to the gate that exists to
  catch them**. Fixed this session; the convention existed and I diverged from it.
- **Artifact promotion has never run.** No `promotion-log.md` exists for any spec in the repository.
  Stage 6's promotion half has never executed, for any feature.

## Root Cause Patterns

1. **Two bookkeeping systems, no reconciliation.** The retrospective skill calls the board "the
   primary status authority"; the delivery workflow mandates a parallel file chain in
   `.agent-output/`. Nothing reconciles them, so each stage uses whichever is convenient and the
   other rots silently. This single cause explains the stale `status.md`, all six missing artifacts,
   *and* my own instinct to file issues instead of writing `review-report.md`. It is the highest-value
   thing to fix, and it is a harness-design decision rather than a bug.

2. **Gates fail open, so absence reads as consent.** "Every stage owner must enforce status entry
   gates" — but the gate state lives in a file that was never created, and a missing file is
   indistinguishable from a satisfied gate. Every stage passed by absence. A gate that cannot fail
   is decoration.

3. **Role separation is a document, not a mechanism.** The harness assigns distinct agents per stage;
   a single session collapses them and nothing notices. Self-review is structurally invisible —
   there is no record of *who* reviewed, so "reviewed" and "reviewed by the author" look identical
   downstream.

4. **External-process discipline is unowned.** Three defects (#41, #47, and the unbounded teardown
   fixed alongside #47) share a root: no skill, checklist, or never-event covers driving a subprocess
   — drain every stream, bound every wait, never trust cancellation of a pipe read, always kill.

5. **Retry budgets were specified against protocol failures only.** #48 existed because the spec's
   reliability section enumerates Edge Unit rejection codes; nobody asked what happens when the
   transport itself is down. The budget covered the documented failures and missed the likely one.

## Never Event Follow-Through

- **Blocking findings referenced:** #46, #47, #48.
- **Never events per `code-review-gate`: none present.** Explicitly checked all six; the two most
  relevant to this diff — lifecycle-scoped services owning heartbeat ingestion, and feature services
  subscribing to transport directly — were both correctly avoided via `IHostedService` +
  `IMessagingService`.
- **Gap identified in the never-event list itself:** #46 was the most severe finding of the review
  and the list does not describe it. A read-modify-write spanning two persistence operations, on a
  row a concurrent writer owns, is exactly the kind of automatically-blocking structural defect the
  list is for. Proposed below.
- **Verification that guardrails are in place:** *not yet.* All guardrail changes below are proposals;
  none have been applied to canonical docs. This section stays open until they are.

## Guardrail Updates

Each traceable to a finding, and each **filed** in `Greenhouse-Documentation` per the retrospective
skill's step 6. **None applied to canonical docs yet** — items 1, 2 and 6 change how the harness
itself works, which is the user's design call, not mine.

| # | Target | Change | From | Filed |
|---|---|---|---|---|
| 1 | `workflows/feature-delivery-harness.md` | Make a missing `spec-status.md` a **hard blocker**, not a pass. Stage entry gates must fail closed on absent artifacts. | Pattern 2 | docs#33 |
| 2 | `feature-delivery-harness.md` + `.agents/skills/retrospective` | Resolve the board-vs-`.agent-output` duplication: pick one authority per concern, state where the other mirrors it. | Pattern 1 | docs#34 |
| 3 | `.agents/skills/code-review-gate` | Add never event: *read-modify-write spanning two persistence operations where a concurrent writer can interleave.* | #46 | docs#35 |
| 4 | New skill or `code-review-gate` section | External-process discipline: drain stdout **and** stderr, bound every wait, never rely on cancelling a pipe read, always kill on timeout. | #47, #41, Pattern 4 | docs#35 |
| 5 | `.agents/skills/retrospective` Board Operations | Require defect issues be linked via the sub-issues API, not prose, so the Done gate can see them. | Self-inflicted gap | docs#36 |
| 6 | `.agents/skills/retrospective` | (a) Disambiguate "all defect sub-issues closed" from delivery sub-issues that close on merge. (b) Require independent review of defects fixed by the pass that raised them. | Pattern 3 | docs#36 |
| 7 | `specs/edge-unit-configuration/spec.md` reliability section | State that a retry budget must cover transport failure, not only protocol-level rejection. | #48 | doc-feedback item 2 |

## Loop Evidence Considered

- Implementation loop count: **2** — initial delivery (`5dfdb13`), review-fix pass (`041c86f`).
- Test artifacts reviewed: **none exist.** Substituted: `dotnet test` runs at both commits
  (235 → 251 passing).
- Review artifacts reviewed: `review-report.md` (back-filled this session), issues #46–#50.
- QA artifacts reviewed: **none exist.** Substituted: loopback smoke test at both commits.
- Documentation feedback items reviewed: **1** — Greenhouse-Documentation#32.

## Documentation Changes Made

- `.agent-output/specs/edge-unit-configuration/spec-status.md` — created (back-filled). Execution
  status set to `ready-for-review`, with the outstanding gates named.
- `.agent-output/specs/edge-unit-configuration/review-report.md` — created (back-filled) so Stage 6
  has a stage artifact to consolidate.
- `.agent-output/specs/edge-unit-configuration/retrospective.md` — this file.
- `specs/edge-unit-configuration/status.md` (docs repo) — `ready-for-dev` → `in-dev`, correcting
  "Last observed execution status: not started".
- Issues #46–#50 linked as real sub-issues of #25.

## Follow-Up Actions

1. **Independent review of `041c86f`.** The review-fix commit has been reviewed by nobody but its
   author. Highest priority — it touches persistence, subprocess lifetime, and retry control flow.
2. **Decide guardrail items 1, 2 and 6** — these are harness-design calls.
3. **Stage 5 QA on the test Pi**, covering #47's scan window, #41's stderr drain, BLE provisioning
   against a real Edge Unit, and the `ghcfg/wr-` → `ghcfg/ack-` round trip.
4. **#48 part 2** — decide the reconnect seam on `IMessagingService`.
5. **Run artifact promotion at least once**, so `promotion-log.md` stops being theoretical.
6. Do **not** merge PR #45, close #25, or set the board item Done until 1 and 3 are done — see the
   gate list in `spec-status.md`.
