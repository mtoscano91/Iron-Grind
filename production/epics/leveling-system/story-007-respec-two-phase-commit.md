# Story 007: Respec Two-Phase Commit & Exception Safety

> **Epic**: Leveling System
> **Status**: Ready (AC-LS-18b sub-case Blocked on OQ-LS-3 — see below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None accepted. **OQ-LS-1 explicitly flags that an ADR is expected** for the reservation-protocol interface between Inventory System and Leveling System — not yet written. Proceed against the GDD's own CR-4.1 text as the interim contract; flag at `/story-readiness` if this becomes a real blocker.
**ADR Decision Summary**: N/A (gap noted above).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — respec commit is a rare, item-gated player action with a 30-second TTL window, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [ ] **AC-LS-18a** [BLOCKING]: Respec combat gate enforced BEFORE item reservation, tested with `HasCombatTaggedEffect` **stubbed** to return `true` — `TryApplyRespec` never called, item stays in active inventory, no stats change. This sub-case is independently runnable against a stub, per the GDD's own explicit distinction from AC-LS-18b.
- [ ] **AC-LS-18b** [BLOCKING] — **BLOCKED on OQ-LS-3**: full integration path using the REAL `HasCombatTaggedEffect()` from the Status Effects System, which must define what "combat-tagged" means. Do not implement until Status Effects' own epic defines this. Track as a follow-up story once Status Effects Story work reaches that definition.
- [ ] **AC-LS-22** [BLOCKING]: Exception mid-respec (injected during CR-4.4 step 2) → `RollbackStatTransaction()` called unconditionally inside `TryApplyRespec`'s catch block; deferred events discarded; all stats revert; `EndStatTransaction()` never called; exception re-thrown; the Inventory-System-side caller (mocked here) calls `ItemReservation.Release()` — item returned, no `OnStatChanged` fires for any stat touched in the aborted transaction.

---

## Implementation Notes

*Derived from CR-4.1, CR-4.6, EC-LS-18, EC-LS-22, EC-LS-23 (leveling-system.md):*

- **CR-4.1 — Two-phase commit** (prevents item loss from network drops/crashes):
  - **Phase 1 — Gate and Reserve** (owned by Inventory System, not this story's production code — but this story must define the interface contract and prove the Leveling-System side against it): `HasCombatTaggedEffect(EntityID)` is checked; if in combat, fails, item untouched. If clear, item is marked reserved (moved to a reserved hold slot). **Reservation TTL: 30 seconds** — if Phase 2 doesn't arrive within the TTL, the Inventory System auto-calls `ItemReservation.Release()`.
  - **Phase 2 — Commit**: Inventory System calls `LevelingSystem.TryApplyRespec(EntityID)` (Story 006's method), which may throw. On success, Inventory System calls `ItemReservation.Consume()` (item permanently destroyed). On exception, calls `ItemReservation.Release()` (item returned).
  - The player loses the item ONLY when commit fully succeeds — this is the core guarantee this story's tests must prove.
- **CR-4.6 — Combat gate ownership**: the **Inventory System** owns this check, calling `HasCombatTaggedEffect(EntityID)` during Phase 1, BEFORE the item is reserved. The Leveling System never independently checks combat state. **The gate must be evaluated server-authoritatively** — client-reported combat state is never trusted (confirmed technical-director security policy in the GDD).
- Since neither the Inventory System nor the Status Effects System has any implementation yet, this story's production code is:
  1. `TryApplyRespec` itself (already built in Story 006 — this story does not re-implement it).
  2. An `ItemReservation`-shaped seam/interface this story defines for the Inventory System to eventually implement against (matching this project's established forward-dependency mock-provider pattern from the Networking Core epic — e.g. `PartyDisbandCoordinator`'s caller-supplied-list precedent).
  3. Tests exercising `TryApplyRespec` through a **test double** standing in for the Inventory System's Phase 1/Phase 2 orchestration — proving the Leveling-System-side contract (exception → item-safety guarantee) without a real Inventory System to call.
- **AC-LS-18a vs AC-LS-18b split is deliberate and matches the GDD's own text**: 18a proves the gate-ordering contract with a stub (runnable now); 18b proves the same contract against the real Status Effects definition of "combat-tagged" (not runnable until that GDD's own epic produces stories — track as a follow-up, do not force it now).
- **EC-LS-22/23 — Exception safety is the core guarantee**: any exception during `TryApplyRespec`'s CR-4.4 steps 2–4 must leave the item recoverable. `RollbackStatTransaction()` is called unconditionally in the catch block (already safe as a no-op if no transaction is open, per Character Stats' F-10 — do not re-guard this). The exception is then re-thrown so the (test-double) Inventory-System-side caller can react.

---

## Out of Scope

*Handled by neighbouring stories:*

- `TryApplyRespec`'s own commit logic — Story 006
- The real Status Effects `HasCombatTaggedEffect` implementation — future Status Effects epic story
- The real Inventory System's `ItemReservation`/TTL/Phase 1 orchestration — future Inventory System epic story
- Respec screen UI (reconnect-recovery TTL surfacing, modal confirm) — Story 013

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_RespecTwoPhaseCommit_tests.cs`

- **AC-LS-18a**: Given a stub `HasCombatTaggedEffect` returning `true`, When Phase 1 gate check fires, Then `TryApplyRespec` is never called, item stays in a mock active-inventory state.
- **AC-LS-22**: Given an injected exception during `TryApplyRespec`'s step 2, When it propagates, Then `RollbackStatTransaction` fires unconditionally, all stats revert, `EndStatTransaction` never called, exception re-thrown, and the test-double Inventory-System caller's `Release()` path is exercised (item "returned" in the mock).
- **AC-LS-18b**: Do not write this test yet — track in tech debt / follow-up story once Status Effects defines "combat-tagged" (OQ-LS-3).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_RespecTwoPhaseCommit_tests.cs` — must exist and pass for AC-LS-18a and AC-LS-22. AC-LS-18b explicitly deferred — do not fail this story's closure on its absence; log it as tech debt instead.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006 (`TryApplyRespec` itself)
- Unlocks: None directly — AC-LS-18b's real integration test is unblocked only once a future Status Effects story defines `HasCombatTaggedEffect`
