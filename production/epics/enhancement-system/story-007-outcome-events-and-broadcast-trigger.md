# Story 007: Outcome Events and +9 Broadcast Trigger

> **Epic**: Enhancement System
> **Status**: Ready — **one open point to settle at `/story-readiness`: who raises `OnPrestigeBandChange`**
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-14 (server broadcast at +9), CR-ENH-15 step 8, Interactions with Other Systems (VFX and Audio rows: `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`, `OnPrestigeBandChange(newBand)`), EC-ENH-8, AC-ENH-18.
**Requirement**: `TR-enh-010` (server-side trigger), `TR-enh-011`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (C# `event Action<T>` with `readonly struct` arguments, `On` prefix; no central bus).
**ADR Decision Summary**: Cross-system notification uses typed C# events with struct arguments and no per-emit heap allocation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Feature layer)**:
- Required: `readonly struct` event arg types declared in the shared `IronGrind.Events` namespace, one file per type — ADR-010
- Forbidden: class-typed event args; an `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [ ] **Success event**: after a successful attempt is committed, `OnEnhancementSuccess` is raised once, carrying the character and the new level.
- [ ] **Destruction event**: after a destruction is committed, `OnEnhancementDestruction` is raised once, carrying the character.
- [ ] **AC-ENH-18 (+9 trigger, server side)**: when an attempt takes an item from +8 to +9, the broadcast trigger is raised once with the player's name and the item's Item Database display name — e.g. `{ "TestPlayer", "Dark Steel Sword" }`. No trigger is raised for the +1 to +8 transitions or for +10.
- [ ] **Order**: the events are raised after the commit succeeds and after the slot is unlocked (CR-ENH-15 steps 7 → 8 → 9); the +9 trigger precedes the attempt result becoming available.
- [ ] **Nothing on failure**: a rejected request, a step 4 abort, and a failed commit raise none of these events.
- [ ] **Broadcast is best effort (EC-ENH-8)**: a subscriber that throws does not change the committed outcome or the returned result.

---

## Implementation Notes

- Events on `EnhancementService`, each `event Action<T>` with a `readonly struct` argument in the `IronGrind.Events` namespace (one file per type), per the control manifest.
- **+9 trigger**: the level that triggers the broadcast is 9 in CR-ENH-14. TK-ENH-3 notes that a cap below 9 "removes the +9 broadcast milestone", so the trigger level is a fixed design value, not derived from the cap. Hold it as a named constant.
- **Player name**: the service knows a `CharacterID`, not a display name. No name lookup exists in `src/` yet. Either the trigger carries the `CharacterID` and the display name is resolved by whoever builds the wire message (Story 010), or a consumer-side name lookup is injected. Raise at `/story-readiness`; the criterion above is written for the form the GDD shows.
- **Item name** is `ItemDefinition`'s display name from the Item Database, read before a destruction could remove the item (a +9 success keeps the item, so the read is always possible).
- **Open point — `OnPrestigeBandChange(newBand)`**. The GDD lists it as a signal this system writes for the VFX System. But an item cannot be enhanced while equipped (CR-ENH-4), and the band shown to other players is recomputed by the Equipment System from the Weapon slot at equip time (CR-ENH-12, equipment-system.md CR-EQS-11). An enhancement attempt therefore never changes a visible band. Do not implement this signal here; record at `/story-readiness` whether it moves to the Equipment System or is dropped, and correct the GDD's Interactions and Downstream Dependencies rows accordingly.
- A throwing subscriber must not prevent the remaining subscribers or the result. Follow whatever the existing services do for subscriber exceptions (check `LootAuctionService` / `InventoryService` before choosing).
- These are server-side events. What reaches a client (`EnhancementAttemptResult`, `ServerBroadcast_Enhancement9`) is Story 010; the VFX and Audio systems consume client-side signals derived from those messages.

---

## Out of Scope

- Story 005: the commit itself and the "nothing before the commit" guarantee this story relies on
- Story 010: `ServerBroadcast_Enhancement9` and `EnhancementAttemptResult` on the wire; delivery to all online players
- VFX System, Audio System and Enhancement UI epics: presentation of success, destruction and glow
- Equipment System epic: recomputing the prestige band on equip

---

## QA Test Cases

**File**: `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` (new). Real `InventoryService`, scripted random source, controllable commit fake.

- **Success event** — item +2, `r = 0.00` → one `OnEnhancementSuccess` with new level 3; zero `OnEnhancementDestruction`.
- **Destruction event** — `r = 0.90` → one `OnEnhancementDestruction`; zero `OnEnhancementSuccess`.
- **AC-ENH-18** — Dark Steel Sword at +8, `r = 0.00` → one +9 trigger with the item's display name; also one success event with level 9.
- **No trigger at other levels** — success transitions into +1 … +8 and +10 (`[TestCase]` per target level) → zero triggers.
- **No trigger on a failed +8 → +9** — `r = 0.99` → destruction event only.
- **Order** — recording subscribers and the commit fake: commit completes → unlock → +9 trigger → result available.
- **Nothing on failure** — each Story 003 rejection, the step 4 abort, and a failed commit → zero events of all three kinds.
- **Throwing subscriber** — a +9 trigger subscriber throws → the returned result is still `{SUCCESS, 9, Success}` and the bag holds the item at +9.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (attempt sequence), Story 005 (commit ordering)
- Unlocks: Story 010 (wire delivery of the result and the broadcast)
