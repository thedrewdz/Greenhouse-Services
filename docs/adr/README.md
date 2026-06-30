# Architecture Decision Records (Local)

This folder holds **repository-local** ADRs for the Greenhouse Main Unit services: decisions specific to this codebase that do not belong in the shared canonical docs.

The cross-cutting Main Unit architecture is governed by **canonical ADRs in the Greenhouse Documentation repository**, not here:

- `adr/0001-main-unit-ui-services-separation.md` - independent UI/services processes; headless services; loopback REST + WebSocket/SSE; OpenAPI contract.
- `adr/0002-main-unit-flutter-flutter-pi-ui.md` - Flutter/flutter-pi UI (boundary context for this repository).

Canonical entry point: https://github.com/thedrewdz/Greenhouse-Documentation/blob/main/README.md

Local ADRs are supplemental and must not override canonical guidance. On conflict, the canonical ADR wins and the local ADR is updated or re-scoped.

## ADR Loading Rules (Partial Disclosure)

- Do not read every ADR up front.
- Read the canonical ADRs above for any structural/architecture work.
- Use the digest below to find local ADRs relevant to the area you are touching, then read only those in full.

## Local ADR Digest

| ADR | Status | Decision (summary) | Touches |
|---|---|---|---|
| _None yet._ | - | Services-specific decisions will be recorded here. Cross-cutting architecture stays canonical (see above). | - |

## Adding a Local ADR

- Number sequentially: `NNNN-short-kebab-title.md`.
- Include `Status:` and `Date:` near the top; state the decision, options considered, and consequences.
- Only record decisions that are repository-specific. If a decision affects the UI or the services/UI boundary, it belongs in the canonical documentation repository instead.
- Add a row to the digest above in the same change.
