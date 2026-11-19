# Story 009: Zone Composition Wiring

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — CR-SHOP-3, CR-SHOP-4 (close triggers), Interactions table
**Requirement**: `TR-shop-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Requests are registered on the inbound dispatcher before `Seal()` and handled in the dispatch phase; async completions run in the tick completion phase.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: one composition root (`ZoneServerComposition`); override seams for tests as the existing `*Override` pattern
- Guardrail: no per-tick allocation added by the shop

---

## Acceptance Criteria

- [ ] **AC-1 Build**: `ZoneServerComposition` loads the catalog asset (Story 001) and validates it at startup — invalid → `ArgumentException` / startup failure, like `PartySystemConfigOverride`; builds one `NpcInteractionSessionTracker` with the production `ITownHubQuery` (Story 002), a wall clock and `SESSION_TTL_SECONDS`, the request cache (Story 004) and `NpcShopService` (Stories 005–007).
- [ ] **AC-2 Requests**: Open, Close, Buy and Sell are registered on the inbound dispatcher before `Seal()` with their body sizes and `RpcTypeTag`s; the dispatcher test seam lists them.
- [ ] **AC-3 Close triggers**: zone exit and session end call `NotifyZoneTransition` / `NotifySessionEnded` (find the existing release / exit listener chain — as Party Story 011 did) and drop the character's cache entries.
- [ ] **AC-4 Reconciliation**: the reconciler (Story 008) is composed on the load and re-auth paths; no purchase stand-in remains in production code.
- [ ] **AC-5 Shared tracker**: the tracker is exposed so the Enhancement handlers can later share it (one tracker per zone); no second tracker is built.
- [ ] **AC-6 Pinned tests**: existing composition / pipeline tests that pin the dispatcher registrations or step lists are updated, and the full EditMode `!Database` suite passes.

---

## Implementation Notes

- Follow Party Story 011 (`ZoneServerComposition` party block, `PartySystemConfigOverride`, `InboundRequestDispatcher` test seams, composite listeners). Add `NpcShopCatalogOverride` (and a clock override) for tests.
- Async completions (BeginPurchase etc.) go through the zone's existing `ITickCompletionQueue`.

---

## Out of Scope

- Story 010: end-to-end scenario
- Enhancement handler registration (Enhancement epic)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: composition with default asset → shop service present; override with an invalid catalog → throws.
- **AC-2**: dispatcher seam → four shop/NPC ids registered with the codec body sizes.
- **AC-3**: admit a player, open shop, release the slot → tracker inactive, cache empty.
- **AC-4**: grep / seam check that the re-auth path uses the reconciler.
- **AC-5**: one tracker instance reachable from the composition.
- **AC-6**: full suite count recorded.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Composition_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 004, 005, 006, 007, 008
- Unlocks: Story 010
