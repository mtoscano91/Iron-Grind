# Story 005: `equipmentAppearanceFlags` Encoding and Change Event

> **Epic**: Equipment System
> **Status**: Complete (2026-10-26)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-10 (no on-equip triggers), CR-EQS-11 (encoding, PrestigeBand from the Weapon slot only), EC-EQS-8, EC-EQS-9, EC-EQS-11, Visual/Audio Requirements
**Requirement**: `TR-equip-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (ADR-004 governs the wire delivery in Story 009)
**ADR Decision Summary**: A change other systems react to is raised as a C# `event Action<T>` with a `readonly struct` argument, named `On…`; subscribers attach in a constructor and detach in `Dispose()`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: None — pure bit arithmetic.

**Control Manifest Rules (this layer)**:
- Required: `event Action<T>` with `readonly struct` T, `On` prefix (ADR-010)
- Forbidden: lambda captures for persistent subscriptions (ADR-010)
- Guardrail: zero allocation per raise

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md`, scoped to this story:*

- [x] `GetAppearanceFlags(charId)` returns `(weaponTier & 0x03) << 6 | (elementType & 0x07) << 3 | (prestigeBand & 0x03) << 1 | (armorBit & 0x01)` (CR-EQS-11)
- [x] WeaponTier and ElementType come from the Weapon slot's item; both 0 with no weapon (EC-EQS-9)
- [x] PrestigeBand comes from the **Weapon slot's** `EnhancementLevel` only, through `EnhancementConfig.GetPrestigeBand`; NONE with no weapon; no other slot's level affects the byte
- [x] ArmorTier = 1 when the highest-tier equipped armor (Helmet, Chest, Legs, Boots) is Steel or Dark Steel; 0 otherwise or with no armor
- [x] **AC-EQS-11** — Dark Steel Fire sword at level 0 + Steel / Dark Steel armor → `0xC9`
- [x] **AC-EQS-30** — (a) +9 Bronze Chest only → `0x00`, chest level reads 9; (b) +0 Iron Sword → `0x40`; (c) swap to +7 Iron Sword → `0x44`; (d) unequip weapon → `0x00`
- [x] **EC-EQS-11** — fresh character, all slots empty → `0x00`
- [x] The byte is recomputed after every successful equip, swap and unequip (incl. the `GetItem`-null unequip); `OnAppearanceChanged` (`readonly struct { CharacterID, EntityID, byte Flags }`) is raised only when the byte value actually changes; never on `NoChange` or a failure that leaves the slots unchanged (EC-EQS-8). The one failure that changes a slot — the failed step-7 reclaim in `RollBackSwap` (Story 004 option A: `InventoryError`, slot left Empty) — recomputes the byte and raises the event if it changed (user decision 2026-10-26)
- [x] **AC-EQS-18** — an equip produces exactly: `OnStatChanged` per registered modifier and the appearance update; the Equipment System raises no other event and calls no damage, healing, status-effect or audio API (CR-EQS-10)

---

## Implementation Notes

*Derived from CR-EQS-11 and ADR-010:*

- Keep the encoder a pure static function (`EquipmentAppearanceEncoder.Encode(weaponTier, elementType, prestigeBand, armorBit)`) plus a per-character cached byte, so Story 009 and the snapshot source read a field, not a recompute.
- `GearTier` byte values must map to Bronze = 0 … Dark Steel = 3 for the 2-bit field; `GearTier.None` exists in the enum — check the actual numeric values and map explicitly rather than casting. Same for `ElementType` (None = 0, Fire = 1, Cold = 2, Lightning = 3, Poison = 4).
- PrestigeBand: reuse `EnhancementConfig.GetPrestigeBand(byte)` and `PrestigeBand` (Enhancement Story 001) — the thresholds 5 / 7 / 8 are owned there; never re-declare them.
- Raise the event after the slot write, at the end of the operation (CR-EQS-5 step 5, CR-EQS-6 step 10, CR-EQS-7 step 7). Contain a subscriber exception the way `EnhancementService` does for its events, so a faulty subscriber cannot undo an equip. Simplest shape: recompute at the end of every `Equip` / `Unequip` call whatever the result, compare with the cached byte, raise only on a difference — every unchanged-slot failure then never fires by construction, and the `RollBackSwap` empty-slot path is covered without a special case.
- No decoder in production code (user decision 2026-10-26) — tests assert the byte; a decoder waits for a consumer (client / Story 009).
- The snapshot field `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags` is fed by `IZoneEntityStateSource.TryGetPlayerState`, whose only implementation is still `LoggingZoneEntityStateSource` (a placeholder). Wiring the real player-state source is a forward dependency outside this epic; OQ-EQS-6 (full snapshots always carry the byte) stays open.

---

## Out of Scope

- Story 009: `AppearanceChangedEvent` on the wire
- Story 007: computing the byte after a restore (it calls the same recompute)
- The real `IZoneEntityStateSource` (Zone Instancing / Networking forward dependency)

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_AppearanceFlags_tests.cs` (new)

- **Encoder table** — each WeaponTier (0–3) alone, each ElementType (0–4) alone, each band (0–3) alone, armor bit alone → the expected bit position; all-max → `0b11_100_11_1`.
- **AC-EQS-11** — Dark Steel Fire sword level 0 + Dark Steel Chest → `0xC9` (byte only — no decoder).
- **Band boundaries** — weapon at levels 4, 5, 6, 7, 8, 10 → band 0, 1, 1, 2, 3, 3.
- **AC-EQS-30 (a)–(d)** — exact sequence and bytes from the GDD; (a) also `GetSlotEnhancementLevel(Chest)` = 9.
- **Armor bit** — Iron armor only → 0; Iron Helmet + Steel Boots → 1; Steel Chest unequipped leaving Bronze → 0.
- **EC-EQS-11** — new character → `0x00`.
- **Event** — equip that changes the byte → one `OnAppearanceChanged` with the new byte and both ids; `NoChange` → none; `StatRequirementNotMet` / `InventoryFull` / `InventoryError` → none; equip of a Bronze Helmet when the armor bit stays 0 → no event (byte unchanged).
- **Failed reclaim** — weapon swap where the new item's `MoveItemOut` fails and the old weapon's reclaim also fails (Story 004's `InventoryDecorator` path): result `InventoryError`, Weapon slot Empty, `GetAppearanceFlags` drops the weapon bits, exactly one `OnAppearanceChanged` with that byte. Same path on a Bronze armor swap (byte unchanged) → no event.
- **AC-EQS-18** — equip into an empty slot with a 2-modifier item: subscriber log shows exactly 2 `OnStatChanged` and ≤ 1 `OnAppearanceChanged`, and no other public event on the service fired.
- **Subscriber throws** — a throwing `OnAppearanceChanged` subscriber does not change the `EquipResult` or the slot.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EquipmentSystem/EquipmentSystem_AppearanceFlags_tests.cs` — must exist and pass

**Status**: [x] Created — 64 tests (39 `[Test]` + 25 `[TestCase]`), all passing (EditMode 4298 / 0 failed, 2026-10-26)

---

## Dependencies

- Depends on: Story 004 (swap step 10 is part of AC-EQS-30 (c))
- Unlocks: Stories 007, 009

---

## Completion Notes

**Completed**: 2026-10-26
**Criteria**: 9/9 passing, all covered by `EquipmentSystem_AppearanceFlags_tests.cs` (no deferred items)
**Deviations** (advisory, accepted):
1. The byte is recomputed at the end of every `Equip` / `Unequip` call whatever the result (public methods wrap `EquipCore` / `UnequipCore`), and `OnAppearanceChanged` is raised only on a byte change — the `RollBackSwap` empty-slot path (Story 004 option A) raises without a special case (user decision at `/story-readiness`).
2. No production decoder (user decision at `/story-readiness`).
3. An equipped item whose definition vanished from the Item Database counts as empty in the byte, silently; Unequip and the swap path log that defect when they reach the slot. No GDD rule.
4. `RefreshAppearance` is not in a `finally`: a throw out of `Equip` / `Unequip` (the dev-build `ItemID.Invalid` throw changes nothing; any other is an unexpected collaborator throw) skips the recompute. Documented on `IEquipmentService.OnAppearanceChanged`, with re-entrancy (allowed) and multicast behaviour (a throwing subscriber skips later ones).
**Scope**: new `EquipmentAppearanceEncoder.cs`, `EquipmentAppearanceChangedEventArgs.cs` (+ `.meta`); `EquipmentService.cs`, `IEquipmentService.cs`. No snapshot wiring (forward dependency, OQ-EQS-6 open), no wire event (Story 009), no restore path (Story 007). Not this story: the flaky `TickLoop_CommitBeforeBroadcast_Tests` 200 ms case failed once in this session (199 ms) → TD-083.
**Test Evidence**: Logic — `tests/EditMode/EquipmentSystem/EquipmentSystem_AppearanceFlags_tests.cs`
**Code Review**: Complete — 2026-10-26, APPROVED WITH SUGGESTIONS (unity-specialist CLEAN, qa-tester GAPS, all advisory) → "fix all" applied (3 doc/comment fixes, tests 54 → 64). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
