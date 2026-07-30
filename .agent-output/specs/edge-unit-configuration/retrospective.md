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

---

# Retrospective Round 2 (epic #25)

Date: `2026-07-30` · Repo: `thedrewdz/Greenhouse-Services` · Trigger: follow-up action 1 above —
independent review of `041c86f` — was executed, and produced findings.

## Inputs

- Round 2 review: `review-report.md` § "Review Round 2", issues #51, #52, #53 (all filed, fixed in
  `1be33f4`, closed).
- Board hygiene: #46 was found still at `Todo` on the board despite being closed; corrected.
- Epic hygiene: #47 and #48 detached from #25 as sub-issues; #48 rescoped to part 2 and retitled.
- Loop evidence: `5dfdb13` → `041c86f` → `1be33f4`; epic #25 comments; PR #45 body (twice corrected).

## What Worked

- **The previous retrospective's top follow-up was the right call, and it paid.** It predicted that
  `041c86f` had been reviewed by nobody but its author and ranked that highest. The independent pass
  found three issues — and, more tellingly, **two of the five first-round issues had been closed on
  claims that were not true** (#49 fixed narrowly, #50 item 8 not fixed at all). A 40% false-close
  rate on a single review round is not a hunch any more; it is a measurement.
- **Removing `UpsertAsync` from the port held up under adversarial reading.** Round 2 tried to find a
  way for heartbeat ingestion to touch mapping columns and could not. Structural fixes survive
  re-review in a way that conditional fixes (#49's status guard) do not.
- **Injected policy objects paid out a second time.** `ConfigurationPublishPolicy` and
  `OnboardingTimeouts` again let #51's and #53's regression tests run in milliseconds.
- **Reproducing before fixing changed the outcome.** #51 was written as a failing test first
  (`Expected: "provisioning" / Actual: "failed"`). #52 was proved with a ten-line DI probe. Both would
  have been arguable as prose; neither was arguable as output.
- **Detaching open defects from the epic worked as intended.** #47 and #48 no longer hold #25 open for
  work that is not epic follow-through, and the epic's sub-issue list now reads as "what #25
  delivered".

## What Failed or Repeated

- **Fix completion is asserted, never demonstrated.** #50 item 8's fix was described in the commit
  message, in the issue comment, and in a new source comment — all three wrong, and all three
  checkable in ten lines. Separately, #50's acceptance criterion "test coverage added for items 1 and
  3" was ticked with only item 3 covered. **Nothing in the harness asks a fix to produce evidence**;
  it asks for a description, and a description is what it gets.
- **The same defect shape appeared twice, again.** #49 → #51 is "guard a transition on a mutable
  global rather than on the acting session's identity". This is the second time in this epic that a
  fix addressed the instance and left the class (the first being #47/#41, subprocess lifetime). The
  Round 1 retrospective already named "one blind spot, two findings" and it recurred within the same
  delivery.
- **Round 2 collapsed review and fix exactly as Round 1 did.** I raised #51–#53, fixed them, wrote
  their tests, and closed them. `1be33f4` has had no independent review. The guardrail identified
  last round was not available to me because it was filed, not applied — see Root Cause 1.
- **The board did not follow the issue.** #46 was closed but sat at `Todo` on the board, while #49 and
  #50 — closed in the same batch — went to `Done`. The board is the retrospective skill's "primary
  status authority" and it was silently wrong for one item in three.
- **PR #45 has zero CI checks and zero approvals**, at 7,414 additions. `Greenhouse-Services` has one
  workflow, `add-to-project.yml`, which is issue-triggered. The harness's merge gate assumes an
  approval that no mechanism produces and a check suite that does not exist. Round 1 did not catch
  this because it never queried the PR's review state.

## Root Cause Patterns

1. **Guardrails filed are not guardrails applied.** Round 1 produced seven guardrail updates and
   applied none, deferring items 1, 2 and 6 to the user as design calls — correctly. But the effect is
   that Round 2 ran under exactly the conditions Round 1 diagnosed, and reproduced two of the same
   failures. A finding parked in an issue queue changes nothing about the next pass. **Filed ≠ fixed
   applies to process defects as much as to code defects.**

2. **The harness verifies descriptions, not behaviour.** Every stage artifact is prose. The acceptance
   criteria are checkboxes that the ticking party also authors. Nothing anywhere requires a fix to be
   accompanied by output — a failing test that now passes, a probe, a command transcript. Both of
   Round 2's substantive findings (#51, #52) were things a *description* got wrong and an *execution*
   got right. This is distinct from the self-review problem: an independent reviewer reading prose
   would also have missed #52.

3. **The stated working preference and the harness are in direct conflict, and nobody has adjudicated
   it.** The user's standing instruction is that every review finding is filed as an issue *and* fixed
   on the branch in the same session. The harness routes fixable findings to
   `ready-for-implementation` → Test → Review again, precisely so the fixer is not the reviewer.
   Following one violates the other. Two rounds have now resolved this silently in favour of the user
   preference and then flagged the result as a gate — which converges on nothing, because each round's
   fixes need a round that will itself need a round. **This needs a decision, not another finding.**

4. **Structural fixes survive; conditional fixes regress.** #46 (remove the capability) held up.
   #49 (add a condition) did not. #50 item 8 (change a registration) was wrong about the framework.
   The pattern worth generalising: prefer fixes that remove the ability to express the defect.

## Never Event Follow-Through

- **Blocking findings this round: none.** #51, #52 behavioural non-blocking; #53 tech-debt.
- **Never events per `code-review-gate`: none present.** Re-checked against the Round 2 diff.
- **Round 1's proposed never event (cross-operation read-modify-write) would not have caught #51 or
  #52.** Two additions are proposed below to cover the shapes that actually recurred.
- **Verification that guardrails are in place: still *no*.** Round 1's seven items remain filed and
  unapplied (docs#33–#36 all open). This section stays open.

## Guardrail Updates

Round 1's items 1–7 stand unchanged and undecided. Deliberately **not applied** here: items 1, 2 and 6
are harness-design calls the user has not made, and pre-empting them would decide docs#33–#36 by fiat.
New this round:

| # | Target | Change | From | Filed |
|---|---|---|---|---|
| 8 | `feature-delivery-harness.md` + `.agents/skills/implementation` | A fix must ship **evidence**, not a description: the failing-test-now-passing, a probe transcript, or a command output, quoted on the issue. Acceptance criteria may not be ticked by the author without it. | Pattern 2, #52, #50 item 1 | docs#37 |
| 9 | `.agents/skills/code-review-gate` | Add never event: *guarding a state transition on a mutable shared value (status, flag, name) rather than on the identity of the actor the work belongs to.* | #49 → #51 | docs#38 |
| 10 | `feature-delivery-harness.md` | Decide the review/fix separation conflict, and write the answer down either way. | Pattern 3 | docs#39 |
| 11 | `.agents/skills/retrospective` Board Operations | Board status must be verified after closing an issue, not assumed to follow. | #46 stuck at Todo | comment on docs#36 |
| 12 | `Greenhouse-Services` repo | No CI. The harness assumes a PR approval and check suite that do not exist for this repo. | PR #45: 0 reviews, 0 checks | docs#39 |

## Loop Evidence Considered

- Implementation loop count: **3** — delivery (`5dfdb13`), review-fix (`041c86f`), re-review-fix (`1be33f4`).
- Test artifacts: still **none exist** as stage artifacts. Substituted: `dotnet test` at all three
  commits (235 → 251 → 254 passing).
- Review artifacts: `review-report.md`, now with an independent Round 2 section; issues #46–#53.
- QA artifacts: **none exist.** Stage 5 has still never run.

## Documentation Changes Made

- `.agent-output/specs/edge-unit-configuration/review-report.md` — Round 2 section appended (the
  independent review gate 1 required).
- `.agent-output/specs/edge-unit-configuration/retrospective.md` — this section.
- `.agent-output/specs/edge-unit-configuration/spec-status.md` — gates re-evaluated; gate 1 satisfied.
- Issues #47, #48 detached from #25; #48 rescoped and retitled; #46 board status corrected to Done.
- PR #45 body corrected twice (stale test count; stale "remain open and unchanged" claim).

## Follow-Up Actions

1. **Decide the review/fix separation conflict (docs#39).** Everything else is downstream of this.
   Until it is decided, every round of fixes generates a gate that only another round can clear.
2. **Stage 5 QA on the test Pi** — #47's scan window, #41's stderr drain, BLE provisioning against a
   real Edge Unit, and the `ghcfg/wr-` → `ghcfg/ack-` round trip. Unchanged from Round 1.
3. **Apply, or explicitly decline, Round 1 guardrails 1–7 (docs#33–#36).** Two rounds have now run
   under conditions those items describe.
4. **Add CI to `Greenhouse-Services` (docs#39)** — at minimum build + test on PR, so a merge gate
   exists at all.
5. **#48 part 2** — decide the reconnect seam on `IMessagingService`. Now tracked independently of
   this epic.
6. Do **not** merge PR #45, close #25, or set the board item Done until the skill's own step 2 gate is
   satisfiable — see `spec-status.md`.
