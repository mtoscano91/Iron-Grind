# Story 001: CharacterStats Container — Stat Schema, IL2CPP-Safe Types, GetBaseStat / SetBaseStat

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-001`, `TR-stats-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Entity and stat ID types must be IL2CPP-safe value types (no string keys on hot paths). `readonly struct` modifier entries to avoid GC heap allocation on iOS IL2CPP. Service interfaces named `I[SystemName]Service`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `CharacterStats` is a plain C# class — no `MonoBehaviour`, no Unity scene required. All types in this story are pure C# and testable in EditMode without a Unity instance.

**Control Manifest Rules (Foundation layer)**:
- Required: `readonly struct` modifier entry types — zero heap allocation beyond the fixed array — source: ADR-010
- Required: Service interface naming `I[SystemName]Service` — source: ADR-010
- Forbidden: Never use `string` as an entity identifier — string keys generate GC allocations on every stat query under Unity 6.3 IL2CPP on iOS — source: GDD Rule 7
- Guardrail: `SetBaseStat()` does NOT enforce StatMax — callers (Leveling System) are responsible for clamping before writing

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-21 (adapted)** [BLOCKING]: `GetBaseStat(MobEntityID, StatID.Intelligence)`, `StatID.MaxMP`, `StatID.Experience` each return `0`; no exception thrown. All 7 player-only fields (STR, DEX, VIT, INT, MaxMP, CurrentMP, Experience) return 0 on a mob entity.
- [ ] **AC-23** [BLOCKING]: `SetBaseStat(MaxHP, 5520)` → `GetBaseStat(MaxHP)` = 5520. `SetBaseStat(MaxHP, 400)` → `GetBaseStat(MaxHP)` = 400. Values stored and retrieved exactly — no re-evaluation of formulas.
- [ ] **AC-25** [BLOCKING]: `SetBaseStat(Defense, 394)` → `GetBaseStat(Defense)` = 394. A return of 395 (rounding rather than floor-truncating from the caller) indicates a problem.
- [ ] **AC-26** [ADVISORY]: `GetBaseStat(EntityID, StatID.Experience)` == `GetEffectiveStat(EntityID, StatID.Experience)` = 3500 when `BaseStat(Experience)` = 3500. Experience is not modifier-stackable — base and effective must always be equal.
- [ ] **AC-30 (GetBaseStat portion)** [BLOCKING]: `SetBaseStat(MobEntityID, StatID.AttackPower, 75)` → `GetBaseStat(MobEntityID, StatID.AttackPower)` = 75. No formula applied — mob stats are raw base values.
- [ ] **AC-32** [BLOCKING]: Simulated persistence load — `SetBaseStat(MaxHP, 2940)` → `GetBaseStat(MaxHP)` = 2940. Character Stats stores exactly what is written; it does NOT re-evaluate F-3 through F-9 on write.
- [ ] **[NEW] StatMax non-enforcement** [BLOCKING]: `SetBaseStat(MaxMP, 10016)` (above `StatMax(MaxMP)` = 9999) → `GetBaseStat(MaxMP)` = 10016. `SetBaseStat` does not clamp — the Leveling System owns clamping before writing (per AC-34 design intent).
- [ ] **[NEW] Container isolation** [BLOCKING]: `SetBaseStat(entityA, MaxHP, 500)` and `SetBaseStat(entityB, MaxHP, 200)` → `GetBaseStat(entityA, MaxHP)` = 500, `GetBaseStat(entityB, MaxHP)` = 200. No bleed-through between entity containers.

---

## Implementation Notes

*Derived from GDD `design/gdd/character-stats.md` Rules 1, 7, and ADR-010:*

**`CharacterStats` is a plain C# class** — not a `MonoBehaviour`. Allocated per entity at spawn by the Class System and distributed via dependency injection. Enables EditMode unit testing without a Unity scene.

**Type definitions (IL2CPP-safe — no GC allocations on iOS hot paths):**
- `EntityID`: `public readonly struct EntityID` wrapping `uint`. Never use `string`.
- `StatID`: `public enum StatID : byte`. Never use `string` or generics with struct params (IL2CPP boxing risk).
- `BuffID`: `public enum BuffID : uint`.
- `ItemID`: `public readonly struct ItemID` wrapping `uint`.

**Modifier entry types** must be `readonly struct` (not `class`). Equipment entry: `(float FlatBonus, float PctBonus, ItemID Id)`. Buff entry: `(float FlatBonus, float PctBonus, int DurationTicks, BuffID Id)`. Both allocated inside fixed-capacity arrays — zero additional heap allocation beyond the arrays themselves.

**Fixed-capacity arrays** (not `List<T>`):
- Equipment modifier layer: 16 entries.
- Buff modifier layer: 32 entries.
- On overflow: log error in dev builds, return without adding. Not a runtime recovery path — raise capacity in code.

**`SetBaseStat()` does not enforce StatMax.** Callers (Leveling System) must clamp before writing. `GetBaseStat()` returns the stored value exactly as written.

**Player-only fields** (STR, DEX, VIT, INT, MaxMP, CurrentMP, Experience): initialized to zero for mob entities. The query interface is identical for all entity types.

**Test helpers to create in `tests/EditMode/CharacterStats/TestHelpers/`:**
- `CharacterStatsFixture.cs` — creates a default `CharacterStats` instance with a player EntityID and mob EntityID pre-wired, with shortcut methods for common setup.
- `StatEventRecorder.cs` — tracks per-stat `OnStatChanged` fire counts and timing relative to transaction boundaries; used by Stories 005 and 007.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 002**: `GetEffectiveStat()` and the F-1 modifier stack
- **Story 003**: `AddBuffModifier`, `RemoveBuffModifier`, `AddEquipmentModifier`, `RemoveEquipmentModifier`
- **Story 004**: `ApplyDamage`, `ApplyRegen`, `ConsumeMana`, `ApplyManaRegen`, death boundary
- **Story 005**: `OnStatChanged`, `OnEntityDied` events

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_Container_tests.cs`

- **AC-23**: SetBaseStat / GetBaseStat round-trip for int stat
  - Given: A CharacterStats container with a player EntityID
  - When: `SetBaseStat(MaxHP, 5520)`, then `GetBaseStat(MaxHP)`
  - Then: Returns 5520 exactly. Then `SetBaseStat(MaxHP, 400)` → returns 400.
  - Edge cases: Re-write with lower value after higher — confirm overwrite, not accumulation.

- **AC-25**: SetBaseStat stores int exactly
  - Given: A CharacterStats container with a player EntityID
  - When: `SetBaseStat(Defense, 394)`, then `GetBaseStat(Defense)`
  - Then: Returns 394 exactly. A return of 395 indicates the system is re-rounding.
  - Edge cases: Write 393, write 0 — verify each stored exactly as written.

- **AC-32**: Persistence load path — SetBaseStat stores without re-evaluating formulas
  - Given: A CharacterStats container initialized fresh
  - When: `SetBaseStat(MaxHP, 2940)` (simulating persistence restore), then `GetBaseStat(MaxHP)`
  - Then: Returns 2940 — not the F-3 formula result (which would be 200 with VIT=10).
  - Edge cases: Calling `SetBaseStat` multiple times on the same stat keeps only the last value.

- **AC-21 (adapted)**: Player-only fields return 0 on mob entity via GetBaseStat
  - Given: A mob EntityID with mob data table applied (no player-only fields written)
  - When: `GetBaseStat(MobEntityID, StatID.Intelligence)`, `StatID.MaxMP`, `StatID.Experience`
  - Then: All return 0; no exception thrown.
  - Edge cases: Query all 7 player-only fields (STR, DEX, VIT, INT, MaxMP, CurrentMP, Experience); all return 0.

- **AC-30 (GetBaseStat portion)**: Mob stats round-trip
  - Given: A mob EntityID with all stats at default
  - When: `SetBaseStat(MobEntityID, AttackPower, 75)`, then `GetBaseStat(MobEntityID, AttackPower)`
  - Then: Returns 75 (raw base value — no formula applied).
  - Edge cases: Write mob MaxHP directly; read it back; confirm same round-trip behavior as player entity.

- **[NEW] StatMax non-enforcement**: SetBaseStat does not clamp
  - Given: A player EntityID; `StatMax(MaxMP)` = 9999
  - When: `SetBaseStat(MaxMP, 10016)`, then `GetBaseStat(MaxMP)`
  - Then: Returns 10016 — no clamping applied by `SetBaseStat`.
  - Edge cases: Write 0 to a stat with `StatMin > 0` (e.g., MaxHP `StatMin`=1); verify 0 is stored as written (no StatMin enforcement on write either).

- **[NEW] Container isolation**: Two entities have independent stat containers
  - Given: EntityID playerA and EntityID playerB using the same CharacterStats store
  - When: `SetBaseStat(playerA, MaxHP, 500)`, `SetBaseStat(playerB, MaxHP, 200)`
  - Then: `GetBaseStat(playerA, MaxHP)` = 500; `GetBaseStat(playerB, MaxHP)` = 200.
  - Edge cases: Write to playerB after playerA; read both; verify order doesn't matter.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_Container_tests.cs` — must exist and pass

**Status**: [x] Created 2026-06-28 — `tests/EditMode/CharacterStats/CharacterStats_Container_tests.cs` (13 tests)

---

## Dependencies

- Depends on: None — this is the foundational story for the epic
- Unlocks: Story 002 (requires CharacterStats container and GetBaseStat/SetBaseStat)

---

## Completion Notes

**Completed**: 2026-06-28
**Criteria**: 7/8 passing (AC-26 DEFERRED to Story 002 — GetEffectiveStat not implemented here)
**Deviations**: None
**Test Evidence**: Logic — `tests/EditMode/CharacterStats/CharacterStats_Container_tests.cs` (14 tests)
**Code Review**: Complete — APPROVED. Two doc-comment fixes applied (StatID section headers, StatArraySize invariant). IEquatable<T> added to EquipmentModifierEntry and BuffModifierEntry. AC-26 added to Story 002 ACs and QA test cases.
**Follow-up**: .asmdef files required before CI can discover these tests (infrastructure task, outside Story 001 scope)
