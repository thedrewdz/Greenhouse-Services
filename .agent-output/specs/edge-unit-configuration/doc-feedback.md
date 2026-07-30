# Documentation Feedback: Edge Unit Configuration

Append-only. Each item identifies source role, impact, recommended change, and disposition.

> **Back-filled 2026-07-30.** This file did not exist during implementation or review. Item 1 is
> reconstructed from the epic #25 comment and the PR #45 body; items 2–8 are from the code review and
> retrospective of this session.

---

## 1. Topology drift has no documented notification channel; no error code for Main Unit onboarding failure

- **Source role:** Implementation Agent
- **Impact:** The hub contract defines only `DeviceDiscovered` and `OnboardingStateChanged`, and no
  Edge Unit response field reports drift, so the UI has no documented way to learn a unit drifted.
  Separately, the 2001–2099 set is the Edge Unit's BLE response vocabulary — a Main Unit-side
  90-second heartbeat timeout, or missing WiFi credentials / local address, has no canonical code, so
  the session fails with `errorCode: null`.
- **Recommended change:** Define a drift notification channel (hub event or response field) and a
  Main Unit-side onboarding error code range.
- **Disposition:** **Filed** as Greenhouse-Documentation#32. Implementation proceeded without
  inventing either contract; drift is detected and persisted but not surfaced. Blocks the UI epic's
  drift prompt.

## 2. Reliability section specifies retry only against protocol-level rejection

- **Source role:** Code Review Agent
- **Impact:** `specs/edge-unit-configuration/spec.md` enumerates Edge Unit rejection codes and the
  8s × 3 budget, but says nothing about the broker being unreachable. Implementation followed the spec
  literally and let a transport throw escape the budget (#48), stranding mappings at
  `publish-pending` with no terminal status and no retry.
- **Recommended change:** State that the retry budget covers transport failure as well as
  protocol-level rejection, and define the terminal status when the broker never becomes reachable.
- **Disposition:** Proposed — guardrail item 7 in `retrospective.md`.

## 3. `code-review-gate` never-event list omits cross-operation read-modify-write

- **Source role:** Code Review Agent
- **Impact:** #46 was the most severe finding of the review — silent persisted-state corruption on the
  happy path — and the never-event list does not describe it. Reviewers get no prompt to look for it.
- **Recommended change:** Add never event: *read-modify-write spanning two persistence operations
  where a concurrent writer can interleave.*
- **Disposition:** **Filed** as Greenhouse-Documentation#35. Guardrail item 3. Marked `systemic`.

## 4. No guidance anywhere on driving an external subprocess

- **Source role:** Code Review Agent
- **Impact:** Three defects share this root: #41 (stderr never drained), #47 (read cancellation
  assumed to interrupt a pipe read), and the unbounded teardown fixed alongside #47. Two of the three
  are in the same file, found by two separate reviews.
- **Recommended change:** A skill or `code-review-gate` section: drain stdout **and** stderr, bound
  every wait, never rely on cancelling a pipe read, always kill on timeout.
- **Disposition:** **Filed** as Greenhouse-Documentation#35. Guardrail item 4. Marked `systemic`.

## 5. Stage entry gates fail open when their status file is absent

- **Source role:** Retrospective Agent
- **Impact:** `.agent-output/specs/edge-unit-configuration/` did not exist for the whole delivery, so
  every stage gate passed by absence. Six mandated artifacts were skipped without anything objecting.
  A missing file is indistinguishable from a satisfied gate.
- **Recommended change:** `feature-delivery-harness.md` — a missing `spec-status.md` is a hard
  blocker, not a pass.
- **Disposition:** **Filed** as Greenhouse-Documentation#33. Guardrail item 1. Marked `systemic`.

## 6. Board and `.agent-output` are duplicate authorities with no reconciliation rule

- **Source role:** Retrospective Agent
- **Impact:** The retrospective skill names the board "the primary status authority"; the delivery
  workflow mandates a parallel file chain. Nothing reconciles them, so each stage uses whichever is
  convenient. This is the single root cause of the stale canonical `status.md`, all six missing
  artifacts, and the review's own choice to file issues rather than write `review-report.md`.
- **Recommended change:** Pick one authority per concern and state where the other must mirror it.
- **Disposition:** **Filed** as Greenhouse-Documentation#34. Guardrail item 2. Marked `systemic`.
  **Harness-design decision, deferred to the user.**

## 7. Retrospective skill cannot distinguish self-review from independent review

- **Source role:** Retrospective Agent
- **Impact:** The harness assigns distinct roles per stage, but a single session collapses them and
  nothing records *who* reviewed. `041c86f` was raised, fixed, tested and blessed by one actor, and
  downstream that is indistinguishable from a reviewed commit.
- **Recommended change:** Require defects fixed during a review pass to get an independent review
  before merge; record the reviewing actor.
- **Disposition:** **Filed** as Greenhouse-Documentation#36. Guardrail item 6(b). Marked `systemic`.

## 8. "All defect sub-issues closed" is ambiguous against delivery sub-issues

- **Source role:** Retrospective Agent
- **Impact:** The retrospective skill gates Done on closed sub-issues, but Greenhouse-Services
  #30–#36 are delivery sub-issues that close **on merge** — so the gate is unsatisfiable pre-merge if
  read literally, while step 9 merges before step 10 checks. Separately, defect issues linked by prose
  rather than the sub-issues API are invisible to the gate entirely (as #46–#50 were until this
  session).
- **Recommended change:** Distinguish defect from delivery sub-issues in the gate; require API
  linkage.
- **Disposition:** **Filed** as Greenhouse-Documentation#36. Guardrail items 5 and 6(a).
