# Story 007: Outcome Events and +9 Broadcast Trigger

> **Epic**: Enhancement System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

> **Rewritten 2026-10-07 at readiness** for the two-phase service (Story 004) and with three decisions: `OnPrestigeBandChange` is not implemented here; the +9 trigger carries ids, not names; event argument types live in this system's namespace.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-14 (server broadcast at +9), CR-ENH-15 steps 7–9 (unlock → broadcast → result), CR-ENH-11 (nothing is announced before the commit), Interactions with Other Systems (VFX and Audio rows: `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`), EC-ENH-8 (broadcast is best effort), AC-ENH-18 (server side).
**Requirement**: `TR-enh-010` (server-side trigger), `TR-enh-011`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (C# `event Action<T>` with `readonly struct` arguments, `On` prefix; no central bus).
**ADR Decision Summary**: Cross-system notification uses typed C# events with struct arguments and no per-emit heap allocation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. The only engine call is `UnityEngine.Debug.LogException`.

**Control Manifest Rules (Feature layer)**:
- Required: `readonly struct` event argument types; `On`-prefixed event names — ADR-010
- Forbidden: class-typed event args; an `EventBus` class — ADR-010
- *Known drift, not resolved here:* the manifest also says event argument types are "declared in the shared `IronGrind.Events` namespace". No file in `src/` uses that namespace — Inventory, Loot Table and Leveling each declare their event arguments in their own namespace. This story follows the codebase (`IronGrind.EnhancementSystem`). The manifest/code mismatch is a separate cleanup.

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. Events are raised by `EnhancementService.CompleteAttempt` — the call the commit orchestrator (Story 011) makes only after a successful commit.*

- [x] **Success event**: `CompleteAttempt` for a success outcome raises `OnEnhancementSuccess` exactly once, carrying the character, the item's `ItemID` and the new level; `OnEnhancementDestruction` is not raised.
- [x] **Destruction event**: `CompleteAttempt` for a destruction raises `OnEnhancementDestruction` exactly once, carrying the character and the destroyed item's `ItemID`; `OnEnhancementSuccess` is not raised.
- [x] **AC-ENH-18 (+9 trigger, server side)**: when an attempt takes an item from +8 to +9, `OnEnhancementBroadcastLevelReached` is raised exactly once, carrying the character, the item's `ItemID` and the level 9. It is not raised for a success into +1 … +8 or into +10, nor for any destruction.
- [x] **Order (CR-ENH-15 steps 7–9)**: inside `CompleteAttempt` — `UnlockSlot` first, then the success or destruction event, then the +9 trigger, then the method returns its result. When an event is raised the attempt is already over: `IsAttemptInProgress` is false and the item slot is unlocked.
- [x] **Nothing before the commit (CR-ENH-11)**: `BeginAttempt` raises none of the three events, for a pending attempt or a rejection.
- [x] **Nothing on a rollback**: `RollBackAttempt` raises none of the three events.
- [x] **A throwing subscriber is contained (EC-ENH-8)**: if a subscriber of any of the three events throws, the exception is logged and not propagated; `CompleteAttempt` still returns its normal result; the bag is unchanged by the failure; the remaining events are still raised.

---

## Implementation Notes

- **Events on `EnhancementService`**, each `event Action<T>` with a `readonly struct` argument declared in `IronGrind.EnhancementSystem`, one file per type:
  - `OnEnhancementSuccess` — `{ CharacterId, ItemId, NewLevel }`
  - `OnEnhancementDestruction` — `{ CharacterId, ItemId }`
  - `OnEnhancementBroadcastLevelReached` — `{ CharacterId, ItemId, Level }`
  - Type names are a suggestion (e.g. `EnhancementSuccessEventArgs`, `EnhancementDestructionEventArgs`, `EnhancementBroadcastEventArgs`); the fields are not.
- **The +9 trigger carries ids, not names** *(decided at readiness)*. The GDD's client message is `ServerBroadcast_Enhancement9 { playerName, itemName }`. The service knows a `CharacterID` and an `ItemID`, and no player-name lookup exists in `src/`. Whoever builds the client message (Story 010) resolves both display names — the item's from `ItemDefinition.DisplayName`. The event therefore allocates nothing.
- **Trigger level**: a named constant in `EnhancementConstants` — `SERVER_BROADCAST_LEVEL = 9` (CR-ENH-14). It is a fixed design value, not a tuning knob and not derived from the level cap (TK-ENH-3 notes that a cap below 9 "removes the +9 broadcast milestone").
- **Where**: all three are raised from `CompleteAttempt`, after the pending record is removed and the slot unlocked. Read what the events need (`ItemId`, outcome, new level) from the pending record before it is discarded.
- **Subscriber exceptions**: wrap each raise so that an exception from a subscriber is caught and logged with `UnityEngine.Debug.LogException`, as `LootAuctionService` and `GroundItemService` already do, and execution continues. A multicast delegate stops at the first throwing subscriber, so later subscribers of *that* event are skipped; that is acceptable and matches the Loot services. The other events and the return value must not be affected.
- **`OnPrestigeBandChange` is not implemented** *(decided at readiness)*. The GDD lists it as a signal this system writes for the VFX System, but an item cannot be enhanced while equipped (CR-ENH-4) and the band other players see is recomputed by the Equipment System from the Weapon slot on equip (CR-ENH-12, equipment-system.md CR-EQS-11). An enhancement attempt never changes a visible band. The signal belongs to the Equipment System; `enhancement-system.md`'s Interactions (VFX row) and Downstream Dependencies (VFX row) should be corrected when that GDD is next edited.
- These are server-side events. The VFX and Audio systems run on the client and react to what the client receives (`EnhancementAttemptResult`, Story 010); these events are what the server side of that delivery subscribes to.
- No event is raised for a rejection: a rejection has no outcome, and the caller already holds the code.

---

## Out of Scope

- Story 011: calling `CompleteAttempt` only after a successful commit (the end-to-end "nothing before the commit" check)
- Story 010: `ServerBroadcast_Enhancement9` and `EnhancementAttemptResult` on the wire; resolving the player and item display names; delivery to all online players
- VFX System, Audio System and Enhancement UI epics: presentation of success, destruction and glow
- Equipment System epic: recomputing the prestige band on equip, and any band-change signal

---

## QA Test Cases

**File**: `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` (new). Real `InventoryService`, `ScriptedRandom`, `StubNpcSessions` and `RecordingInventoryDecorator` from `tests/EditMode/Integration/EnhancementSystem/EnhancementTestDoubles.cs`.

- **Success event** — item +2, `r = 0.00`, `BeginAttempt` + `CompleteAttempt` → one `OnEnhancementSuccess` with the character, the sword's `ItemID` and level 3; zero `OnEnhancementDestruction`; zero +9 triggers.
- **Destruction event** — `r = 0.90` → one `OnEnhancementDestruction` with the character and the sword's `ItemID`; zero `OnEnhancementSuccess`; zero +9 triggers.
- **AC-ENH-18** — item at +8, `r = 0.00` → one `OnEnhancementBroadcastLevelReached` with the character, the item's `ItemID` and level 9; also one success event with level 9.
- **No trigger at other levels** — success into +1, +2, +3, +4, +5, +6, +7, +8 and +10 (`[TestCase]` per starting level 0–7 and 9) → zero triggers each.
- **No trigger on a failed +8 → +9** — `r = 0.99` → one destruction event, zero triggers.
- **Order** — record `UnlockSlot` (via the decorator) and each event in one list: `UnlockSlot`, success event, +9 trigger. Inside each handler, `IsAttemptInProgress` is false and `IsSlotLocked(itemSlot)` is false.
- **Nothing from `BeginAttempt`** — after a pending `BeginAttempt` (success and destruction): zero events of all three kinds.
- **Nothing on rejection** — a tier-mismatch `BeginAttempt` → zero events.
- **Nothing on rollback** — `BeginAttempt` + `RollBackAttempt` (success and destruction; expect the `CriticalEnhancementWriteFailed` error) → zero events.
- **Throwing success subscriber** — handler throws → `LogAssert.Expect(LogType.Exception, …)`; `CompleteAttempt` returns `{SUCCESS, 3, Success}`; the bag holds the item at +3, unlocked.
- **Throwing +9 subscriber** — handler throws → exception logged; the result is `{SUCCESS, 9, Success}`; the success event was still raised before it.
- **Throwing success subscriber does not stop the +9 trigger** — at +8 → +9 with a throwing `OnEnhancementSuccess` handler: the trigger is still raised once.
- **Constant** — `EnhancementConstants.SERVER_BROADCAST_LEVEL` is 9.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` — must exist and pass; the Story 003, 004 and 005 suites must still pass.

**Status**: [x] Created — 16 test methods + 9 parameterised cases (25 cases), passing (EditMode 1757/1757, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: Story 004 (`CompleteAttempt`), Story 005 (`RollBackAttempt`, for the "nothing on a rollback" check) — both Complete
- Unlocks: Story 010 (wire delivery of the result and the broadcast)

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 7/7 passing (none deferred)
**Deviations**: None blocking. Notes (all decided at readiness): `OnPrestigeBandChange` is not implemented — it belongs to the Equipment System, and the two `enhancement-system.md` rows naming it still need correcting; the +9 trigger carries ids instead of the names in the GDD's message (Story 010 resolves them); event argument types are in `IronGrind.EnhancementSystem`, not the shared `IronGrind.Events` namespace the control manifest names (no code uses it).
**Test Evidence**: Logic — `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` (25 cases); EditMode 1757/1757 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; both applied (one generic `Raise<TArgs>` helper; throwing-destruction-subscriber test). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
**Files**: `src/Foundation/EnhancementSystem/EnhancementService.cs` (three events, `Raise<TArgs>`), `EnhancementConstants.cs` (`SERVER_BROADCAST_LEVEL`), `EnhancementSuccessEventArgs.cs`, `EnhancementDestructionEventArgs.cs`, `EnhancementBroadcastEventArgs.cs`.
**For Story 010**: subscribe to `OnEnhancementBroadcastLevelReached` and resolve the player name and `ItemDefinition.DisplayName` there. On the first IL2CPP server build, check whether `Action<T>` over these structs needs a `link.xml` entry (control manifest).
