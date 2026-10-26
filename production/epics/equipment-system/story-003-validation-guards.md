# Story 003: Validation Guards — Input, Slot Type, Stat Gate, Same Item

> **Epic**: Equipment System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-2 (slot type), CR-EQS-3 (stat gate), CR-EQS-4 (same-item guard), CR-EQS-13 (input validation), F-EQS-3, EC-EQS-2, EC-EQS-3, EC-EQS-8
**Requirement**: `TR-equip-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Tier 1 calls into injected services, acting on return values; the read-only `Inventory.GetSlot` peek is a Tier 1 read.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: IL2CPP does not bounds-check enum casts — the `GearSlot` range check must run before any `_slots[(int)slot]` access.

**Control Manifest Rules (this layer)**:
- Required: Tier 1 calls; server-side validation lives in `IronGrind.ServerLogic` (ADR-012)
- Forbidden: none specific beyond the Core tier list
- Guardrail: every rejection happens before any Inventory mutation or Character Stats write

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md`, scoped to this story:*

- [ ] **AC-EQS-4** — base STR 62 vs Dark Steel Sword min 85 → `StatRequirementNotMet`; sword stays in the bag; slot unchanged; no `OnStatChanged`
- [ ] **AC-EQS-5** — base STR exactly 85 → not rejected; sword equipped; STR rises by the sword's modifier (gate is `>=`)
- [ ] **AC-EQS-6** — base STR 62 with a +30 STR buff (effective 92) → `StatRequirementNotMet`; the gate reads `GetBaseStat` only
- [ ] **AC-EQS-15** — base MaxHP 280 vs Iron Helmet min 350 → `StatRequirementNotMet`; helmet stays in the bag
- [ ] Accessories (`EquipRequirementStat = null`) are never stat-gated (CR-EQS-3)
- [ ] **AC-EQS-8** — the bag item has the same `ItemID` **and** `EnhancementLevel` as the equipped one → `NoChange`; no `MoveItemOut` / `MoveItemIn`, no stat call, slot unchanged, effective stats unchanged (CR-EQS-4, EC-EQS-8)
- [ ] **AC-EQS-9** — a Helmet into the Weapon slot → `SlotMismatch`; no state change
- [ ] **AC-EQS-19** — `ItemID.Invalid` → rejected, no state change, no Inventory or Character Stats call; no exception in release mode, `InvalidOperationException` in dev mode (CR-EQS-13)
- [ ] **AC-EQS-20** — `(GearSlot)7` → rejected before any array access; no `IndexOutOfRangeException`; no Inventory or Character Stats call; same for `Unequip`
- [ ] **AC-EQS-33** — stale-render guard: bag slot empty, holding a different item, or index outside `[0, 19]` → `ItemNotInInventory`; zero `MoveItemOut` / `MoveItemIn`; no state or stat change
- [ ] Order of checks is fixed and documented: slot range → `itemId ≠ Invalid` → stale-render guard (`GetSlot`) → item is equipment and its `GearSlot` matches (`SlotMismatch`) → same-item guard (occupied target only) → stat gate. Matches `networking-wire-protocol.md` `EquipRequest` validation order.

---

## Implementation Notes

*Derived from the GDD rules:*

- All guards run at the top of `Equip` before the empty / occupied branch, so story 004's auto-swap inherits them (CR-EQS-6 steps 1–2).
- **Stat gate:** `GetBaseStat(entityId, stat) >= EquipRequirementMin` — compare as `float`. Use the Character Stats getter that matches the stat's schema type (`StatSchema`); STR, INT and MaxHP are int stats. Never `GetEffectiveStat`.
- **Same-item guard** reads `GetSlot(charId, inventorySlotIndex)` (already read by the stale-render guard — read once) and compares `ItemId` and `EnhancementLevel` with the slot entry.
- **Result codes the GDD does not name — confirm at `/story-readiness`:** out-of-range `GearSlot` → `SlotMismatch`; `ItemID.Invalid` in release mode → `ItemNotInInventory`; an item that is not equipment (no `EquipmentData`) → `SlotMismatch`; `Unequip` with an out-of-range slot → `SlotMismatch`. The wire never sends `itemId = 0` to `Equip` (it routes to `Unequip`, story 008), so the Invalid path is defence only.
- **Dev vs release throw (CR-EQS-13):** make the behaviour an injected option (e.g. a constructor flag defaulting to `Debug.isDebugBuild`) so both branches are testable in EditMode, where every build is a dev build.
- The `StatRequirementNotMet(StatID, required, actual)` payload in UI Requirements is for a future client path; the wire `EquipResult` does not carry it (wire protocol note). Not implemented here.

---

## Out of Scope

- Story 004: `HasFreeSlot` check and the swap sequence (CR-EQS-6 step 3 onward)
- Story 008: mapping these codes onto the wire

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_Validation_tests.cs` (new). Inventory spy counting `MoveItemOut` / `MoveItemIn`; stat-change subscriber counting events.

- **AC-EQS-4** — Given base STR 62, sword min 85 in slot S; When `Equip(Weapon, sword, S)`; Then `StatRequirementNotMet`, S still holds the sword, Weapon `(Invalid, Empty)`, 0 stat events, 0 `MoveItemOut`.
- **AC-EQS-5** — base STR 85 → `Success`; edge: 84 → rejected (boundary pair).
- **AC-EQS-6** — base 62, buff +30 via `AddBuffModifier` (effective 92) → `StatRequirementNotMet`.
- **AC-EQS-15** — base MaxHP 280, Iron Helmet min 350 → `StatRequirementNotMet`; edge: 350 → `Success`.
- **Accessory ungated** — Ring with `EquipRequirementStat = null`, every base stat 0 → `Success`.
- **AC-EQS-8** — Iron Sword level 0 equipped; identical level-0 sword in slot S → `NoChange`, 0 Inventory mutations, effective STR unchanged, slot `(ItemA, Occupied)`, the S copy still in S.
- **AC-EQS-9** — Helmet → Weapon slot → `SlotMismatch`, 0 Inventory mutations.
- **AC-EQS-19** — release mode: `ItemID.Invalid` → rejected (documented code), 0 Inventory / stat calls, slot unchanged; dev mode: throws `InvalidOperationException`, same no-change assertions.
- **AC-EQS-20** — `Equip((GearSlot)7, …)` and `Unequip((GearSlot)7)` → rejected, no exception, 0 calls.
- **AC-EQS-33 (a)(b)(c)** — empty slot T; slot S holding a different item; index 20 (and -1) → `ItemNotInInventory` each, 0 `MoveItemOut` / `MoveItemIn`, Weapon still Empty, S unchanged, 0 stat events. (b) closes a Story 002 `/code-review` 2026-10-26 gap: until this guard exists, `Equip` takes Y out of S but registers X's modifiers and records X in the slot.
- **Order** — a request that fails two checks (e.g. wrong slot type and stat gate) returns the earlier code in the documented order.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EquipmentSystem/EquipmentSystem_Validation_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Stories 004, 008
