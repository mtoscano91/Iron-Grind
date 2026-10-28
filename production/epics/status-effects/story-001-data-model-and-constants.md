# Story 001: Data Model, Constants and Duration Conversion

> **Epic**: Status Effects / Buffs
> **Status**: Complete — 2026-10-28 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28: NEEDS WORK (3 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-1 (data model), CR-SE-2 (stat targets, as a static check), CR-SE-15 (table layout), F-SE-1 (duration conversion), Tuning Knobs, AC-SE-26
**Requirement**: `TR-se-001`

**ADR Governing Implementation**: ADR-012 (Server/Client Assembly Boundary); ADR-010 (Event/Messaging) for the value-type rule
**ADR Decision Summary**: server-authoritative core logic lives in `IronGrind.ServerLogic`; event and message argument types are `readonly struct`s with no per-use allocation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#; no Unity API. The value types must be unmanaged so IL2CPP stores them without boxing.

**Performance**: no runtime cost in this story (types and constants only). Allocation-free storage is checked by the `IsReferenceOrContainsReferences` test. Tick cost is budgeted in Story 005.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative core logic is `IronGrind.ServerLogic` (`src/ServerLogic/`) — ADR-012
- Required: a new server-only namespace is added to `ServerOnlyNamespaces` in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` — ADR-012
- Required: `readonly struct` value types, no reference-type fields — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/status-effects.md`, scoped to this story:*

- [x] **Types (CR-SE-1)**: `StatModifier` (`StatID StatId`, `float FlatBonus`, `float PctBonus`), `BuffDefinition` (`BuffID BuffId`, `byte ModifierCount`, `StatModifier Modifier0`…`Modifier7`, `uint DurationTicks`, `float HpRegenPerTick`, `float MpRegenPerTick`) are `readonly struct`s; `ActiveEffect` (`BuffID BuffId`, `EntityID EntityId`, `EntityID CasterEntityId`, `uint TicksRemaining`, `byte ModifierCount`, `Modifier0`…`Modifier7`, `float HpRegenPerTick`, `float MpRegenPerTick`) is a mutable `struct` (not `readonly`), because Story 005 decrements `TicksRemaining` in place. All fields of the three are value types; a reflection test asserts no field of any of the three is a reference type, and `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` is false for each.
- [x] **Indexed access**: `BuffDefinition.GetModifier(int index)` returns `Modifier0`…`Modifier7` for 0–7 and throws `ArgumentOutOfRangeException` outside; entries at index ≥ `ModifierCount` are `default`. `ActiveEffect.GetModifier(int index)` has the same contract.
- [x] **`ApplyResult`** enum: `Success`, `CapacityExceeded`, `EffectSlotsFull`, `InvalidDuration`, `InvalidStat`, `InvalidTarget`, `InvalidAmount`, `NoOpDefinition`.
- [x] **Constants** in `StatusEffectsConstants`: `MAX_STATS_PER_BUFF = 8`, `MAX_ACTIVE_BUFFS_PER_ENTITY = 8`, `MODIFIER_CAPACITY_PER_ENTITY = 32` (F-SE-2), `MAX_DURATION_TICKS = 6000` (300 s × 20 Hz), `MAX_ENTITY_SLOTS = ZoneSlotPool.MAX_PLAYERS_PER_ZONE + ZoneSlotPool.MAX_MOBS_PER_ZONE` (= 200; an EditMode test asserts 200).
- [x] **AC-SE-26 — constant constraint guard**: `MAX_ACTIVE_BUFFS_PER_ENTITY ≥ floor(MODIFIER_CAPACITY_PER_ENTITY / MAX_STATS_PER_BUFF)` and `MAX_STATS_PER_BUFF ≤ MODIFIER_CAPACITY_PER_ENTITY` are enforced at compile time (a `const` expression that fails to compile, e.g. a negative array size or `static_assert` pattern) **and** by an EditMode test whose failure message names both constants.
- [x] **F-SE-1 duration conversion**: `StatusEffectsMath.SecondsToTicks(float seconds)` = `floor(seconds × TICK_RATE_HZ)` with `TICK_RATE_HZ` from `ServerTickLoop`; the table holds: 0.5 s → 10, 3.0 s → 60, 300.0 s → 6000, 0.0 s → 0. `IsWithinAuthoringCeiling(ticks)` is true for 1–6000 and false for 0 and 72 000 (3600 s).
- [x] **Namespace**: types in `IronGrind.StatusEffects`, folder `src/ServerLogic/StatusEffects/`; `IronGrind.StatusEffects` added to `ServerOnlyNamespaces`; the assembly boundary test passes.

---

## Implementation Notes

- `BuffID` and `EntityID` already exist (Character Stats, `src/Foundation/CharacterStats/` / `src/ServerLogic/CharacterStats/`); reuse them.
- Eight inline fields instead of an array is deliberate (CR-SE-1: no heap reference). Write `GetModifier` as a `switch`.
- `BuffDefinition` gets a constructor that takes up to eight modifiers (e.g. a `ReadOnlySpan<StatModifier>`) and throws `ArgumentException` above `MAX_STATS_PER_BUFF`; it does not validate stats or duration — that is `ApplyEffect`'s job (Story 002), so tests can build invalid definitions.
- The compile-time guard: e.g. `private const int GUARD = 1 / (MAX_ACTIVE_BUFFS_PER_ENTITY >= MODIFIER_CAPACITY_PER_ENTITY / MAX_STATS_PER_BUFF ? 1 : 0);` gives a compile error (division by constant zero) when violated; keep a comment naming the rule.
- `MAX_STATS_PER_BUFF` and `MAX_ACTIVE_BUFFS_PER_ENTITY` are already registered in `design/registry/entities.yaml`; no registry change.
- Fix the GDD header status to match `systems-index.md` (Approved, lean re-review 2026-05-20) — done at story creation.

---

## Out of Scope

- Validation and application (Stories 002, 003), the table and its tick (005), anything wire-related (TR-se-006).

---

## QA Test Cases

- **Value types** — Given the three structs, When their fields are reflected, Then every field type is a value type and `IsReferenceOrContainsReferences` is false. Edge: nested `StatModifier` fields included.
- **GetModifier** — Given a definition with 3 modifiers, When `GetModifier(0..7)` is read, Then 0–2 return them, 3–7 return `default`; `GetModifier(-1)` and `GetModifier(8)` throw.
- **Constructor bound** — Given 9 modifiers, When a definition is built, Then `ArgumentException`.
- **Constant guard** — Given the constants, Then `MAX_ACTIVE_BUFFS_PER_ENTITY >= 32 / MAX_STATS_PER_BUFF` (= 4) and `MAX_STATS_PER_BUFF <= 32`; the assert message names both.
- **Duration table** — 0.5 → 10, 3.0 → 60, 300.0 → 6000, 0.0 → 0; ceiling check true at 1 and 6000, false at 0 and 72 000.
- **Boundary** — the architecture boundary test passes with `IronGrind.StatusEffects` listed as server-only.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_DataModel_tests.cs` — must exist and pass

**Status**: [x] Created and passing — 16 test methods, 31 cases (fixture 31/31; full EditMode 4619 / 4490 passed / 0 failed / 129 skipped, 2026-10-28)

---

## Dependencies

- Depends on: None (Character Stats epic Complete)
- Unlocks: Stories 002–009

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 7/7 passing, all covered by `tests/EditMode/StatusEffects/StatusEffects_DataModel_tests.cs` (namespace/boundary by the existing `AssemblyBoundary_Tests`, 12/12)
**Deviations** (advisory, functionally equivalent):
1. `SecondsToTicks` returns 0 for negative/NaN input and saturates at `uint.MaxValue` (computed in `double`); the story did not specify these inputs. Story 002's ceiling check rejects a saturated value.
2. `MAX_DURATION_TICKS` is `uint` (compares directly with tick values).
3. Constructor signatures (left open by the story): `BuffDefinition(BuffID, ReadOnlySpan<StatModifier>, uint durationTicks, float hpRegenPerTick, float mpRegenPerTick)`; `ActiveEffect(in BuffDefinition, EntityID, EntityID)`.
4. Tuning knobs are `const`s, not external config — the AC-SE-26 compile-time guard needs them at compile time (same as sibling systems).
**Files**: `src/ServerLogic/StatusEffects/` (`StatModifier`, `BuffDefinition`, `ActiveEffect`, `ApplyResult`, `StatusEffectsConstants`, `StatusEffectsMath`); `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (`IronGrind.StatusEffects` server-only); Story 005 Implementation Notes (mutate `ActiveEffect` rows by array element / `ref`, from code review).
**Test Evidence**: Logic — `tests/EditMode/StatusEffects/StatusEffects_DataModel_tests.cs`
**Code Review**: Complete — `/code-review` 2026-10-28 APPROVED WITH SUGGESTIONS, all 6 applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
**Tech debt**: None.
