# Story 009: Attempt Exclusivity — Held Requests

> **Epic**: Enhancement System
> **Status**: Blocked — **OQ-ENH-7 is undecided (which layer holds requests during an attempt). Do not start until it is decided and, if it needs one, an ADR is Accepted.**
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: not estimated — depends on the OQ-ENH-7 decision

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-18 (attempt exclusivity), OQ-ENH-7, AC-ENH-38. Related: CR-ENH-15 Rollback (which relies on this rule), character-persistence.md CR-CP-5.
**Requirement**: `TR-enh-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: **None yet.** OQ-ENH-7 has two candidates and no decision: (a) the session's request dispatcher checks `IsAttemptInProgress` once for every inbound request (one enforcement point; needs a Networking Core rule), or (b) each mutating system (Inventory, Equipment, NPC Shop, Consumable Use) checks it. The decision must also cover server-originated bag mutations that are not client requests — e.g. the Loot Table System's pickup, which returns a synchronous result and cannot be deferred by a dispatcher — and applies equally to the other callers of `SaveIrreversibleOutcome` (level-up, respec, item consumption). Owner: Lead Programmer / Networking Core.
**ADR Decision Summary**: N/A until decided. Expect `/architecture-decision`.

**Engine**: Unity 6.3 LTS | **Risk**: unknown until the enforcement layer is chosen (a dispatcher rule would sit next to ADR-004 code — HIGH knowledge risk there)
**Engine Notes**: To be filled in when the decision is made.

**Control Manifest Rules (Feature layer)**:
- To be filled in when the decision is made. If option (a) is chosen, the rule belongs to the Foundation layer (Networking Core), not this epic.

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [ ] **CR-ENH-18**: from the slot lock until the attempt is idle, the server processes no other inventory-mutating request for that character — move, equip, unequip, discard, sell, buy, consumable use, pickup, accessory merge.
- [ ] **Held, not rejected**: requests that arrive in that window are held and processed in arrival order once the attempt is idle. A second `ConfirmEnhancement` is the exception: it is rejected (`RejectedConcurrentAttempt`, Story 005), never held.
- [ ] **AC-ENH-38**: Bronze item at +4 in slot 0; a Bronze scroll stack of 2 in slot 1; slots 2–19 occupied; a Bronze Helmet equipped; destruction injected; the commit is held open, then returns `DatabaseError`. While the write is in flight the client sends an unequip for the Helmet. → While in flight, `IsAttemptInProgress` is true and the Helmet is still equipped. After the failure, the rollback restores the item at level 4 and the scroll stack to 2 with no `CriticalEnhancementRollbackFailed`. The held unequip is processed only after that, and fails for lack of bag space.
- [ ] **Other characters are unaffected**: another character's requests are processed normally during the window.

---

## Implementation Notes

- **Nothing to implement until OQ-ENH-7 is decided.** What already exists for this story to build on: `EnhancementService.IsAttemptInProgress(CharacterID)` (Story 004) and the in-flight commit seam (Story 005).
- Questions the decision has to answer before this story can be rewritten as Ready:
  1. Which layer holds requests, and where is the queue?
  2. What happens to server-originated mutations (loot pickup) during the window — deferred, rejected, or allowed with a rule that keeps the rollback safe?
  3. Is the same mechanism used for the other irreversible outcomes (level-up, respec, item consumption), i.e. is the flag really "an attempt is in progress" or a general "commit in flight for this character"?
  4. A bound on how long requests are held (the write budget is ≤ 50 ms P95 per ADR-006, but a timed-out write is possible).
- AC-ENH-38 needs an Equipment System unequip request to exist. The Equipment epic has no stories yet; if this story becomes Ready first, the held request in the test can be any implemented inventory-mutating request, with the unequip case added when Equipment lands.
- When the decision is recorded, re-run `/story-readiness` on this file and replace this section with concrete guidance.

---

## Out of Scope

- Story 004: `IsAttemptInProgress`
- Story 005: the rollback and the `RejectedConcurrentAttempt` rule for a second attempt
- Networking Core: the request dispatcher itself (if option (a) is chosen, the dispatcher rule is a Networking Core story, and this story reduces to the Enhancement-side test)

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptExclusivity_integration_tests.cs` (to be created). The cases below state what must be observable; the setup depends on the chosen enforcement layer.

- **AC-ENH-38** — as stated in the criterion.
- **Held move** — commit held open; a move request for another slot of the same character arrives → not applied while in flight; applied after the commit succeeds.
- **Arrival order** — two held requests whose results depend on order (e.g. move A→B then discard B) are applied in arrival order.
- **Second attempt is rejected, not held** — `RejectedConcurrentAttempt` immediately.
- **Other character** — a move for a different character during the window is applied immediately.
- **Server-originated pickup** — behaviour per the OQ-ENH-7 decision (case to be written then).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptExclusivity_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **OQ-ENH-7 decision (blocking)**; Story 005 (commit seam and rollback). AC-ENH-38 as written also needs an Equipment System unequip request.
- Unlocks: closes the assumption the Story 005 rollback relies on
