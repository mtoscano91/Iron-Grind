# Story 010: End-to-End Buy and Sell

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — Player Fantasy (sell-off then buy), CR-SHOP-3 to CR-SHOP-7
**Requirement**: `TR-shop-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014; ADR-001
**ADR Decision Summary**: Requests flow through the real dispatcher and tick; purchase writes complete through the tick completion queue on the in-memory store.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: deterministic tests (no wall-clock dependence — inject the clock)
- Guardrail: shop handling adds no measurable tick cost (log, no assert)

---

## Acceptance Criteria

- [ ] **AC-1 Session**: in the real zone composition (EditMode rig as Party Story 012), a player in the hub sends `OpenNPCInteraction` → `NPCInteractionOpened`.
- [ ] **AC-2 Sell then buy**: the player sells a stack of potions (`SellResult`, gold up, slot down, `GoldSyncEvent`), then buys 1 Bronze Scroll (`BuyResult` after the purchase write completes, gold down by 90, scroll in the bag, no outstanding purchase record).
- [ ] **AC-3 Refund round trip**: with a full bag, a buy returns `RejectedInventoryFull` and the balance equals the balance before the request.
- [ ] **AC-4 Close**: `CloseNPCInteraction`, then a buy → `RejectedNoNPCSession`.
- [ ] **AC-5 Crash refund**: a record left outstanding (written directly to the in-memory store) is refunded when the character is loaded again, before any buy is processed.
- [ ] **AC-6 Quiet**: no unexpected warnings or errors over the scenario (expected startup lines listed, as Party Story 012).

---

## Implementation Notes

- Reuse the Party Story 012 rig (`PartySystem_EndToEnd_tests.cs`): Admit, ClearSent, decode `Seam.Sent`, ProblemCounter. Players must be registered with Inventory and Currency through the load path (`ApplyLoadedRecord`).
- Pump the tick until the purchase write completes; never sleep.

---

## Out of Scope

- Client UI (presentation epics)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1–AC-4**: one scenario test split into helpers (< 40 lines each), decoding every message the player receives; a bystander gets no shop messages.
- **AC-5**: second test: seed a record, admit, assert refund log + balance before the first buy result.
- **AC-6**: problem counter 0 over the window.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_EndToEnd_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009
- Unlocks: None (epic complete)
