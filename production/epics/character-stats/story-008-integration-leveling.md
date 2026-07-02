# Story 008: Integration — Leveling System ↔ Character Stats (Spawn Path, Tier Transitions, MaxMP Ceiling)

> **Epic**: Character Stats
> **Status**: Blocked — Leveling System epic has not been created yet
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-06-28

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-001`, `TR-stats-003`, `TR-stats-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the Leveling System ↔ Character Stats collaboration. Integration tests verify that the Leveling System calls Character Stats APIs in the correct sequence: auto-alloc before tier recompute; `MaxMP` clamped before `SetBaseStat`; spawn path uses `LevelTierMultiplier ×1.0`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Integration tests require real `CharacterStats` and real `LevelingSystem` instances — no mocks for the two principal systems. Wire dependencies via constructor injection. No Unity scene required if both systems are plain C# classes (EditMode-compatible).

**Control Manifest Rules (Foundation layer)**:
- Required: Leveling System must use `Mathf.FloorToInt()` (not `System.Math.Floor()`) for F-3 through F-9 — source: GDD Rule 7 / AC-25
- Required: Auto-allocation fires before tier recompute at milestone level-ups — ordering contract per AC-27b
- Guardrail: Leveling System must clamp F-4 output to 9,999 before calling `SetBaseStat(MaxMP)` — Character Stats does NOT enforce StatMax (verified in Story 001)

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-27a** [BLOCKING]: Warrior Tank entity at L19 (VIT=46). Leveling System processes L19→L20 (auto-alloc VIT→48; tier ×1.0→×1.2; F-3 recomputed). `GetBaseStat(MaxHP)` = **1392** (`floor((200+48×20)×1.2)`). A return of 1160 (tier ×1.0 still applied) is failure.
- [ ] **AC-27b** [BLOCKING]: Warrior Tank at L39 (VIT=86). L39→L40 level-up: auto-alloc VIT→88 **FIRST**, THEN F-3 recomputed at tier ×1.5. `GetBaseStat(MaxHP)` = **2940** (`floor((200+88×20)×1.5)`). A return of 2790 (VIT=86 used — ordering violation) is an ordering regression failure. A return of 1960 (tier ×1.0 still applied) or 2352 (tier ×1.2) is also failure.
- [ ] **AC-27c** [BLOCKING]: Warrior Tank at L59 (VIT=126). L59→L60 (VIT→128; tier→×2.0; F-3 recomputed). `GetBaseStat(MaxHP)` = **5520**.
- [ ] **AC-31** [BLOCKING]: New Warrior at spawn. Class System triggers Leveling System init (tier ×1.0, all primary attributes=10). `GetBaseStat(MaxHP)` immediately after spawn = **400** (`floor((200+10×20)×1.0)`). A return of 200 (formula base without tier) is failure.
- [ ] **AC-34** [BLOCKING]: INT=409 at L60 (tier ×2.0). Leveling System evaluates F-4 raw=10,016; clamps to 9,999; calls `SetBaseStat(MaxMP, 9999)`. `GetBaseStat(MaxMP)` = **9999**. Then INT=500 → F-4 raw=12,200; clamp → 9999; `GetBaseStat(MaxMP)` = **9999**. Also verify: direct `SetBaseStat(MaxMP, 10016)` (bypassing Leveling System) → `GetBaseStat(MaxMP)` = **10016** (proves Character Stats does NOT clamp — Leveling System owns it).

---

## Implementation Notes

*Derived from GDD F-2a (LevelTierMultiplier), F-3–F-9, F-10 (level-up auto-alloc), F-4 (MaxMP ceiling):*

**AC-27b ordering contract (critical regression detector):** At milestone level-ups (L20, L40, L60), the Leveling System MUST:
1. Apply all auto-allocations for the new level via `SetBaseStat()` (e.g., VIT→88 at L40)
2. THEN recompute F-3 through F-9 with the new `LevelTierMultiplier`

Failure mode: if F-3 fires before auto-alloc, Warrior Tank at L40 returns 2790 (VIT=86) instead of 2940 (VIT=88) — a detectable ordering violation.

**AC-31 spawn path:** At entity spawn, the Class System calls the Leveling System's stat-initialization routine. The Leveling System treats L1 spawn as a tier ×1.0 event and computes F-3 through F-9 with `LevelTierMultiplier = 1.0` and all starting primary attributes = 10. This path is triggered by Class System → Leveling System initialization, not by a level-up event.

**AC-34 clamp ownership split:** The Leveling System evaluates F-4 and calls `min(rawResult, 9999)` before calling `SetBaseStat(MaxMP, ...)`. Character Stats' `SetBaseStat()` does NOT enforce `StatMax` — it stores exactly what is written. The two-part AC-34 test verifies both halves: (a) Leveling System correctly writes 9999 even when raw formula = 10016; (b) direct `SetBaseStat(MaxMP, 10016)` writes 10016, proving Character Stats does not clamp.

**Integration test setup:** Instantiate real `CharacterStats` and real `LevelingSystem`. Wire `ICharacterStats` into `LevelingSystem` via constructor injection. Provide a stub `ClassSystem` that calls `LevelingSystem.InitializeEntity()` at spawn. No mocks for the two principal systems — integration means real collaboration.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 001–007**: All Logic stories — must be Done before this story starts
- **Respec procedure**: Story 007 covers the transaction API unit test; Leveling System epic covers the Leveling System's respec logic
- **Status Effects integration**: Belongs in Status Effects epic

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs`
*Setup: real `CharacterStats` + real `LevelingSystem` + stub `ClassSystem` — no mocks for principal systems*

- **AC-31**: L1 spawn path initializes MaxHP with LevelTierMultiplier ×1.0
  - Given: New Warrior entity; stub Class System triggers Leveling System init (tier ×1.0, all primary attributes=10)
  - When: Initialization sequence completes. `GetBaseStat(MaxHP)`.
  - Then: Returns 400 (`floor((200+10×20)×1.0) = 400`). A return of 200 (tier not applied) is failure.
  - Edge cases: Verify CharacterStats has no MaxHP value before spawn sequence fires.

- **AC-27a**: L19→L20 tier transition applies ×1.2 multiplier
  - Given: Warrior Tank entity at L19; VIT=46 (10 base + 18 level-ups × 2 VIT auto-alloc)
  - When: Leveling System processes L19→L20 (auto-alloc: VIT→48; tier: ×1.0→×1.2; F-3 recomputed). `GetBaseStat(MaxHP)`.
  - Then: Returns 1392 (`floor((200+48×20)×1.2)`). A return of 1160 (tier ×1.0 still applied) is failure.
  - Edge cases: Verify `GetBaseStat(VIT)` = 48 after level-up.

- **AC-27b**: L39→L40 transition — auto-alloc fires BEFORE tier recompute (ordering regression detector)
  - Given: Warrior Tank entity at L39; VIT=86 (10 base + 38 level-ups × 2 VIT auto-alloc)
  - When: Leveling System processes L39→L40 (ordering: VIT→88 FIRST via auto-alloc; THEN F-3 recomputed at tier ×1.5). `GetBaseStat(MaxHP)`.
  - Then: Returns 2940 (`floor((200+88×20)×1.5)`). A return of 2790 (VIT=86 used — auto-alloc fired after recompute) is an ordering regression failure. Returns of 1960 (tier ×1.0) or 2352 (tier ×1.2) also indicate failure.
  - Edge cases: Compare MaxHP at L39 end vs L40 start to confirm the tier spike is visible.

- **AC-27c**: L59→L60 transition — final tier milestone ×2.0
  - Given: Warrior Tank at L59; VIT=126 (10 + 58 level-ups × 2 VIT auto-alloc)
  - When: Leveling System processes L59→L60 (VIT→128; tier→×2.0; F-3 recomputed). `GetBaseStat(MaxHP)`.
  - Then: Returns 5520 (`floor((200+128×20)×2.0)`). Returns of 2760 (tier ×1.0) or 4140 (tier ×1.5) are failure.
  - Edge cases: Verify Level=60 after this level-up (cap); subsequent `AddExperience` does not trigger further level-up.

- **AC-34**: Leveling System clamps F-4 before SetBaseStat; Character Stats does not clamp
  - Given: Player entity with INT=409 at L60 (tier ×2.0)
  - When: Leveling System evaluates F-4 = `floor((100+409×12)×2.0)` = 10,016; clamps to 9999; calls `SetBaseStat(MaxMP, 9999)`
  - Then: `GetBaseStat(MaxMP)` = 9999 (not 10016).
  - When: INT→500; Leveling System evaluates F-4 = 12,200; clamps to 9999; calls `SetBaseStat(MaxMP, 9999)`
  - Then: `GetBaseStat(MaxMP)` = 9999.
  - When (direct path, no Leveling System): `SetBaseStat(MaxMP, 10016)` called directly
  - Then: `GetBaseStat(MaxMP)` = 10016 (Character Stats stores unclamped — proves Leveling System owns the clamp).
  - Edge cases: INT=408 → F-4 = `floor((100+408×12)×2.0)` = 9992 → no clamp needed → `GetBaseStat(MaxMP)` = 9992.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs` OR documented playtest — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: All of Story 001–007 must be Done; Leveling System epic stories must be Done
- **BLOCKED**: Leveling System epic has not been created yet. This story cannot start until `/create-epics layer: core` (or equivalent) creates the Leveling System epic and all its stories are Done.
- Unlocks: Nothing — this is the final story in the character-stats epic
